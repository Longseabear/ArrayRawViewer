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

        // The native debugger may bind a data breakpoint only after execution
        // resumes. Resolve the frame-dependent pointer while paused, then pass
        // the debugger a literal byte address. This keeps a watch on
        // this->output.m_data valid after the capture frame resumes.
        public static string BuildByteAddress(ulong baseAddress, int elementSizeInBytes, int stride, int x, int y)
        {
            if (baseAddress == 0)
            {
                throw new ArgumentOutOfRangeException("baseAddress");
            }
            if (elementSizeInBytes <= 0)
            {
                throw new ArgumentOutOfRangeException("elementSizeInBytes");
            }

            var sampleOffset = checked((long)y * stride + x);
            if (stride <= 0 || x < 0 || y < 0 || sampleOffset < 0)
            {
                throw new ArgumentOutOfRangeException("stride");
            }

            var byteOffset = checked((ulong)sampleOffset * (ulong)elementSizeInBytes);
            if (byteOffset > UInt64.MaxValue - baseAddress)
            {
                throw new OverflowException("The data breakpoint address overflowed.");
            }

            return "0x" + (baseAddress + byteOffset).ToString("X", CultureInfo.InvariantCulture);
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
