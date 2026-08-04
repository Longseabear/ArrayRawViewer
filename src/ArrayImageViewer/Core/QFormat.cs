using System;
using System.Globalization;

namespace ArrayImageViewer.Core
{
    internal static class QFormat
    {
        public static bool TryParse(string text, out int integerBits, out int fractionalBits)
        {
            integerBits = 0;
            fractionalBits = 0;
            if (String.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var trimmed = text.Trim();
            if (trimmed.EndsWith("b", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 1);
            }

            var separator = trimmed.IndexOf('.');
            if (separator <= 0 || separator != trimmed.LastIndexOf('.') || separator == trimmed.Length - 1)
            {
                return false;
            }

            return Int32.TryParse(trimmed.Substring(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out integerBits) &&
                   Int32.TryParse(trimmed.Substring(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out fractionalBits);
        }

        public static string FormatSpecification(int integerBits, int fractionalBits)
        {
            return integerBits.ToString(CultureInfo.InvariantCulture) + "." +
                   fractionalBits.ToString(CultureInfo.InvariantCulture) + "b";
        }

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
