using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

public sealed class FailedCleanupBreakpoint
{
    public int DeleteAttempts { get; private set; }
    public int HitCount { get { return 1; } }
    public void Delete()
    {
        DeleteAttempts++;
        throw new InvalidOperationException("Native engine has not acknowledged removal.");
    }
}

// Standalone host for the real viewer, not a mockup. No debugger reads or
// profile writes: deterministic samples let layout be reviewed without VS restart.
internal static class Program
{
    private static object viewer;
    private static Type viewerType;
    private static object syntheticFrame;
    private static object syntheticOverview;
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
            SetSyntheticInputs();
            var configuration = Activator.CreateInstance(assembly.GetType("ArrayImageViewer.Core.FrameConfiguration"), new object[] {
                64, 48, 64, 13, 0, false,
                Enum.Parse(assembly.GetType("ArrayImageViewer.Core.PixelOrder"), "GRFirst"),
                Enum.Parse(assembly.GetType("ArrayImageViewer.Core.PixelType"), "Bayer"),
                Enum.Parse(assembly.GetType("ArrayImageViewer.Core.VisualizeChannel"), "BayerRaw") });
            var samples = new long[64 * 48];
            for (int y = 0; y < 48; y++) for (int x = 0; x < 64; x++) samples[y * 64 + x] = 400 + x * 70 + y * 30;
            syntheticFrame = Activator.CreateInstance(assembly.GetType("ArrayImageViewer.Core.FrameBuffer"), new object[] { configuration, samples });
            var overviewConfiguration = Activator.CreateInstance(assembly.GetType("ArrayImageViewer.Core.FrameConfiguration"), new object[] {
                64, 48, 64, 13, 0, false,
                Enum.Parse(assembly.GetType("ArrayImageViewer.Core.PixelOrder"), "GRFirst"),
                Enum.Parse(assembly.GetType("ArrayImageViewer.Core.PixelType"), "Bayer"),
                Enum.Parse(assembly.GetType("ArrayImageViewer.Core.VisualizeChannel"), "Gray") });
            syntheticOverview = Activator.CreateInstance(assembly.GetType("ArrayImageViewer.Core.FrameBuffer"), new object[] { overviewConfiguration, samples });
            var window = new Window { Title = "Array RAW Viewer — Workspace preview", Width = 1050, Height = 780, Content = viewer };
            window.Loaded += delegate
            {
                viewerType.GetMethod("ApplyLoadedFrame", Private).Invoke(viewer, new object[] { syntheticFrame, 64, 48, 32, 24, false, true, "imageSimulator.output.m_data" });
                viewerType.GetMethod("ApplyNavigatorPreview", Private).Invoke(viewer, new object[] { syntheticOverview });
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
                else
                {
                    window.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        try { AddSyntheticPreviewTab(window); }
                        catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; window.Close(); }
                    }));
                }
            };
            new Application().Run(window);
        }
        catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
    }
    private static object Get(string name) { return viewerType.GetField(name, Private).GetValue(viewer); }
    private static void Set(string name, object value) { viewerType.GetField(name, Private).SetValue(viewer, value); }

    private static void SetSyntheticInputs()
    {
        // Synthetic frames own their map directly; clear debugger-only metadata
        // that a preceding tab regression may deliberately have populated.
        Set("navigatorSourceConfiguration", null);
        Set("navigatorPreviewKey", null);
        ((CheckBox)Get("rememberForSolution")).IsChecked = false;
        ((CheckBox)Get("autoUpdate")).IsChecked = false;
        foreach (string key in new[] { "width", "height", "stride", "renderWidth", "renderHeight" })
            ((TextBox)Get(key)).Text = key == "height" || key == "renderHeight" ? "48" : "64";
        ((TextBox)Get("selectedX")).Text = "32";
        ((TextBox)Get("selectedY")).Text = "24";
        ((TextBox)Get("roiWidth")).Text = "5";
        ((TextBox)Get("roiHeight")).Text = "5";
        ((TextBox)Get("qFormat")).Text = "13.0b";
        ((CheckBox)Get("signed")).IsChecked = false;
        ((ComboBox)Get("normalizationMode")).SelectedIndex = 0;
        ((ComboBox)Get("sourceElementType")).SelectedItem = Enum.Parse(viewerType.Assembly.GetType("ArrayImageViewer.Core.SourceElementType"), "UInt32");
        ((ComboBox)Get("pixelOrder")).SelectedItem = Enum.Parse(viewerType.Assembly.GetType("ArrayImageViewer.Core.PixelOrder"), "GRFirst");
        ((ComboBox)Get("pixelType")).SelectedItem = Enum.Parse(viewerType.Assembly.GetType("ArrayImageViewer.Core.PixelType"), "Bayer");
        ((ComboBox)Get("visualizeChannel")).SelectedItem = Enum.Parse(viewerType.Assembly.GetType("ArrayImageViewer.Core.VisualizeChannel"), "BayerRaw");
        ((TextBox)Get("expression")).Text = "imageSimulator.output.m_data";
    }

    private static void AddSyntheticPreviewTab(Window window)
    {
        // Exercise real cached array tabs without requesting a debugger read or
        // changing the single-tab setup used by the crash/watch regressions.
        Set("isApplyingProfile", false);
        viewerType.GetMethod("AddViewerTab", Private).Invoke(viewer, new object[] { null, null });
        Set("isApplyingProfile", true);
        ((TextBox)Get("expression")).Text = "imageSimulator.input.m_data";
        viewerType.GetMethod("ApplyLoadedFrame", Private).Invoke(viewer, new object[] {
            syntheticFrame, 64, 48, 32, 24, false, true, "imageSimulator.input.m_data" });
        viewerType.GetMethod("ApplyNavigatorPreview", Private).Invoke(viewer, new object[] { syntheticOverview });
        window.UpdateLayout();
        viewerType.GetMethod("FitLoadedView", Private).Invoke(viewer, new object[] { null, null });
        Set("isApplyingProfile", false);
        viewerType.GetMethod("CaptureActiveViewerTab", Private).Invoke(viewer, null);
        Set("isApplyingProfile", true);
        var panel = (Panel)Get("viewerTabPanel");
        ((Button)((Panel)panel.Children[0]).Children[0]).RaiseEvent(
            new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.Dispatcher.BeginInvoke(new Action(delegate
        {
            window.UpdateLayout();
            viewerType.GetMethod("FitLoadedView", Private).Invoke(viewer, new object[] { null, null });
            window.UpdateLayout();
            viewerType.GetMethod("UpdateViewportAndOverlay", Private).Invoke(viewer, null);
            ((TextBox)Get("status")).Text = "Synthetic 64 x 48 preview. Cached array tabs and display controls use no debugger connection.";
        }));
    }

    private static Rect Bounds(FrameworkElement element)
    {
        return new Rect(element.TranslatePoint(new Point(), (UIElement)viewer),
            new Size(element.ActualWidth, element.ActualHeight));
    }

    private static void VerifyInWindow(FrameworkElement element, string name, int size, bool vertical)
    {
        var bounds = Bounds(element);
        var surface = (FrameworkElement)viewer;
        if (!element.IsVisible || bounds.Width <= 0 || bounds.Height <= 0 || bounds.Left < -1 ||
            bounds.Right > surface.ActualWidth + 1 || (vertical && (bounds.Top < -1 || bounds.Bottom > surface.ActualHeight + 1)))
            throw new Exception("Clipped inspection control: " + name + " at " + size + " (" + bounds + ").");
    }

    private static void FitPresentationFrame(Window window)
    {
        // Screenshots must show a coherent sample, not the deliberately oversized
        // view dimensions or high zoom used by the center-command regression.
        Set("isApplyingProfile", true);
        SetSyntheticInputs();
        viewerType.GetMethod("ApplyLoadedFrame", Private).Invoke(viewer, new object[] {
            syntheticFrame, 64, 48, 32, 24, false, true, "imageSimulator.output.m_data" });
        viewerType.GetMethod("ApplyNavigatorPreview", Private).Invoke(viewer, new object[] { syntheticOverview });
        var dispatcherFrame = new System.Windows.Threading.DispatcherFrame();
        window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(delegate { dispatcherFrame.Continue = false; }));
        System.Windows.Threading.Dispatcher.PushFrame(dispatcherFrame);
        window.UpdateLayout();
        viewerType.GetMethod("FitLoadedView", Private).Invoke(viewer, new object[] { null, null });
        window.UpdateLayout();
        viewerType.GetMethod("UpdateViewportAndOverlay", Private).Invoke(viewer, null);
        ((TextBox)Get("status")).Text = "Synthetic preview: 64 x 48 samples, selected X=32 / Y=24, kernel 5 x 5. No debugger memory was read.";
        window.UpdateLayout();
    }

    private static void SavePresentationSnapshot(int width)
    {
        SavePresentationSnapshot(width, "preview");
    }

    private static void SavePresentationSnapshot(int width, string presentation)
    {
        var surface = (FrameworkElement)viewer;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth),
            (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        string snapshot = Path.Combine(Path.GetTempPath(), "ArrayRawViewer-inspector-" + presentation + "-" + width + ".png");
        using (var stream = File.Create(snapshot)) encoder.Save(stream);
        Console.WriteLine("Synthetic inspector layout snapshot: " + snapshot);
    }

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
            if (button != null && (String.Equals(button.Content as string, caption) ||
                String.Equals(System.Windows.Automation.AutomationProperties.GetName(button), caption))) return button;
        }
        throw new Exception("Missing command: " + caption);
    }
    private static void Pump(Window window)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(delegate { frame.Continue = false; }));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        window.UpdateLayout();
    }

    private static void VerifySavedOptionsRefreshViewer(Window window)
    {
        var store = viewerType.Assembly.GetType("ArrayImageViewer.Options.StructureTemplateStore", true);
        var save = store.GetMethod("SaveSettings");
        if (save == null) throw new Exception("Saving Options has no complete-settings transaction/notification for the live viewer.");
        string directory = Path.Combine(Path.GetTempPath(), "array-options-viewer-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.txt");
        var deserialize = store.GetMethod("Deserialize", BindingFlags.Static | BindingFlags.NonPublic);
        var document = deserialize.Invoke(null, new object[] { "{\"FormatVersion\":1,\"Templates\":[{\"ClassName\":\"LiveStream<unsigned short>\",\"DataAccess\":\"m_data\",\"WidthAccess\":\"m_width\",\"HeightAccess\":\"m_height\"}]}" });
        var items = document.GetType().GetProperty("Templates").GetValue(document, null);
        var root = (TextBox)Get("structureTemplateRoot");
        root.Text = "ctx->frame";
        string buffer = ((TextBox)Get("expression")).Text;
        double savedZoom = (double)Get("zoom");
        object oldFrame = Get("frame");
        int epoch = (int)Get("structureSearchEpoch");
        try
        {
            save.Invoke(null, new object[] { "<no-solution>", items, 17, false, path });
            Pump(window);
            var picker = (ComboBox)Get("structureTemplatePicker");
            if (picker.Items.Count != 1 || !picker.Items[0].ToString().Contains("LiveStream<unsigned short>"))
                throw new Exception("Saved template was not pushed to the viewer without Reload.");
            if (root.Text != "ctx->frame" || ((TextBox)Get("expression")).Text != buffer ||
                (double)Get("zoom") != savedZoom || !Object.ReferenceEquals(Get("frame"), oldFrame) || (int)Get("structureSearchEpoch") <= epoch)
                throw new Exception("Options refresh changed the capture/view state or failed to invalidate stale search results.");
            picker.SelectedIndex = 0;
            var item = ((System.Collections.IList)items)[0];
            item.GetType().GetProperty("DataAccess").SetValue(item, "pixels", null);
            save.Invoke(null, new object[] { "<no-solution>", items, 17, false, path }); Pump(window);
            if (((TextBox)Get("structureTemplateData")).Text != "pixels" || picker.SelectedIndex != 0)
                throw new Exception("Selected template fields were not refreshed after Save.");
            int currentEpoch = (int)Get("structureSearchEpoch");
            save.Invoke(null, new object[] { "other.sln", items, 17, false, path }); Pump(window);
            if ((int)Get("structureSearchEpoch") != currentEpoch) throw new Exception("Another solution's save changed this viewer.");
            Console.WriteLine("Saved Options immediately refresh templates/selected fields without changing root, buffer, zoom or frame.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static void Verify(Window window)
    {
        VerifyWatchCleanupChecks();
        VerifySavedOptionsRefreshViewer(window);
        var mapImage = Button("Frame map").Content as Image;
        if (mapImage == null || mapImage.Source == null)
            throw new Exception("Generated Frame map image resource is missing.");
        var iconBitmap = new System.Windows.Media.Imaging.FormatConvertedBitmap(
            (System.Windows.Media.Imaging.BitmapSource)mapImage.Source, PixelFormats.Bgra32, null, 0);
        var corner = new byte[4];
        iconBitmap.CopyPixels(new Int32Rect(0, 0, 1, 1), corner, 4, 0);
        if (corner[3] != 0) throw new Exception("Frame map icon background is not transparent.");
        foreach (string name in new[] { "Center view", "Left", "Right", "Up", "Down", "Frame map", "Zoom ▾", "Export ▾" })
        {
            var icon = Button(name);
            if (icon.Content is string || icon.ToolTip == null || String.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(icon)))
                throw new Exception("Icon command lacks visual, tooltip or accessible name: " + name);
            if (icon.Width > 44 || !icon.Focusable) throw new Exception("Icon command must be compact and keyboard accessible.");
        }
        ((TextBox)Get("renderWidth")).Text = "4096";
        ((TextBox)Get("renderHeight")).Text = "3072";
        var generation = Get("renderGeneration");
        viewerType.GetMethod("ApplyZoom", Private).Invoke(viewer, new object[] { 0.5 });
        Button("Center view").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if ((double)Get("zoom") < 48) throw new Exception("Center must zoom to readable pixel values.");
        viewerType.GetMethod("ApplyZoom", Private).Invoke(viewer, new object[] { 96.0 });
        Button("Center view").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if ((double)Get("zoom") < 96) throw new Exception("Center must preserve a higher inspection zoom.");
        if (!Equals(generation, Get("renderGeneration"))) throw new Exception("Center must not start a read/render.");
        if (((TextBox)Get("status")).Text.Contains("Cannot")) throw new Exception("Center failed with oversized View dimensions.");
        if (!Button("Select X/Y").IsVisible || Button("Select X/Y").ToolTip == null)
            throw new Exception("Pixel selection must remain visible and explained.");
        foreach (string runName in new[] { "Run to write X/Y", "Run to next X", "Run to next Y" })
            if (!Button(runName).IsVisible || !((string)Button(runName).Content).StartsWith("▶ ") || Button(runName).ToolTip == null)
                throw new Exception("Debugger run command must be visible and clearly marked: " + runName);
        var caption = viewerType.GetMethod("CompactTabCaption", BindingFlags.Static | BindingFlags.NonPublic);
        if ((string)caption.Invoke(null, new object[] { "(&(((imageSimulator).input_aux_stream)[0]))->m_data" }) != "input_aux_stream[0].m_data")
            throw new Exception("Array index lost from tab caption.");
        foreach (int size in new[] { 600, 900, 1200 })
        {
            window.Width = size;
            FitPresentationFrame(window);
            var imageBounds = Bounds((ScrollViewer)Get("scrollViewer"));
            var settingsBounds = Bounds((ScrollViewer)Get("configurationScrollViewer"));
            var mapBounds = Bounds((Canvas)Get("navigatorCanvas"));
            double minimumHeight = size == 600 ? 300 : 400;
            if (imageBounds.Height < minimumHeight)
                throw new Exception("Image has insufficient height at " + size + ": " + imageBounds.Height + " < " + minimumHeight + ".");
            if (settingsBounds.Left < imageBounds.Right - 2 || settingsBounds.Width < 240)
                throw new Exception("Settings must occupy a usable inspector to the image's right at " + size + ".");
            if (mapBounds.Left < settingsBounds.Left - 2 || mapBounds.Right > settingsBounds.Right + 2)
                throw new Exception("Frame map must align with the right inspector at " + size + ".");
            if (Bounds((Panel)Get("viewerTabPanel")).Bottom > Bounds((TextBox)Get("expression")).Top + 1)
                throw new Exception("Array tabs must remain above the compact source row.");
            foreach (string name in new[] { "expression", "availablePointers", "autoUpdate", "selectedX", "selectedY", "roiWidth", "roiHeight", "visualizeChannel", "renderWidth", "renderHeight", "navigatorCanvas" })
                VerifyInWindow((FrameworkElement)Get(name), name, size, true);
            foreach (string name in new[] { "width", "height", "stride", "qFormat", "sourceElementType", "signed", "pixelOrder", "pixelType" })
                VerifyInWindow((FrameworkElement)Get(name), name, size, false);
            foreach (string name in new[] { "Capture", "Select X/Y", "Center view", "Run to write X/Y", "Run to next X", "Run to next Y", "Frame", "Structure", "Profiles", "Frame map", "Zoom ▾", "Export ▾", "Stats", "Left", "Right", "Up", "Down" })
                VerifyInWindow(Button(name), name, size, true);
            foreach (string name in new[] { "Run to write X/Y", "Run to next X", "Run to next Y", "Select X/Y" })
                if (Bounds(Button(name)).Bottom > imageBounds.Top + 1)
                    throw new Exception("Coordinate selection and debugger Run commands must stay above the image: " + name + ".");
            if (Bounds((TextBox)Get("selectedX")).Bottom > imageBounds.Top + 1 || Bounds((TextBox)Get("selectedY")).Bottom > imageBounds.Top + 1)
                throw new Exception("X/Y controls must remain above the image.");
            bool hasInspectorSplitter = false;
            foreach (var item in Descendants((DependencyObject)viewer))
            {
                var splitter = item as GridSplitter;
                if (splitter != null && splitter.IsVisible && splitter.ResizeDirection == GridResizeDirection.Columns)
                    hasInspectorSplitter = true;
            }
            if (!hasInspectorSplitter) throw new Exception("Inspector needs a visible column-resizing splitter.");
            double imageHeight = ((ScrollViewer)Get("scrollViewer")).ActualHeight;
            string originalExpression = ((TextBox)Get("expression")).Text;
            string originalWidth = ((TextBox)Get("width")).Text;
            foreach (string tabName in new[] { "Structure", "Profiles", "Frame" })
            {
                Button(tabName).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                window.UpdateLayout();
                if (Math.Abs(((ScrollViewer)Get("scrollViewer")).ActualHeight - imageHeight) > 1)
                    throw new Exception("Settings tab changed image height.");
                if (Application.Current.Windows.Count != 1)
                    throw new Exception("Settings must switch inside the same viewer window.");
            }
            if (((TextBox)Get("expression")).Text != originalExpression || ((TextBox)Get("width")).Text != originalWidth)
                throw new Exception("Settings tab switching changed capture settings.");
            if (!((Canvas)Get("navigatorCanvas")).IsVisible) throw new Exception("Map must be visible by default.");
            Button("Frame map").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            window.UpdateLayout();
            if (((Canvas)Get("navigatorCanvas")).IsVisible) throw new Exception("Map failed to hide.");
            if (Math.Abs(((ScrollViewer)Get("scrollViewer")).ActualHeight - imageHeight) > 1)
                throw new Exception("Hiding the inspector map must not change image height.");
            Button("Frame map").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            window.UpdateLayout();
            if (size == 900 || size == 1200) SavePresentationSnapshot(size);
            Console.WriteLine("Inspector layout " + size + " px: image " + imageBounds.Width + " x " + imageHeight + "; settings/map aligned right.");
        }
        if (Button("Export ▾").ContextMenu.Items.Count != 4) throw new Exception("Export scopes missing.");
        Button("Stats").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if (((StackPanel)Get("statisticsPanel")).Visibility != Visibility.Visible) throw new Exception("Statistics failed to open.");
        Button("Hide stats").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        VerifyShortInspector(window);
        VerifyWatchNavigation(window);
        VerifyCompletionFocus(window);
        VerifyInlineCaptionMouseDown(window);
        VerifyArrayTabs(window);
        VerifyRunCancelsDeferredDebuggerWork();
        VerifySessionEndCancelsDeferredDebuggerWork();
        VerifyCancelledWatchTickIsIgnored();
        var searchButton = Button("Search objects");
        var searchWidth = searchButton.Width;
        viewerType.GetMethod("SetSearchBusy", Private).Invoke(viewer, new object[] { true });
        if (Button("Cancel search") != searchButton || searchButton.Width != searchWidth || !searchButton.IsEnabled ||
            !((UIElement)viewer).IsEnabled || ((TextBox)Get("structureTemplateRoot")).IsReadOnly)
            throw new Exception("Search state must reuse its icon without disabling or resizing the viewer/root.");
        viewerType.GetMethod("SetSearchBusy", Private).Invoke(viewer, new object[] { false });
        Console.WriteLine("Search/cancel icon fixed-layout and enabled-style checks passed.");
        VerifySyntheticPreview(window);
    }

    private static void VerifyShortInspector(Window window)
    {
        double originalWidth = window.Width;
        double originalHeight = window.Height;
        try
        {
            window.Width = 600;
            window.Height = 620;
            Button("Stats").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            window.UpdateLayout();
            if (!((Canvas)Get("navigatorCanvas")).IsVisible || !((StackPanel)Get("statisticsPanel")).IsVisible)
                throw new Exception("Short-window test requires both Frame map and statistics open.");
            DependencyObject parent = (DependencyObject)Get("statisticsPanel");
            while (parent != null && !(parent is ScrollViewer)) parent = LogicalTreeHelper.GetParent(parent);
            var details = parent as ScrollViewer;
            if (details == null) throw new Exception("Inspector statistics need a scrollable details viewport.");
            var detailsBounds = Bounds(details);
            var imageBounds = Bounds((ScrollViewer)Get("scrollViewer"));
            if (detailsBounds.Height <= 0 || detailsBounds.Left < imageBounds.Right - 2 || detailsBounds.Bottom > imageBounds.Bottom + 1)
                throw new Exception("Short-window inspector details overlap the footer: details=" + detailsBounds + "; image=" + imageBounds + ".");
            VerifyInWindow(details, "inspector details viewport", 600, true);
            Console.WriteLine("Short 600 x 620 host keeps Frame map/statistics inside a bounded inspector viewport.");
        }
        finally
        {
            if (((StackPanel)Get("statisticsPanel")).Visibility == Visibility.Visible)
                Button("Hide stats").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            window.Width = originalWidth;
            window.Height = originalHeight;
            window.UpdateLayout();
        }
    }

    private static void VerifySyntheticPreview(Window window)
    {
        FitPresentationFrame(window);
        AddSyntheticPreviewTab(window);
        var dispatcherFrame = new System.Windows.Threading.DispatcherFrame();
        window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(delegate { dispatcherFrame.Continue = false; }));
        System.Windows.Threading.Dispatcher.PushFrame(dispatcherFrame);
        if (((System.Collections.IList)Get("viewerTabs")).Count != 2)
            throw new Exception("Standalone preview must open two synthetic array tabs.");
        var panel = (Panel)Get("viewerTabPanel");
        for (int index = 1; index >= 0; index--)
        {
            ((Button)((Panel)panel.Children[index]).Children[0]).RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if (!Object.ReferenceEquals(Get("frame"), syntheticFrame) || Get("pendingMemoryRead") != null)
                throw new Exception("Standalone array tabs must restore their synthetic cache without a debugger read.");
        }
        window.Width = 900;
        window.UpdateLayout();
        viewerType.GetMethod("ApplyZoom", Private).Invoke(viewer, new object[] { 48.0 });
        window.UpdateLayout();
        viewerType.GetMethod("CenterOnSelection", Private).Invoke(viewer, null);
        window.UpdateLayout();
        viewerType.GetMethod("UpdateViewportAndOverlay", Private).Invoke(viewer, null);
        ((TextBox)Get("status")).Text = "Synthetic pixel inspection: X=32 / Y=24, kernel 5 x 5, 48x zoom. Cached samples only.";
        window.UpdateLayout();
        SavePresentationSnapshot(900, "detail");
        Console.WriteLine("Standalone preview opens and switches two cached synthetic tabs without debugger reads.");
    }

    private static void VerifyWatchCleanupChecks()
    {
        int failures = 0;
        foreach (Action check in new Action[] {
            VerifyOneShotCleanupFailureRemainsVisible,
            VerifySessionEndRetainsUnreleasedWatch,
            VerifyQueuedWatchDoesNotCleanUpOutsideBreakMode })
        {
            try { check(); }
            catch (Exception exception) { failures++; Console.Error.WriteLine(check.Method.Name + ": " + exception.Message); }
        }
        if (failures > 0) throw new Exception(failures + " watch cleanup regressions failed.");
    }

    private static void VerifyOneShotCleanupFailureRemainsVisible()
    {
        var retired = (System.Collections.IList)Get("retiredHardwareWatchBreakpoints");
        var watch = new FailedCleanupBreakpoint();
        Set("hardwareWatchBreakpoint", watch);
        Set("hardwareWatchHitCount", 0);
        Set("hardwareWatchRunning", true);
        Set("hardwareWatchX", 5);
        Set("hardwareWatchY", 5);
        ((CheckBox)Get("keepHardwareWatchArmed")).IsChecked = false;
        try
        {
            var hit = (bool)viewerType.GetMethod("HardwareWatchReturnedToBreakMode", Private).Invoke(viewer, null);
            if (!hit) throw new Exception("The fake watch did not report its incremented HitCount.");
            var message = ((TextBlock)Get("hardwareWatchInfo")).Text;
            if (message.Contains("was removed") || !message.Contains("cleanup pending") || !retired.Contains(watch))
                throw new Exception("A failed one-shot delete must stay tracked and report cleanup pending, not removed: " + message);
            if (watch.DeleteAttempts != 1) throw new Exception("A single Clear must make one delete attempt per owned watch.");
            Console.WriteLine("A hit with failed one-shot cleanup remains owned and does not report removal.");
        }
        finally
        {
            retired.Remove(watch);
            Set("hardwareWatchBreakpoint", null);
            Set("hardwareWatchRunning", false);
        }
    }

    private static void VerifySessionEndRetainsUnreleasedWatch()
    {
        var retired = (System.Collections.IList)Get("retiredHardwareWatchBreakpoints");
        var watch = new FailedCleanupBreakpoint();
        Set("hardwareWatchBreakpoint", watch);
        try
        {
            viewerType.GetMethod("DebuggerSessionEnded", Private).Invoke(viewer, null);
            if (!retired.Contains(watch) || retired.Count != 1 || Get("hardwareWatchBreakpoint") != null)
                throw new Exception("Session end discarded an unresolved owned watch instead of retaining cleanup ownership.");
            Console.WriteLine("Session end preserves unresolved owned breakpoints for the next cleanup attempt.");
        }
        finally { retired.Remove(watch); Set("hardwareWatchBreakpoint", null); }
    }

    private static void VerifyQueuedWatchDoesNotCleanUpOutsideBreakMode()
    {
        var retired = (System.Collections.IList)Get("retiredHardwareWatchBreakpoints");
        var watch = new FailedCleanupBreakpoint();
        retired.Add(watch);
        Set("pendingHardwareWatchConfiguration", Get("frame").GetType().GetProperty("Configuration").GetValue(Get("frame"), null));
        try
        {
            // This standalone host has no VS debugger. A delayed Tick must not
            // delete an owned breakpoint or resume execution outside Break.
            viewerType.GetMethod("HardwareWatchReleaseTimerTick", Private).Invoke(viewer, new object[] { null, EventArgs.Empty });
            if (watch.DeleteAttempts != 0 || Get("pendingHardwareWatchConfiguration") != null || !retired.Contains(watch))
                throw new Exception("A release timer tick outside Break touched debugger state or retained an auto-resume request.");
            Console.WriteLine("A queued watch outside Break cancels without deleting breakpoints or resuming.");
        }
        finally { retired.Remove(watch); Set("pendingHardwareWatchConfiguration", null); }
    }

    private static void VerifyCancelledWatchTickIsIgnored()
    {
        Set("pendingHardwareWatchConfiguration", null);
        var retired = (System.Collections.IList)Get("retiredHardwareWatchBreakpoints");
        retired.Add(null); // Already released, but still present in the retirement queue.
        viewerType.GetMethod("ClearHardwareWatch", Private, null, new[] { typeof(bool) }, null).Invoke(viewer, new object[] { true });
        if (retired.Count != 0) throw new Exception("Clear did not clean the retirement queue.");
        Console.WriteLine("Clear cleans already released retirement entries.");
        var sentinel = new object();
        retired.Add(sentinel);
        var statusBefore = ((TextBox)Get("status")).Text;
        var attemptsBefore = Get("pendingHardwareWatchReleaseAttempts");
        try
        {
            viewerType.GetMethod("HardwareWatchReleaseTimerTick", Private).Invoke(viewer, new object[] { null, EventArgs.Empty });
            if (!Equals(attemptsBefore, Get("pendingHardwareWatchReleaseAttempts")) || ((TextBox)Get("status")).Text != statusBefore)
                throw new Exception("A cancelled watch retry must not enumerate/delete breakpoints or start another watch.");
            Console.WriteLine("Cancelled watch timer delivery does no breakpoint work.");
            viewerType.GetMethod("ClearHardwareWatch", Private, null, new[] { typeof(bool) }, null).Invoke(viewer, new object[] { true });
            if (!((TextBox)Get("status")).Text.Contains("still releasing"))
                throw new Exception("Clear falsely reported success with a failed retired watch.");
            Console.WriteLine("Clear reports a remaining failed watch instead of claiming removal.");
        }
        finally { retired.Remove(sentinel); }
    }

    private static void VerifySessionEndCancelsDeferredDebuggerWork()
    {
        var pendingType = viewerType.GetNestedType("PendingMemoryRead", BindingFlags.NonPublic);
        Set("pendingMemoryRead", Activator.CreateInstance(pendingType, new object[] { null, 64, 48, 5, 5, false, false, "raw_buffer", null, 0, false }));
        var timer = (System.Windows.Threading.DispatcherTimer)Get("memoryReadTimer");
        var generation = Get("renderGeneration");
        timer.Start();
        try
        {
            viewerType.GetMethod("DebuggerSessionEnded", Private).Invoke(viewer, null);
            if (timer.IsEnabled || Get("pendingMemoryRead") != null || Equals(generation, Get("renderGeneration")))
                throw new Exception("Session end must cancel old native reads and invalidate their render generation.");
            viewerType.GetMethod("MemoryReadTimerTick", Private).Invoke(viewer, new object[] { null, EventArgs.Empty });
            Console.WriteLine("Session end cancels native memory work before another cached tab is selected.");
        }
        finally { timer.Stop(); Set("pendingMemoryRead", null); }
    }

    private static void VerifyRunCancelsDeferredDebuggerWork()
    {
        var pendingType = viewerType.GetNestedType("PendingMemoryRead", BindingFlags.NonPublic);
        var pending = Activator.CreateInstance(pendingType, new object[] { null, 64, 48, 5, 5, false, false, "raw_buffer", null, 0, false });
        var timers = new[] { "memoryReadTimer", "autoRefreshTimer", "coordinateUpdateTimer", "debuggerBreakRefreshTimer" };
        var generation = Get("renderGeneration");
        var watch = new object(); // Must stay armed: Run cancellation is not breakpoint deletion.
        Set("pendingMemoryRead", pending);
        Set("hardwareWatchBreakpoint", watch);
        Set("pendingHardwareWatchConfiguration", Get("frame").GetType().GetProperty("Configuration").GetValue(Get("frame"), null));
        try
        {
            foreach (var timer in timers) ((System.Windows.Threading.DispatcherTimer)Get(timer)).Start();
            viewerType.GetMethod("DebuggerStartedRunning", Private).Invoke(viewer, null);
            foreach (var timer in timers)
                if (((System.Windows.Threading.DispatcherTimer)Get(timer)).IsEnabled)
                    throw new Exception("Run must stop deferred debugger work: " + timer);
            if (Get("pendingMemoryRead") != null || Get("pendingHardwareWatchConfiguration") != null || Equals(generation, Get("renderGeneration")))
                throw new Exception("Run must discard old memory/watch requests and invalidate background rendering.");
            if (!Object.ReferenceEquals(Get("hardwareWatchBreakpoint"), watch))
                throw new Exception("Run must not disarm the active hardware breakpoint.");
            viewerType.GetMethod("MemoryReadTimerTick", Private).Invoke(viewer, new object[] { null, EventArgs.Empty });
            Console.WriteLine("Run cancels old debugger reads/refreshes/render results without disarming the watch.");
        }
        finally
        {
            foreach (var timer in timers) ((System.Windows.Threading.DispatcherTimer)Get(timer)).Stop();
            Set("pendingMemoryRead", null); Set("pendingHardwareWatchConfiguration", null); Set("hardwareWatchBreakpoint", null);
        }
    }

    private static void VerifyInlineCaptionMouseDown(Window window)
    {
        var panel = (Panel)Get("viewerTabPanel");
        var button = (Button)((Panel)panel.Children[0]).Children[0];
        var originalCaption = button.Content;
        try
        {
            // Button's default AccessText template turns an underscore caption
            // into Run content. Unlike RaiseEvent(Click), this exercises the
            // parent PreviewMouseDown route that executes BEFORE tab selection.
            button.Content = "raw_buffer";
            window.UpdateLayout();
            System.Windows.ContentElement hit = null;
            for (double x = 0; x < button.ActualWidth && hit == null; x += 1)
                hit = button.InputHitTest(new Point(x, button.ActualHeight / 2)) as System.Windows.ContentElement;
            if (hit == null) throw new Exception("Test setup: underscore button caption must expose inline hit content.");
            var args = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            {
                RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent
            };
            hit.RaiseEvent(args);
            if (args.Handled) throw new Exception("Inline caption mouse-down must not consume the tab click.");
            Console.WriteLine("Underscore caption Run hit safely routes PreviewMouseDown before tab Click.");
        }
        finally { button.Content = originalCaption; }
    }

    private static void VerifyArrayTabs(Window window)
    {
        Set("isApplyingProfile", false);
        var original = Get("activeViewerTab");
        var cachedFrame = Get("frame");
        var tabPanel = (Panel)Get("viewerTabPanel");
        var selectedButton = (Button)((Panel)tabPanel.Children[0]).Children[0];
        var coordinateTimer = (System.Windows.Threading.DispatcherTimer)Get("coordinateUpdateTimer");
        coordinateTimer.Start();
        var previousStatus = ((TextBox)Get("status")).Text;
        for (int repeat = 0; repeat < 5; repeat++)
            selectedButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        if (!coordinateTimer.IsEnabled || !Object.ReferenceEquals(Get("activeViewerTab"), original) ||
            !Object.ReferenceEquals(Get("frame"), cachedFrame) || ((TextBox)Get("status")).Text != previousStatus)
            throw new Exception("Clicking the active array tab must not run transition cleanup or change state.");
        coordinateTimer.Stop();
        Console.WriteLine("Repeated active array tab routed clicks are no-ops.");
        Set("isApplyingProfile", true);
        ((TextBox)Get("expression")).Text = "(this->buffer.C).m_data";
        ((TextBox)Get("width")).Text = "(this->buffer.C).m_width";
        ((TextBox)Get("height")).Text = "(this->buffer.C).m_height";
        ((TextBox)Get("stride")).Text = "(this->buffer.C).m_width";
        var sourceTypes = (ComboBox)Get("sourceElementType");
        sourceTypes.SelectedItem = Enum.Parse(sourceTypes.SelectedItem.GetType(), "UInt16");
        Set("isApplyingProfile", false);
        Set("navigatorSourceConfiguration", cachedFrame.GetType().GetProperty("Configuration").GetValue(cachedFrame, null));
        Set("navigatorPreviewKey", null); // Missing map must not trigger a debugger read on restore.
        foreach (string timer in new[] { "autoRefreshTimer", "coordinateUpdateTimer", "debuggerBreakRefreshTimer" })
            ((System.Windows.Threading.DispatcherTimer)Get(timer)).Start();
        viewerType.GetMethod("AddViewerTab", Private).Invoke(viewer, new object[] { null, null });
        var added = Get("activeViewerTab");
        if (original == added || Get("frame") != null) throw new Exception("New array tab must be empty.");
        if (!Object.ReferenceEquals(((Panel)tabPanel.Children[0]).Children[0], selectedButton))
            throw new Exception("Adding an array must preserve the existing tab button and its focus identity.");
        var addedButton = (Button)((Panel)tabPanel.Children[1]).Children[0];
        for (int i = 0; i < 12; i++)
        {
            selectedButton.Focus();
            selectedButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (!Object.ReferenceEquals(((Panel)tabPanel.Children[0]).Children[0], selectedButton) || !selectedButton.IsKeyboardFocused)
                throw new Exception("Selecting an array must retain the clicked button and keyboard focus.");
            var caption = selectedButton.Content as TextBlock;
            if (caption == null || caption.Text != "C.m_data" ||
                System.Windows.Automation.AutomationProperties.GetName(selectedButton) != caption.Text)
                throw new Exception("Pointer tab captions must display literal underscores and expose an accessible name.");
            if (!Object.ReferenceEquals(Get("frame"), cachedFrame)) throw new Exception("Array tab did not restore its cached frame.");
            if (Get("pendingMemoryRead") != null || Get("navigatorPreviewKey") != null)
                throw new Exception("Restoring a tab must not start a frame-map debugger read.");
            addedButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        }
        var dispatcherFrame = new System.Windows.Threading.DispatcherFrame();
        window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(delegate { dispatcherFrame.Continue = false; }));
        System.Windows.Threading.Dispatcher.PushFrame(dispatcherFrame);
        foreach (string timer in new[] { "autoRefreshTimer", "coordinateUpdateTimer", "debuggerBreakRefreshTimer" })
            if (((System.Windows.Threading.DispatcherTimer)Get(timer)).IsEnabled) throw new Exception("Old tab timer survived switching.");
        viewerType.GetMethod("CloseViewerTab", Private).Invoke(viewer, new object[] { new Button { Tag = added }, null });
        if (!Object.ReferenceEquals(Get("frame"), cachedFrame)) throw new Exception("Closing active tab must restore previous cached frame.");
        if (((TextBox)Get("status")).Text.StartsWith("Cannot")) throw new Exception("Array switch failed.");
        var traceType = viewerType.Assembly.GetType("ArrayImageViewer.Debugging.StructureSearchTrace", true);
        string logPath = null;
        try
        {
            using (var trace = (IDisposable)Activator.CreateInstance(traceType, new object[] { true, "array-tab" }))
            {
                logPath = (string)traceType.GetProperty("FilePath").GetValue(trace, null);
                Set("tabTrace", trace);
                Set("switchingViewerTab", true);
                viewerType.GetMethod("RestoreViewerTab", Private).Invoke(viewer, new object[] { original });
            }
            var log = File.ReadAllText(logPath);
            if (!log.Contains("type=UInt16") || !log.Contains("BEGIN ApplyProfile") || !log.Contains("END Frame.Zoom") || !log.Contains("END ApplyCachedFrame"))
                throw new Exception("Array trace must include type and restore stages.");
            Console.WriteLine("UInt16 expression-backed tab restore trace verified (synthetic log removed).");
        }
        finally
        {
            Set("tabTrace", null); Set("switchingViewerTab", false);
            if (logPath != null) File.Delete(logPath);
        }
        Console.WriteLine("Array add/12 round trips/close, missing map cache and pending timer regression checks passed.");
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
        Button("Run to next X").RaiseEvent(mouseDown);
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
