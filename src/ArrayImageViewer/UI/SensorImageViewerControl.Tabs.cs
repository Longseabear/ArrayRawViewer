using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArrayImageViewer.Core;

namespace ArrayImageViewer.UI
{
    internal sealed partial class SensorImageViewerControl
    {
        private int nextViewerTabId = 1;
        private ViewerTab activeViewerTab;
        private bool switchingViewerTab;
        private Button addViewerTabButton;
        private ArrayImageViewer.Debugging.StructureSearchTrace tabTrace;

        private void TraceTabStep(string name, Action action)
        {
            if (tabTrace == null) { action(); return; }
            tabTrace.Call(name, delegate { action(); return true; });
        }

        private void TraceTabMemory(string phase)
        {
            if (tabTrace == null || tabTrace.FilePath == null) return;
            try
            {
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
                tabTrace.Write(phase + " process=" + process.Id + " bits=" + (Environment.Is64BitProcess ? 64 : 32) +
                    " privateBytes=" + process.PrivateMemorySize64 + " managedBytes=" + GC.GetTotalMemory(false) +
                    " tabs=" + viewerTabs.Count + " active=" + (activeViewerTab == null ? 0 : activeViewerTab.Id));
            }
            catch (Exception exception) { tabTrace.Write("Memory metrics unavailable: " + exception.Message); }
        }

        private void StopTabTimers()
        {
            autoRefreshTimer.Stop();
            coordinateUpdateTimer.Stop();
            debuggerBreakRefreshTimer.Stop();
            viewportOverlayTimer.Stop();
        }

        private void ChangeViewerTab(Action change)
        {
            if (switchingViewerTab) return;
            switchingViewerTab = true;
            var applying = isApplyingProfile;
            try
            {
                try { tabTrace = new ArrayImageViewer.Debugging.StructureSearchTrace(ArrayImageViewer.Options.StructureTemplateStore.LoadSearchDebug(), "array-tab"); }
                catch (Exception exception) { SetStatus("Cannot create array debug log: " + exception.Message); }
                TraceTabMemory("START");
                TraceTabStep("StopTimers", StopTabTimers);
                TraceTabStep("InvalidateSearch", InvalidateStructureSearchCache);
                TraceTabStep("CancelStatistics", CancelStatisticsRequest);
                TraceTabStep("DismissCompletion", DismissExpressionSuggestions);
                TraceTabStep("TabOperation", change);
            }
            catch (Exception exception)
            {
                if (tabTrace != null) tabTrace.Write("ERROR " + exception.ToString());
                // Do not let a WPF button callback terminate the IDE. Cached
                // tabs remain available even if this restore failed.
                SetStatus("Cannot switch array tab: " + exception.Message);
            }
            finally
            {
                StopTabTimers();
                isApplyingProfile = applying;
                switchingViewerTab = false;
                if (tabTrace != null)
                {
                    try { TraceTabMemory("FINISH"); }
                    finally
                    {
                        var path = tabTrace.FilePath;
                        tabTrace.Dispose(); tabTrace = null;
                        if (path != null) status.Text += " Debug dump: " + path;
                    }
                }
            }
        }

        private UIElement CreateViewerTabStrip()
        {
            var strip = new Border
            {
                Background = PanelBrush,
                BorderBrush = PanelBorderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 5, 10, 5)
            };
            viewerTabPanel.VerticalAlignment = VerticalAlignment.Center;
            strip.Child = viewerTabPanel;
            return strip;
        }

        private void InitializeViewerTabs()
        {
            if (activeViewerTab != null)
            {
                return;
            }

            activeViewerTab = new ViewerTab(nextViewerTabId++, CreateCurrentProfile());
            viewerTabs.Add(activeViewerTab);
            RefreshViewerTabs();
        }

        private void RefreshViewerTabs()
        {
            // Keep live headers attached: this also runs from their Click
            // callbacks and after capture. Replacing the focused native-hosted
            // WPF subtree can trigger focus restoration while switching tabs.
            for (var childIndex = viewerTabPanel.Children.Count - 1; childIndex >= 0; childIndex--)
            {
                var header = viewerTabPanel.Children[childIndex] as StackPanel;
                if (header != null && !viewerTabs.Contains(header.Tag as ViewerTab))
                    viewerTabPanel.Children.RemoveAt(childIndex);
            }
            for (var index = 0; index < viewerTabs.Count; index++)
            {
                var tab = viewerTabs[index];
                if (tab.Header == null) CreateViewerTabHeader(tab);
                if (!viewerTabPanel.Children.Contains(tab.Header))
                    viewerTabPanel.Children.Insert(index, tab.Header);

                var select = tab.SelectButton;
                var selected = tab == activeViewerTab;
                ((TextBlock)select.Content).Text = tab.Caption;
                System.Windows.Automation.AutomationProperties.SetName(select, tab.Caption);
                select.ToolTip = tab.Profile == null ? tab.Caption : tab.Profile.Expression;
                select.Foreground = selected ? RootBrush : TextBrush;
                select.Background = selected ? AccentBrush : ControlBrush;
                select.BorderBrush = selected ? AccentBrush : PanelBorderBrush;
                select.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
                tab.CloseButton.Visibility = viewerTabs.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            }

            if (addViewerTabButton == null)
            {
                addViewerTabButton = CreateButton("+ Array", AddViewerTab, false);
                addViewerTabButton.Margin = new Thickness(2, 0, 0, 0);
                viewerTabPanel.Children.Add(addViewerTabButton);
            }
        }

        private void CreateViewerTabHeader(ViewerTab tab)
        {
            var select = CreateButton(tab.Caption, SelectViewerTab, false);
            // A debugger expression is literal text, not a WPF access-key label.
            select.Content = new TextBlock { Text = tab.Caption, TextTrimming = TextTrimming.CharacterEllipsis };
            select.Tag = tab;
            select.Margin = new Thickness(0, 0, 2, 0);
            select.MinWidth = 105;
            select.MaxWidth = 210;
            var close = CreateButton("×", CloseViewerTab, false);
            close.Tag = tab;
            close.Width = 28;
            close.Padding = new Thickness(0, 2, 0, 3);
            var header = new StackPanel { Tag = tab, Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 7, 0) };
            header.Children.Add(select);
            header.Children.Add(close);
            tab.Header = header;
            tab.SelectButton = select;
            tab.CloseButton = close;
        }

        private void AddViewerTab(object sender, RoutedEventArgs e)
        {
            ChangeViewerTab(delegate
            {
                if (tabTrace != null) tabTrace.Write("ACTION Add Array");
                TraceTabStep("CapturePreviousTab", CaptureActiveViewerTab);
                TraceTabStep("CancelPreviousRead", BeginNewReadRequest);
                TraceTabStep("ClearHardwareWatch", delegate { ClearHardwareWatch(false); });
                var copiedSettings = CreateCurrentProfile(String.Empty);
                var tab = new ViewerTab(nextViewerTabId++, copiedSettings);
                viewerTabs.Add(tab);
                activeViewerTab = tab;
                RestoreViewerTab(tab);
                RefreshViewerTabs();
                SetStatus("New array tab created. Choose a pointer or enter an expression, then load its View ROI.");
            });
        }

        private void SelectViewerTab(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var tab = button == null ? null : button.Tag as ViewerTab;
            // A selected (or stale) header is not a transition. In particular,
            // do not touch timers, completion popups or search state on re-click.
            if (tab == null || tab == activeViewerTab || !viewerTabs.Contains(tab)) return;

            ChangeViewerTab(delegate
            {
                if (tabTrace != null) tabTrace.Write("ACTION Select Array");
                TraceTabStep("CapturePreviousTab", CaptureActiveViewerTab);
                TraceTabStep("CancelPreviousRead", BeginNewReadRequest);
                TraceTabStep("ClearHardwareWatch", delegate { ClearHardwareWatch(false); });
                activeViewerTab = tab;
                RestoreViewerTab(tab);
                RefreshViewerTabs();
                SetStatus(tab.Frame == null
                    ? "Switched to " + tab.Caption + ". Load its View ROI when ready."
                    : "Switched to " + tab.Caption + " using its cached ROI; no debugger read was made.");
            });
        }

        private void CloseViewerTab(object sender, RoutedEventArgs e)
        {
            ChangeViewerTab(delegate
            {
                if (tabTrace != null) tabTrace.Write("ACTION Close Array");
                var button = sender as Button;
                var tab = button == null ? null : button.Tag as ViewerTab;
                if (tab == null || viewerTabs.Count <= 1)
                {
                    return;
                }
                var removedIndex = viewerTabs.IndexOf(tab);
                if (tab == activeViewerTab)
                {
                    TraceTabStep("CancelPreviousRead", BeginNewReadRequest);
                    TraceTabStep("ClearHardwareWatch", delegate { ClearHardwareWatch(false); });
                    viewerTabs.Remove(tab);
                    activeViewerTab = viewerTabs[Math.Max(0, removedIndex - 1)];
                    RestoreViewerTab(activeViewerTab);
                }
                else
                {
                    viewerTabs.Remove(tab);
                }
                RefreshViewerTabs();
                SetStatus("Closed " + tab.Caption + ". Cached samples for that tab were discarded.");
            });
        }

        private void CaptureActiveViewerTab()
        {
            if (activeViewerTab == null || isApplyingProfile)
            {
                return;
            }

            activeViewerTab.Profile = CreateCurrentProfile();
            activeViewerTab.SelectedX = currentX;
            activeViewerTab.SelectedY = currentY;
            activeViewerTab.Frame = frame;
            activeViewerTab.Bitmap = image.Source;
            activeViewerTab.Normalization = activeNormalization;
            activeViewerTab.FullFrameWidth = fullFrameWidth;
            activeViewerTab.FullFrameHeight = fullFrameHeight;
            activeViewerTab.ShowsFullFrameContext = showsFullFrameContext;
            activeViewerTab.Zoom = zoom;
            activeViewerTab.HorizontalOffset = scrollViewer.HorizontalOffset;
            activeViewerTab.VerticalOffset = scrollViewer.VerticalOffset;
            activeViewerTab.NavigatorPreview = navigatorPreview.Source;
            activeViewerTab.NavigatorPreviewKey = navigatorPreviewKey;
            activeViewerTab.NavigatorSourceConfiguration = navigatorSourceConfiguration;
        }

        private void RestoreViewerTab(ViewerTab tab)
        {
            if (tabTrace != null) tabTrace.Write("RESTORE id=" + tab.Id + " expression=" + tab.Profile.Expression +
                " type=" + tab.Profile.SourceElementType + " width=" + tab.Profile.Width + " height=" + tab.Profile.Height +
                " zoom=" + tab.Zoom + " frame=" + (tab.Frame == null ? "none" : tab.Frame.Configuration.Width + "x" + tab.Frame.Configuration.Height));
            TraceTabStep("ApplyProfile", delegate { ApplyProfile(tab.Profile); });
            activeNormalization = tab.Normalization;
            navigatorPreview.Source = tab.NavigatorPreview;
            navigatorPreviewKey = tab.NavigatorPreviewKey;
            navigatorSourceConfiguration = tab.NavigatorSourceConfiguration;
            zoom = tab.Zoom > 0 ? tab.Zoom : 0.15;

            if (tab.Frame != null && tab.Bitmap != null)
            {
                TraceTabStep("ApplyCachedFrame", delegate { ApplyFrame(tab.Frame, tab.Bitmap, tab.FullFrameWidth, tab.FullFrameHeight,
                    tab.SelectedX, tab.SelectedY, tab.ShowsFullFrameContext, false); });
                var generation = renderGeneration;
                var offsetX = tab.HorizontalOffset;
                var offsetY = tab.VerticalOffset;
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (activeViewerTab != tab || generation != renderGeneration) return;
                    try
                    {
                        scrollViewer.ScrollToHorizontalOffset(offsetX);
                        scrollViewer.ScrollToVerticalOffset(offsetY);
                    }
                    catch (Exception exception) { SetStatus("Cannot restore array viewport: " + exception.Message); }
                }));
                return;
            }

            frame = null;
            image.Source = null;
            fullFrameWidth = 0;
            fullFrameHeight = 0;
            showsFullFrameContext = false;
            currentX = currentY = -1;
            canvas.Width = 0;
            canvas.Height = 0;
            ClearValueOverlay();
            unloadedFrame.Visibility = Visibility.Collapsed;
            selectedCellRectangle.Visibility = Visibility.Collapsed;
            roiRectangle.Visibility = Visibility.Collapsed;
            emptyState.Visibility = Visibility.Visible;
            UpdateNavigator();
        }

        private sealed class ViewerTab
        {
            public ViewerTab(int idValue, ViewerProfile profileValue)
            {
                Id = idValue;
                Profile = profileValue;
                Normalization = new NormalizationRange(0, 1);
                Zoom = 0.15;
            }

            public int Id { get; private set; }
            public StackPanel Header { get; set; }
            public Button SelectButton { get; set; }
            public Button CloseButton { get; set; }
            public ViewerProfile Profile { get; set; }
            public FrameBuffer Frame { get; set; }
            public int SelectedX { get; set; }
            public int SelectedY { get; set; }
            public ImageSource Bitmap { get; set; }
            public NormalizationRange Normalization { get; set; }
            public int FullFrameWidth { get; set; }
            public int FullFrameHeight { get; set; }
            public bool ShowsFullFrameContext { get; set; }
            public double Zoom { get; set; }
            public double HorizontalOffset { get; set; }
            public double VerticalOffset { get; set; }
            public ImageSource NavigatorPreview { get; set; }
            public string NavigatorPreviewKey { get; set; }
            public FrameConfiguration NavigatorSourceConfiguration { get; set; }

            public string Caption
            {
                get
                {
                    var expression = Profile == null ? String.Empty : Profile.Expression;
                    if (String.IsNullOrWhiteSpace(expression))
                    {
                        return "Array " + Id.ToString(CultureInfo.InvariantCulture);
                    }
                    return CompactTabCaption(expression);
                }
            }
        }

        // Display-only shortening. Never feed this back into the debugger expression.
        private static string CompactTabCaption(string expression)
        {
            var text = expression.Trim().Replace("->", ".").Replace("(", "").Replace(")", "").Replace("&", "");
            var parts = text.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1) text = parts[parts.Length - 2] + "." + parts[parts.Length - 1];
            return text.Length > 32 ? "…" + text.Substring(text.Length - 31) : text;
        }
    }
}
