using System;
using System.Reflection;
using System.Windows.Forms;
using System.IO;
using System.Runtime.InteropServices;

internal static class Program
{
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
            var type = assembly.GetType("ArrayImageViewer.Options.StructureTemplateOptionsControl", true);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            // Isolated identity: never save or overwrite the user's templates.
            using (var control = (Control)Activator.CreateInstance(type, new object[] { Path.Combine(Path.GetTempPath(), "options-enter-regression.sln") }))
            using (var form = new Form { Width = 1000, Height = 600 })
            {
                form.Controls.Add(control);
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
