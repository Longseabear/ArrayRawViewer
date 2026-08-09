using System;
using System.Globalization;

namespace ArrayImageViewer.Core
{
    // Keeps the address calculation for a native data breakpoint identical to
    // the frame reader's y * stride + x addressing.  Native data breakpoints
    // expect an address expression, not a dereferenced sample value.
    public static class DataWatchExpression
    {
        public static string Build(string pointerExpression, int stride, int x, int y)
        {
            if (String.IsNullOrWhiteSpace(pointerExpression))
            {
                throw new ArgumentException("A pointer expression is required.", "pointerExpression");
            }
            if (stride <= 0)
            {
                throw new ArgumentOutOfRangeException("stride");
            }
            if (x < 0 || y < 0)
            {
                throw new ArgumentOutOfRangeException(x < 0 ? "x" : "y");
            }

            var offset = checked((long)y * stride + x);
            return "((" + pointerExpression.Trim() + ") + " +
                offset.ToString(CultureInfo.InvariantCulture) + ")";
        }

        // A native data breakpoint has no supported user-data field in the
        // EnvDTE API. Add an algebraic no-op so a Viewer-created breakpoint
        // has a distinct Data expression from a manually created breakpoint
        // watching the same address.
        public static string BuildViewerOwned(string pointerExpression, int stride, int x, int y, int tag)
        {
            if (tag <= 0)
            {
                throw new ArgumentOutOfRangeException("tag");
            }

            return "(" + Build(pointerExpression, stride, x, y) + " + (0 * " +
                tag.ToString(CultureInfo.InvariantCulture) + "))";
        }

        public static bool TryParsePointerAddress(string value, out ulong address)
        {
            address = 0;
            if (String.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var start = value.IndexOf("0x", StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return false;
            }

            start += 2;
            var end = start;
            while (end < value.Length && Uri.IsHexDigit(value[end]))
            {
                end++;
            }

            return end > start && UInt64.TryParse(value.Substring(start, end - start),
                NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out address);
        }
    }
}
