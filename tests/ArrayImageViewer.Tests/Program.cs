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
                SourceElementTypesPreserveStorageShape();
                SourceElementCodecDecodesSignedAndUnsignedValues();
                StrideAndOriginRemainFullFrameRelative();
                RoiGeometryClampsAndPreservesDragSelection();
                BayerLayoutsRepeatAtExpectedBlockSizes();
                RendererWritesExpectedGrayPixels();
                DisplayNormalizationUsesCachedRawSamples();
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
            Assert(QFormat.TryParse("13.0b", out integerBits, out fractionalBits) && integerBits == 13 && fractionalBits == 0,
                "Zero fractional bits parse.");
            Assert(QFormat.Decode(-3, 1) == -1.5m, "Negative fixed-point values decode exactly.");
            Assert(QFormat.Decode(1, 24) == 0.000000059604644775390625m, "High fractional-bit values stay decimal-exact.");
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

        private static void SourceElementTypesPreserveStorageShape()
        {
            var signed16 = new FrameConfiguration(2, 1, 2, 8, 8, true, PixelOrder.GRFirst, PixelType.Bayer,
                VisualizeChannel.Gray, 0, 0, SourceElementType.Int16);
            var unsigned8 = new FrameConfiguration(2, 1, 2, 8, 0, false, PixelOrder.GRFirst, PixelType.Bayer,
                VisualizeChannel.Gray, 0, 0, SourceElementType.UInt8);
            Assert(signed16.ElementSizeInBytes == 2 && signed16.SourceElementIsSigned, "int16 storage is retained.");
            Assert(unsigned8.ElementSizeInBytes == 1 && !unsigned8.SourceElementIsSigned, "uint8 storage is retained.");
            ExpectArgumentException(delegate
            {
                new FrameConfiguration(1, 1, 1, 8, 0, false, PixelOrder.GRFirst, PixelType.Bayer,
                    VisualizeChannel.Gray, 0, 0, SourceElementType.Int16);
            }, "Mismatched signed source type is rejected.");
        }

        private static void SourceElementCodecDecodesSignedAndUnsignedValues()
        {
            var bytes = new byte[] { 0xFE, 0xFF, 0xFF, 0xFF, 0x7F };
            Assert(SourceElementCodec.DecodeLittleEndian(bytes, 0, SourceElementType.Int8) == -2, "int8 decodes sign.");
            Assert(SourceElementCodec.DecodeLittleEndian(bytes, 0, SourceElementType.UInt8) == 254, "uint8 decodes value.");
            Assert(SourceElementCodec.DecodeLittleEndian(bytes, 0, SourceElementType.Int16) == -2, "int16 decodes sign.");
            Assert(SourceElementCodec.DecodeLittleEndian(bytes, 0, SourceElementType.UInt16) == 65534, "uint16 decodes value.");
            Assert(SourceElementCodec.DecodeLittleEndian(bytes, 0, SourceElementType.Int32) == -2, "int32 decodes sign.");
            Assert(SourceElementCodec.DecodeUInt32(0xffffffffU, SourceElementType.UInt32) == UInt32.MaxValue, "uint32 preserves full range.");
        }

        private static void RoiGeometryClampsAndPreservesDragSelection()
        {
            var topLeft = RoiGeometry.ClampCentered(4096, 3072, 0, 0, 5, 5);
            Assert(topLeft.X == 0 && topLeft.Y == 0 && topLeft.Width == 5 && topLeft.Height == 5, "5x5 ROI clamps at top-left.");
            Assert(topLeft.CenterX == 2 && topLeft.CenterY == 2, "Clamped ROI reports actual center.");
            var bottomRight = RoiGeometry.ClampCentered(4096, 3072, 4095, 3071, 5, 5);
            Assert(bottomRight.X == 4091 && bottomRight.Y == 3067, "5x5 ROI clamps at bottom-right.");
            var full = RoiGeometry.ClampCentered(4096, 3072, 1978, 369, 99999, 99999);
            Assert(full.X == 0 && full.Y == 0 && full.Width == 4096 && full.Height == 3072, "Oversized ROI becomes full frame.");
            var drag = RoiGeometry.FromDrag(4096, 3072, 100, 200, 104, 204);
            Assert(drag.X == 100 && drag.Y == 200 && drag.Width == 5 && drag.Height == 5, "Navigator drag defines inclusive ROI size.");
            Assert(drag.CenterX == 102 && drag.CenterY == 202, "Navigator drag reports ROI center.");
            var evenDrag = RoiGeometry.FromDrag(4096, 3072, 100, 200, 103, 203);
            Assert(evenDrag.CenterX == 102 && evenDrag.CenterY == 202, "Even-sized drag keeps the ROI origin stable after centering.");
            var evenRoundTrip = RoiGeometry.ClampCentered(4096, 3072, evenDrag.CenterX, evenDrag.CenterY, evenDrag.Width, evenDrag.Height);
            Assert(evenRoundTrip.X == 100 && evenRoundTrip.Y == 200, "Even-sized drag survives center-to-ROI round trip.");
            var context = RoiGeometry.CreateContext(4096, 3072, 1978, 369, 5, 5, 16384);
            Assert((long)context.Width * context.Height <= 16384, "Context preview stays within its rendering sample budget.");
            Assert(context.X <= 1978 && context.X + context.Width > 1978 && context.Y <= 369 && context.Y + context.Height > 369,
                "Context preview contains the requested full-frame coordinate.");
            var oversizedContext = RoiGeometry.CreateContext(32, 16, 31, 15, 32, 16, 16);
            Assert(oversizedContext.Width == 32 && oversizedContext.Height == 16 && oversizedContext.X == 0 && oversizedContext.Y == 0,
                "A requested ROI larger than the context budget is preserved rather than clipped.");
        }

        private static void BayerLayoutsRepeatAtExpectedBlockSizes()
        {
            Assert(BayerLayout.GetSite(PixelOrder.GRFirst, PixelType.Bayer, 0, 0) == BayerSite.Gr, "GRBG starts at Gr.");
            Assert(BayerLayout.GetSite(PixelOrder.GRFirst, PixelType.Bayer, 1, 0) == BayerSite.R, "GRBG R is correct.");
            Assert(BayerLayout.GetSite(PixelOrder.GRFirst, PixelType.Bayer, 0, 1) == BayerSite.B, "GRBG B is correct.");
            Assert(BayerLayout.GetSite(PixelOrder.GRFirst, PixelType.Bayer, 1, 1) == BayerSite.Gb, "GRBG Gb is correct.");
            AssertBayerTile(PixelOrder.RFirst, BayerSite.R, BayerSite.Gr, BayerSite.Gb, BayerSite.B, "RGGB");
            AssertBayerTile(PixelOrder.BFirst, BayerSite.B, BayerSite.Gb, BayerSite.Gr, BayerSite.R, "BGGR");
            AssertBayerTile(PixelOrder.GBFirst, BayerSite.Gb, BayerSite.B, BayerSite.R, BayerSite.Gr, "GBRG");
            Assert(BayerLayout.GetSite(PixelOrder.GRFirst, PixelType.Tetra, 2, 0) == BayerSite.R, "Tetra repeats 2x2 sites.");
            Assert(BayerLayout.GetSite(PixelOrder.GRFirst, PixelType.Tetra, 0, 2) == BayerSite.B, "Tetra lower site is correct.");
            Assert(BayerLayout.GetSite(PixelOrder.GRFirst, PixelType.TetraSquare, 4, 0) == BayerSite.R, "TetraSquare repeats 4x4 sites.");
            Assert(BayerLayout.GetSite(PixelOrder.GRFirst, PixelType.TetraSquare, 0, 4) == BayerSite.B, "TetraSquare lower site is correct.");
            Assert(BayerLayout.IsVisible(VisualizeChannel.G, BayerSite.Gr) && BayerLayout.IsVisible(VisualizeChannel.G, BayerSite.Gb),
                "Combined green includes Gr and Gb.");
            Assert(!BayerLayout.IsVisible(VisualizeChannel.Gr, BayerSite.Gb) && BayerLayout.IsVisible(VisualizeChannel.Gr, BayerSite.Gr),
                "Gr and Gb remain individually selectable.");
        }

        private static void AssertBayerTile(PixelOrder order, BayerSite upperLeft, BayerSite upperRight, BayerSite lowerLeft, BayerSite lowerRight, string name)
        {
            Assert(BayerLayout.GetSite(order, PixelType.Bayer, 0, 0) == upperLeft, name + " upper-left is correct.");
            Assert(BayerLayout.GetSite(order, PixelType.Bayer, 1, 0) == upperRight, name + " upper-right is correct.");
            Assert(BayerLayout.GetSite(order, PixelType.Bayer, 0, 1) == lowerLeft, name + " lower-left is correct.");
            Assert(BayerLayout.GetSite(order, PixelType.Bayer, 1, 1) == lowerRight, name + " lower-right is correct.");
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

        private static void DisplayNormalizationUsesCachedRawSamples()
        {
            var frame = new FrameBuffer(Config(2, 1, 2, 8, 0, false, PixelType.Bayer), new long[] { 50, 100 });
            var loadedRange = frame.GetLoadedDataRange();
            Assert(loadedRange.Minimum == 50 && loadedRange.Maximum == 100, "Loaded-data normalization ignores Q-format headroom.");
            var bitmap = FrameRenderer.Render(frame, loadedRange);
            var pixels = new byte[8];
            bitmap.CopyPixels(pixels, 8, 0);
            Assert(pixels[0] == 0 && pixels[4] == 255, "Manual display range recolors cached values without changing samples.");
            ExpectArgumentException(delegate { new NormalizationRange(10, 10); }, "Equal normalization bounds are rejected.");
            var constant = new FrameBuffer(Config(1, 1, 1, 8, 0, false, PixelType.Bayer), new long[] { 42 });
            var constantRange = constant.GetLoadedDataRange();
            Assert(constantRange.Minimum == 42 && constantRange.Maximum == 43, "Constant frames receive a safe display range.");
            var paddedConfig = new FrameConfiguration(2, 2, 3, 8, 0, false, PixelOrder.GRFirst, PixelType.Bayer, VisualizeChannel.Gray);
            var padded = new FrameBuffer(paddedConfig, new long[] { 10, 30, 999, 20, 40, 999 });
            var paddedRange = padded.GetLoadedDataRange();
            Assert(paddedRange.Minimum == 10 && paddedRange.Maximum == 40, "Display normalization ignores row padding samples.");
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
