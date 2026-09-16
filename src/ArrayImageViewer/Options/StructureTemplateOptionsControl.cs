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
    internal sealed class StructureTemplateOptionsControl : UserControl
    {
        private string solutionIdentity;
        private readonly string settingsPath;
        private readonly DataGridView grid;
        private BindingList<StructureTemplateItem> templates;
        private string jsonEditPath;
        private readonly ComboBox scope = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        private readonly Dictionary<string, BindingList<StructureTemplateItem>> drafts = new Dictionary<string, BindingList<StructureTemplateItem>>();
        private string editingIdentity;
        private readonly NumericUpDown searchSeconds = new NumericUpDown { Minimum = 1, Maximum = 120, Width = 55 };
        private readonly CheckBox searchDebug = new CheckBox { Text = "Debug dump: Search / Array tabs (local expressions/types)", AutoSize = true };
        private readonly Label status = new Label { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(3, 6, 3, 2) };

        public StructureTemplateOptionsControl(string solutionIdentityValue)
            : this(solutionIdentityValue, null)
        {
        }

        internal StructureTemplateOptionsControl(string solutionIdentityValue, string settingsFilePath)
        {
            settingsPath = settingsFilePath;
            solutionIdentity = String.IsNullOrWhiteSpace(solutionIdentityValue) ? "<no-solution>" : solutionIdentityValue;
            Dock = DockStyle.Fill;
            MinimumSize = new System.Drawing.Size(800, 410);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(8)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(status, 0, 3);
            status.Text = "Enter commits an edit. Save or Options OK applies changes to the viewer.";

            var help = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                MaximumSize = new System.Drawing.Size(0, 42),
                Padding = new Padding(2, 2, 2, 7),
                Text = "Map a class to RAW, Width, and Height members. Enter the live root (this, ctx, etc.) only in the Viewer capture box; stride follows Width."
            };
            var heading = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            scope.Items.Add("Current solution (overrides)");
            scope.Items.Add("Global (all solutions)");
            scope.SelectedIndex = 0;
            heading.Controls.Add(scope);
            heading.Controls.Add(help);
            layout.Controls.Add(heading, 0, 0);

            grid = new StructureTemplateGrid
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false
            };
            grid.Columns.Add(CreateColumn("ClassName", "Class / template name", 150));
            grid.Columns.Add(CreateColumn("DataAccess", "RAW data", 180));
            grid.Columns.Add(CreateColumn("WidthAccess", "Width", 110));
            grid.Columns.Add(CreateColumn("HeightAccess", "Height", 110));
            layout.Controls.Add(grid, 0, 1);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Padding = new Padding(0, 7, 0, 0),
                FlowDirection = FlowDirection.LeftToRight
            };
            buttons.Controls.Add(CreateButton("Add template", AddTemplate));
            buttons.Controls.Add(CreateButton("Remove selected", RemoveSelected));
            buttons.Controls.Add(CreateButton("Save", SaveTemplates));
            buttons.Controls.Add(CreateButton("Import JSON", ImportTemplates));
            buttons.Controls.Add(CreateButton("Export JSON", ExportTemplates));
            buttons.Controls.Add(CreateButton("Open JSON", OpenJson));
            buttons.Controls.Add(CreateButton("Load edited JSON", LoadEditedJson));
            buttons.Controls.Add(CreateButton("Reload", ReloadTemplates));
            buttons.Controls.Add(new Label { Text = "Search timeout (sec)", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            buttons.Controls.Add(searchSeconds);
            buttons.Controls.Add(searchDebug);
            buttons.Controls.Add(CreateButton("Open debug folder", OpenDebugFolder));
            layout.Controls.Add(buttons, 0, 2);

            LoadTemplates();
            scope.SelectedIndexChanged += delegate
            {
                grid.EndEdit();
                drafts[editingIdentity] = templates;
                editingIdentity = scope.SelectedIndex == 1 ? StructureTemplateStore.GlobalIdentity : solutionIdentity;
                if (!drafts.TryGetValue(editingIdentity, out templates))
                    templates = new BindingList<StructureTemplateItem>(StructureTemplateStore.LoadScope(editingIdentity, settingsPath));
                grid.DataSource = templates;
                jsonEditPath = null;
                status.Text = "Global applies to all solutions; a matching class in Current solution overrides it. Save before closing.";
            };
        }

        private static DataGridViewTextBoxColumn CreateColumn(string property, string header, int minimumWidth)
        {
            return new DataGridViewTextBoxColumn { DataPropertyName = property, HeaderText = header, MinimumWidth = minimumWidth };
        }

        private static Button CreateButton(string text, EventHandler click)
        {
            var button = new Button { Text = text, AutoSize = true, Height = 26, Margin = new Padding(3, 0, 3, 0) };
            button.Click += click;
            return button;
        }

        private void LoadTemplates()
        {
            drafts.Clear();
            editingIdentity = scope.SelectedIndex == 1 ? StructureTemplateStore.GlobalIdentity : solutionIdentity;
            templates = new BindingList<StructureTemplateItem>(StructureTemplateStore.LoadScope(editingIdentity, settingsPath));
            grid.DataSource = templates;
            searchSeconds.Value = StructureTemplateStore.LoadSearchSeconds(settingsPath);
            searchDebug.Checked = StructureTemplateStore.LoadSearchDebug(settingsPath);
            status.ForeColor = System.Drawing.SystemColors.ControlText;
            status.Text = "Enter commits an edit. Save or Options OK applies changes to the viewer.";
        }

        internal void ReloadForSolution(string identity)
        {
            grid.CancelEdit();
            if (!String.Equals(solutionIdentity, identity, StringComparison.Ordinal)) jsonEditPath = null;
            solutionIdentity = String.IsNullOrWhiteSpace(identity) ? "<no-solution>" : identity;
            LoadTemplates();
        }

        private void AddTemplate(object sender, EventArgs e)
        {
            templates.Add(new StructureTemplateItem { ClassName = "MyStructure", DataAccess = "D", WidthAccess = "W", HeightAccess = "H" });
            grid.CurrentCell = grid.Rows[grid.Rows.Count - 1].Cells[0];
            grid.Focus();
            grid.BeginEdit(true);
        }

        private void RemoveSelected(object sender, EventArgs e)
        {
            if (grid.CurrentRow != null && grid.CurrentRow.Index >= 0 && grid.CurrentRow.Index < templates.Count)
            {
                templates.RemoveAt(grid.CurrentRow.Index);
            }
        }

        private void SaveTemplates(object sender, EventArgs e)
        {
            TrySave();
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if ((keyData & Keys.KeyCode) == Keys.Enter)
            {
                grid.EndEdit();
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        internal bool TrySave()
        {
            try
            {
                if (!grid.EndEdit()) throw new InvalidOperationException("Finish the invalid cell before saving.");
                BindingContext[templates].EndCurrentEdit();
                if (!ValidateTemplateRows()) throw new ArgumentException("Check the highlighted cells. Required fields must be filled and class names must be unique.");
                drafts[editingIdentity] = templates;
                // Validate every visited scope before writing any of them.
                foreach (var draft in drafts)
                {
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in draft.Value)
                    {
                        StructureTemplateStore.Validate(item);
                        if (!names.Add(item.ClassName.Trim())) throw new ArgumentException("Duplicate class in " + draft.Key + ": " + item.ClassName);
                    }
                }
                var scopes = new Dictionary<string, IList<StructureTemplateItem>>(StringComparer.Ordinal);
                foreach (var draft in drafts) scopes.Add(draft.Key, new List<StructureTemplateItem>(draft.Value));
                StructureTemplateStore.SaveSettingsScopes(scopes, (int)searchSeconds.Value, searchDebug.Checked, settingsPath);
                status.ForeColor = System.Drawing.Color.DarkGreen;
                status.Text = "Saved edited scopes and applied to the viewer. No Reload is needed.";
                return true;
            }
            catch (Exception exception)
            {
                status.ForeColor = System.Drawing.Color.Firebrick;
                status.Text = "Not saved: " + exception.Message;
                return false;
            }
        }

        private void ReloadTemplates(object sender, EventArgs e)
        {
            LoadTemplates();
        }

        private bool ValidateTemplateRows()
        {
            bool valid = true;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow row in grid.Rows)
            {
                for (int column = 0; column < row.Cells.Count; column++)
                {
                    var cell = row.Cells[column];
                    string value = Convert.ToString(cell.Value).Trim();
                    string error = value.Length == 0 ? "Required field." :
                        column == 0 && !names.Add(value) ? "Duplicate class / template name." : String.Empty;
                    cell.ErrorText = error;
                    cell.Style.BackColor = error.Length == 0 ? System.Drawing.Color.Empty : System.Drawing.Color.MistyRose;
                    if (error.Length != 0) valid = false;
                }
            }
            return valid;
        }

        private void OpenDebugFolder(object sender, EventArgs e)
        {
            try
            {
                Directory.CreateDirectory(StructureSearchTrace.DirectoryPath);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = StructureSearchTrace.DirectoryPath, UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Cannot open debug folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ImportTemplates(object sender, EventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Array RAW structure templates (*.json)|*.json|All files (*.*)|*.*", CheckFileExists = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var imported = StructureTemplateStore.Import(dialog.FileName);
                var merged = new Dictionary<string, StructureTemplateItem>(StringComparer.OrdinalIgnoreCase);
                for (var index = 0; index < templates.Count; index++) merged[templates[index].ClassName] = templates[index];
                for (var index = 0; index < imported.Count; index++) merged[imported[index].ClassName] = imported[index];
                templates = new BindingList<StructureTemplateItem>(new List<StructureTemplateItem>(merged.Values));
                grid.DataSource = templates;
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Cannot import structure templates", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportTemplates(object sender, EventArgs e)
        {
            var dialog = new SaveFileDialog { Filter = "Array RAW structure templates (*.json)|*.json|All files (*.*)|*.*", FileName = "ArrayRawViewer.structure-templates.json", AddExtension = true, DefaultExt = ".json" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                grid.EndEdit();
                StructureTemplateStore.Export(dialog.FileName, new List<StructureTemplateItem>(templates));
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Cannot export structure templates", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenJson(object sender, EventArgs e)
        {
            try
            {
                grid.EndEdit();
                if (jsonEditPath == null)
                {
                    var directory = Path.Combine(Path.GetTempPath(), "ArrayRawViewer-template-edit");
                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
                    StructureTemplateStore.Export(path, new List<StructureTemplateItem>(templates));
                    jsonEditPath = path;
                }
                // Reopening never overwrites unsaved external edits.
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe"),
                    Arguments = "\"" + jsonEditPath + "\"",
                    UseShellExecute = true
                });
                MessageBox.Show("Edit and save the JSON in Notepad, then click Load edited JSON and Save here. This is an editing copy, not the live settings file.",
                    "Edit structure templates", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Cannot open JSON", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadEditedJson(object sender, EventArgs e)
        {
            try
            {
                if (jsonEditPath == null) throw new InvalidOperationException("Click Open JSON first.");
                var imported = StructureTemplateStore.Import(jsonEditPath);
                if (MessageBox.Show("Replace the displayed rows with the edited JSON? Click Save afterwards to apply them to this solution.",
                    "Load edited JSON", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                grid.CancelEdit();
                templates = new BindingList<StructureTemplateItem>(imported);
                grid.DataSource = templates;
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Cannot load edited JSON", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
