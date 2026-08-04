using System;
using System.Globalization;

namespace ArrayImageViewer.Core
{
    internal static class QFormat
    {
        public static decimal Decode(long rawValue, int fractionalBits)
        {
            var divisor = 1m;
            for (var index = 0; index < fractionalBits; index++)
            {
                divisor *= 2m;
            }

            return rawValue / divisor;
        }

        public static string Format(long rawValue, int fractionalBits)
        {
            return Decode(rawValue, fractionalBits).ToString("0.############################", CultureInfo.InvariantCulture);
        }
    }
}
