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
    internal sealed class StructureSearchTrace : IDisposable
    {
        private System.IO.StreamWriter writer;
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        private int lines;
        public string FilePath { get; private set; }
        public static string DirectoryPath { get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArrayImageViewer", "debug-dump"); } }
        public StructureSearchTrace(bool enabled) : this(enabled, "search") { }
        public StructureSearchTrace(bool enabled, string category)
        {
            if (!enabled) return;
            System.IO.Directory.CreateDirectory(DirectoryPath);
            if (category != "search" && category != "array-tab") throw new ArgumentException("Unknown trace category.");
            FilePath = System.IO.Path.Combine(DirectoryPath, category + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".log");
            writer = new System.IO.StreamWriter(new System.IO.FileStream(FilePath, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite), Encoding.UTF8);
            writer.AutoFlush = true;
            Write("START UTC=" + DateTime.UtcNow.ToString("o") + " (expressions/types only; no RAW sample values)");
        }
        public void Write(string message)
        {
            if (writer == null || lines >= 20000) return;
            try
            {
                message = (message ?? "").Replace("\r", "\\r").Replace("\n", "\\n");
                if (message.Length > 2000) message = message.Substring(0, 2000) + " [truncated]";
                writer.WriteLine(clock.ElapsedMilliseconds + "ms " + message);
                if (++lines == 20000) writer.WriteLine("LOG LINE LIMIT REACHED");
            }
            catch (System.IO.IOException) { Dispose(); }
        }
        public T Call<T>(string operation, Func<T> action)
        {
            if (writer == null) return action();
            Write("BEGIN " + operation);
            var start = clock.ElapsedMilliseconds;
            try { var result = action(); Write("END " + operation + " duration=" + (clock.ElapsedMilliseconds - start) + "ms"); return result; }
            catch (Exception ex) { Write("ERROR " + operation + " duration=" + (clock.ElapsedMilliseconds - start) + "ms " + ex.GetType().Name + " HRESULT=" + ex.HResult.ToString("X8") + " " + ex.Message); throw; }
        }
        public IEnumerable TraceChildren(IEnumerable children)
        {
            var iterator = Call("Children.GetEnumerator", delegate { return children.GetEnumerator(); });
            try
            {
                while (Call("Children.MoveNext", delegate { return iterator.MoveNext(); }))
                    yield return Call("Children.Current", delegate { return iterator.Current; });
            }
            finally { var disposable = iterator as IDisposable; if (disposable != null) disposable.Dispose(); }
        }
        public void Dispose()
        {
            var current = writer; writer = null;
            if (current != null) { try { current.Dispose(); } catch (System.IO.IOException) { } }
        }
    }

    // The Visual Studio expression evaluator is intentionally kept behind this
    // small adapter. It is suitable for small ROI fallback reads; native debugger
    // memory reads are used whenever the active engine exposes memory contexts.
    internal static class DebugExpressionFrameReader
    {
        public const int DraftSampleLimit = 16384;
        public static bool LastRoiReadUsedMemory { get; private set; }

        // Native data breakpoint publication can be delayed. Resolve only a
        // unique new entry matching the requested address; collection position
        // alone cannot distinguish it from a breakpoint added by the user.
        private sealed class NativeDataBreakpointReference
        {
            private readonly object breakpoints;
            private readonly HashSet<string> existingBreakpointIdentities;
            private readonly string dataExpression;
            private readonly int byteCount;
            private string resolvedBreakpointIdentity;
            public string LastResolutionError { get; private set; }
            public bool IsKnownRemoved { get; private set; }

            public NativeDataBreakpointReference(object breakpoints, HashSet<string> existingBreakpointIdentities,
                string dataExpression, int byteCount)
            {
                this.breakpoints = breakpoints;
                this.existingBreakpointIdentities = existingBreakpointIdentities;
                this.dataExpression = dataExpression;
                this.byteCount = byteCount;
            }

            public object TryResolve()
            {
                if (IsKnownRemoved) return null;
                try
                {
                    LastResolutionError = null;
                    var breakpointSnapshot = GetBreakpointSnapshot(breakpoints);
                    return String.IsNullOrWhiteSpace(resolvedBreakpointIdentity)
                        ? ResolveNewBreakpoint(breakpointSnapshot) : ResolveKnownBreakpoint(breakpointSnapshot);
                }
                catch (Exception exception)
                {
                    // The engine may still be publishing the breakpoint. A
                    // later hit/clear attempt can resolve it again.
                    LastResolutionError = exception.GetType().Name + ": " + exception.Message;
                }

                return null;
            }

            private object ResolveKnownBreakpoint(List<object> snapshot)
            {
                // Identity wins over display name, address and collection order.
                foreach (var candidate in snapshot)
                    if (String.Equals(GetAutomationObjectIdentity(candidate), resolvedBreakpointIdentity, StringComparison.Ordinal))
                        return candidate;
                // A recreated wrapper or same-address replacement is ambiguous,
                // not proof that the original hardware watch was removed.
                foreach (var candidate in snapshot)
                    if (MatchesRequestedWatch(candidate))
                    {
                        LastResolutionError = "The watched address is still present with a different breakpoint identity; ownership cannot be verified.";
                        return null;
                    }
                IsKnownRemoved = true;
                return null;
            }

            private object ResolveNewBreakpoint(List<object> snapshot)
            {
                object match = null;
                foreach (var candidate in snapshot)
                {
                    if (!MatchesRequestedWatch(candidate)) continue;
                    if (match != null)
                    {
                        LastResolutionError = "Multiple breakpoints match the watched address; ownership is ambiguous.";
                        return null;
                    }
                    match = candidate;
                }
                var identity = GetAutomationObjectIdentity(match);
                if (match == null || String.IsNullOrWhiteSpace(identity) || existingBreakpointIdentities.Contains(identity)) return null;
                resolvedBreakpointIdentity = identity;
                return match;
            }

            private bool MatchesRequestedWatch(object candidate)
            {
                var count = GetOptionalMember(candidate, "DataCount");
                if (count != null && Convert.ToInt32(count, CultureInfo.InvariantCulture) != byteCount) return false;
                var data = Convert.ToString(GetOptionalMember(candidate, "Data"), CultureInfo.InvariantCulture);
                // Some native engines expose Data as empty and only publish a
                // generated address Name. Accept that unique literal fallback,
                // but never override a conflicting nonempty Data value.
                if (String.IsNullOrWhiteSpace(data))
                    data = Convert.ToString(GetOptionalMember(candidate, "Name"), CultureInfo.InvariantCulture);
                ulong requestedAddress;
                ulong candidateAddress;
                return TryParseLiteralAddress(dataExpression, out requestedAddress) &&
                    TryParseLiteralAddress(data, out candidateAddress) && requestedAddress == candidateAddress;
            }

            private static bool TryParseLiteralAddress(string value, out ulong address)
            {
                address = 0;
                value = value == null ? String.Empty : value.Trim();
                return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                    UInt64.TryParse(value.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out address);
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
        internal static System.Threading.Tasks.Task<IList<StructureCandidate>> CaptureInterestedStructures(string rootExpression,
            IList<string> interestedTypeNames, int maximumDepth, int maximumNodes, int timeoutSeconds, Action<string> reportWarning, Func<bool> isCurrentSearch, StructureSearchTrace trace)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return SearchStructureGraph(rootExpression, interestedTypeNames, maximumDepth, maximumNodes, timeoutSeconds,
                reportWarning, isCurrentSearch, trace, delegate(string root)
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    var debugger = trace.Call("GetDebugger", GetDebugger);
                    return Invoke(debugger, "GetExpression", root, true, 500);
                }, YieldSearchUi);
        }

        private static async System.Threading.Tasks.Task YieldSearchUi()
        {
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        }

        // The traversal can also run against a deterministic fake debugger graph.
        internal static async System.Threading.Tasks.Task<IList<StructureCandidate>> SearchStructureGraph(string rootExpression,
            IList<string> interestedTypeNames, int maximumDepth, int maximumNodes, int timeoutSeconds, Action<string> reportWarning,
            Func<bool> isCurrentSearch, StructureSearchTrace trace, Func<string, object> evaluateRoot, Func<System.Threading.Tasks.Task> yieldUi)
        {
            if (String.IsNullOrWhiteSpace(rootExpression))
            {
                throw new ArgumentException("Enter a root object expression such as this.");
            }
            if (interestedTypeNames == null || interestedTypeNames.Count == 0)
            {
                throw new ArgumentException("Create at least one structure template in Tools > Options before capture.");
            }

            trace.Write("LIMITS depth=" + maximumDepth + " nodes=" + maximumNodes + " seconds=" + timeoutSeconds);
            foreach (var interest in interestedTypeNames) trace.Write("TEMPLATE " + interest);
            string searchWarning = null;
            var results = new List<StructureCandidate>();
            var searchClock = System.Diagnostics.Stopwatch.StartNew();
            long lastYield = 0;
            var seenPointers = new HashSet<string>(StringComparer.Ordinal);
            var matches = new Dictionary<string, string>(StringComparer.Ordinal);
            var expansions = new Dictionary<string, StructureSearchExpansion>(StringComparer.Ordinal);
            Func<string, string> findMatch = delegate(string value)
            {
                value = value ?? String.Empty;
                string match;
                if (!matches.TryGetValue(value, out match)) matches[value] = match = StructureSearchPolicy.FindInterestedTypeName(value, interestedTypeNames);
                return match;
            };
            Func<string, StructureSearchExpansion> findExpansion = delegate(string value)
            {
                value = value ?? String.Empty;
                StructureSearchExpansion expansion;
                if (!expansions.TryGetValue(value, out expansion)) expansions[value] = expansion = StructureSearchPolicy.GetExpansion(value, interestedTypeNames);
                return expansion;
            };
            Action checkBudget = delegate
            {
                if (!isCurrentSearch()) throw new OperationCanceledException("Debugger or viewer context changed. Search again at the current break.");
                if (searchClock.ElapsedMilliseconds >= timeoutSeconds * 1000L)
                    throw new StructureSearchLimitException("Search time limit (" + timeoutSeconds + " seconds) reached.");
            };
            var pending = new Queue<StructureCaptureNode>();
            var seenExpressions = new HashSet<string>(StringComparer.Ordinal);
            pending.Enqueue(new StructureCaptureNode(rootExpression.Trim(), 0));

            var visited = 0;
            var scheduled = 1;
            try
            {
            while (pending.Count > 0 && visited < maximumNodes)
            {
                if (searchClock.ElapsedMilliseconds - lastYield >= 40) { await yieldUi(); lastYield = searchClock.ElapsedMilliseconds; }
                checkBudget();
                var node = pending.Dequeue();
                if (!seenExpressions.Add(node.Expression))
                {
                    trace.Write("SKIP duplicate expression=" + node.Expression);
                    continue;
                }

                visited++;
                trace.Write("NODE depth=" + node.Depth + " visited=" + visited + " queued=" + pending.Count + " expression=" + node.Expression);
                var evaluated = node.EvaluatedExpression;
                if (evaluated == null)
                {
                    try
                    {
                        evaluated = trace.Call("GetExpression " + node.Expression, delegate { return evaluateRoot(node.Expression); });
                    }
                    catch (Exception)
                    {
                        trace.Write("SKIP evaluation failed expression=" + node.Expression);
                        // A member can disappear in optimized native code. Keep
                        // capture useful for the remaining object graph.
                        continue;
                    }
                }

                checkBudget();
                var type = !String.IsNullOrWhiteSpace(node.Type)
                    ? node.Type
                    : Convert.ToString(GetSearchMember(evaluated, "Type", trace), CultureInfo.InvariantCulture) ?? String.Empty;
                var matchedTemplate = findMatch(type);
                trace.Write("TYPE " + type + " MATCH=" + (matchedTemplate ?? "none"));
                if (matchedTemplate != null)
                {
                    trace.Write("FOUND expression=" + node.Expression + " template=" + matchedTemplate + " STOP descending into target");
                    results.Add(new StructureCandidate(node.Expression, GetPointerRootExpression(node.Expression, type), type, matchedTemplate));
                    // A template object has already been found. Its m_data
                    // pointer may expose thousands of raw elements, none of
                    // which can contain another registered structure.
                    continue;
                }

                if (node.Depth >= maximumDepth)
                {
                    searchWarning = "Search depth limit reached.";
                    trace.Write("SKIP depth limit expression=" + node.Expression);
                    continue;
                }

                var expansion = findExpansion(type);
                trace.Write("EXPANSION " + expansion);
                if (expansion == StructureSearchExpansion.None)
                {
                    trace.Write("SKIP non-expandable type=" + type + " expression=" + node.Expression);
                    continue;
                }

                if (IsPointerType(type))
                {
                    var identity = StructureSearchPolicy.PointerIdentity(type, Convert.ToString(GetSearchMember(evaluated, "Value", trace), CultureInfo.InvariantCulture));
                    if (identity != null && !seenPointers.Add(identity))
                    {
                        trace.Write("SKIP already expanded pointer=" + identity + " expression=" + node.Expression);
                        continue;
                    }
                }

                var containerElements = 0;
                try
                {
                foreach (var child in EnumerateExpressionChildren(evaluated, 64, checkBudget, trace))
                {
                    if (searchClock.ElapsedMilliseconds - lastYield >= 40) { await yieldUi(); lastYield = searchClock.ElapsedMilliseconds; }
                    checkBudget();
                    if (child == null) { trace.Write("SKIP null child parent=" + node.Expression); continue; }
                    var childName = Convert.ToString(GetSearchMember(child, "Name", trace), CultureInfo.InvariantCulture);
                    trace.Write("CHILD parent=" + node.Expression + " name=" + childName);
                    if (String.IsNullOrWhiteSpace(childName) || IsDebuggerPresentationMember(childName))
                    {
                        trace.Write("SKIP unnamed/presentation child");
                        continue;
                    }

                    if (expansion == StructureSearchExpansion.InterestedContainerElements)
                    {
                        if (!StructureSearchPolicy.IsContainerElementName(childName))
                        {
                            trace.Write("SKIP container implementation member=" + childName);
                            continue;
                        }
                        if (containerElements >= 16)
                        {
                            searchWarning = "Container element limit reached.";
                            trace.Write("LIMIT container parent=" + node.Expression);
                            break;
                        }
                        containerElements++;
                    }

                    var childType = Convert.ToString(GetSearchMember(child, "Type", trace), CultureInfo.InvariantCulture);
                    if (StructureSearchPolicy.IsBaseClassEntry(childName, childType))
                    {
                        trace.Write("SKIP base-class entry parent=" + node.Expression + " name=" + childName + " type=" + childType);
                        continue;
                    }
                    var childMatch = findMatch(childType);
                    trace.Write("CHILD TYPE=" + childType + " MATCH=" + (childMatch ?? "none") + " EXPANSION=" + findExpansion(childType));
                    if (childMatch != null)
                    {
                        var childExpression = ComposeChildExpression(node.Expression, type, childName.Trim());
                        trace.Write("FOUND expression=" + childExpression + " template=" + childMatch + " STOP descending into target");
                        if (seenExpressions.Add(childExpression))
                            results.Add(new StructureCandidate(childExpression, GetPointerRootExpression(childExpression, childType), childType, childMatch));
                        continue;
                    }
                    if (findExpansion(childType) == StructureSearchExpansion.None)
                    {
                        trace.Write("SKIP non-expandable child=" + childName + " type=" + childType);
                        continue;
                    }
                    if (scheduled >= maximumNodes)
                    {
                        searchWarning = "Search node limit reached.";
                        trace.Write("LIMIT queue capacity parent=" + node.Expression);
                        break;
                    }
                    scheduled++;
                    trace.Write("ENQUEUE depth=" + (node.Depth + 1) + " expression=" + ComposeChildExpression(node.Expression, type, childName.Trim()));
                    pending.Enqueue(new StructureCaptureNode(ComposeChildExpression(node.Expression, type, childName.Trim()), node.Depth + 1, child, childType));
                }
                }
                catch (StructureSearchLimitException exception)
                {
                    searchWarning = exception.Message;
                    checkBudget(); // A child cap must not discard already queued objects.
                }
            }
            }
            catch (StructureSearchLimitException exception)
            {
                searchWarning = exception.Message;
            }

            if (!isCurrentSearch()) throw new OperationCanceledException("Debugger or viewer context changed. Search again at the current break.");
            reportWarning(searchWarning);
            trace.Write("RESULT matches=" + results.Count + " visited=" + visited + " queued=" + pending.Count + " warning=" + searchWarning);
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
            var breakpointIdentitiesBeforeAdd = GetBreakpointIdentities(breakpoints);
            Invoke(breakpoints, "Add", "", "", 1, 1, "", 1,
                "", dataExpression, byteCount, "", 0, 1);
            // Native engines can publish the new breakpoint to the automation
            // collection later (often only once the debugger resumes). Add has
            // already accepted the request, so keep a lazy reference instead
            // of falsely reporting failure from a stale Count value.
            return new NativeDataBreakpointReference(breakpoints, breakpointIdentitiesBeforeAdd, dataExpression, byteCount);
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
                        if (pending.IsKnownRemoved) return true;
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
            // This is the ownership baseline captured before Add. A partial
            // snapshot must abort Add, not make old user entries appear new.
            foreach (var breakpoint in GetBreakpointSnapshot(breakpoints))
            {
                var identity = GetAutomationObjectIdentity(breakpoint);
                if (String.IsNullOrWhiteSpace(identity))
                    throw new InvalidOperationException("Cannot identify an existing Visual Studio breakpoint; no hardware watch was added.");
                identities.Add(identity);
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

        internal static bool IsDebuggerInBreakMode()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var debugger = Package.GetGlobalService(typeof(SVsShellDebugger)) as IVsDebugger;
            var modes = new DBGMODE[1];
            return debugger != null && debugger.GetMode(modes) >= 0 && modes[0] == DBGMODE.DBGMODE_Break;
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

        private static object GetSearchMember(object target, string name, StructureSearchTrace trace)
        {
            try { return trace.Call("GetMember " + name, delegate { return GetMember(target, name); }); }
            catch (Exception) { return null; }
        }

        private static IEnumerable<object> EnumerateExpressionChildren(object expression, int maximumChildren, Action checkBudget, StructureSearchTrace trace)
        {
            checkBudget();
            var children = GetSearchMember(expression, "DataMembers", trace);
            checkBudget();
            if (children == null)
            {
                yield break;
            }

            var enumerable = children as IEnumerable;
            if (enumerable != null)
            {
                foreach (var child in StructureSearchPolicy.EnumerateBoundedChildren(trace.TraceChildren(enumerable), maximumChildren, checkBudget))
                {
                    yield return child;
                }
                yield break;
            }

            var countObject = GetSearchMember(children, "Count", trace);
            var count = countObject == null ? 0 : Convert.ToInt32(countObject, CultureInfo.InvariantCulture);
            for (var index = 1; index <= count; index++)
            {
                checkBudget();
                if (index > maximumChildren)
                    throw new StructureSearchLimitException("Child enumeration limit reached.");
                object child;
                try
                {
                    child = trace.Call("Children.Item " + index, delegate { return GetItem(children, index); });
                }
                catch (Exception)
                {
                    continue;
                }
                checkBudget();
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
