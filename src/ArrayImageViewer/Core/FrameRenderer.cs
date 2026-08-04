using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ArrayImageViewer.Core
{
    internal static class FrameRenderer
    {
        public static WriteableBitmap Render(FrameBuffer frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException("frame");
            }

            var config = frame.Configuration;
            var range = FindRange(frame, config.DisplayMode);
            var pixels = new byte[checked(config.Width * config.Height * 4)];
            var offset = 0;

            for (var y = 0; y < config.Height; y++)
            {
                for (var x = 0; x < config.Width; x++)
                {
                    var color = GetColor(frame, x, y, range.Minimum, range.Maximum);
                    pixels[offset++] = color.B;
                    pixels[offset++] = color.G;
                    pixels[offset++] = color.R;
                    pixels[offset++] = 255;
                }
            }

            var bitmap = new WriteableBitmap(config.Width, config.Height, 96, 96, PixelFormats.Bgra32, null);
            bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, config.Width, config.Height), pixels, config.Width * 4, 0);
            bitmap.Freeze();
            return bitmap;
        }

        private static Color GetColor(FrameBuffer frame, int x, int y, long minimum, long maximum)
        {
            var config = frame.Configuration;
            var site = BayerLayout.GetSite(config.BayerPattern, x, y);
            if (config.DisplayMode == DisplayMode.Composite && config.BayerPattern != BayerPattern.None)
            {
                return Color.FromRgb(
                    ToByte(FindNearest(frame, x, y, BayerSite.R), minimum, maximum),
                    ToByte((FindNearest(frame, x, y, BayerSite.Gr) + FindNearest(frame, x, y, BayerSite.Gb)) / 2, minimum, maximum),
                    ToByte(FindNearest(frame, x, y, BayerSite.B), minimum, maximum));
            }

            if (!BayerLayout.IsVisible(config.DisplayMode, site))
            {
                return Colors.Black;
            }

            var value = ToByte(frame.GetRaw(x, y), minimum, maximum);
            if (config.DisplayMode == DisplayMode.Raw || config.BayerPattern == BayerPattern.None)
            {
                return Color.FromRgb(value, value, value);
            }

            switch (site)
            {
                case BayerSite.R:
                    return Color.FromRgb(value, 0, 0);
                case BayerSite.Gr:
                case BayerSite.Gb:
                    return Color.FromRgb(0, value, 0);
                case BayerSite.B:
                    return Color.FromRgb(0, 0, value);
                default:
                    return Color.FromRgb(value, value, value);
            }
        }

        private static Range FindRange(FrameBuffer frame, DisplayMode displayMode)
        {
            var minimum = long.MaxValue;
            var maximum = long.MinValue;
            var config = frame.Configuration;
            for (var y = 0; y < config.Height; y++)
            {
                for (var x = 0; x < config.Width; x++)
                {
                    var site = BayerLayout.GetSite(config.BayerPattern, x, y);
                    if (!BayerLayout.IsVisible(displayMode, site))
                    {
                        continue;
                    }

                    var value = frame.GetRaw(x, y);
                    minimum = Math.Min(minimum, value);
                    maximum = Math.Max(maximum, value);
                }
            }

            return minimum == long.MaxValue ? new Range(0, 1) : new Range(minimum, maximum);
        }

        private static long FindNearest(FrameBuffer frame, int x, int y, BayerSite target)
        {
            var config = frame.Configuration;
            for (var distance = 0; distance <= 2; distance++)
            {
                for (var offsetY = -distance; offsetY <= distance; offsetY++)
                {
                    for (var offsetX = -distance; offsetX <= distance; offsetX++)
                    {
                        var candidateX = x + offsetX;
                        var candidateY = y + offsetY;
                        if (candidateX < 0 || candidateY < 0 || candidateX >= config.Width || candidateY >= config.Height)
                        {
                            continue;
                        }

                        if (BayerLayout.GetSite(config.BayerPattern, candidateX, candidateY) == target)
                        {
                            return frame.GetRaw(candidateX, candidateY);
                        }
                    }
                }
            }

            return frame.GetRaw(x, y);
        }

        private static byte ToByte(long value, long minimum, long maximum)
        {
            if (maximum <= minimum)
            {
                return 0;
            }

            var normalized = (value - minimum) / (double)(maximum - minimum);
            return (byte)Math.Max(0, Math.Min(255, Math.Round(normalized * 255)));
        }

        private struct Range
        {
            public Range(long minimum, long maximum)
            {
                Minimum = minimum;
                Maximum = maximum;
            }

            public long Minimum;
            public long Maximum;
        }
    }
}
