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

    internal sealed class StructureTemplateOptionsControl : UserControl
    {
        private const int FormatVersion = 1;
        private string solutionIdentity;
        private readonly DataGridView grid;
        private BindingList<StructureTemplateItem> templates;
        private string jsonEditPath;
        private readonly NumericUpDown searchSeconds = new NumericUpDown { Minimum = 1, Maximum = 120, Width = 55 };

        public StructureTemplateOptionsControl(string solutionIdentityValue)
        {
            solutionIdentity = String.IsNullOrWhiteSpace(solutionIdentityValue) ? "<no-solution>" : solutionIdentityValue;
            Dock = DockStyle.Fill;
            MinimumSize = new System.Drawing.Size(800, 410);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(8)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);

            var help = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                MaximumSize = new System.Drawing.Size(0, 42),
                Padding = new Padding(2, 2, 2, 7),
                Text = "Map a class to RAW, Width, and Height members. Enter the live root (this, ctx, etc.) only in the Viewer capture box; stride follows Width."
            };
            layout.Controls.Add(help, 0, 0);

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
            searchSeconds.Value = StructureTemplateStore.LoadSearchSeconds();
            buttons.Controls.Add(searchSeconds);
            layout.Controls.Add(buttons, 0, 2);

            LoadTemplates();
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
            templates = new BindingList<StructureTemplateItem>(StructureTemplateStore.Load(solutionIdentity));
            grid.DataSource = templates;
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
            try
            {
                grid.EndEdit();
                StructureTemplateStore.Replace(solutionIdentity, new List<StructureTemplateItem>(templates));
                StructureTemplateStore.SaveSearchSeconds((int)searchSeconds.Value);
                MessageBox.Show("Structure templates saved for the active solution.", "Array RAW Viewer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Cannot save structure templates", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ReloadTemplates(object sender, EventArgs e)
        {
            LoadTemplates();
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

    [DataContract]
    internal sealed class StructureTemplateItem
    {
        [DataMember(Name = "ClassName")]
        public string ClassName { get; set; }
        [DataMember(Name = "DataAccess")]
        public string DataAccess { get; set; }
        [DataMember(Name = "WidthAccess")]
        public string WidthAccess { get; set; }
        [DataMember(Name = "HeightAccess")]
        public string HeightAccess { get; set; }
    }

    [DataContract]
    internal sealed class StructureTemplateDocument
    {
        [DataMember(Name = "FormatVersion")]
        public int FormatVersion { get; set; }
        [DataMember(Name = "Templates")]
        public List<StructureTemplateItem> Templates { get; set; }
    }

    internal static class StructureTemplateStore
    {
        public static int LoadSearchSeconds()
        {
            string value;
            int seconds;
            return ReadRecords().TryGetValue("search-timeout-seconds", out value) &&
                Int32.TryParse(value, out seconds) && seconds >= 1 && seconds <= 120 ? seconds : 10;
        }

        public static void SaveSearchSeconds(int seconds)
        {
            if (seconds < 1 || seconds > 120) throw new ArgumentOutOfRangeException("seconds");
            var records = ReadRecords();
            records["search-timeout-seconds"] = seconds.ToString(CultureInfo.InvariantCulture);
            WriteRecords(records);
        }

        private static string PathValue
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArrayImageViewer", "structure-templates-v1.txt"); }
        }

        public static List<StructureTemplateItem> Load(string solutionIdentity)
        {
            var result = new List<StructureTemplateItem>();
            var prefix = solutionIdentity + "\nstructure-template\n";
            foreach (var pair in ReadRecords())
            {
                if (!pair.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var document = Deserialize(pair.Value);
                if (document == null || document.FormatVersion != 1 || document.Templates == null || document.Templates.Count != 1) continue;
                try { Validate(document.Templates[0]); result.Add(document.Templates[0]); } catch (Exception) { }
            }

            // Make the shipped native debuggee immediately usable. This is a
            // display-only starter row until the user presses Save, and is
            // scoped to this repository's sample solution so it never
            // pollutes an unrelated solution's template list.
            if (result.Count == 0 && IsArrayImageViewerSampleSolution(solutionIdentity))
            {
                result.Add(new StructureTemplateItem
                {
                    ClassName = "ImageStream",
                    DataAccess = "m_data",
                    WidthAccess = "m_width",
                    HeightAccess = "m_height"
                });
            }
            return result;
        }

        private static bool IsArrayImageViewerSampleSolution(string solutionIdentity)
        {
            return !String.IsNullOrWhiteSpace(solutionIdentity) &&
                solutionIdentity.IndexOfAny(Path.GetInvalidPathChars()) < 0 &&
                String.Equals(Path.GetFileName(solutionIdentity), "ArrayImageViewer.sln", StringComparison.OrdinalIgnoreCase);
        }

        public static void Replace(string solutionIdentity, IList<StructureTemplateItem> templates)
        {
            var records = ReadRecords();
            var prefix = solutionIdentity + "\nstructure-template\n";
            var keys = new List<string>();
            foreach (var key in records.Keys) if (key.StartsWith(prefix, StringComparison.Ordinal)) keys.Add(key);
            for (var index = 0; index < keys.Count; index++) records.Remove(keys[index]);
            for (var index = 0; index < templates.Count; index++)
            {
                Validate(templates[index]);
                records[prefix + templates[index].ClassName] = Serialize(new StructureTemplateDocument { FormatVersion = 1, Templates = new List<StructureTemplateItem> { templates[index] } });
            }
            WriteRecords(records);
        }

        public static List<StructureTemplateItem> Import(string fileName)
        {
            var document = Deserialize(File.ReadAllText(fileName, Encoding.UTF8));
            if (document == null || document.FormatVersion != 1 || document.Templates == null) throw new InvalidDataException("This is not a supported Array RAW structure-template JSON file.");
            for (var index = 0; index < document.Templates.Count; index++) Validate(document.Templates[index]);
            return document.Templates;
        }

        public static void Export(string fileName, IList<StructureTemplateItem> templates)
        {
            for (var index = 0; index < templates.Count; index++) Validate(templates[index]);
            File.WriteAllText(fileName, Serialize(new StructureTemplateDocument { FormatVersion = 1, Templates = new List<StructureTemplateItem>(templates) }), Encoding.UTF8);
        }

        private static void Validate(StructureTemplateItem item)
        {
            if (item == null || String.IsNullOrWhiteSpace(item.ClassName)) throw new ArgumentException("Every template requires a class/template name.");
            if (String.IsNullOrWhiteSpace(item.DataAccess)) throw new ArgumentException("Template '" + item.ClassName + "' requires RAW data access.");
            if (String.IsNullOrWhiteSpace(item.WidthAccess)) throw new ArgumentException("Template '" + item.ClassName + "' requires Width access.");
            if (String.IsNullOrWhiteSpace(item.HeightAccess)) throw new ArgumentException("Template '" + item.ClassName + "' requires Height access.");
        }

        private static string Serialize(StructureTemplateDocument document)
        {
            using (var stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(StructureTemplateDocument)).WriteObject(stream, document); return Encoding.UTF8.GetString(stream.ToArray()); }
        }

        private static StructureTemplateDocument Deserialize(string value)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(value ?? String.Empty))) { return new DataContractJsonSerializer(typeof(StructureTemplateDocument)).ReadObject(stream) as StructureTemplateDocument; }
        }

        private static Dictionary<string, string> ReadRecords()
        {
            var records = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!File.Exists(PathValue)) return records;
            foreach (var line in File.ReadAllLines(PathValue))
            {
                var tab = line.IndexOf('\t');
                if (tab > 0) records[Decode(line.Substring(0, tab))] = line.Substring(tab + 1);
            }
            return records;
        }

        private static void WriteRecords(Dictionary<string, string> records)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathValue));
            var lines = new List<string>();
            foreach (var pair in records) lines.Add(Encode(pair.Key) + "\t" + pair.Value);
            File.WriteAllLines(PathValue, lines.ToArray());
        }

        private static string Encode(string value) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? String.Empty)); }
        private static string Decode(string value) { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
    }
}
