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
