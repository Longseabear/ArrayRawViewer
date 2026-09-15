using System;
using System.Reflection;
using System.Windows.Forms;
using System.IO;
using System.Collections;
using System.Runtime.InteropServices;

internal static class Program
{
    public sealed class FakeExpression
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public object[] Children = new object[0];
        public int Expansions;
        public object[] DataMembers { get { Expansions++; return Children; } }
    }

    private static void CheckStructureTraversal(Assembly assembly)
    {
        var engine = assembly.GetType("ArrayImageViewer.Debugging.DebugExpressionFrameReader", true);
        var traceType = assembly.GetType("ArrayImageViewer.Debugging.StructureSearchTrace", true);
        var target = new FakeExpression { Name = "C", Type = "ns::Target< unsigned short >" };
        var branch = new FakeExpression { Name = "buffer", Type = "B", Children = new object[] { target } };
        FakeExpression nested = target;
        for (int i = 0; i < 6; i++) nested = new FakeExpression { Name = "nested" + i, Type = "Wrapper" + i, Children = new object[] { nested } };
        branch.Children = new object[] { nested };
        var root = new FakeExpression { Name = "this", Type = "A *", Value = "0x00001234" };
        var cycle = new FakeExpression { Name = "self", Type = "A *", Value = "0x1234", Children = new object[] { root } };
        var children = new System.Collections.Generic.List<object>();
        var baseClass = new FakeExpression { Name = "Base", Type = "ns::Base", Children = new object[] { root, target } };
        children.Add(baseClass);
        for (int i = 0; i < 50; i++) children.Add(new FakeExpression { Name = "value" + i, Type = "unsigned int" });
        children.Add(cycle); children.Add(branch); root.Children = children.ToArray();
        int evaluations = 0;
        string warning = null;
        using (var trace = (IDisposable)Activator.CreateInstance(traceType, new object[] { false }))
        {
            var task = (System.Threading.Tasks.Task)engine.GetMethod("SearchStructureGraph", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
                new object[] { "this", new string[] { "Target<unsigned short>" }, 12, 512, 10,
                    new Action<string>(delegate(string value) { warning = value; }), new Func<bool>(delegate { return true; }), trace,
                    new Func<string, object>(delegate(string value) { evaluations++; return root; }),
                    new Func<System.Threading.Tasks.Task>(delegate { return System.Threading.Tasks.Task.FromResult(0); }) });
            task.GetAwaiter().GetResult();
            var result = (IList)task.GetType().GetProperty("Result").GetValue(task, null);
            if (result.Count != 1 || evaluations != 1 || target.Expansions != 0 || cycle.Expansions != 0 || baseClass.Expansions != 0 || branch.Expansions != 1 || warning != null)
                throw new Exception("Traversal must find nested target, avoid cycles/target storage, and evaluate root only once.");
        }
        Console.WriteLine("Synthetic debugger traversal: 50 scalar siblings, depth-8 template, cycle pruning, one root evaluation passed.");
    }

    private static void CheckTemplateSerialization(Assembly assembly)
    {
        var store = assembly.GetType("ArrayImageViewer.Options.StructureTemplateStore", true);
        var itemType = assembly.GetType("ArrayImageViewer.Options.StructureTemplateItem", true);
        var documentType = assembly.GetType("ArrayImageViewer.Options.StructureTemplateDocument", true);
        const BindingFlags privateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        string fixture = "{\"FormatVersion\":1,\"Templates\":[{\"ClassName\":\"CInputStream<uint16>\",\"DataAccess\":\"m_data\",\"WidthAccess\":\"m_width\",\"HeightAccess\":\"m_height\"},{\"ClassName\":\"한글Stream\",\"DataAccess\":\"buffer.data\",\"WidthAccess\":\"W\",\"HeightAccess\":\"H\"}]}";
        var deserialize = store.GetMethod("Deserialize", privateStatic);
        var serialize = store.GetMethod("Serialize", privateStatic);
        var document = deserialize.Invoke(null, new object[] { fixture });
        var json = (string)serialize.Invoke(null, new object[] { document });
        var roundtrip = deserialize.Invoke(null, new object[] { json });
        var items = (IList)documentType.GetProperty("Templates").GetValue(roundtrip, null);
        if ((int)documentType.GetProperty("FormatVersion").GetValue(roundtrip, null) != 1 || items.Count != 2)
            throw new Exception("Template document version/count was lost.");
        var originalItems = (IList)documentType.GetProperty("Templates").GetValue(document, null);
        string file = Path.Combine(Path.GetTempPath(), "array-raw-template-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            store.GetMethod("Export").Invoke(null, new object[] { file, items });
            var imported = (IList)store.GetMethod("Import").Invoke(null, new object[] { file });
            if (imported.Count != originalItems.Count) throw new Exception("Imported template count differs.");
            for (int i = 0; i < imported.Count; i++)
                foreach (string name in new[] { "ClassName", "DataAccess", "WidthAccess", "HeightAccess" })
                    if (!Object.Equals(itemType.GetProperty(name).GetValue(originalItems[i], null), itemType.GetProperty(name).GetValue(imported[i], null)))
                        throw new Exception("Template roundtrip lost " + name);
            // Empty documents are valid export/import containers too.
            items.Clear();
            store.GetMethod("Export").Invoke(null, new object[] { file, items });
            if (((IList)store.GetMethod("Import").Invoke(null, new object[] { file })).Count != 0)
                throw new Exception("Empty document roundtrip failed.");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
        Console.WriteLine("Template JSON serialization and file export/import passed; user settings untouched.");
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);
    private delegate IntPtr DialogProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern IntPtr CreateDialogIndirectParamW(IntPtr instance, IntPtr template, IntPtr parent, DialogProc proc, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr window, IntPtr parent);
    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);

    private static void CheckNativeDeactivate(Control control, Control editor, Form form)
    {
        IntPtr template = Marshal.AllocHGlobal(24);
        IntPtr dialog = IntPtr.Zero;
        DialogProc callback = delegate { return IntPtr.Zero; };
        try
        {
            Marshal.Copy(new byte[24], 0, template, 24);
            Marshal.WriteInt32(template, unchecked((int)0x90C80080)); // visible native dialog
            Marshal.WriteInt16(template, 14, 500);
            Marshal.WriteInt16(template, 16, 300);
            dialog = CreateDialogIndirectParamW(IntPtr.Zero, template, form.Handle, callback, IntPtr.Zero);
            if (dialog == IntPtr.Zero) throw new Exception("Cannot create native Options regression host.");
            SetParent(control.Handle, dialog);
            SetFocus(editor.Handle);
            // This enters DefDlgProc's saved-focus/default-button traversal,
            // the exact path seen in the hung Visual Studio native stack.
            SendMessageW(dialog, 6, IntPtr.Zero, IntPtr.Zero); // WM_ACTIVATE / WA_INACTIVE
            Console.WriteLine("Native Options host deactivated with a focused cell editor.");
        }
        finally
        {
            SetParent(control.Handle, form.Handle);
            if (dialog != IntPtr.Zero) DestroyWindow(dialog);
            Marshal.FreeHGlobal(template);
            GC.KeepAlive(callback);
        }
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string directory = Path.GetFullPath(args[0]);
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
            {
                string path = Path.Combine(directory, new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            var assembly = Assembly.LoadFrom(Path.Combine(directory, "ArrayImageViewer.dll"));
            CheckTemplateSerialization(assembly);
            CheckStructureTraversal(assembly);
            var traceType = assembly.GetType("ArrayImageViewer.Debugging.StructureSearchTrace", true);
            using (var disabledTrace = (IDisposable)Activator.CreateInstance(traceType, new object[] { false }))
                if (traceType.GetProperty("FilePath").GetValue(disabledTrace, null) != null)
                    throw new Exception("Disabled tracing must not create a dump.");
            string testDump = null;
            try
            {
                using (var trace = (IDisposable)Activator.CreateInstance(traceType, new object[] { true }))
                {
                    testDump = (string)traceType.GetProperty("FilePath").GetValue(trace, null);
                    var call = traceType.GetMethod("Call").MakeGenericMethod(typeof(int));
                    call.Invoke(trace, new object[] { "test-only-operation", new Func<int>(delegate { return 42; }) });
                    try { call.Invoke(trace, new object[] { "test-only-failure", new Func<int>(delegate { throw new InvalidOperationException("synthetic failure"); }) }); }
                    catch (TargetInvocationException) { }
                    string log;
                    using (var stream = new FileStream(testDump, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(stream)) log = reader.ReadToEnd();
                    if (!log.Contains("BEGIN test-only-operation") || !log.Contains("END test-only-operation duration=") || !log.Contains("ERROR test-only-failure"))
                        throw new Exception("Trace must flush call timing and errors before disposal.");
                }
            }
            finally { if (testDump != null) File.Delete(testDump); }
            Console.WriteLine("Search trace disabled/timing/error/flush checks passed (synthetic dump removed).");
            var type = assembly.GetType("ArrayImageViewer.Options.StructureTemplateOptionsControl", true);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            // Isolated identity: never save or overwrite the user's templates.
            using (var control = (Control)Activator.CreateInstance(type, new object[] { Path.Combine(Path.GetTempPath(), "options-enter-regression.sln") }))
            using (var form = new Form { Width = 1000, Height = 600 })
            {
                form.Controls.Add(control);
                var timeout = (NumericUpDown)type.GetField("searchSeconds", flags).GetValue(control);
                if (timeout.Minimum != 1 || timeout.Maximum != 120 || timeout.Value < 1 || timeout.Value > 120)
                    throw new Exception("Search timeout option must be bounded to 1..120 seconds.");
                var accept = new Button();
                int accepts = 0;
                accept.Click += delegate { accepts++; };
                form.Controls.Add(accept);
                form.AcceptButton = accept;
                form.Show();
                type.GetMethod("AddTemplate", flags).Invoke(control, new object[] { null, EventArgs.Empty });
                var grid = (DataGridView)type.GetField("grid", flags).GetValue(control);
                if (!grid.ContainsFocus) throw new Exception("Add template did not focus its editor.");
                var editor = grid.EditingControl as TextBox;
                if (editor == null) throw new Exception("New row is not editing.");
                editor.Text = "afaf";
                // Native Options dialogs must be able to recurse all the way
                // back to the focused editing textbox while walking controls.
                for (Control parent = editor.Parent; parent != null && parent != form; parent = parent.Parent)
                {
                    int style = GetWindowLong(parent.Handle, -20);
                    Console.WriteLine(parent.GetType().Name + " ExStyle=" + style.ToString("X"));
                    if ((style & 0x10000) == 0 && !(args.Length > 1 && args[1] == "--native-only"))
                        throw new Exception("Native dialog navigation cannot reach the editor through " + parent.GetType().Name);
                }
                CheckNativeDeactivate(control, editor, form);
                int rows = grid.Rows.Count;
                bool consumed = (bool)grid.GetType().GetMethod("ProcessDialogKey", flags).Invoke(grid, new object[] { Keys.Enter });
                if (!consumed || grid.IsCurrentCellInEditMode || grid.Rows.Count != rows || accepts != 0 ||
                    Convert.ToString(grid.CurrentCell.Value) != "afaf") throw new Exception("Enter did not commit only the class-name cell.");
                consumed = (bool)grid.GetType().GetMethod("ProcessDataGridViewKey", flags).Invoke(grid, new object[] { new KeyEventArgs(Keys.Enter) });
                if (!consumed || grid.Rows.Count != rows || accepts != 0) throw new Exception("Repeated Enter escaped to the host.");
                var pageType = assembly.GetType("ArrayImageViewer.Options.StructureTemplateOptionsPage", true);
                var helperType = pageType.BaseType.Assembly.GetType("Microsoft.VisualStudio.Shell.ThreadHelper");
                if (helperType == null) helperType = Assembly.LoadFrom(Path.Combine(directory, "Microsoft.VisualStudio.Shell.Framework.dll")).GetType("Microsoft.VisualStudio.Shell.ThreadHelper");
                // Supply only the SDK's UI-thread context in this standalone
                // process; no Visual Studio services or user settings are mocked.
                var contextField = helperType.GetField("_joinableTaskContextCache", BindingFlags.Static | BindingFlags.NonPublic);
                contextField.SetValue(null, Activator.CreateInstance(contextField.FieldType));
                using (var page = (IDisposable)Activator.CreateInstance(pageType))
                {
                    pageType.GetField("control", flags).SetValue(page, control);
                    IntPtr originalHandle = control.Handle;
                    pageType.GetMethod("OnClosed", flags).Invoke(page, new object[] { EventArgs.Empty });
                    if (control.IsDisposed)
                        throw new Exception("Closing Options disposed the window still cached by the shell.");
                    var reopened = (Control)pageType.GetProperty("Window", flags).GetValue(page, null);
                    if (!Object.ReferenceEquals(control, reopened) || reopened.Handle != originalHandle)
                        throw new Exception("Reopening Options replaced the shell-cached window.");
                    grid.CurrentCell = grid.Rows[0].Cells[1];
                    if (!grid.BeginEdit(true)) throw new Exception("Reopened Options cannot edit RAW access.");
                    ((TextBox)grid.EditingControl).Text = "m_data";
                    grid.EndEdit();
                    for (int cycle = 0; cycle < 3; cycle++)
                    {
                        pageType.GetMethod("OnClosed", flags).Invoke(page, new object[] { EventArgs.Empty });
                        pageType.GetMethod("OnActivate", flags).Invoke(page, new object[] { new System.ComponentModel.CancelEventArgs() });
                        if (control.IsDisposed || control.Handle != originalHandle)
                            throw new Exception("Reactivation destroyed the cached window.");
                        type.GetMethod("AddTemplate", flags).Invoke(control, new object[] { null, EventArgs.Empty });
                        ((TextBox)grid.EditingControl).Text = "afaf";
                        grid.GetType().GetMethod("ProcessDialogKey", flags).Invoke(grid, new object[] { Keys.Enter });
                        if (Convert.ToString(grid.CurrentCell.Value) != "afaf")
                            throw new Exception("Cannot edit class name after reactivation.");
                    }
                    Console.WriteLine("Options close/reopen preserves its window and allows editing.");
                }
                form.Close();
            }
            Console.WriteLine("New template -> afaf -> Enter: committed; no extra row or host accept. No settings saved.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
