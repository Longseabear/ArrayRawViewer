using System;
using System.Threading;

namespace ArrayImageViewer.Core
{
    internal sealed class SampleStatistics
    {
        private long count;
        private long minimum = Int64.MaxValue;
        private long maximum = Int64.MinValue;
        private double mean;
        private double sumOfSquaredDifferences;

        public long Count { get { return count; } }
        public long Minimum { get { return count == 0 ? 0 : minimum; } }
        public long Maximum { get { return count == 0 ? 0 : maximum; } }
        public double Mean { get { return mean; } }
        public double StandardDeviation { get { return count == 0 ? 0 : Math.Sqrt(sumOfSquaredDifferences / count); } }

        public void Add(long value)
        {
            count++;
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
            var delta = value - mean;
            mean += delta / count;
            sumOfSquaredDifferences += delta * (value - mean);
        }
    }

    internal sealed class FrameStatistics
    {
        public FrameStatistics()
        {
            Visible = new SampleStatistics();
            R = new SampleStatistics();
            Gr = new SampleStatistics();
            Gb = new SampleStatistics();
            B = new SampleStatistics();
        }

        public SampleStatistics Visible { get; private set; }
        public SampleStatistics R { get; private set; }
        public SampleStatistics Gr { get; private set; }
        public SampleStatistics Gb { get; private set; }
        public SampleStatistics B { get; private set; }
    }

    internal static class FrameStatisticsCalculator
    {
        public static FrameStatistics Calculate(FrameBuffer frame, int x, int y, int width, int height, VisualizeChannel visibleFilter)
        {
            return Calculate(frame, x, y, width, height, visibleFilter, CancellationToken.None);
        }

        public static FrameStatistics Calculate(FrameBuffer frame, int x, int y, int width, int height, VisualizeChannel visibleFilter, CancellationToken cancellationToken)
        {
            if (frame == null)
            {
                throw new ArgumentNullException("frame");
            }
            if (x < 0 || y < 0 || width <= 0 || height <= 0 || x > frame.Configuration.Width - width || y > frame.Configuration.Height - height)
            {
                throw new ArgumentOutOfRangeException("The statistics rectangle is outside the loaded frame.");
            }

            var result = new FrameStatistics();
            for (var localY = y; localY < y + height; localY++)
            {
                for (var localX = x; localX < x + width; localX++)
                {
                    if (((localX - x) & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    var value = frame.GetRaw(localX, localY);
                    var site = frame.Configuration.GetBayerSite(localX, localY);
                    if (BayerLayout.IsVisible(visibleFilter, site))
                    {
                        result.Visible.Add(value);
                    }

                    switch (site)
                    {
                        case BayerSite.R: result.R.Add(value); break;
                        case BayerSite.Gr: result.Gr.Add(value); break;
                        case BayerSite.Gb: result.Gb.Add(value); break;
                        case BayerSite.B: result.B.Add(value); break;
                    }
                }
            }

            return result;
        }
    }
}
