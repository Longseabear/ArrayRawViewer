using System;

namespace ArrayImageViewer.Core
{
    internal enum PixelOrder
    {
        GRFirst,
        RFirst,
        BFirst,
        GBFirst
    }

    internal enum PixelType
    {
        Bayer,
        Tetra,
        TetraSquare
    }

    internal enum BayerSite
    {
        Mono,
        R,
        Gr,
        Gb,
        B
    }

    internal enum VisualizeChannel
    {
        Gray,
        BayerRaw,
        Composite,
        R,
        G,
        Gr,
        Gb,
        B
    }

    internal static class BayerLayout
    {
        public static BayerSite GetSite(PixelOrder order, PixelType pixelType, int x, int y)
        {
            if (pixelType == PixelType.Tetra)
            {
                x /= 2;
                y /= 2;
            }

            if (pixelType == PixelType.TetraSquare)
            {
                x /= 4;
                y /= 4;
            }

            var evenX = (x & 1) == 0;
            var evenY = (y & 1) == 0;
            switch (order)
            {
                case PixelOrder.GRFirst:
                    return GetGrbgSite(x, y);
                case PixelOrder.RFirst:
                    return evenY ? (evenX ? BayerSite.R : BayerSite.Gr) : (evenX ? BayerSite.Gb : BayerSite.B);
                case PixelOrder.BFirst:
                    return evenY ? (evenX ? BayerSite.B : BayerSite.Gb) : (evenX ? BayerSite.Gr : BayerSite.R);
                case PixelOrder.GBFirst:
                    return evenY ? (evenX ? BayerSite.Gb : BayerSite.B) : (evenX ? BayerSite.R : BayerSite.Gr);
                default:
                    throw new ArgumentOutOfRangeException("order");
            }
        }

        public static int GetNearestSearchRadius(PixelType pixelType)
        {
            switch (pixelType)
            {
                case PixelType.Tetra:
                    return 2;
                case PixelType.TetraSquare:
                    return 4;
                default:
                    return 2;
            }
        }

        private static BayerSite GetGrbgSite(int x, int y)
        {
            var evenX = (x & 1) == 0;
            var evenY = (y & 1) == 0;
            return evenY ? (evenX ? BayerSite.Gr : BayerSite.R) : (evenX ? BayerSite.B : BayerSite.Gb);
        }

        public static bool IsVisible(VisualizeChannel channel, BayerSite site)
        {
            switch (channel)
            {
                case VisualizeChannel.Gray:
                case VisualizeChannel.BayerRaw:
                case VisualizeChannel.Composite:
                    return true;
                case VisualizeChannel.R:
                    return site == BayerSite.R;
                case VisualizeChannel.G:
                    return site == BayerSite.Gr || site == BayerSite.Gb;
                case VisualizeChannel.Gr:
                    return site == BayerSite.Gr;
                case VisualizeChannel.Gb:
                    return site == BayerSite.Gb;
                case VisualizeChannel.B:
                    return site == BayerSite.B;
                default:
                    return false;
            }
        }
    }
}
