using System;
using Microsoft.VisualStudio.Shell;
using ArrayImageViewer.UI;

namespace ArrayImageViewer
{
    [System.Runtime.InteropServices.Guid("e84e06c2-e1cd-4fcd-b0f4-312b10296fcf")]
    public sealed class SensorImageToolWindow : ToolWindowPane
    {
        public SensorImageToolWindow()
            : base(null)
        {
            Caption = "Sensor RAW Array Viewer";
            Content = new SensorImageViewerControl();
        }
    }
}
