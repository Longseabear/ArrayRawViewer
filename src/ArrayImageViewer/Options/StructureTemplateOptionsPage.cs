using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using ArrayImageViewer.Debugging;

namespace ArrayImageViewer.Options
{
    // Standard Visual Studio Tools > Options page. Templates are keyed by the
    // active solution and are intentionally configuration-only: no debugger
    // samples or addresses are stored here.
    // Visual Studio creates option pages by reflection from the pkgdef entry.
    // Keep the page public and give it a stable GUID; internal pages can be
    // registered during build yet be omitted from the Options tree on newer
    // shells.
    [Guid("2E986271-94F2-3739-AAB1-E514B88E8DD9")]
    public sealed class StructureTemplateOptionsPage : DialogPage
    {
        private StructureTemplateOptionsControl control;
        private bool refreshOnActivate = true;

        protected override IWin32Window Window
        {
            get
            {
                // The shell requests this window for keyboard routing as well
                // as activation. Never create/rebind controls during routing.
                if (control == null || control.IsDisposed)
                    control = new StructureTemplateOptionsControl(DebugExpressionFrameReader.GetActiveSolutionIdentity());
                return control;
            }
        }

        protected override void OnActivate(CancelEventArgs e)
        {
            base.OnActivate(e);
            if (refreshOnActivate)
            {
                var pageControl = (StructureTemplateOptionsControl)Window;
                pageControl.ReloadForSolution(DebugExpressionFrameReader.GetActiveSolutionIdentity());
                refreshOnActivate = false;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // DialogPage/the shell retains the hosted window between openings.
            // Closing Options is NOT disposal: destroying this HWND leaves the
            // cached property-page host pointing at a dead control on reopen.
            base.OnClosed(e);
            refreshOnActivate = true;
        }

        protected override void OnApply(PageApplyEventArgs e)
        {
            base.OnApply(e);
            if (control != null && !control.IsDisposed && !control.TrySave())
                e.ApplyBehavior = ApplyKind.CancelNoNavigate;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && control != null)
            {
                control.Dispose();
                control = null;
            }
            base.Dispose(disposing);
        }
    }
}
