using System;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using ArrayImageViewer.Core;

namespace ArrayImageViewer.UI
{
    internal sealed partial class SensorImageViewerControl
    {
        // Small kernel/View requests finish immediately. Full previews can
        // contain tens of millions of samples, so their statistics must never
        // hold the Visual Studio UI thread while panning or stepping.
        private const long BackgroundStatisticsSampleLimit = 262144;
        private CancellationTokenSource statisticsCancellation;

        private void ToggleStatistics(object sender, RoutedEventArgs e)
        {
            var show = statisticsPanel.Visibility != Visibility.Visible;
            statisticsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            statisticsToggle.Content = show ? "Hide stats" : "Stats";
            if (show)
            {
                UpdateStatistics();
            }
            else CancelStatisticsRequest();
        }

        private void StatisticsScopeChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateStatistics();
        }

        private void StatisticsFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (String.Equals(GetStatisticsScope(), "Filter match", StringComparison.Ordinal))
            {
                UpdateStatistics();
            }
        }

        private void UpdateStatistics()
        {
            Dispatcher.VerifyAccess();
            CancelStatisticsRequest();
            if (statisticsPanel.Visibility != Visibility.Visible)
            {
                return;
            }
            if (frame == null)
            {
                statisticsSummary.Text = "No cached samples.";
                statisticsChannels.Text = String.Empty;
                return;
            }

            try
            {
                int x;
                int y;
                int widthInSamples;
                int heightInSamples;
                if (!TryGetStatisticsBounds(out x, out y, out widthInSamples, out heightInSamples))
                {
                    statisticsSummary.Text = "The selected scope is outside the loaded ROI.";
                    statisticsChannels.Text = String.Empty;
                    return;
                }

                var filter = String.Equals(GetStatisticsScope(), "Filter match", StringComparison.Ordinal)
                    ? frame.Configuration.VisualizeChannel : VisualizeChannel.Gray;
                var source = frame;
                var generation = statisticsGeneration;
                var sampleCount = (long)widthInSamples * heightInSamples;
                if (sampleCount <= BackgroundStatisticsSampleLimit)
                {
                    DisplayStatistics(FrameStatisticsCalculator.Calculate(source, x, y, widthInSamples, heightInSamples, filter));
                    return;
                }

                statisticsSummary.Text = "Calculating raw statistics for " + sampleCount.ToString("N0", CultureInfo.InvariantCulture) + " cached samples...";
                statisticsChannels.Text = "The viewer remains interactive; a newer scope or frame cancels this result.";
                var cancellation = new CancellationTokenSource();
                statisticsCancellation = cancellation;
                var worker = new Thread(delegate()
                {
                    try
                    {
                        var result = FrameStatisticsCalculator.Calculate(source, x, y, widthInSamples, heightInSamples, filter, cancellation.Token);
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            if (generation == statisticsGeneration && Object.ReferenceEquals(frame, source) && statisticsPanel.Visibility == Visibility.Visible)
                            {
                                DisplayStatistics(result);
                            }
                        }));
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception exception)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            if (generation == statisticsGeneration && Object.ReferenceEquals(frame, source) && statisticsPanel.Visibility == Visibility.Visible)
                            {
                                statisticsSummary.Text = "Stats unavailable: " + exception.Message;
                                statisticsChannels.Text = String.Empty;
                            }
                        }));
                    }
                });
                worker.IsBackground = true;
                worker.Start();
            }
            catch (Exception exception)
            {
                statisticsSummary.Text = "Stats unavailable: " + exception.Message;
                statisticsChannels.Text = String.Empty;
            }
        }

        private void CancelStatisticsRequest()
        {
            statisticsGeneration++;
            if (statisticsCancellation != null)
            {
                statisticsCancellation.Cancel();
                statisticsCancellation = null;
            }
        }

        private void DisplayStatistics(FrameStatistics statistics)
        {
            statisticsSummary.Text = "Visible raw  " + FormatStatistics(statistics.Visible);
            statisticsChannels.Text = "R  " + FormatStatistics(statistics.R) + "    Gr " + FormatStatistics(statistics.Gr) +
                "\nGb " + FormatStatistics(statistics.Gb) + "    B  " + FormatStatistics(statistics.B);
        }

        private string GetStatisticsScope()
        {
            return Convert.ToString(statisticsScope.SelectedItem, CultureInfo.InvariantCulture) ?? "View";
        }

        private bool TryGetStatisticsBounds(out int x, out int y, out int widthInSamples, out int heightInSamples)
        {
            x = 0;
            y = 0;
            widthInSamples = 0;
            heightInSamples = 0;
            var scope = GetStatisticsScope();
            if (String.Equals(scope, "Loaded ROI", StringComparison.Ordinal) || String.Equals(scope, "Filter match", StringComparison.Ordinal))
            {
                x = 0;
                y = 0;
                widthInSamples = frame.Configuration.Width;
                heightInSamples = frame.Configuration.Height;
                return true;
            }

            var configuration = ReadConfiguration();
            RoiBounds requested;
            if (String.Equals(scope, "Kernel", StringComparison.Ordinal))
            {
                requested = GetRoiBounds(configuration);
            }
            else
            {
                requested = GetRenderBounds(configuration, GetRoiBounds(configuration));
            }

            var loadedLeft = frame.Configuration.OriginX;
            var loadedTop = frame.Configuration.OriginY;
            var loadedRight = loadedLeft + frame.Configuration.Width;
            var loadedBottom = loadedTop + frame.Configuration.Height;
            var requestedRight = requested.X + requested.Width;
            var requestedBottom = requested.Y + requested.Height;
            var globalLeft = Math.Max(loadedLeft, requested.X);
            var globalTop = Math.Max(loadedTop, requested.Y);
            var globalRight = Math.Min(loadedRight, requestedRight);
            var globalBottom = Math.Min(loadedBottom, requestedBottom);
            if (globalRight <= globalLeft || globalBottom <= globalTop)
            {
                return false;
            }

            x = globalLeft - loadedLeft;
            y = globalTop - loadedTop;
            widthInSamples = globalRight - globalLeft;
            heightInSamples = globalBottom - globalTop;
            return true;
        }

        private static string FormatStatistics(SampleStatistics statistics)
        {
            if (statistics.Count == 0)
            {
                return "--";
            }

            return "n=" + statistics.Count.ToString(CultureInfo.InvariantCulture) +
                " min=" + statistics.Minimum.ToString(CultureInfo.InvariantCulture) +
                " max=" + statistics.Maximum.ToString(CultureInfo.InvariantCulture) +
                " mean=" + statistics.Mean.ToString("0.###", CultureInfo.InvariantCulture) +
                " sd=" + statistics.StandardDeviation.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
