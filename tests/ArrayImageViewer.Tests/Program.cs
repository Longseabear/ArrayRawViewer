using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Media.Imaging;
using ArrayImageViewer.Core;

namespace ArrayImageViewer.Tests
{
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            try
            {
                QFormatAndRangeAreExact();
                StrideAndOriginRemainFullFrameRelative();
                BayerLayoutsRepeatAtExpectedBlockSizes();
                RendererWritesExpectedGrayPixels();
                FrozenRendererResultCrossesStaThread();
                FullFrameRendererCompletes();
                FrameLimitRejectsUnsafeRequests();
                Console.WriteLine("ArrayImageViewer core checks passed.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static void QFormatAndRangeAreExact()
        {
            int integerBits;
            int fractionalBits;
            Assert(QFormat.TryParse("8.8b", out integerBits, out fractionalBits), "8.8b parses.");
            Assert(integerBits == 8 && fractionalBits == 8, "Q bits are retained.");
            Assert(QFormat.Decode(384, 8) == 1.5m, "Fractional value decodes exactly.");
            var signed32 = Config(1, 1, 1, 32, 0, true, PixelType.Bayer);
            var unsigned32 = Config(1, 1, 1, 32, 0, false, PixelType.Bayer);
            Assert(signed32.RawMinimum == Int32.MinValue && signed32.RawMaximum == Int32.MaxValue, "Signed range is exact.");
            Assert(unsigned32.RawMinimum == 0 && unsigned32.RawMaximum == UInt32.MaxValue, "Unsigned range is exact.");
        }

        private static void StrideAndOriginRemainFullFrameRelative()
        {
            var config = new FrameConfiguration(3, 2, 5, 8, 0, false, PixelOrder.GRFirst, PixelType.Bayer, VisualizeChannel.Gray, 3, 2);
            var frame = new FrameBuffer(config, new long[] { 0, 1, 2, 99, 99, 5, 6, 7, 99, 99 });
            Assert(frame.GetRaw(2, 1) == 7, "Padding is not a pixel.");
            Assert(config.GetBayerSite(0, 0) == BayerSite.R, "ROI origin preserves GRBG phase.");
        }

        private static void BayerLayoutsRepeatAtExpectedBlockSizes()
        {
            Assert(BayerLayout.GetSite(PixelOrder.GRFirst, PixelType.Bayer, 0, 0) == BayerSite.Gr, "GRBG starts at Gr.");
            Assert(BayerLayout.GetSite(PixelOrder.GRFirst, PixelType.Bayer, 1, 0) == BayerSite.R, "GRBG R is correct.");
            Assert(BayerLayout.GetSite(PixelOrder.GRFirst, PixelType.Tetra, 2, 0) == BayerSite.R, "Tetra repeats 2x2 sites.");
            Assert(BayerLayout.GetSite(PixelOrder.GRFirst, PixelType.Tetra, 0, 2) == BayerSite.B, "Tetra lower site is correct.");
            Assert(BayerLayout.GetSite(PixelOrder.GRFirst, PixelType.TetraSquare, 4, 0) == BayerSite.R, "TetraSquare repeats 4x4 sites.");
            Assert(BayerLayout.GetSite(PixelOrder.GRFirst, PixelType.TetraSquare, 0, 4) == BayerSite.B, "TetraSquare lower site is correct.");
        }

        private static void RendererWritesExpectedGrayPixels()
        {
            var frame = new FrameBuffer(Config(2, 2, 2, 8, 0, false, PixelType.Bayer), new long[] { 0, 255, 128, 64 });
            var bitmap = FrameRenderer.Render(frame);
            var pixels = new byte[16];
            bitmap.CopyPixels(pixels, 8, 0);
            Assert(pixels[0] == 0 && pixels[1] == 0 && pixels[2] == 0 && pixels[3] == 255, "Black BGRA pixel is correct.");
            Assert(pixels[4] == 255 && pixels[5] == 255 && pixels[6] == 255 && pixels[7] == 255, "White BGRA pixel is correct.");
        }

        private static void FullFrameRendererCompletes()
        {
            // Mirrors the common sensor size.  Gray avoids the deliberately
            // more expensive composite preview, and no text is rendered by
            // FrameRenderer at any scale.
            var stopwatch = Stopwatch.StartNew();
            var config = Config(4096, 3072, 4096, 8, 8, false, PixelType.Bayer);
            var bitmap = FrameRenderer.Render(FrameBuffer.CreateSynthetic(config));
            stopwatch.Stop();
            Assert(bitmap.PixelWidth == 4096 && bitmap.PixelHeight == 3072, "4096x3072 preview renders at full resolution.");
            Console.WriteLine("4096x3072 Gray render: " + stopwatch.ElapsedMilliseconds + " ms");
        }

        private static void FrozenRendererResultCrossesStaThread()
        {
            WriteableBitmap result = null;
            Exception error = null;
            var worker = new Thread(delegate()
            {
                try
                {
                    result = FrameRenderer.Render(new FrameBuffer(Config(2, 2, 2, 8, 0, false, PixelType.Bayer), new long[] { 0, 1, 2, 3 }));
                }
                catch (Exception exception)
                {
                    error = exception;
                }
            });
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            worker.Join();
            if (error != null) throw error;
            Assert(result != null && result.IsFrozen, "Background STA renderer returns a frozen bitmap.");
            var pixels = new byte[16];
            result.CopyPixels(pixels, 8, 0);
        }

        private static void FrameLimitRejectsUnsafeRequests()
        {
            ExpectArgumentException(delegate { Config(Int32.MaxValue, 2, Int32.MaxValue, 8, 0, false, PixelType.Bayer); }, "Unsafe frame request is rejected.");
            ExpectArgumentException(delegate { Config(1, 1, 1, 0, 0, false, PixelType.Bayer); }, "0.0b is rejected.");
        }

        private static FrameConfiguration Config(int width, int height, int stride, int integerBits, int fractionalBits, bool isSigned, PixelType pixelType)
        {
            return new FrameConfiguration(width, height, stride, integerBits, fractionalBits, isSigned, PixelOrder.GRFirst, pixelType, VisualizeChannel.Gray);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void ExpectArgumentException(Action action, string message)
        {
            try { action(); }
            catch (ArgumentException) { return; }
            throw new InvalidOperationException(message);
        }
    }
}
