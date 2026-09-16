using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class TemplateOptionsChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(Assembly assembly)
    {
        var pageType = assembly.GetType("ArrayImageViewer.Options.StructureTemplateOptionsPage", true);
        var controlType = assembly.GetType("ArrayImageViewer.Options.StructureTemplateOptionsControl", true);
        var helper = pageType.BaseType.Assembly.GetType("Microsoft.VisualStudio.Shell.ThreadHelper") ??
            Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(assembly.Location), "Microsoft.VisualStudio.Shell.Framework.dll")).GetType("Microsoft.VisualStudio.Shell.ThreadHelper");
        var context = helper.GetField("_joinableTaskContextCache", BindingFlags.Static | BindingFlags.NonPublic);
        if (context.GetValue(null) == null) context.SetValue(null, Activator.CreateInstance(context.FieldType));
        int failures = 0;
        Check("Global templates inherit, solution overrides, and both scope drafts save", delegate
        {
            WithIsolatedControl(controlType, delegate(Control control, string path)
            {
                using (var form = new Form())
                {
                form.Controls.Add(control); form.Show();
                var scope = (ComboBox)controlType.GetField("scope", Private).GetValue(control);
                var grid = (DataGridView)controlType.GetField("grid", Private).GetValue(control);
                var add = controlType.GetMethod("AddTemplate", Private);
                scope.SelectedIndex = 1;
                add.Invoke(control, new object[] { null, EventArgs.Empty });
                grid.EndEdit();
                scope.SelectedIndex = 0;
                add.Invoke(control, new object[] { null, EventArgs.Empty });
                grid.EndEdit();
                grid.Rows[0].Cells[1].Value = "localData";
                var store = assembly.GetType("ArrayImageViewer.Options.StructureTemplateStore", true);
                var load = store.GetMethod("Load");
                var loadScope = store.GetMethod("LoadScope");
                bool notifiedBeforeCommit = false;
                EventHandler handler = delegate
                {
                    var committedLocal = (System.Collections.IList)loadScope.Invoke(null, new object[] { "test.sln", path });
                    var committedGlobal = (System.Collections.IList)loadScope.Invoke(null, new object[] { "<global>", path });
                    notifiedBeforeCommit |= committedLocal.Count != 1 || committedGlobal.Count != 1;
                };
                var changed = store.GetEvent("SettingsChanged");
                changed.AddEventHandler(null, handler);
                try { RequireSave(controlType, control); }
                finally { changed.RemoveEventHandler(null, handler); }
                if (notifiedBeforeCommit) throw new Exception("Viewer was notified before both edited scopes were committed.");
                var local = (System.Collections.IList)load.Invoke(null, new object[] { "test.sln", path });
                var other = (System.Collections.IList)load.Invoke(null, new object[] { "other.sln", path });
                if (local.Count != 1 || other.Count != 1) throw new Exception("Global inheritance/override count incorrect.");
                if ((string)local[0].GetType().GetProperty("DataAccess").GetValue(local[0], null) != "localData" ||
                    (string)other[0].GetType().GetProperty("DataAccess").GetValue(other[0], null) != "D") throw new Exception("Wrong scope precedence.");
                scope.SelectedIndex = 1;
                if (grid.Rows.Count != 1) throw new Exception("Global draft was lost.");
                }
            });
        }, ref failures);
        Check("Save then OK does not write or notify twice", delegate
        {
            WithIsolatedControl(controlType, delegate(Control control, string path)
            {
                var store = assembly.GetType("ArrayImageViewer.Options.StructureTemplateStore", true);
                var changed = store.GetEvent("SettingsChanged");
                int notifications = 0;
                bool completeCommit = true;
                EventHandler handler = delegate
                {
                    notifications++;
                    completeCommit = completeCommit && File.ReadAllText(path).Contains("\t19");
                };
                changed.AddEventHandler(null, handler);
                try
                {
                    using (var form = new Form())
                    {
                        form.Controls.Add(control); form.Show();
                        ((NumericUpDown)controlType.GetField("searchSeconds", Private).GetValue(control)).Value = 19;
                        var save = controlType.GetMethod("TrySave", Private);
                        if (!(bool)save.Invoke(control, null) || !(bool)save.Invoke(control, null) || notifications != 1 || !completeCommit)
                            throw new Exception("Unchanged Save/OK performed redundant writes or refresh notifications.");
                    }
                }
                finally { changed.RemoveEventHandler(null, handler); }
            });
        }, ref failures);
        Check("Reopen discards cancelled timeout edits", delegate
        {
            WithIsolatedControl(controlType, delegate(Control control, string path)
            {
                var timeout = (NumericUpDown)controlType.GetField("searchSeconds", Private).GetValue(control);
                timeout.Value = 19;
                controlType.GetMethod("TrySave", Private).Invoke(control, null);
                timeout.Value = 7;
                controlType.GetMethod("ReloadForSolution", Private).Invoke(control, new object[] { "test.sln" });
                if (timeout.Value != 19) throw new Exception("Cancelled search settings survived reopening Options.");
            });
        }, ref failures);
        Check("Explicitly removing the sample template stays empty after reopen", delegate
        {
            WithIsolatedControl(controlType, delegate(Control control, string path)
            {
                string sample = Path.Combine(Path.GetTempPath(), "ArrayImageViewer.sln");
                controlType.GetMethod("ReloadForSolution", Private).Invoke(control, new object[] { sample });
                var grid = (DataGridView)controlType.GetField("grid", Private).GetValue(control);
                var list = (System.Collections.IList)controlType.GetField("templates", Private).GetValue(control);
                list.Clear();
                if (!(bool)controlType.GetMethod("TrySave", Private).Invoke(control, null)) throw new Exception("Cannot save an empty template set.");
                controlType.GetMethod("ReloadForSolution", Private).Invoke(control, new object[] { sample });
                if (grid.Rows.Count != 0) throw new Exception("Deleted starter template was resurrected.");
            });
        }, ref failures);
        Check("Options OK commits an active editor and all settings together", delegate
        {
            WithIsolatedControl(controlType, delegate(Control control, string path)
            {
                using (var form = new Form())
                using (var page = (IDisposable)Activator.CreateInstance(pageType))
                {
                    form.Controls.Add(control); form.Show();
                    pageType.GetField("control", Private).SetValue(page, control);
                    controlType.GetMethod("AddTemplate", Private).Invoke(control, new object[] { null, EventArgs.Empty });
                    var grid = (DataGridView)controlType.GetField("grid", Private).GetValue(control);
                    ((TextBox)grid.EditingControl).Text = "Stream<unsigned short>";
                    ((NumericUpDown)controlType.GetField("searchSeconds", Private).GetValue(control)).Value = 17;
                    var applyType = pageType.BaseType.GetNestedType("PageApplyEventArgs", BindingFlags.Public | BindingFlags.NonPublic);
                    var apply = Activator.CreateInstance(applyType, true);
                    pageType.GetMethod("OnApply", Private).Invoke(page, new[] { apply });
                    if (!File.Exists(path) || !File.ReadAllText(path).Contains("Stream<unsigned short>") ||
                        !File.ReadAllText(path).Contains("\t17")) throw new Exception("OK lost the editor value or search settings.");
                    string saved = File.ReadAllText(path);
                    grid.Rows[0].Cells[1].Value = "";
                    pageType.GetMethod("OnApply", Private).Invoke(page, new[] { apply });
                    if (Convert.ToString(applyType.GetProperty("ApplyBehavior").GetValue(apply, null)) == "Apply" || File.ReadAllText(path) != saved)
                        throw new Exception("Invalid edits changed the persisted settings.");
                    if (String.IsNullOrEmpty(grid.Rows[0].Cells[1].ErrorText)) throw new Exception("Invalid RAW access has no cell-level feedback.");
                }
            });
        }, ref failures);
        Check("Cancel does not save draft template edits", delegate
        {
            WithIsolatedControl(controlType, delegate(Control control, string path)
            {
                using (var page = (IDisposable)Activator.CreateInstance(pageType))
                {
                    pageType.GetField("control", Private).SetValue(page, control);
                    controlType.GetMethod("AddTemplate", Private).Invoke(control, new object[] { null, EventArgs.Empty });
                    pageType.GetMethod("OnClosed", Private).Invoke(page, new object[] { EventArgs.Empty });
                    if (File.Exists(path)) throw new Exception("Closing without Apply persisted the draft.");
                }
            });
        }, ref failures);
        Check("Duplicate template names do not silently overwrite each other", delegate
        {
            WithIsolatedControl(controlType, delegate(Control control, string path)
            {
                using (var form = new Form())
                {
                    form.Controls.Add(control); form.Show();
                    for (int i = 0; i < 2; i++) controlType.GetMethod("AddTemplate", Private).Invoke(control, new object[] { null, EventArgs.Empty });
                    bool saved = (bool)controlType.GetMethod("TrySave", Private).Invoke(control, null);
                    if (saved || File.Exists(path)) throw new Exception("Duplicate class definitions were silently collapsed.");
                }
            });
        }, ref failures);
        Check("Options OK refuses an incomplete template instead of closing without saving", delegate
        {
            using (var control = (Control)Activator.CreateInstance(controlType, new object[] { "options-validation-test.sln" }))
            using (var page = (IDisposable)Activator.CreateInstance(pageType))
            using (var form = new Form())
            {
                form.Controls.Add(control);
                form.Show();
                pageType.GetField("control", Private).SetValue(page, control);
                controlType.GetMethod("AddTemplate", Private).Invoke(control, new object[] { null, EventArgs.Empty });
                var grid = (DataGridView)controlType.GetField("grid", Private).GetValue(control);
                grid.EndEdit();
                grid.Rows[0].Cells[1].Value = "";
                var applyType = pageType.BaseType.GetNestedType("PageApplyEventArgs", BindingFlags.Public | BindingFlags.NonPublic);
                var apply = Activator.CreateInstance(applyType, true);
                pageType.GetMethod("OnApply", Private).Invoke(page, new object[] { apply });
                if (Convert.ToString(applyType.GetProperty("ApplyBehavior").GetValue(apply, null)) == "Apply")
                    throw new Exception("Options accepted an invalid template; the default OK path did not validate/save it.");
            }
        }, ref failures);
        Check("Enter in the timeout field stays in Options", delegate
        {
            using (var control = (Control)Activator.CreateInstance(controlType, new object[] { "options-enter-test.sln" }))
            using (var form = new Form())
            {
                int accepts = 0;
                var accept = new Button();
                accept.Click += delegate { accepts++; };
                form.AcceptButton = accept;
                form.Controls.Add(control); form.Controls.Add(accept); form.Show();
                ((NumericUpDown)controlType.GetField("searchSeconds", Private).GetValue(control)).Focus();
                bool handled = (bool)controlType.GetMethod("ProcessDialogKey", Private).Invoke(control, new object[] { Keys.Enter });
                if (!handled || accepts != 0) throw new Exception("Enter escaped the Options editor and reached the host OK button.");
            }
        }, ref failures);
        if (failures != 0) throw new Exception(failures + " Options workflow regression(s) failed.");
    }

    private static void Check(string name, Action action, ref int failures)
    {
        try { action(); Console.WriteLine("PASS: " + name); }
        catch (Exception exception) { failures++; Console.WriteLine("FAIL: " + name + "\n" + exception); }
    }

    private static void RequireSave(Type controlType, Control control)
    {
        if ((bool)controlType.GetMethod("TrySave", Private).Invoke(control, null)) return;
        var status = (Label)controlType.GetField("status", Private).GetValue(control);
        var grid = (DataGridView)controlType.GetField("grid", Private).GetValue(control);
        string detail = status.Text;
        foreach (DataGridViewRow row in grid.Rows)
            foreach (DataGridViewCell cell in row.Cells)
                if (!String.IsNullOrEmpty(cell.ErrorText))
                    detail += " | row " + row.Index + ", " + grid.Columns[cell.ColumnIndex].DataPropertyName +
                        "='" + Convert.ToString(cell.Value) + "': " + cell.ErrorText;
        throw new Exception("Save failed: " + detail);
    }

    private static void WithIsolatedControl(Type type, Action<Control, string> test)
    {
        var constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(string), typeof(string) }, null);
        if (constructor == null) throw new Exception("Options editor has no isolated settings-file boundary; save transaction cannot be tested without touching user settings.");
        string directory = Path.Combine(Path.GetTempPath(), "array-options-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "templates.txt");
        try
        {
            using (var control = (Control)constructor.Invoke(new object[] { "test.sln", path })) test(control, path);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
