using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
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

        // Breakpoints.Add returns the collection rather than the new item.
        // Resolve the actual COM breakpoint as the new automation object that
        // appeared after Add. Native data breakpoints expose an empty Data
        // property on several VS engines, and collection ordering is unstable.
        private sealed class NativeDataBreakpointReference
        {
            private readonly object breakpoints;
            private readonly HashSet<string> existingBreakpointIdentities;
            private readonly int breakpointCountBeforeAdd;
            private string resolvedBreakpointIdentity;
            private string resolvedBreakpointName;
            public string LastResolutionError { get; private set; }

            public NativeDataBreakpointReference(object breakpoints, HashSet<string> existingBreakpointIdentities,
                int breakpointCountBeforeAdd)
            {
                this.breakpoints = breakpoints;
                this.existingBreakpointIdentities = existingBreakpointIdentities;
                this.breakpointCountBeforeAdd = breakpointCountBeforeAdd;
            }

            public object TryResolve()
            {
                try
                {
                    LastResolutionError = null;
                    var breakpointSnapshot = GetBreakpointSnapshot(breakpoints);
                    var count = breakpointSnapshot.Count;

                    // The generated Name contains the resolved native address
                    // even when EnvDTE exposes an empty Data expression. Match
                    // it first so Delete always receives a freshly retrieved
                    // collection item rather than a stale COM wrapper.
                    if (!String.IsNullOrWhiteSpace(resolvedBreakpointName))
                    {
                        for (var index = 1; index <= count; index++)
                        {
                            var candidate = breakpointSnapshot[index - 1];
                            var candidateName = Convert.ToString(GetOptionalMember(candidate, "Name"), CultureInfo.InvariantCulture);
                            if (String.Equals(candidateName, resolvedBreakpointName, StringComparison.Ordinal))
                            {
                                resolvedBreakpointIdentity = GetAutomationObjectIdentity(candidate);
                                return candidate;
                            }
                        }
                    }

                    // EnvDTE appends a newly added native data breakpoint. This
                    // exact position remains reliable while its COM identity is
                    // still being published, whereas identity-only matching can
                    // temporarily see the old collection snapshot.
                    if (count >= breakpointCountBeforeAdd + 1)
                    {
                        var appended = breakpointSnapshot[breakpointCountBeforeAdd];
                        var appendedIdentity = GetAutomationObjectIdentity(appended);
                        // Add was invoked specifically with the Data argument,
                        // so this appended item is the viewer's native data
                        // breakpoint. Some native engines do not implement
                        // LocationType through IDispatch (DISP_E_MEMBERNOTFOUND).
                        if (!existingBreakpointIdentities.Contains(appendedIdentity))
                        {
                            resolvedBreakpointIdentity = appendedIdentity;
                            resolvedBreakpointName = Convert.ToString(GetOptionalMember(appended, "Name"), CultureInfo.InvariantCulture);
                            return appended;
                        }
                    }

                    for (var index = 1; index <= count; index++)
                    {
                        var candidate = breakpointSnapshot[index - 1];
                        var candidateIdentity = GetAutomationObjectIdentity(candidate);
                        if (!String.IsNullOrWhiteSpace(resolvedBreakpointIdentity) &&
                            String.Equals(candidateIdentity, resolvedBreakpointIdentity, StringComparison.Ordinal))
                        {
                            return candidate;
                        }
                        if (!String.IsNullOrWhiteSpace(candidateIdentity) &&
                            !existingBreakpointIdentities.Contains(candidateIdentity))
                        {
                            resolvedBreakpointIdentity = candidateIdentity;
                            resolvedBreakpointName = Convert.ToString(GetOptionalMember(candidate, "Name"), CultureInfo.InvariantCulture);
                            return candidate;
                        }
                    }
                }
                catch (Exception exception)
                {
                    // The engine may still be publishing the breakpoint. A
                    // later hit/clear attempt can resolve it again.
                    LastResolutionError = exception.GetType().Name + ": " + exception.Message;
                }

                return null;
            }
        }

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

            var dte = GetDte();
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

        internal static bool TryStartMemoryOverviewRead(string expression, FrameConfiguration sourceConfiguration, int targetWidth, int targetHeight,
            out DebugMemoryFrameReader.IFrameReadSession session)
        {
            LastRoiReadUsedMemory = false;
            var debugger = GetDebugger();
            var currentStackFrame = GetCurrentStackFrame(debugger);
            if (DebugMemoryFrameReader.TryStartDecimatedRead(currentStackFrame, expression, sourceConfiguration, targetWidth, targetHeight, out session))
            {
                return true;
            }

            var evaluated = Invoke(debugger, "GetExpression", expression, true, 2000);
            return DebugMemoryFrameReader.TryStartDecimatedReadFromProperty(evaluated, sourceConfiguration, targetWidth, targetHeight, out session);
        }

        internal static void MarkMemoryReadComplete()
        {
            LastRoiReadUsedMemory = true;
        }

        public static string GetActiveEditorSelection()
        {
            var dte = GetDte();
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

        // Structure capture deliberately starts at one expression supplied by
        // the user (normally "this").  It never walks Locals, Arguments, or
        // another stack frame, which keeps discovery predictable in large
        // native applications.  The debugger only expands children below that
        // root and the two limits prevent a container or cyclic view from
        // making the tool window unresponsive.
        internal static IList<StructureCandidate> CaptureInterestedStructures(string rootExpression,
            IList<string> interestedTypeNames, int maximumDepth, int maximumNodes)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (String.IsNullOrWhiteSpace(rootExpression))
            {
                throw new ArgumentException("Enter a root object expression such as this.");
            }
            if (interestedTypeNames == null || interestedTypeNames.Count == 0)
            {
                throw new ArgumentException("Create at least one structure template in Tools > Options before capture.");
            }

            var debugger = GetDebugger();
            var results = new List<StructureCandidate>();
            var pending = new Queue<StructureCaptureNode>();
            var seenExpressions = new HashSet<string>(StringComparer.Ordinal);
            pending.Enqueue(new StructureCaptureNode(rootExpression.Trim(), 0));

            var visited = 0;
            while (pending.Count > 0 && visited < maximumNodes)
            {
                var node = pending.Dequeue();
                if (!seenExpressions.Add(node.Expression))
                {
                    continue;
                }

                visited++;
                var evaluated = node.EvaluatedExpression;
                if (evaluated == null)
                {
                    try
                    {
                        evaluated = Invoke(debugger, "GetExpression", node.Expression, true, 2000);
                    }
                    catch (Exception)
                    {
                        // A member can disappear in optimized native code. Keep
                        // capture useful for the remaining object graph.
                        continue;
                    }
                }

                var type = !String.IsNullOrWhiteSpace(node.Type)
                    ? node.Type
                    : Convert.ToString(GetOptionalMember(evaluated, "Type"), CultureInfo.InvariantCulture) ?? String.Empty;
                var matchedTemplate = StructureSearchPolicy.FindInterestedTypeName(type, interestedTypeNames);
                if (matchedTemplate != null)
                {
                    results.Add(new StructureCandidate(node.Expression, GetPointerRootExpression(node.Expression, type), type, matchedTemplate));
                    // A template object has already been found. Its m_data
                    // pointer may expose thousands of raw elements, none of
                    // which can contain another registered structure.
                    continue;
                }

                if (node.Depth >= maximumDepth)
                {
                    continue;
                }

                var expansion = StructureSearchPolicy.GetExpansion(type, interestedTypeNames);
                if (expansion == StructureSearchExpansion.None)
                {
                    continue;
                }

                var containerElements = 0;
                foreach (var child in EnumerateExpressionChildren(evaluated))
                {
                    var childName = Convert.ToString(GetOptionalMember(child, "Name"), CultureInfo.InvariantCulture);
                    if (String.IsNullOrWhiteSpace(childName) || IsDebuggerPresentationMember(childName))
                    {
                        continue;
                    }

                    if (expansion == StructureSearchExpansion.InterestedContainerElements)
                    {
                        if (!StructureSearchPolicy.IsContainerElementName(childName) || containerElements >= 16)
                        {
                            continue;
                        }
                        containerElements++;
                    }

                    var childType = Convert.ToString(GetOptionalMember(child, "Type"), CultureInfo.InvariantCulture);
                    pending.Enqueue(new StructureCaptureNode(ComposeChildExpression(node.Expression, type, childName.Trim()), node.Depth + 1, child, childType));
                }
            }

            return results;
        }

        internal static string GetActiveSolutionIdentity()
        {
            try
            {
                var dte = GetDte();
                var solution = GetOptionalMember(dte, "Solution");
                var fullName = GetOptionalMember(solution, "FullName") as string;
                if (!String.IsNullOrWhiteSpace(fullName))
                {
                    return fullName;
                }
            }
            catch (Exception)
            {
                // A solution is optional for a miscellaneous-files debug
                // session; use a deterministic fallback in that case.
            }

            return "<no-solution>";
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

        internal static bool TryGetExpressionType(string expression, out string type)
        {
            type = null;
            if (String.IsNullOrWhiteSpace(expression))
            {
                return false;
            }

            try
            {
                var evaluated = Invoke(GetDebugger(), "GetExpression", expression.Trim(), true, 2000);
                type = Convert.ToString(GetOptionalMember(evaluated, "Type"), CultureInfo.InvariantCulture);
                return !String.IsNullOrWhiteSpace(type);
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static bool TryGetIntegralPointerType(string expression, out SourceElementType elementType, out string error)
        {
            elementType = SourceElementType.UInt32;
            error = null;
            if (String.IsNullOrWhiteSpace(expression))
            {
                error = "The RAW data expression is empty.";
                return false;
            }

            try
            {
                var debugger = GetDebugger();
                var evaluated = Invoke(debugger, "GetExpression", expression.Trim(), true, 2000);
                var type = Convert.ToString(GetMember(evaluated, "Type"), CultureInfo.InvariantCulture);
                if (!IsSupportedIntegralPointerType(type))
                {
                    error = "'" + expression + "' has type '" + type + "'. RAW data must be an 8/16/32-bit signed or unsigned integral pointer.";
                    return false;
                }

                elementType = GetSourceElementType(type);
                return true;
            }
            catch (Exception exception)
            {
                error = "Cannot evaluate RAW data expression '" + expression + "': " + exception.Message;
                return false;
            }
        }

        // A native data breakpoint must receive an address expression.  Passing
        // data->GetPointer() directly makes some native engines reevaluate the
        // method while programming or checking the breakpoint.  Resolve it
        // exactly once while paused, then watch the resulting literal address.
        internal static string ResolvePointerExpressionToAddress(string expression)
        {
            if (String.IsNullOrWhiteSpace(expression))
            {
                throw new ArgumentException("A pointer expression is required.", "expression");
            }

            var debugger = GetDebugger();
            var evaluated = Invoke(debugger, "GetExpression", expression, true, 2000);
            var value = Convert.ToString(GetMember(evaluated, "Value"), CultureInfo.InvariantCulture);
            ulong address;
            if (!DataWatchExpression.TryParsePointerAddress(value, out address))
            {
                throw new FormatException("The debugger did not return a pointer address for " + expression + ": " + value);
            }
            if (address == 0)
            {
                throw new InvalidOperationException("The pointer expression resolved to null.");
            }

            return "0x" + address.ToString("X", CultureInfo.InvariantCulture);
        }

        // EnvDTE exposes native data breakpoints through the ordinary
        // Breakpoints collection. Keep this late-bound: the automation wrapper
        // is available from VS 2015 onward, while the exact COM type differs
        // between debugger engines and VS shells.
        internal static object AddNativeDataBreakpoint(string dataExpression, int byteCount)
        {
            if (String.IsNullOrWhiteSpace(dataExpression))
            {
                throw new ArgumentException("A data expression is required.", "dataExpression");
            }
            if (byteCount <= 0)
            {
                throw new ArgumentOutOfRangeException("byteCount");
            }

            var debugger = GetDebugger();
            var breakpoints = GetMember(debugger, "Breakpoints");
            if (breakpoints == null)
            {
                throw new InvalidOperationException("The current debugger does not expose a breakpoint collection.");
            }

            // EnvDTE.Breakpoints.Add(Function, File, Line, Column,
            // Condition, ConditionType, Language, Data, DataCount, Address,
            // HitCount, HitCountType). Enum value 1 is the documented
            // WhenTrue/None default for VS 2015-2022.
            var breakpointCountBeforeAdd = GetBreakpointSnapshot(breakpoints).Count;
            var breakpointIdentitiesBeforeAdd = GetBreakpointIdentities(breakpoints);
            Invoke(breakpoints, "Add", "", "", 1, 1, "", 1,
                "", dataExpression, byteCount, "", 0, 1);
            // Native engines can publish the new breakpoint to the automation
            // collection later (often only once the debugger resumes). Add has
            // already accepted the request, so keep a lazy reference instead
            // of falsely reporting failure from a stale Count value.
            return new NativeDataBreakpointReference(breakpoints, breakpointIdentitiesBeforeAdd, breakpointCountBeforeAdd);
        }

        internal static bool DeleteBreakpoint(object breakpoint)
        {
            string ignored;
            return DeleteBreakpoint(breakpoint, out ignored);
        }

        internal static bool DeleteBreakpoint(object breakpoint, out string failureReason)
        {
            failureReason = null;
            if (breakpoint == null)
            {
                return true;
            }

            try
            {
                var pending = breakpoint as NativeDataBreakpointReference;
                if (pending != null)
                {
                    breakpoint = pending.TryResolve();
                    if (breakpoint == null)
                    {
                        failureReason = "The Visual Studio breakpoint entry was not published. " + (pending.LastResolutionError ?? String.Empty);
                        return false;
                    }
                }

                // __ComObject's reflection binder can acknowledge Delete
                // without dispatching it to the native engine.  The C# COM
                // binder issues the IDispatch call used by EnvDTE itself.
                dynamic nativeBreakpoint = breakpoint;
                nativeBreakpoint.Delete();
                return true;
            }
            catch (Exception exception)
            {
                // A native debugger can discard its data breakpoint at the
                // end of a session. Treat that as already deleted.
                failureReason = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        internal static bool TryGetBreakpointHitCount(object breakpoint, out int hitCount)
        {
            hitCount = 0;
            if (breakpoint == null)
            {
                return false;
            }

            try
            {
                var pending = breakpoint as NativeDataBreakpointReference;
                if (pending != null)
                {
                    breakpoint = pending.TryResolve();
                    if (breakpoint == null)
                    {
                        return false;
                    }
                }
                var value = GetOptionalMember(breakpoint, "HitCount");
                if (value == null)
                {
                    return false;
                }
                hitCount = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static bool WasBreakpointLastHit(object breakpoint)
        {
            if (breakpoint == null)
            {
                return false;
            }

            try
            {
                var pending = breakpoint as NativeDataBreakpointReference;
                if (pending != null)
                {
                    breakpoint = pending.TryResolve();
                    if (breakpoint == null)
                    {
                        return false;
                    }
                }

                var debugger = GetDebugger();
                var lastHit = GetOptionalMember(debugger, "BreakpointLastHit");
                if (lastHit == null)
                {
                    return false;
                }

                // Native data breakpoints commonly expose an empty Data value
                // and use a generated address as Name. Compare COM identity
                // first; the text fields below are only compatibility
                // fallbacks for engines that do not preserve COM identity.
                if (AutomationObjectsMatch(breakpoint, lastHit))
                {
                    return true;
                }

                var watchedData = Convert.ToString(GetOptionalMember(breakpoint, "Data"), CultureInfo.InvariantCulture);
                var hitData = Convert.ToString(GetOptionalMember(lastHit, "Data"), CultureInfo.InvariantCulture);
                if (DataExpressionsMatch(watchedData, hitData))
                {
                    return true;
                }

                var watchedName = Convert.ToString(GetOptionalMember(breakpoint, "Name"), CultureInfo.InvariantCulture);
                var hitName = Convert.ToString(GetOptionalMember(lastHit, "Name"), CultureInfo.InvariantCulture);
                return !String.IsNullOrWhiteSpace(watchedName) &&
                    String.Equals(watchedName, hitName, StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool DataExpressionsMatch(string first, string second)
        {
            if (String.IsNullOrWhiteSpace(first) || String.IsNullOrWhiteSpace(second))
            {
                return false;
            }

            if (String.Equals(first, second, StringComparison.Ordinal))
            {
                return true;
            }

            return String.Equals(RemoveWhitespace(first), RemoveWhitespace(second), StringComparison.Ordinal);
        }

        private static string RemoveWhitespace(string value)
        {
            var builder = new StringBuilder(value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                if (!Char.IsWhiteSpace(value[index]))
                {
                    builder.Append(value[index]);
                }
            }
            return builder.ToString();
        }

        private static HashSet<string> GetBreakpointIdentities(object breakpoints)
        {
            var identities = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (var breakpoint in GetBreakpointSnapshot(breakpoints))
                {
                    var identity = GetAutomationObjectIdentity(breakpoint);
                    if (!String.IsNullOrWhiteSpace(identity))
                    {
                        identities.Add(identity);
                    }
                }
            }
            catch (Exception)
            {
                // Adding a data breakpoint still succeeds on engines that do
                // not publish their collection synchronously. Resolution will
                // retry when the debugger later enters break mode.
            }
            return identities;
        }

        private static List<object> GetBreakpointSnapshot(object breakpoints)
        {
            var result = new List<object>();
            var enumerable = breakpoints as IEnumerable;
            if (enumerable == null)
            {
                throw new InvalidOperationException("The Visual Studio breakpoint collection is not enumerable.");
            }

            foreach (var breakpoint in enumerable)
            {
                if (breakpoint != null)
                {
                    result.Add(breakpoint);
                }
            }
            return result;
        }

        private static bool AutomationObjectsMatch(object first, object second)
        {
            if (Object.ReferenceEquals(first, second))
            {
                return true;
            }

            var firstIdentity = GetAutomationObjectIdentity(first);
            var secondIdentity = GetAutomationObjectIdentity(second);
            return !String.IsNullOrWhiteSpace(firstIdentity) &&
                String.Equals(firstIdentity, secondIdentity, StringComparison.Ordinal);
        }

        private static string GetAutomationObjectIdentity(object value)
        {
            if (value == null)
            {
                return null;
            }

            IntPtr unknown = IntPtr.Zero;
            try
            {
                unknown = Marshal.GetIUnknownForObject(value);
                return unknown.ToInt64().ToString("X", CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (unknown != IntPtr.Zero)
                {
                    Marshal.Release(unknown);
                }
            }
        }

        internal static void ContinueDebuggee()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var uiShell = Package.GetGlobalService(typeof(SVsUIShell)) as IVsUIShell;
            if (uiShell == null)
            {
                throw new InvalidOperationException("No active Visual Studio shell service is available.");
            }

            // Standard command set 97, command 295, is Visual Studio's F5
            // Start command. In break mode it continues the current native
            // debuggee. PostExecCommand is stable across VS 2015-2022 and
            // does not rely on debugger-specific EnvDTE wrappers.
            var commandGroup = new Guid("5EFC7975-14BC-11CF-9B2B-00AA00573819");
            var result = uiShell.PostExecCommand(ref commandGroup, 295, 0, IntPtr.Zero);
            if (result < 0)
            {
                Marshal.ThrowExceptionForHR(result);
            }
        }

        private static IList<LocalExpression> GetCurrentFrameLocals()
        {
            var result = new List<LocalExpression>();
            var dte = GetDte();
            var debugger = GetMember(dte, "Debugger");
            // IDebugStackFrame2 is useful for direct memory access but does
            // not expose DTE's Locals/Arguments collections. Prefer the DTE
            // frame for discovery so the pointer dropdown works in native C++.
            var frame = GetCurrentAutomationStackFrame(debugger) ?? GetCurrentStackFrame(debugger);
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
            var internalFrame = TryGetInternalCurrentStackFrame();
            if (internalFrame != null)
            {
                return internalFrame;
            }

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

        private static object GetCurrentAutomationStackFrame(object debugger)
        {
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

        // Visual Studio's shell exposes the current DTE stack frame as an
        // automation wrapper, not as IDebugStackFrame2.  Recent VS hosts also
        // provide the active AD7 frame through their debugger service.  Query
        // it dynamically so VS 2015 and engines without that component retain
        // the ordinary DTE/expression fallback without a hard dependency on
        // a version-specific private assembly.
        private static IDebugStackFrame2 TryGetInternalCurrentStackFrame()
        {
            IntPtr unknown = IntPtr.Zero;
            IntPtr typedUnknown = IntPtr.Zero;
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var debuggerService = Package.GetGlobalService(typeof(SVsShellDebugger));
                if (debuggerService == null)
                {
                    return null;
                }

                var internalType = Type.GetType("Microsoft.VisualStudio.Debugger.Interop.Internal.IDebuggerInternal, Microsoft.VisualStudio.Debugger.Interop.Internal", false);
                if (internalType == null)
                {
                    try
                    {
                        internalType = Assembly.Load("Microsoft.VisualStudio.Debugger.Interop.Internal").GetType("Microsoft.VisualStudio.Debugger.Interop.Internal.IDebuggerInternal", false);
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }

                if (internalType == null)
                {
                    return null;
                }

                unknown = Marshal.GetIUnknownForObject(debuggerService);
                var interfaceId = internalType.GUID;
                if (Marshal.QueryInterface(unknown, ref interfaceId, out typedUnknown) != 0 || typedUnknown == IntPtr.Zero)
                {
                    return null;
                }

                var internalDebugger = Marshal.GetTypedObjectForIUnknown(typedUnknown, internalType);
                var property = internalType.GetProperty("CurrentStackFrame", BindingFlags.Instance | BindingFlags.Public);
                var frame = property == null ? null : property.GetValue(internalDebugger, null);
                return frame as IDebugStackFrame2;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (typedUnknown != IntPtr.Zero)
                {
                    Marshal.Release(typedUnknown);
                }

                if (unknown != IntPtr.Zero)
                {
                    Marshal.Release(unknown);
                }
            }
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

        private static IEnumerable<object> EnumerateExpressionChildren(object expression)
        {
            var children = GetOptionalMember(expression, "DataMembers");
            if (children == null)
            {
                yield break;
            }

            var enumerable = children as IEnumerable;
            if (enumerable != null)
            {
                foreach (var child in enumerable)
                {
                    if (child != null)
                    {
                        yield return child;
                    }
                }
                yield break;
            }

            var countObject = GetOptionalMember(children, "Count");
            var count = countObject == null ? 0 : Convert.ToInt32(countObject, CultureInfo.InvariantCulture);
            for (var index = 1; index <= count; index++)
            {
                object child;
                try
                {
                    child = GetItem(children, index);
                }
                catch (Exception)
                {
                    continue;
                }
                if (child != null)
                {
                    yield return child;
                }
            }
        }

        private static string ComposeChildExpression(string parentExpression, string parentType, string childName)
        {
            if (childName.StartsWith("[", StringComparison.Ordinal))
            {
                return "(" + parentExpression + ")" + childName;
            }

            return "(" + parentExpression + ")" + (IsPointerType(parentType) ? "->" : ".") + childName;
        }

        private static string GetPointerRootExpression(string expression, string type)
        {
            return IsPointerType(type) ? expression : "&(" + expression + ")";
        }

        private static bool IsPointerType(string type)
        {
            return !String.IsNullOrWhiteSpace(type) && type.IndexOf('*') >= 0;
        }

        private static bool IsDebuggerPresentationMember(string name)
        {
            return name.StartsWith("{", StringComparison.Ordinal) || name.StartsWith("<", StringComparison.Ordinal) ||
                String.Equals(name, "Raw View", StringComparison.OrdinalIgnoreCase);
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
            var dte = GetDte();
            var debugger = GetMember(dte, "Debugger");
            if (debugger == null)
            {
                throw new InvalidOperationException("No active debugger is available. Break in the debuggee and try again.");
            }

            return debugger;
        }

        private static object GetDte()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dte = Package.GetGlobalService(typeof(SDTE));
            if (dte == null)
            {
                throw new InvalidOperationException("Visual Studio's DTE service is unavailable.");
            }

            return dte;
        }

        private static bool IsIntegerType(string type)
        {
            var normalized = type.ToLowerInvariant();
            return normalized.IndexOf("int") >= 0 || normalized.IndexOf("long") >= 0 || normalized.IndexOf("short") >= 0 ||
                   normalized.IndexOf("char") >= 0 || normalized.IndexOf("size_t") >= 0 || normalized.IndexOf("dword") >= 0 ||
                   normalized.IndexOf("word") >= 0;
        }

        private static bool IsSupportedIntegralPointerType(string type)
        {
            if (String.IsNullOrWhiteSpace(type))
            {
                return false;
            }

            var normalized = type.ToLowerInvariant();
            var pointerCount = 0;
            for (var index = 0; index < normalized.Length; index++)
            {
                if (normalized[index] == '*') pointerCount++;
            }
            if (pointerCount != 1 || normalized.IndexOf("float", StringComparison.Ordinal) >= 0 ||
                normalized.IndexOf("double", StringComparison.Ordinal) >= 0 || normalized.IndexOf("bool", StringComparison.Ordinal) >= 0 ||
                normalized.IndexOf("void", StringComparison.Ordinal) >= 0 || normalized.IndexOf("enum", StringComparison.Ordinal) >= 0 ||
                normalized.IndexOf("class", StringComparison.Ordinal) >= 0 || normalized.IndexOf("struct", StringComparison.Ordinal) >= 0 ||
                normalized.IndexOf("int64", StringComparison.Ordinal) >= 0 || normalized.IndexOf("uint64", StringComparison.Ordinal) >= 0 ||
                normalized.IndexOf("__int64", StringComparison.Ordinal) >= 0 || normalized.IndexOf("long long", StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            return IsIntegerType(normalized);
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

        internal sealed class StructureCandidate
        {
            public StructureCandidate(string expression, string pointerRootExpression, string type, string templateClassName)
            {
                Expression = expression;
                PointerRootExpression = pointerRootExpression;
                Type = type;
                TemplateClassName = templateClassName;
            }

            public string Expression { get; private set; }
            public string PointerRootExpression { get; private set; }
            public string Type { get; private set; }
            public string TemplateClassName { get; private set; }

            public override string ToString()
            {
                return Expression + "  (" + Type + ")";
            }
        }

        private sealed class StructureCaptureNode
        {
            public StructureCaptureNode(string expression, int depth)
                : this(expression, depth, null, null)
            {
            }

            public StructureCaptureNode(string expression, int depth, object evaluatedExpression, string type)
            {
                Expression = expression;
                Depth = depth;
                EvaluatedExpression = evaluatedExpression;
                Type = type;
            }

            public string Expression { get; private set; }
            public int Depth { get; private set; }
            public object EvaluatedExpression { get; private set; }
            public string Type { get; private set; }
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
