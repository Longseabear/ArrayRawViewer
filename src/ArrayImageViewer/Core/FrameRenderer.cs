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
                if (config.VisualizeChannel == VisualizeChannel.Composite)
                {
                    RenderCompositeBlock(frame, range.Minimum, range.Maximum, blockY, rowCount, pixels);
                }
                else
                {
                    RenderDirectBlock(frame, range.Minimum, range.Maximum, blockY, rowCount, pixels);
                }

                bitmap.WritePixels(new System.Windows.Int32Rect(0, blockY, config.Width, rowCount), pixels, rowByteCount, 0);
            }

            bitmap.Freeze();
            return bitmap;
        }

        private static void RenderDirectBlock(FrameBuffer frame, long minimum, long maximum, int blockY, int rowCount, byte[] pixels)
        {
            var config = frame.Configuration;
            var samples = frame.RawSamples;
            var mask = config.TotalBits == 32 ? 0xffffffffL : (1L << config.TotalBits) - 1L;
            var signBit = 1L << (config.TotalBits - 1);
            var scale = maximum <= minimum ? 0.0 : 255.0 / (maximum - minimum);
            var offset = 0;
            for (var y = blockY; y < blockY + rowCount; y++)
            {
                var sourceOffset = y * config.Stride;
                for (var x = 0; x < config.Width; x++)
                {
                    var raw = samples[sourceOffset + x] & mask;
                    if (config.IsSigned && (raw & signBit) != 0)
                    {
                        raw -= 1L << config.TotalBits;
                    }

                    var value = ToByte(raw, minimum, maximum, scale);
                    if (config.VisualizeChannel == VisualizeChannel.Gray)
                    {
                        pixels[offset++] = value;
                        pixels[offset++] = value;
                        pixels[offset++] = value;
                        pixels[offset++] = 255;
                        continue;
                    }

                    var site = BayerLayout.GetSite(config.PixelOrder, config.PixelType, config.OriginX + x, config.OriginY + y);
                    if (!BayerLayout.IsVisible(config.VisualizeChannel, site))
                    {
                        pixels[offset++] = 0;
                        pixels[offset++] = 0;
                        pixels[offset++] = 0;
                        pixels[offset++] = 255;
                        continue;
                    }

                    switch (site)
                    {
                        case BayerSite.R:
                            pixels[offset++] = 0;
                            pixels[offset++] = 0;
                            pixels[offset++] = value;
                            break;
                        case BayerSite.Gr:
                        case BayerSite.Gb:
                            pixels[offset++] = 0;
                            pixels[offset++] = value;
                            pixels[offset++] = 0;
                            break;
                        case BayerSite.B:
                            pixels[offset++] = value;
                            pixels[offset++] = 0;
                            pixels[offset++] = 0;
                            break;
                        default:
                            pixels[offset++] = value;
                            pixels[offset++] = value;
                            pixels[offset++] = value;
                            break;
                    }

                    pixels[offset++] = 255;
                }
            }
        }

        private static void RenderCompositeBlock(FrameBuffer frame, long minimum, long maximum, int blockY, int rowCount, byte[] pixels)
        {
            var offset = 0;
            var config = frame.Configuration;
            for (var y = blockY; y < blockY + rowCount; y++)
            {
                for (var x = 0; x < config.Width; x++)
                {
                    var color = GetCompositeColor(frame, x, y, minimum, maximum);
                    pixels[offset++] = color.B;
                    pixels[offset++] = color.G;
                    pixels[offset++] = color.R;
                    pixels[offset++] = 255;
                }
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

        private static byte ToByte(long value, long minimum, long maximum, double scale)
        {
            if (maximum <= minimum)
            {
                return 0;
            }

            return (byte)Math.Max(0, Math.Min(255, Math.Round((value - minimum) * scale)));
        }

    }
}
