using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace ArrayImageViewer
{
    [System.Runtime.InteropServices.Guid("6b770c22-4f19-45d6-b254-67903f8c8f11")]
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [InstalledProductRegistration("Sensor RAW Array Viewer", "View pointer-backed sensor RAW arrays.", "0.1")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(SensorImageToolWindow), Width = 1000, Height = 780)]
    public sealed class SensorImageViewerPackage : Package, IVsDebuggerEvents
    {
        private uint debuggerEventsCookie;
        protected override void Initialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            base.Initialize();

            var commandService = GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService == null)
            {
                return;
            }

            var commandId = new CommandID(CommandIds.CommandSet, CommandIds.ShowViewer);
            commandService.AddCommand(new MenuCommand(ShowViewer, commandId));
            var selectionCommandId = new CommandID(CommandIds.CommandSet, CommandIds.ShowViewerFromSelection);
            commandService.AddCommand(new MenuCommand(ShowViewerFromSelection, selectionCommandId));

            var debugger = GetService(typeof(SVsShellDebugger)) as IVsDebugger;
            if (debugger != null)
            {
                debugger.AdviseDebuggerEvents(this, out debuggerEventsCookie);
            }
        }

        protected override void Dispose(bool disposing)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (disposing && debuggerEventsCookie != 0)
            {
                var debugger = GetService(typeof(SVsShellDebugger)) as IVsDebugger;
                if (debugger != null)
                {
                    debugger.UnadviseDebuggerEvents(debuggerEventsCookie);
                }
                debuggerEventsCookie = 0;
            }

            base.Dispose(disposing);
        }

        int IVsDebuggerEvents.OnModeChange(DBGMODE debuggerMode)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var window = FindToolWindow(typeof(SensorImageToolWindow), 0, false);
            var viewer = window == null ? null : window.Content as UI.SensorImageViewerControl;
            if (debuggerMode == DBGMODE.DBGMODE_Break)
            {
                if (viewer != null)
                {
                    viewer.DebuggerReturnedToBreakMode();
                }
            }
            else if (debuggerMode == DBGMODE.DBGMODE_Design && viewer != null)
            {
                // Native data breakpoint addresses are session-specific. Do
                // not retain an invisible viewer-owned breakpoint after the
                // debuggee exits.
                viewer.DebuggerSessionEnded();
            }

            return Microsoft.VisualStudio.VSConstants.S_OK;
        }

        private void ShowViewer(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var window = FindToolWindow(typeof(SensorImageToolWindow), 0, true);
            if (window == null || window.Frame == null)
            {
                throw new NotSupportedException("Cannot create the Sensor RAW Array Viewer window.");
            }

            var frame = (IVsWindowFrame)window.Frame;
            Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(frame.Show());
        }

        private void ShowViewerFromSelection(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ShowViewer(sender, e);

            var window = FindToolWindow(typeof(SensorImageToolWindow), 0, false);
            var viewer = window == null ? null : window.Content as UI.SensorImageViewerControl;
            if (viewer != null)
            {
                viewer.CaptureActiveEditorSelection();
            }
        }
    }
}
