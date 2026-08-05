using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ArrayImageViewer.Core
{
    internal static class FrameRenderer
    {
        // A 4096 x 3072 BGRA frame is 48 MiB by itself.  Converting it in
        // small row blocks avoids allocating another full-size image buffer.
        private const int RowsPerWrite = 64;

        public static WriteableBitmap Render(FrameBuffer frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException("frame");
            }

            var config = frame.Configuration;
            return Render(frame, new NormalizationRange(config.RawMinimum, config.RawMaximum));
        }

        public static WriteableBitmap Render(FrameBuffer frame, NormalizationRange range)
        {
            if (frame == null)
            {
                throw new ArgumentNullException("frame");
            }

            var config = frame.Configuration;
            var bitmap = new WriteableBitmap(config.Width, config.Height, 96, 96, PixelFormats.Bgra32, null);
            var rowByteCount = checked(config.Width * 4);
            var blockRows = Math.Min(RowsPerWrite, config.Height);
            var pixels = new byte[checked(rowByteCount * blockRows)];
            for (var blockY = 0; blockY < config.Height; blockY += blockRows)
            {
                var rowCount = Math.Min(blockRows, config.Height - blockY);
                var offset = 0;
                for (var y = blockY; y < blockY + rowCount; y++)
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

                bitmap.WritePixels(new System.Windows.Int32Rect(0, blockY, config.Width, rowCount), pixels, rowByteCount, 0);
            }

            bitmap.Freeze();
            return bitmap;
        }

        private static Color GetColor(FrameBuffer frame, int x, int y, long minimum, long maximum)
        {
            var config = frame.Configuration;
            var site = config.GetBayerSite(x, y);
            if (config.VisualizeChannel == VisualizeChannel.Composite)
            {
                return GetCompositeColor(frame, x, y, minimum, maximum);
            }

            if (!BayerLayout.IsVisible(config.VisualizeChannel, site))
            {
                return Colors.Black;
            }

            var value = ToByte(frame.GetRaw(x, y), minimum, maximum);
            if (config.VisualizeChannel == VisualizeChannel.Gray)
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

        private static Color GetCompositeColor(FrameBuffer frame, int x, int y, long minimum, long maximum)
        {
            var red = ToByte(GetNearestSiteValue(frame, x, y, BayerSite.R), minimum, maximum);
            var greenGr = ToByte(GetNearestSiteValue(frame, x, y, BayerSite.Gr), minimum, maximum);
            var greenGb = ToByte(GetNearestSiteValue(frame, x, y, BayerSite.Gb), minimum, maximum);
            var blue = ToByte(GetNearestSiteValue(frame, x, y, BayerSite.B), minimum, maximum);
            return Color.FromRgb(red, (byte)((greenGr + greenGb) / 2), blue);
        }

        private static long GetNearestSiteValue(FrameBuffer frame, int x, int y, BayerSite wantedSite)
        {
            var config = frame.Configuration;
            var maximumRadius = BayerLayout.GetNearestSearchRadius(config.PixelType);
            for (var radius = 0; radius <= maximumRadius; radius++)
            {
                for (var yOffset = -radius; yOffset <= radius; yOffset++)
                {
                    for (var xOffset = -radius; xOffset <= radius; xOffset++)
                    {
                        if (Math.Abs(xOffset) != radius && Math.Abs(yOffset) != radius)
                        {
                            continue;
                        }

                        var sampleX = x + xOffset;
                        var sampleY = y + yOffset;
                        if (sampleX >= 0 && sampleY >= 0 && sampleX < config.Width && sampleY < config.Height &&
                            config.GetBayerSite(sampleX, sampleY) == wantedSite)
                        {
                            return frame.GetRaw(sampleX, sampleY);
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

    }
}
