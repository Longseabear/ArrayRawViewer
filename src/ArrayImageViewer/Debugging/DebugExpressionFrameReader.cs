using System;
using System.Collections.Generic;
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

        public static IList<PointerExpression> GetCurrentFramePointers()
        {
            var result = new List<PointerExpression>();
            foreach (var local in GetCurrentFrameLocals())
            {
                if (local.Type.IndexOf('*') >= 0)
                {
                    result.Add(new PointerExpression(local.Name, local.Type));
                }
            }

            return result;
        }

        public static IList<ScalarExpression> GetCurrentFrameScalars()
        {
            var result = new List<ScalarExpression>();
            foreach (var local in GetCurrentFrameLocals())
            {
                if (local.Type.IndexOf('*') < 0 && IsIntegerType(local.Type))
                {
                    result.Add(new ScalarExpression(local.Name, local.Type));
                }
            }

            return result;
        }

        public static int EvaluateInt32(string expression)
        {
            var debugger = GetDebugger();
            var evaluated = Invoke(debugger, "GetExpression", expression, true, 2000);
            var value = Convert.ToString(GetMember(evaluated, "Value"), CultureInfo.InvariantCulture);
            long parsed;
            if (!TryParseInteger(value, out parsed) || parsed < Int32.MinValue || parsed > Int32.MaxValue)
            {
                throw new FormatException("The debugger value for " + expression + " is not a 32-bit integer: " + value);
            }

            return (int)parsed;
        }

        private static IList<LocalExpression> GetCurrentFrameLocals()
        {
            var result = new List<LocalExpression>();
            var dte = Package.GetGlobalService(typeof(SDTE));
            var debugger = GetMember(dte, "Debugger");
            var frame = GetMember(debugger, "CurrentStackFrame");
            var locals = GetMember(frame, "Locals");
            if (locals == null)
            {
                throw new InvalidOperationException("No current stack frame. Pause the native debuggee first.");
            }

            var countObject = GetMember(locals, "Count");
            var count = countObject == null ? 0 : Convert.ToInt32(countObject, CultureInfo.InvariantCulture);
            for (var index = 1; index <= count; index++)
            {
                var local = GetItem(locals, index);
                var name = Convert.ToString(GetMember(local, "Name"), CultureInfo.InvariantCulture);
                var type = Convert.ToString(GetMember(local, "Type"), CultureInfo.InvariantCulture);
                if (!String.IsNullOrWhiteSpace(name) && !String.IsNullOrWhiteSpace(type))
                {
                    result.Add(new LocalExpression(name.Trim(), type.Trim()));
                }
            }

            return result;
        }

        private static object GetDebugger()
        {
            var dte = Package.GetGlobalService(typeof(SDTE));
            var debugger = GetMember(dte, "Debugger");
            if (debugger == null)
            {
                throw new InvalidOperationException("No active debugger is available. Break in the debuggee and try again.");
            }

            return debugger;
        }

        private static bool IsIntegerType(string type)
        {
            var normalized = type.ToLowerInvariant();
            return normalized.IndexOf("int") >= 0 || normalized.IndexOf("long") >= 0 || normalized.IndexOf("short") >= 0 ||
                   normalized.IndexOf("char") >= 0 || normalized.IndexOf("size_t") >= 0 || normalized.IndexOf("dword") >= 0 ||
                   normalized.IndexOf("word") >= 0;
        }

        private static bool TryParseInteger(string value, out long parsed)
        {
            parsed = 0;
            if (String.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim();
            var hexadecimalStart = trimmed.IndexOf("0x", StringComparison.OrdinalIgnoreCase);
            if (hexadecimalStart >= 0)
            {
                var index = hexadecimalStart + 2;
                while (index < trimmed.Length && Uri.IsHexDigit(trimmed[index]))
                {
                    index++;
                }

                ulong hexValue;
                if (!UInt64.TryParse(trimmed.Substring(hexadecimalStart + 2, index - hexadecimalStart - 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out hexValue) || hexValue > Int32.MaxValue)
                {
                    return false;
                }

                parsed = (long)hexValue;
                return true;
            }

            var end = 0;
            if (trimmed.Length > 0 && (trimmed[0] == '-' || trimmed[0] == '+'))
            {
                end = 1;
            }

            while (end < trimmed.Length && Char.IsDigit(trimmed[end]))
            {
                end++;
            }

            return end > 0 && Int64.TryParse(trimmed.Substring(0, end), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
        }

        private static object GetMember(object target, string name)
        {
            return target == null ? null : target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);
        }

        private static object Invoke(object target, string name, params object[] parameters)
        {
            return target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, parameters);
        }

        private static object GetItem(object collection, int index)
        {
            return collection.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, collection, new object[] { index });
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

        internal sealed class PointerExpression
        {
            public PointerExpression(string name, string type)
            {
                Name = name;
                Type = type;
            }

            public string Name { get; private set; }
            public string Type { get; private set; }
            public bool IsSigned { get { return Type.IndexOf("unsigned", StringComparison.OrdinalIgnoreCase) < 0; } }

            public override string ToString()
            {
                return Name + "  (" + Type + ")";
            }
        }

        internal sealed class ScalarExpression
        {
            public ScalarExpression(string name, string type)
            {
                Name = name;
                Type = type;
            }

            public string Name { get; private set; }
            public string Type { get; private set; }

            public override string ToString()
            {
                return Name + "  (" + Type + ")";
            }
        }

        private sealed class LocalExpression
        {
            public LocalExpression(string name, string type)
            {
                Name = name;
                Type = type;
            }

            public string Name { get; private set; }
            public string Type { get; private set; }
        }
    }
}
