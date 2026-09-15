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
    internal sealed class StructureTemplateGrid : DataGridView
    {
        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                // Tools > Options is a native property sheet. Its focus/default-
                // button traversal must be able to return to our nested editing
                // textbox. Without this flag it skips the grid's children and
                // can loop forever in USER32!xxxRemoveDefaultButton on deactivate.
                parameters.ExStyle |= 0x00010000; // WS_EX_CONTROLPARENT
                return parameters;
            }
        }

        private bool CommitEnter(Keys keyData)
        {
            if ((keyData & Keys.KeyCode) != Keys.Enter) return false;
            // Consume Enter even when validation fails. Do not let the host
            // invoke its default button or re-enter the Add-template handler.
            EndEdit();
            return true;
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            return CommitEnter(keyData) || base.ProcessDialogKey(keyData);
        }

        protected override bool ProcessDataGridViewKey(KeyEventArgs e)
        {
            return CommitEnter(e.KeyData) || base.ProcessDataGridViewKey(e);
        }
    }
}
