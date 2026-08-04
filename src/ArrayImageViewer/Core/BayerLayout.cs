using System;

namespace ArrayImageViewer.Core
{
    internal enum BayerPattern
    {
        Gray,
        GRBG,
        Tetra,
        TetraSquare,
        RGGB,
        GBRG,
        BGGR
    }

    internal enum BayerSite
    {
        Mono,
        R,
        Gr,
        Gb,
        B
    }

    internal enum DisplayMode
    {
        Raw,
        Mosaic,
        Composite,
        R,
        G,
        Gr,
        Gb,
        B
    }

    internal static class BayerLayout
    {
        public static BayerSite GetSite(BayerPattern pattern, int x, int y)
        {
            if (pattern == BayerPattern.Gray)
            {
                return BayerSite.Mono;
            }

            if (pattern == BayerPattern.Tetra)
            {
                return GetGrbgSite(x / 2, y / 2);
            }

            if (pattern == BayerPattern.TetraSquare)
            {
                return GetGrbgSite(x / 4, y / 4);
            }

            var evenX = (x & 1) == 0;
            var evenY = (y & 1) == 0;
            switch (pattern)
            {
                case BayerPattern.GRBG:
                    return GetGrbgSite(x, y);
                case BayerPattern.RGGB:
                    return evenY ? (evenX ? BayerSite.R : BayerSite.Gr) : (evenX ? BayerSite.Gb : BayerSite.B);
                case BayerPattern.GBRG:
                    return evenY ? (evenX ? BayerSite.Gb : BayerSite.B) : (evenX ? BayerSite.R : BayerSite.Gr);
                case BayerPattern.BGGR:
                    return evenY ? (evenX ? BayerSite.B : BayerSite.Gb) : (evenX ? BayerSite.Gr : BayerSite.R);
                default:
                    throw new ArgumentOutOfRangeException("pattern");
            }
        }

        public static int GetNearestSearchRadius(BayerPattern pattern)
        {
            switch (pattern)
            {
                case BayerPattern.Tetra:
                    return 2;
                case BayerPattern.TetraSquare:
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

        public static bool IsVisible(DisplayMode mode, BayerSite site)
        {
            switch (mode)
            {
                case DisplayMode.Raw:
                case DisplayMode.Mosaic:
                case DisplayMode.Composite:
                    return true;
                case DisplayMode.R:
                    return site == BayerSite.R;
                case DisplayMode.G:
                    return site == BayerSite.Gr || site == BayerSite.Gb;
                case DisplayMode.Gr:
                    return site == BayerSite.Gr;
                case DisplayMode.Gb:
                    return site == BayerSite.Gb;
                case DisplayMode.B:
                    return site == BayerSite.B;
                default:
                    return false;
            }
        }
    }
}
