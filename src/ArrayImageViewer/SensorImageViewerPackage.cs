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
    [ProvideToolWindow(typeof(SensorImageToolWindow))]
    public sealed class SensorImageViewerPackage : Package
    {
        protected override void Initialize()
        {
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
        }

        private void ShowViewer(object sender, EventArgs e)
        {
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
