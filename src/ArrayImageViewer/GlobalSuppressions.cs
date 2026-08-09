using System.Diagnostics.CodeAnalysis;

// This extension intentionally uses WPF's Dispatcher for UI handoff because
// it must run on the VS 2015 shell/.NET 4.5 baseline. Routed-event handlers
// execute on that dispatcher, and every worker path posts back before touching
// the control. The VS threading analyzer cannot infer that WPF event model.
[assembly: SuppressMessage("Usage", "VSTHRD001:Avoid legacy thread switching APIs", Scope = "type", Target = "~T:ArrayImageViewer.UI.SensorImageViewerControl", Justification = "WPF Dispatcher.BeginInvoke is the non-blocking UI handoff compatible with VS 2015.")]
[assembly: SuppressMessage("Usage", "VSTHRD010:Invoke single-threaded types on Main thread", Scope = "type", Target = "~T:ArrayImageViewer.UI.SensorImageViewerControl", Justification = "The control is owned by the WPF UI dispatcher; worker callbacks post before accessing it.")]
[assembly: SuppressMessage("Usage", "VSTHRD010:Invoke single-threaded types on Main thread", Scope = "type", Target = "~T:ArrayImageViewer.Debugging.DebugExpressionFrameReader", Justification = "Debugger automation is entered only from the UI dispatcher and checks it before obtaining shell services.")]
