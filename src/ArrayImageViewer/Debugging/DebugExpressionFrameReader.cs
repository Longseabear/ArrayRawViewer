using System;
using System.Globalization;
using System.Reflection;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using ArrayImageViewer.Core;

namespace ArrayImageViewer.Debugging
{
    // The Visual Studio expression evaluator is intentionally kept behind this
    // small adapter. It is suitable for small arrays in this draft; a production
    // reader should replace it with a debugger-memory reader for large frames.
    internal static class DebugExpressionFrameReader
    {
        public const int DraftSampleLimit = 16384;

        public static FrameBuffer Read(string expression, FrameConfiguration configuration)
        {
            if (String.IsNullOrWhiteSpace(expression))
            {
                throw new ArgumentException("Enter a pointer or array expression.");
            }

            if (configuration.RequiredSampleCount > DraftSampleLimit)
            {
                throw new InvalidOperationException(
                    "Expression evaluation is limited to 16,384 samples in this draft. Use the synthetic preview for large-frame UI testing; direct debug-memory reads are the next implementation step.");
            }

            var dte = Package.GetGlobalService(typeof(SDTE));
            if (dte == null)
            {
                throw new InvalidOperationException("Visual Studio's debugger service is unavailable. Break in the debuggee and try again.");
            }

            var debugger = GetMember(dte, "Debugger");
            if (debugger == null)
            {
                throw new InvalidOperationException("No active debugger is available. Break in the debuggee and try again.");
            }

            var data = new long[checked((int)configuration.RequiredSampleCount)];
            for (var index = 0; index < data.Length; index++)
            {
                var debuggerExpression = String.Format(CultureInfo.InvariantCulture, "({0})[{1}]", expression, index);
                var evaluated = Invoke(debugger, "GetExpression", debuggerExpression, true, 2000);
                var value = Convert.ToString(GetMember(evaluated, "Value"), CultureInfo.InvariantCulture);
                data[index] = ParseValue(value, configuration.IsSigned);
            }

            return new FrameBuffer(configuration, data);
        }

        public static string GetActiveEditorSelection()
        {
            var dte = Package.GetGlobalService(typeof(SDTE));
            var document = GetMember(dte, "ActiveDocument");
            var selection = GetMember(document, "Selection");
            var selectedText = Convert.ToString(GetMember(selection, "Text"), CultureInfo.InvariantCulture);
            if (String.IsNullOrWhiteSpace(selectedText))
            {
                throw new InvalidOperationException("Select a pointer expression in the active code editor first.");
            }

            return selectedText.Trim();
        }

        private static object GetMember(object target, string name)
        {
            return target == null ? null : target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);
        }

        private static object Invoke(object target, string name, params object[] parameters)
        {
            return target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, parameters);
        }

        private static long ParseValue(string value, bool isSigned)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                throw new FormatException("The debugger returned an empty value.");
            }

            var trimmed = value.Trim();
            var hexadecimal = trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (hexadecimal)
            {
                var raw = UInt32.Parse(trimmed.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
                return isSigned ? (long)unchecked((int)raw) : (long)raw;
            }

            return isSigned
                ? (long)Int32.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture)
                : (long)UInt32.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }
    }
}
