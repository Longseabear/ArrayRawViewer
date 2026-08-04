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
                if (!String.IsNullOrWhiteSpace(name) && !String.IsNullOrWhiteSpace(type) && type.IndexOf('*') >= 0)
                {
                    result.Add(new PointerExpression(name.Trim(), type.Trim()));
                }
            }

            return result;
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
    }
}
