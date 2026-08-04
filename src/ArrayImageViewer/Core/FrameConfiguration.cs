using System;

namespace ArrayImageViewer.Core
{
    internal sealed class FrameConfiguration
    {
        public FrameConfiguration(int width, int height, int stride, int integerBits, int fractionalBits, bool isSigned, BayerPattern bayerPattern, DisplayMode displayMode)
        {
            Width = width;
            Height = height;
            Stride = stride;
            IntegerBits = integerBits;
            FractionalBits = fractionalBits;
            IsSigned = isSigned;
            BayerPattern = bayerPattern;
            DisplayMode = displayMode;
            Validate();
        }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Stride { get; private set; }
        public int IntegerBits { get; private set; }
        public int FractionalBits { get; private set; }
        public int TotalBits { get { return IntegerBits + FractionalBits; } }
        public bool IsSigned { get; private set; }
        public BayerPattern BayerPattern { get; private set; }
        public DisplayMode DisplayMode { get; private set; }

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

            if (Stride < Width)
            {
                throw new ArgumentException("Stride must be at least the image width.");
            }

            if (IntegerBits < 0 || FractionalBits < 0 || IntegerBits > 32 || FractionalBits > 32 ||
                IntegerBits > 32 - FractionalBits)
            {
                throw new ArgumentException("Q format must contain 1 to 32 total bits.");
            }

            var samples = RequiredSampleCount;
            if (samples > 16L * 1024L * 1024L)
            {
                throw new ArgumentException("This draft limits a frame to 16,777,216 samples.");
            }
        }
    }
}
