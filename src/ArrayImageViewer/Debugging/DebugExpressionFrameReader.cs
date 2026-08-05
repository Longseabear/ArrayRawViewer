using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Debugger.Interop;
using ArrayImageViewer.Core;

namespace ArrayImageViewer.Debugging
{
    // The Visual Studio expression evaluator is intentionally kept behind this
    // small adapter. It is suitable for small ROI fallback reads; native debugger
    // memory reads are used whenever the active engine exposes memory contexts.
    internal static class DebugExpressionFrameReader
    {
        public const int DraftSampleLimit = 16384;
        public static bool LastRoiReadUsedMemory { get; private set; }

        public static FrameBuffer Read(string expression, FrameConfiguration configuration)
        {
            if (String.IsNullOrWhiteSpace(expression))
            {
                throw new ArgumentException("Enter a pointer or array expression.");
            }

            if (configuration.RequiredSampleCount > DraftSampleLimit)
            {
                throw new InvalidOperationException(
                    "Expression evaluation is limited to 16,384 samples. Use Load ROI on a native engine with debugger-memory access for larger data.");
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
                data[index] = ParseValue(value, configuration.SourceElementType);
            }

            return new FrameBuffer(configuration, data);
        }

        public static FrameBuffer ReadRoi(string expression, FrameConfiguration sourceConfiguration, int originX, int originY, int roiWidth, int roiHeight)
        {
            LastRoiReadUsedMemory = false;
            if (originX < 0 || originY < 0 || roiWidth <= 0 || roiHeight <= 0 ||
                originX + roiWidth > sourceConfiguration.Width || originY + roiHeight > sourceConfiguration.Height)
            {
                throw new ArgumentException("The requested ROI is outside the configured full frame.");
            }

            if (String.IsNullOrWhiteSpace(expression))
            {
                throw new ArgumentException("Enter a pointer or array expression.");
            }

            var roiSampleCount = checked((long)roiWidth * roiHeight);
            DebugMemoryFrameReader.RoiReadSession memoryRead;
            if (TryStartMemoryRoiRead(expression, sourceConfiguration, originX, originY, roiWidth, roiHeight, out memoryRead))
            {
                if (!memoryRead.TryReadRows(roiHeight))
                {
                    throw new InvalidOperationException(memoryRead.ErrorMessage ?? "The debugger could not read the requested memory.");
                }

                LastRoiReadUsedMemory = true;
                return memoryRead.CreateFrame();
            }

            if (roiSampleCount > DraftSampleLimit)
            {
                throw new InvalidOperationException("This debug engine does not expose native memory reads. Expression fallback is limited to 16,384 ROI samples; reduce ROI W/H.");
            }

            var roiConfiguration = new FrameConfiguration(roiWidth, roiHeight, roiWidth,
                sourceConfiguration.IntegerBits, sourceConfiguration.FractionalBits, sourceConfiguration.IsSigned,
                sourceConfiguration.PixelOrder, sourceConfiguration.PixelType, sourceConfiguration.VisualizeChannel,
                originX, originY, sourceConfiguration.SourceElementType);
            var debugger = GetDebugger();
            var data = new long[checked((int)roiSampleCount)];
            for (var y = 0; y < roiHeight; y++)
            {
                for (var x = 0; x < roiWidth; x++)
                {
                    var sourceIndex = checked((long)(originY + y) * sourceConfiguration.Stride + originX + x);
                    var debuggerExpression = String.Format(CultureInfo.InvariantCulture, "({0})[{1}]", expression, sourceIndex);
                    var evaluated = Invoke(debugger, "GetExpression", debuggerExpression, true, 2000);
                    var value = Convert.ToString(GetMember(evaluated, "Value"), CultureInfo.InvariantCulture);
                    data[y * roiWidth + x] = ParseValue(value, sourceConfiguration.SourceElementType);
                }
            }

            return new FrameBuffer(roiConfiguration, data);
        }

        internal static bool TryStartMemoryRoiRead(string expression, FrameConfiguration sourceConfiguration, int originX, int originY,
            int roiWidth, int roiHeight, out DebugMemoryFrameReader.RoiReadSession session)
        {
            LastRoiReadUsedMemory = false;
            var debugger = GetDebugger();
            var currentStackFrame = GetCurrentStackFrame(debugger);
            if (DebugMemoryFrameReader.TryStartRoiRead(currentStackFrame, expression, sourceConfiguration, originX, originY, roiWidth, roiHeight, out session))
            {
                return true;
            }

            var evaluated = Invoke(debugger, "GetExpression", expression, true, 2000);
            return DebugMemoryFrameReader.TryStartRoiReadFromProperty(evaluated, sourceConfiguration, originX, originY, roiWidth, roiHeight, out session);
        }

        internal static void MarkMemoryReadComplete()
        {
            LastRoiReadUsedMemory = true;
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

            return NormalizePointerExpression(selectedText);
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
            var frame = GetCurrentStackFrame(debugger);
            if (frame == null)
            {
                throw new InvalidOperationException("No current stack frame. Pause the native debuggee first.");
            }

            try
            {
                AddExpressions(result, GetOptionalMember(frame, "Locals"));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("The native debugger exposed locals but they could not be enumerated: " + exception.Message, exception);
            }

            try
            {
                AddExpressions(result, GetOptionalMember(frame, "Arguments"));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("The native debugger exposed arguments but they could not be enumerated: " + exception.Message, exception);
            }
            return result;
        }

        // EnvDTE.Debugger does not expose CurrentStackFrame for every native
        // debug engine.  The selected thread is the stable automation entry
        // point, and its CurrentStackFrame is available while C++ is paused.
        // Keep the direct lookup as a fallback for engines which do expose it.
        private static object GetCurrentStackFrame(object debugger)
        {
            var nativeFrame = TryGetNativeCurrentStackFrame(debugger);
            if (nativeFrame != null)
            {
                return nativeFrame;
            }

            var frame = GetOptionalMember(debugger, "CurrentStackFrame");
            if (frame != null)
            {
                return frame;
            }

            var thread = GetOptionalMember(debugger, "CurrentThread");
            frame = GetOptionalMember(thread, "CurrentStackFrame");
            if (frame != null)
            {
                return frame;
            }

            var frames = GetOptionalMember(thread, "StackFrames");
            var count = GetOptionalMember(frames, "Count");
            if (count != null && Convert.ToInt32(count, CultureInfo.InvariantCulture) > 0)
            {
                return GetItem(frames, 1);
            }

            return null;
        }

        // The EnvDTE StackFrame wrapper itself is not an IDebugStackFrame2.
        // Query the underlying current native thread and enumerate its first
        // frame, which is the frame Visual Studio has selected while stopped.
        private static IDebugStackFrame2 TryGetNativeCurrentStackFrame(object debugger)
        {
            try
            {
                var thread = GetOptionalMember(debugger, "CurrentThread") as IDebugThread2;
                if (thread == null)
                {
                    return null;
                }

                IEnumDebugFrameInfo2 frames;
                if (thread.EnumFrameInfo((uint)enum_FRAMEINFO_FLAGS.FIF_FRAME, 10, out frames) < 0 || frames == null)
                {
                    return null;
                }

                var information = new FRAMEINFO[1];
                uint fetched = 0;
                if (frames.Next(1, information, ref fetched) < 0 || fetched != 1)
                {
                    return null;
                }

                return information[0].m_pFrame;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void AddExpressions(IList<LocalExpression> result, object expressions)
        {
            if (expressions == null)
            {
                return;
            }

            // Native C++ expression collections are COM enumerables.  Unlike
            // the managed debugger collection, they do not consistently expose
            // an IDispatch Item member, so enumerate them first.
            var enumerable = expressions as IEnumerable;
            if (enumerable != null)
            {
                var index = 0;
                foreach (var local in enumerable)
                {
                    index++;
                    AddExpression(result, local, index);
                }

                return;
            }

            object countObject;
            try
            {
                countObject = GetMember(expressions, "Count");
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("The local collection does not expose Count: " + exception.Message, exception);
            }
            var count = countObject == null ? 0 : Convert.ToInt32(countObject, CultureInfo.InvariantCulture);
            for (var index = 1; index <= count; index++)
            {
                object local;
                try
                {
                    local = GetItem(expressions, index);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException("The local collection could not return item " + index + ": " + exception.Message, exception);
                }

                AddExpression(result, local, index);
            }
        }

        private static void AddExpression(IList<LocalExpression> result, object local, int index)
        {
            string name;
            try
            {
                name = Convert.ToString(GetMember(local, "Name"), CultureInfo.InvariantCulture);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Local item " + index + " does not expose Name: " + exception.Message, exception);
            }

            string type;
            try
            {
                type = Convert.ToString(GetMember(local, "Type"), CultureInfo.InvariantCulture);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Local " + name + " does not expose Type: " + exception.Message, exception);
            }

            if (!String.IsNullOrWhiteSpace(name) && !String.IsNullOrWhiteSpace(type) && !ContainsName(result, name.Trim()))
            {
                result.Add(new LocalExpression(name.Trim(), type.Trim()));
            }
        }

        private static bool ContainsName(IList<LocalExpression> expressions, string name)
        {
            for (var index = 0; index < expressions.Count; index++)
            {
                if (String.Equals(expressions[index].Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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

        private static SourceElementType GetSourceElementType(string type)
        {
            var normalized = type == null ? String.Empty : type.ToLowerInvariant();
            var isUnsigned = normalized.IndexOf("unsigned") >= 0 || normalized.IndexOf("uint") >= 0 ||
                normalized.IndexOf("byte") >= 0 || normalized.IndexOf("dword") >= 0 || normalized.IndexOf("size_t") >= 0;
            if (normalized.IndexOf("int8") >= 0 || normalized.IndexOf("char") >= 0 || normalized.IndexOf("byte") >= 0)
            {
                return isUnsigned ? SourceElementType.UInt8 : SourceElementType.Int8;
            }

            if (normalized.IndexOf("int16") >= 0 || normalized.IndexOf("short") >= 0 || normalized.IndexOf("word") >= 0)
            {
                return isUnsigned ? SourceElementType.UInt16 : SourceElementType.Int16;
            }

            return isUnsigned ? SourceElementType.UInt32 : SourceElementType.Int32;
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
            if (target == null)
            {
                return null;
            }

            try
            {
                return target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        private static object GetOptionalMember(object target, string name)
        {
            try
            {
                return GetMember(target, name);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static object Invoke(object target, string name, params object[] parameters)
        {
            try
            {
                return target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, parameters);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        private static object GetItem(object collection, int index)
        {
            try
            {
                return collection.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, collection, new object[] { index });
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        private static long ParseValue(string value, SourceElementType sourceElementType)
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
                return DecodeExpressionRaw(raw, sourceElementType);
            }

            return sourceElementType == SourceElementType.Int8 || sourceElementType == SourceElementType.Int16 || sourceElementType == SourceElementType.Int32
                ? (long)Int32.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture)
                : (long)UInt32.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static long DecodeExpressionRaw(uint raw, SourceElementType sourceElementType)
        {
            return SourceElementCodec.DecodeUInt32(raw, sourceElementType);
        }

        private static string NormalizePointerExpression(string selectedText)
        {
            var expression = selectedText.Trim().TrimEnd(';', ',');
            var separator = expression.LastIndexOfAny(new[] { ' ', '\t', '\r', '\n' });
            if (separator >= 0 && expression.Substring(0, separator).IndexOf('*') >= 0)
            {
                expression = expression.Substring(separator + 1);
            }

            expression = expression.Trim().TrimStart('*', '&');
            if (String.IsNullOrWhiteSpace(expression))
            {
                throw new InvalidOperationException("The editor selection does not contain a pointer expression.");
            }

            return expression;
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
            public bool IsSigned
            {
                get
                {
                    return SourceElementType == SourceElementType.Int8 || SourceElementType == SourceElementType.Int16 ||
                        SourceElementType == SourceElementType.Int32;
                }
            }

            public SourceElementType SourceElementType
            {
                get { return GetSourceElementType(Type); }
            }

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
