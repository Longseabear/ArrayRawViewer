using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// Standalone host for the real viewer, not a mockup. No debugger reads or
// profile writes: deterministic samples let layout be reviewed without VS restart.
internal static class Program
{
    private static object viewer;
    private static Type viewerType;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            string directory = Path.GetFullPath(args[0]);
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
            {
                string file = Path.Combine(directory, new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(file) ? Assembly.LoadFrom(file) : null;
            };
            var assembly = Assembly.LoadFrom(Path.Combine(directory, "ArrayImageViewer.dll"));
            viewerType = assembly.GetType("ArrayImageViewer.UI.SensorImageViewerControl", true);
            viewer = Activator.CreateInstance(viewerType, true);
            Set("isApplyingProfile", true);
            ((CheckBox)Get("rememberForSolution")).IsChecked = false;
            ((CheckBox)Get("autoUpdate")).IsChecked = false;
            foreach (string key in new[] { "width", "height", "stride", "renderWidth", "renderHeight" })
                ((TextBox)Get(key)).Text = key == "height" || key == "renderHeight" ? "48" : "64";
            ((TextBox)Get("expression")).Text = "imageSimulator.output.m_data";
            var configuration = Activator.CreateInstance(assembly.GetType("ArrayImageViewer.Core.FrameConfiguration"), new object[] {
                64, 48, 64, 13, 0, false,
                Enum.Parse(assembly.GetType("ArrayImageViewer.Core.PixelOrder"), "GRFirst"),
                Enum.Parse(assembly.GetType("ArrayImageViewer.Core.PixelType"), "Bayer"),
                Enum.Parse(assembly.GetType("ArrayImageViewer.Core.VisualizeChannel"), "BayerRaw") });
            var samples = new long[64 * 48];
            for (int y = 0; y < 48; y++) for (int x = 0; x < 64; x++) samples[y * 64 + x] = 400 + x * 70 + y * 30;
            var buffer = Activator.CreateInstance(assembly.GetType("ArrayImageViewer.Core.FrameBuffer"), new object[] { configuration, samples });
            var window = new Window { Title = "Array RAW Viewer — Workspace preview", Width = 1050, Height = 780, Content = viewer };
            window.Loaded += delegate
            {
                viewerType.GetMethod("ApplyLoadedFrame", Private).Invoke(viewer, new object[] { buffer, 64, 48, 32, 24, false, true, "imageSimulator.output.m_data" });
                viewerType.GetMethod("FitLoadedView", Private).Invoke(viewer, new object[] { null, null });
                if (args.Length > 1 && args[1] == "--verify")
                {
                    window.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        try { Verify(window); Console.WriteLine("Workspace layout checks passed (600 / 900 / 1200 px)."); }
                        catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
                        finally { window.Close(); }
                    }));
                }
            };
            new Application().Run(window);
        }
        catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
    }
    private static object Get(string name) { return viewerType.GetField(name, Private).GetValue(viewer); }
    private static void Set(string name, object value) { viewerType.GetField(name, Private).SetValue(viewer, value); }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            var dependency = child as DependencyObject;
            if (dependency != null) foreach (var item in Descendants(dependency)) yield return item;
        }
    }
    private static Button Button(string caption)
    {
        foreach (var item in Descendants((DependencyObject)viewer))
        {
            var button = item as Button;
            if (button != null && String.Equals(button.Content as string, caption)) return button;
        }
        throw new Exception("Missing command: " + caption);
    }
    private static void Verify(Window window)
    {
        ((TextBox)Get("renderWidth")).Text = "4096";
        ((TextBox)Get("renderHeight")).Text = "3072";
        var generation = Get("renderGeneration");
        Button("Center").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if (!Equals(generation, Get("renderGeneration"))) throw new Exception("Center must not start a read/render.");
        if (((TextBox)Get("status")).Text.Contains("Cannot")) throw new Exception("Center failed with oversized View dimensions.");
        if (!Button("Watch Go To X/Y").IsVisible) throw new Exception("Watch must be visible by default.");
        var caption = viewerType.GetMethod("CompactTabCaption", BindingFlags.Static | BindingFlags.NonPublic);
        if ((string)caption.Invoke(null, new object[] { "(&(((imageSimulator).input_aux_stream)[0]))->m_data" }) != "input_aux_stream[0].m_data")
            throw new Exception("Array index lost from tab caption.");
        foreach (int size in new[] { 600, 900, 1200 })
        {
            window.Width = size;
            window.UpdateLayout();
            if (((ScrollViewer)Get("scrollViewer")).ActualHeight < 230) throw new Exception("Image has insufficient height in single-window viewer.");
            foreach (string name in new[] { "selectedX", "selectedY", "roiWidth", "roiHeight", "visualizeChannel" })
            {
                var field = (FrameworkElement)Get(name);
                var point = field.TranslatePoint(new Point(), (UIElement)viewer);
                if (!field.IsVisible || point.X < 0 || point.X + field.ActualWidth > ((FrameworkElement)viewer).ActualWidth + 1)
                    throw new Exception("Clipped inspection control: " + name + " at " + size);
            }
            double imageHeight = ((ScrollViewer)Get("scrollViewer")).ActualHeight;
            string originalExpression = ((TextBox)Get("expression")).Text;
            string originalWidth = ((TextBox)Get("width")).Text;
            foreach (string tabName in new[] { "Structure", "Profiles", "Frame" })
            {
                Button(tabName).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                window.UpdateLayout();
                if (Math.Abs(((ScrollViewer)Get("scrollViewer")).ActualHeight - imageHeight) > 1)
                    throw new Exception("Settings tab changed image height.");
            }
            if (((TextBox)Get("expression")).Text != originalExpression || ((TextBox)Get("width")).Text != originalWidth)
                throw new Exception("Settings tab switching changed capture settings.");
            if (!((Canvas)Get("navigatorCanvas")).IsVisible) throw new Exception("Map must be visible by default.");
            Button("Frame map").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            window.UpdateLayout();
            if (((Canvas)Get("navigatorCanvas")).IsVisible) throw new Exception("Map failed to hide.");
            Button("Frame map").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        }
        if (Button("Export ▾").ContextMenu.Items.Count != 4) throw new Exception("Export scopes missing.");
        Button("Stats").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if (((StackPanel)Get("statisticsPanel")).Visibility != Visibility.Visible) throw new Exception("Statistics failed to open.");
        Button("Hide stats").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        VerifyWatchNavigation(window);
        VerifyCompletionFocus(window);
    }

    private static void VerifyCompletionFocus(Window window)
    {
        var field = (TextBox)Get("expression");
        var popup = (System.Windows.Controls.Primitives.Popup)Get("expressionSuggestions");
        var candidates = (System.Collections.IList)Get("pointerCandidates");
        var pointerType = viewerType.Assembly.GetType("ArrayImageViewer.Debugging.DebugExpressionFrameReader+PointerExpression", true);
        candidates.Clear();
        candidates.Add(Activator.CreateInstance(pointerType, new object[] { "inputBuffer", "unsigned int *" }));
        Set("isApplyingProfile", true);
        field.Text = "input";
        window.Activate();
        field.Focus();
        viewerType.GetMethod("UpdateExpressionSuggestions", Private).Invoke(viewer, null);
        if (popup.IsOpen) throw new Exception("Focus restoration opened completion without typing.");
        Set("isApplyingProfile", false);
        field.Text = "inputB";
        window.UpdateLayout();
        if (!popup.IsOpen) throw new Exception("Typing failed to open completion.");
        if (!popup.StaysOpen || System.Windows.Input.Mouse.Captured != null)
            throw new Exception("Completion captures the mouse and can swallow the first Watch click.");
        var mouseDown = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
        {
            RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent
        };
        Button("Watch Next X").RaiseEvent(mouseDown);
        if (mouseDown.Handled || popup.IsOpen) throw new Exception("Outside click must dismiss completion without consuming the button input.");
        Set("isApplyingProfile", true);
        Console.WriteLine("Completion focus, typing and non-consuming Watch mouse-down checks passed.");
    }

    private static void VerifyWatchNavigation(Window window)
    {
        var timer = (System.Windows.Threading.DispatcherTimer)Get("coordinateUpdateTimer");
        var update = viewerType.GetMethod("UpdateWatchSelectionWithoutChangingView", Private);
        var resolve = viewerType.GetMethod("ResolveWatchNextSelection", Private);
        var config = viewerType.GetMethod("ReadConfiguration", Private).Invoke(viewer, null);
        var x = (TextBox)Get("selectedX");
        var y = (TextBox)Get("selectedY");
        x.Text = "5"; y.Text = "5";
        timer.Stop();
        Set("isApplyingProfile", false);
        update.Invoke(viewer, new object[] { 6, 5 });
        if (timer.IsEnabled) throw new Exception("Watch selection scheduled a redundant coordinate read.");
        Set("isApplyingProfile", true);
        x.Text = "centerX"; y.Text = "centerY";
        for (int next = 7; next <= 10; next++)
        {
            var selection = resolve.Invoke(viewer, new object[] { config });
            int selected = (int)selection.GetType().GetField("X").GetValue(selection);
            if (selected != next - 1) throw new Exception("Next watch did not use the advanced cursor.");
            update.Invoke(viewer, new object[] { next, 5 });
        }
        if (x.Text != "centerX" || y.Text != "centerY") throw new Exception("Watch replaced coordinate expressions.");
        viewerType.GetMethod("ApplyZoom", Private).Invoke(viewer, new object[] { 24.0 });
        window.UpdateLayout();
        viewerType.GetMethod("CenterHardwareWatchTarget", Private).Invoke(viewer, new object[] { 5, 5 });
        window.UpdateLayout();
        if ((int)Get("currentX") != 5 || (int)Get("kernelCenterX") != 5 || (int)Get("viewCenterX") != 5 ||
            (int)Get("currentY") != 5 || (int)Get("kernelCenterY") != 5 || (int)Get("viewCenterY") != 5)
            throw new Exception("Watch cursor, kernel and view centers diverged.");
        if ((double)Get("zoom") != 24.0) throw new Exception("Watch changed zoom.");
        var scroll = (ScrollViewer)Get("scrollViewer");
        double centerX = (scroll.HorizontalOffset + scroll.ViewportWidth / 2) / 24.0 - Convert.ToDouble(Get("virtualCanvasPaddingX")) - 0.5;
        double centerY = (scroll.VerticalOffset + scroll.ViewportHeight / 2) / 24.0 - Convert.ToDouble(Get("virtualCanvasPaddingY")) - 0.5;
        if (Math.Abs(centerX - 5) > 0.1 || Math.Abs(centerY - 5) > 0.1) throw new Exception("Watched pixel is not at the viewport center.");
        Console.WriteLine("Watch cursor/expression/timer/centering regression checks passed.");
    }
}
