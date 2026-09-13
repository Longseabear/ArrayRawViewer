using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ArrayImageViewer.UI
{
    // Composition only. Debugger acquisition, navigation and persistence remain
    // in their existing partials so layout changes cannot change sample semantics.
    internal sealed partial class SensorImageViewerControl
    {

        private UIElement CreateWorkspace()
        {
            var root = new Grid { Background = RootBrush };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var source = CreateRow();
            expression.Width = 220;
            source.Children.Add(WorkspaceField("BUFFER EXPRESSION", expression));
            source.Children.Add(WorkspaceAction("", CreateButton("Capture", LoadContextPreview, true)));
            source.Children.Add(WorkspaceAction("", CreateCommandMenu("Source", new[] { "Refresh pointers", "Use editor selection" }, new RoutedEventHandler[] { RefreshPointers, CaptureSelection })));
            source.Children.Add(WorkspaceField("POINTERS", availablePointers));
            source.Children.Add(WorkspaceField("", autoUpdate));
            source.Children.Add(WorkspaceAction("", CreateCommandMenu("Read options", new[] { "Read exact kernel area", "Full-frame preview", "Refresh exact cells", "Cancel read" }, new RoutedEventHandler[] { LoadExpression, LoadFullPreview, InspectCells, CancelRead })));
            AddWorkspaceCell(root, WorkspaceBand(source), 0);
            root.RowDefinitions.Insert(1, new RowDefinition { Height = new GridLength(145), MinHeight = 70, MaxHeight = 300 });
            root.RowDefinitions.Insert(2, new RowDefinition { Height = new GridLength(5) });
            AddWorkspaceCell(root, CreateHeader(), 1);
            AddWorkspaceCell(root, new GridSplitter { Background = PanelBorderBrush,
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                ResizeDirection = GridResizeDirection.Rows, ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ShowsPreview = true, ToolTip = "Drag to resize settings; image uses the remaining space." }, 2);

            var inspection = new Grid();
            inspection.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            inspection.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            inspection.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            inspection.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddWorkspaceCell(inspection, CreateViewerTabStrip(), 0);

            var tools = CreateRow();
            tools.Children.Add(WorkspaceField("GO TO X", selectedX));
            tools.Children.Add(WorkspaceField("GO TO Y", selectedY));
            tools.Children.Add(WorkspaceAction("COORDINATES", CreateButton("Go To", GoToEnteredCoordinate, false)));
            tools.Children.Add(WorkspaceAction("SELECTED PIXEL", CreateButton("Center", JumpToCoordinate, false)));
            tools.Children.Add(WorkspaceField("KERNEL W", roiWidth));
            tools.Children.Add(WorkspaceField("KERNEL H", roiHeight));
            tools.Children.Add(WorkspaceField("DISPLAY", visualizeChannel));
            tools.Children.Add(WorkspaceAction("", CreateCommandMenu("Zoom", new[] { "Fit loaded view", "100%", "Zoom in", "Zoom out" }, new RoutedEventHandler[] {
                FitLoadedView, delegate { SetWorkspaceZoom(1); },
                delegate { ZoomAroundSelection(1.25); }, delegate { ZoomAroundSelection(0.8); } })));
            tools.Children.Add(WorkspaceAction("", CreateCommandMenu("Export", new[] { "Copy kernel text  ·  Ctrl+C", "Save full RAW…", "Save View RAW…", "Save Kernel RAW…" }, new RoutedEventHandler[] { CopyKernelToClipboard, SaveFullRaw, SaveViewRaw, SaveKernelRaw })));
            tools.Children.Add(WorkspaceAction("", statisticsToggle));

            navigatorCanvas.Width = 174;
            navigatorCanvas.Height = 118;
            var mapContents = new StackPanel { Width = 174, Margin = new Thickness(8) };
            var map = new ScrollViewer { Content = mapContents, Width = 194, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            mapContents.Children.Add(new TextBlock { Text = "FRAME MAP", Foreground = MutedBrush, Margin = new Thickness(0, 0, 0, 8) });
            mapContents.Children.Add(navigatorCanvas);
            navigatorCanvas.ToolTip = "Drag to set View ROI. Orange rectangle = Kernel.";
            mapContents.Children.Add(navigatorInfo);
            var viewSize = CreateRow();
            viewSize.Margin = new Thickness(0, 12, 0, 0);
            viewSize.Children.Add(WorkspaceField("VIEW W", renderWidth));
            viewSize.Children.Add(WorkspaceField("VIEW H", renderHeight));
            mapContents.Children.Add(viewSize);
            var movement = CreateRow();
            movement.Children.Add(WorkspaceAction("MOVE VIEW", CreateButton("Left", PanLeft, false)));
            movement.Children.Add(WorkspaceAction("", CreateButton("Right", PanRight, false)));
            movement.Children.Add(WorkspaceAction("", CreateButton("Up", PanUp, false)));
            movement.Children.Add(WorkspaceAction("", CreateButton("Down", PanDown, false)));
            mapContents.Children.Add(movement);
            tools.Children.Add(WorkspaceAction("", CreateButton("Frame map", delegate { map.Visibility = map.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; }, false)));
            AddWorkspaceCell(inspection, WorkspaceBand(tools), 1);

            var auxiliary = new StackPanel();
            var watch = CreateRow();
            watch.Children.Add(WorkspaceAction("RESUME UNTIL WRITE", CreateButton("Watch Go To X/Y", WatchSelectedPixelAndContinue, false)));
            watch.Children.Add(WorkspaceAction("", CreateButton("Watch Next X", WatchNextPixelAndContinue, false)));
            watch.Children.Add(WorkspaceAction("", CreateButton("Watch Next Y", WatchNextLineAndContinue, false)));
            watch.Children.Add(WorkspaceField("", keepHardwareWatchArmed));
            watch.Children.Add(WorkspaceAction("", CreateButton("Clear", ClearHardwareWatch, false)));
            var watchPanel = new StackPanel();
            watchPanel.Children.Add(watch);
            watchPanel.Children.Add(hardwareWatchInfo);
            watchPanel.ToolTip = "Hardware watch resumes the debugger until the target sample is written.";
            hardwareWatchInfo.Margin = new Thickness(0, 2, 0, 4);
            hardwareWatchInfo.MaxHeight = 36;
            hardwareWatchInfo.SetBinding(TextBlock.ToolTipProperty, new System.Windows.Data.Binding("Text") { Source = hardwareWatchInfo });
            auxiliary.Children.Add(WorkspaceBand(watchPanel));
            var stats = new StackPanel { Margin = new Thickness(8) };
            stats.Children.Add(WorkspaceField("STATISTICS SCOPE", statisticsScope));
            stats.Children.Add(statisticsSummary);
            stats.Children.Add(statisticsChannels);
            statisticsPanel.Children.Add(stats);
            auxiliary.Children.Add(new ScrollViewer { Content = statisticsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 160 });
            AddWorkspaceCell(inspection, auxiliary, 3);
            var stage = new DockPanel();
            DockPanel.SetDock(map, Dock.Right);
            stage.Children.Add(map);
            stage.Children.Add(scrollViewer);
            AddWorkspaceCell(inspection, stage, 2);
            AddWorkspaceCell(root, inspection, 3);
            AddWorkspaceCell(root, CreateFooter(), 4);
            return root;
        }

        private static FrameworkElement WorkspaceAction(string label, Button button)
        {
            if (!String.IsNullOrEmpty(label) && button.ToolTip == null) button.ToolTip = label;
            return new Border { Child = button, Margin = new Thickness(0, 0, 8, 6) };
        }

        private static FrameworkElement WorkspaceField(string label, UIElement control)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 6) };
            if (!String.IsNullOrEmpty(label)) row.Children.Add(new TextBlock { Text = label, Foreground = MutedBrush,
                FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
            row.Children.Add(control);
            return row;
        }

        private static void AddWorkspaceCell(Grid grid, UIElement child, int row)
        {
            Grid.SetRow(child, row);
            grid.Children.Add(child);
        }

        private static Border WorkspaceBand(UIElement child)
        {
            return new Border { Child = child, Background = PanelBrush, Padding = new Thickness(8, 6, 8, 0), BorderBrush = PanelBorderBrush, BorderThickness = new Thickness(0, 0, 0, 1) };
        }

        private static Expander CreateDisclosure(string title, UIElement child, bool open)
        {
            return new Expander { Header = title, Foreground = TextBrush, IsExpanded = open,
                Margin = new Thickness(8, 4, 8, 4), Content = child };
        }

        private static Button CreateCommandMenu(string title, string[] labels, RoutedEventHandler[] handlers)
        {
            var button = CreateButton(title + " ▾", delegate { }, false);
            var menu = new ContextMenu { Background = PanelBrush, Foreground = TextBrush };
            for (int index = 0; index < labels.Length; index++)
            {
                var item = new MenuItem { Header = labels[index] };
                item.Click += handlers[index];
                menu.Items.Add(item);
            }
            button.Click += delegate { menu.PlacementTarget = button; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true; };
            button.ContextMenu = menu;
            return button;
        }

        private void SetWorkspaceZoom(double value)
        {
            if (frame != null && zoom > 0) ZoomAroundSelection(value / zoom);
        }

        private void FitLoadedView(object sender, RoutedEventArgs e)
        {
            if (frame == null) return;
            var fit = Math.Min(Math.Max(1, scrollViewer.ViewportWidth - 24) / frame.Configuration.Width,
                Math.Max(1, scrollViewer.ViewportHeight - 24) / frame.Configuration.Height);
            ApplyZoom(Math.Max(0.05, Math.Min(64.0, fit)));
            scrollViewer.UpdateLayout();
            var originX = showsFullFrameContext ? frame.Configuration.OriginX + virtualCanvasPaddingX : 0;
            var originY = showsFullFrameContext ? frame.Configuration.OriginY + virtualCanvasPaddingY : 0;
            scrollViewer.ScrollToHorizontalOffset(Math.Max(0, (originX + frame.Configuration.Width / 2.0) * zoom - scrollViewer.ViewportWidth / 2.0));
            scrollViewer.ScrollToVerticalOffset(Math.Max(0, (originY + frame.Configuration.Height / 2.0) * zoom - scrollViewer.ViewportHeight / 2.0));
        }
    }
}
