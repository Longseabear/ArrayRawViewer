using System;

namespace ArrayImageViewer.Core
{
    internal enum SourceElementType
    {
        Int8,
        UInt8,
        Int16,
        UInt16,
        Int32,
        UInt32
    }

    internal enum NormalizationMode
    {
        QFormatRange,
        LoadedDataRange,
        ManualRawRange
    }

    internal struct NormalizationRange
    {
        public NormalizationRange(long minimum, long maximum)
        {
            if (maximum <= minimum)
            {
                throw new ArgumentException("Normalization maximum must be greater than its minimum.");
            }

            Minimum = minimum;
            Maximum = maximum;
        }

        public long Minimum;
        public long Maximum;
    }

    internal static class SourceElementCodec
    {
        public static long DecodeLittleEndian(byte[] bytes, int offset, SourceElementType sourceElementType)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException("bytes");
            }

            var requiredBytes = GetSizeInBytes(sourceElementType);
            if (offset < 0 || offset > bytes.Length - requiredBytes)
            {
                throw new ArgumentOutOfRangeException("offset");
            }

            switch (sourceElementType)
            {
                case SourceElementType.Int8:
                    return unchecked((sbyte)bytes[offset]);
                case SourceElementType.UInt8:
                    return bytes[offset];
                case SourceElementType.Int16:
                    return unchecked((short)(bytes[offset] | (bytes[offset + 1] << 8)));
                case SourceElementType.UInt16:
                    return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
                default:
                    var value = (uint)(bytes[offset] |
                        (bytes[offset + 1] << 8) |
                        (bytes[offset + 2] << 16) |
                        (bytes[offset + 3] << 24));
                    return sourceElementType == SourceElementType.Int32 ? unchecked((int)value) : (long)value;
            }
        }

        public static long DecodeUInt32(uint value, SourceElementType sourceElementType)
        {
            switch (sourceElementType)
            {
                case SourceElementType.Int8: return unchecked((sbyte)value);
                case SourceElementType.UInt8: return (byte)value;
                case SourceElementType.Int16: return unchecked((short)value);
                case SourceElementType.UInt16: return (ushort)value;
                case SourceElementType.Int32: return unchecked((int)value);
                default: return value;
            }
        }

        public static int GetSizeInBytes(SourceElementType sourceElementType)
        {
            switch (sourceElementType)
            {
                case SourceElementType.Int8:
                case SourceElementType.UInt8:
                    return 1;
                case SourceElementType.Int16:
                case SourceElementType.UInt16:
                    return 2;
                default:
                    return 4;
            }
        }
    }

    internal struct RoiRectangle
    {
        public RoiRectangle(int x, int y, int width, int height, int centerX, int centerY)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            CenterX = centerX;
            CenterY = centerY;
        }

        public int X;
        public int Y;
        public int Width;
        public int Height;
        public int CenterX;
        public int CenterY;
    }

    internal static class RoiGeometry
    {
        public const int InteractiveSampleLimit = 262144;

        public static RoiRectangle LimitView(int frameWidth, int frameHeight, int centerX, int centerY, int width, int height)
        {
            if (width <= 0 || height <= 0 || frameWidth <= 0 || frameHeight <= 0)
                throw new ArgumentException("Frame and view dimensions must be positive.");
            width = Math.Min(width, frameWidth);
            height = Math.Min(height, frameHeight);
            if ((long)width * height > InteractiveSampleLimit)
            {
                double scale = Math.Sqrt(InteractiveSampleLimit / ((double)width * height));
                width = Math.Max(1, (int)Math.Floor(width * scale));
                height = Math.Max(1, (int)Math.Floor(height * scale));
                width = Math.Min(width, InteractiveSampleLimit);
                height = Math.Min(height, InteractiveSampleLimit / width);
            }
            return ClampCentered(frameWidth, frameHeight, centerX, centerY, width, height);
        }

        public static RoiRectangle CreateContext(int frameWidth, int frameHeight, int requestedCenterX, int requestedCenterY,
            int minimumWidth, int minimumHeight, int sampleLimit)
        {
            if (frameWidth <= 0 || frameHeight <= 0 || minimumWidth <= 0 || minimumHeight <= 0 || sampleLimit <= 0)
            {
                throw new ArgumentException("Frame, ROI, and context dimensions must be positive.");
            }

            var requiredWidth = Math.Min(frameWidth, minimumWidth);
            var requiredHeight = Math.Min(frameHeight, minimumHeight);
            if ((long)requiredWidth * requiredHeight >= sampleLimit)
            {
                return ClampCentered(frameWidth, frameHeight, requestedCenterX, requestedCenterY, requiredWidth, requiredHeight);
            }

            var aspectRatio = frameWidth / (double)frameHeight;
            var targetWidth = (int)Math.Floor(Math.Sqrt(sampleLimit * aspectRatio));
            targetWidth = Math.Max(requiredWidth, Math.Min(frameWidth, targetWidth));
            var targetHeight = Math.Max(requiredHeight, Math.Min(frameHeight, sampleLimit / targetWidth));
            targetWidth = Math.Max(requiredWidth, Math.Min(frameWidth, sampleLimit / targetHeight));
            return ClampCentered(frameWidth, frameHeight, requestedCenterX, requestedCenterY, targetWidth, targetHeight);
        }

        public static RoiRectangle ClampCentered(int frameWidth, int frameHeight, int requestedCenterX, int requestedCenterY,
            int requestedWidth, int requestedHeight)
        {
            if (frameWidth <= 0 || frameHeight <= 0 || requestedWidth <= 0 || requestedHeight <= 0)
            {
                throw new ArgumentException("Frame and ROI dimensions must be positive.");
            }

            var actualWidth = Math.Min(requestedWidth, frameWidth);
            var actualHeight = Math.Min(requestedHeight, frameHeight);
            var centerX = Math.Max(0, Math.Min(frameWidth - 1, requestedCenterX));
            var centerY = Math.Max(0, Math.Min(frameHeight - 1, requestedCenterY));
            var x = Math.Max(0, Math.Min(frameWidth - actualWidth, centerX - actualWidth / 2));
            var y = Math.Max(0, Math.Min(frameHeight - actualHeight, centerY - actualHeight / 2));
            return new RoiRectangle(x, y, actualWidth, actualHeight, x + actualWidth / 2, y + actualHeight / 2);
        }

        // Display-only geometry. Memory reads must use ClampCentered so no
        // address outside the debuggee buffer is ever requested.
        public static RoiRectangle CenteredUnclipped(int centerX, int centerY, int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentException("ROI dimensions must be positive.");
            }

            return new RoiRectangle(centerX - width / 2, centerY - height / 2,
                width, height, centerX, centerY);
        }

        public static RoiRectangle FromDrag(int frameWidth, int frameHeight, int startX, int startY, int endX, int endY)
        {
            if (frameWidth <= 0 || frameHeight <= 0)
            {
                throw new ArgumentException("Frame dimensions must be positive.");
            }

            var left = Math.Max(0, Math.Min(frameWidth - 1, Math.Min(startX, endX)));
            var top = Math.Max(0, Math.Min(frameHeight - 1, Math.Min(startY, endY)));
            var right = Math.Max(0, Math.Min(frameWidth - 1, Math.Max(startX, endX)));
            var bottom = Math.Max(0, Math.Min(frameHeight - 1, Math.Max(startY, endY)));
            var width = right - left + 1;
            var height = bottom - top + 1;
            return new RoiRectangle(left, top, width, height, left + width / 2, top + height / 2);
        }
    }

    internal sealed class FrameConfiguration
    {
        public FrameConfiguration(int width, int height, int stride, int integerBits, int fractionalBits, bool isSigned, PixelOrder pixelOrder, PixelType pixelType, VisualizeChannel visualizeChannel)
            : this(width, height, stride, integerBits, fractionalBits, isSigned, pixelOrder, pixelType, visualizeChannel, 0, 0)
        {
        }

        public FrameConfiguration(int width, int height, int stride, int integerBits, int fractionalBits, bool isSigned, PixelOrder pixelOrder, PixelType pixelType, VisualizeChannel visualizeChannel, int originX, int originY)
            : this(width, height, stride, integerBits, fractionalBits, isSigned, pixelOrder, pixelType, visualizeChannel, originX, originY,
                isSigned ? SourceElementType.Int32 : SourceElementType.UInt32)
        {
        }

        public FrameConfiguration(int width, int height, int stride, int integerBits, int fractionalBits, bool isSigned, PixelOrder pixelOrder, PixelType pixelType,
            VisualizeChannel visualizeChannel, int originX, int originY, SourceElementType sourceElementType)
        {
            Width = width;
            Height = height;
            Stride = stride;
            IntegerBits = integerBits;
            FractionalBits = fractionalBits;
            IsSigned = isSigned;
            PixelOrder = pixelOrder;
            PixelType = pixelType;
            VisualizeChannel = visualizeChannel;
            OriginX = originX;
            OriginY = originY;
            SourceElementType = sourceElementType;
            Validate();
        }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Stride { get; private set; }
        public int IntegerBits { get; private set; }
        public int FractionalBits { get; private set; }
        public int TotalBits { get { return IntegerBits + FractionalBits; } }
        public bool IsSigned { get; private set; }
        public PixelOrder PixelOrder { get; private set; }
        public PixelType PixelType { get; private set; }
        public VisualizeChannel VisualizeChannel { get; private set; }
        public int OriginX { get; private set; }
        public int OriginY { get; private set; }
        public SourceElementType SourceElementType { get; private set; }

        public int ElementSizeInBytes
        {
            get { return SourceElementCodec.GetSizeInBytes(SourceElementType); }
        }

        public bool SourceElementIsSigned
        {
            get
            {
                return SourceElementType == SourceElementType.Int8 || SourceElementType == SourceElementType.Int16 ||
                    SourceElementType == SourceElementType.Int32;
            }
        }

        public long RequiredSampleCount
        {
            get { return checked(((long)Height - 1L) * Stride + Width); }
        }

        public long GetSampleIndex(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
            {
                throw new ArgumentOutOfRangeException("Frame coordinate is outside the image.");
            }

            return checked((long)y * Stride + x);
        }

        public BayerSite GetBayerSite(int x, int y)
        {
            return BayerLayout.GetSite(PixelOrder, PixelType, checked(OriginX + x), checked(OriginY + y));
        }

        public long RawMinimum
        {
            get { return IsSigned ? -(1L << (TotalBits - 1)) : 0L; }
        }

        public long RawMaximum
        {
            get { return IsSigned ? (1L << (TotalBits - 1)) - 1L : (1L << TotalBits) - 1L; }
        }

        public long NormalizeRawValue(long value)
        {
            var mask = TotalBits == 32 ? 0xffffffffL : (1L << TotalBits) - 1L;
            var normalized = value & mask;
            if (IsSigned && (normalized & (1L << (TotalBits - 1))) != 0)
            {
                normalized -= 1L << TotalBits;
            }

            return normalized;
        }

        private void Validate()
        {
            if (Width <= 0 || Height <= 0)
            {
                throw new ArgumentException("Width and height must both be positive.");
            }

            if (OriginX < 0 || OriginY < 0)
            {
                throw new ArgumentException("Frame origin cannot be negative.");
            }

            if (Stride < Width)
            {
                throw new ArgumentException("Stride must be at least the image width.");
            }

            if (IntegerBits < 0 || FractionalBits < 0 || TotalBits == 0 || IntegerBits > 32 || FractionalBits > 32 ||
                IntegerBits > 32 - FractionalBits)
            {
                throw new ArgumentException("Q format must contain 1 to 32 total bits.");
            }

            if (IsSigned != SourceElementIsSigned)
            {
                throw new ArgumentException("The signed interpretation must match the selected source element type.");
            }

            var samples = RequiredSampleCount;
            if (samples > 16L * 1024L * 1024L)
            {
                throw new ArgumentException("This draft limits a frame to 16,777,216 samples.");
            }
        }
    }
}
