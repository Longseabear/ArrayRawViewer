using System;

namespace ArrayImageViewer.Core
{
    internal sealed class FrameConfiguration
    {
        public FrameConfiguration(int width, int height, int stride, int fractionalBits, bool isSigned, BayerPattern bayerPattern, DisplayMode displayMode)
        {
            Width = width;
            Height = height;
            Stride = stride;
            FractionalBits = fractionalBits;
            IsSigned = isSigned;
            BayerPattern = bayerPattern;
            DisplayMode = displayMode;
            Validate();
        }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Stride { get; private set; }
        public int FractionalBits { get; private set; }
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

            if (FractionalBits < 0 || FractionalBits > 30)
            {
                throw new ArgumentException("Fractional bits must be between 0 and 30.");
            }

            var samples = RequiredSampleCount;
            if (samples > 16L * 1024L * 1024L)
            {
                throw new ArgumentException("This draft limits a frame to 16,777,216 samples.");
            }
        }
    }
}
