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
            var settingsRow = new RowDefinition { Height = new GridLength(0), MaxHeight = 350 };
            root.RowDefinitions.Add(settingsRow);
            var dividerRow = new RowDefinition { Height = new GridLength(0) };
            root.RowDefinitions.Add(dividerRow);
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            configurationScrollViewer = new ScrollViewer
            {
                Content = CreateHeader(), Background = RootBrush,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Visibility = Visibility.Collapsed
            };
            AddWorkspaceCell(root, configurationScrollViewer, 1);
            var splitter = new GridSplitter
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = PanelBorderBrush, ResizeDirection = GridResizeDirection.Rows,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext, ShowsPreview = true,
                Visibility = Visibility.Collapsed
            };
            AddWorkspaceCell(root, splitter, 2);
            double lastSettingsHeight = 210;
            var source = CreateRow();
            expression.Width = 260;
            source.Children.Add(CreateField("BUFFER EXPRESSION", expression));
            source.Children.Add(CreateAction("", CreateButton("Capture", LoadContextPreview, true)));
            source.Children.Add(CreateAction("", CreateCommandMenu("Source", new[] { "Refresh pointers", "Use editor selection" }, new RoutedEventHandler[] { RefreshPointers, CaptureSelection })));
            source.Children.Add(CreateField("POINTERS", availablePointers));
            source.Children.Add(CreateField("", autoUpdate));
            var settingsButton = CreateButton("Settings", delegate
            {
                bool open = configurationScrollViewer.Visibility != Visibility.Visible;
                if (!open) lastSettingsHeight = Math.Max(120, settingsRow.ActualHeight);
                configurationScrollViewer.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                splitter.Visibility = configurationScrollViewer.Visibility;
                settingsRow.Height = new GridLength(open ? lastSettingsHeight : 0);
                dividerRow.Height = new GridLength(open ? 5 : 0);
            }, false);
            settingsButton.ToolTip = "Image dimensions, Q-format, Bayer layout, objects and saved profiles";
            source.Children.Add(CreateAction("", settingsButton));
            source.Children.Add(CreateAction("", CreateCommandMenu("Read options", new[] { "Read exact kernel area", "Full-frame preview", "Refresh exact cells", "Cancel read" }, new RoutedEventHandler[] { LoadExpression, LoadFullPreview, InspectCells, CancelRead })));
            AddWorkspaceCell(root, WorkspaceBand(source), 0);

            var inspection = new Grid();
            inspection.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            inspection.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            inspection.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            inspection.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            AddWorkspaceCell(inspection, CreateViewerTabStrip(), 0);

            var tools = CreateRow();
            tools.Children.Add(CreateField("X", selectedX));
            tools.Children.Add(CreateField("Y", selectedY));
            tools.Children.Add(CreateAction("SELECTED PIXEL", CreateButton("Center", JumpToCoordinate, false)));
            tools.Children.Add(CreateField("KERNEL W", roiWidth));
            tools.Children.Add(CreateField("KERNEL H", roiHeight));
            tools.Children.Add(CreateField("DISPLAY", visualizeChannel));
            tools.Children.Add(CreateAction("", CreateCommandMenu("Zoom", new[] { "Fit loaded view", "100%", "Zoom in", "Zoom out" }, new RoutedEventHandler[] {
                FitLoadedView, delegate { SetWorkspaceZoom(1); },
                delegate { ZoomAroundSelection(1.25); }, delegate { ZoomAroundSelection(0.8); } })));
            tools.Children.Add(CreateAction("", CreateCommandMenu("Export", new[] { "Copy kernel text  ·  Ctrl+C", "Save full RAW…", "Save View RAW…", "Save Kernel RAW…" }, new RoutedEventHandler[] { CopyKernelToClipboard, SaveFullRaw, SaveViewRaw, SaveKernelRaw })));
            tools.Children.Add(CreateAction("", statisticsToggle));

            var map = new StackPanel { Width = 246, Margin = new Thickness(8), Visibility = Visibility.Collapsed };
            map.Children.Add(new TextBlock { Text = "FRAME MAP", Foreground = MutedBrush, Margin = new Thickness(0, 0, 0, 8) });
            map.Children.Add(navigatorCanvas);
            map.Children.Add(new TextBlock { Text = "Drag to set View ROI\nOrange rectangle = Kernel", Foreground = MutedBrush, FontSize = 11, Margin = new Thickness(0, 8, 0, 8) });
            map.Children.Add(navigatorInfo);
            var viewSize = CreateRow();
            viewSize.Margin = new Thickness(0, 12, 0, 0);
            viewSize.Children.Add(CreateField("VIEW W", renderWidth));
            viewSize.Children.Add(CreateField("VIEW H", renderHeight));
            map.Children.Add(viewSize);
            map.Children.Add(CreateCommandMenu("Move view", new[] { "Left", "Right", "Up", "Down" }, new RoutedEventHandler[] { PanLeft, PanRight, PanUp, PanDown }));
            tools.Children.Add(CreateAction("", CreateButton("Frame map", delegate { map.Visibility = map.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; }, false)));
            AddWorkspaceCell(inspection, WorkspaceBand(tools), 1);

            var auxiliary = new StackPanel();
            var watch = CreateRow();
            watch.Children.Add(CreateAction("RESUME UNTIL WRITE", CreateButton("Watch", WatchSelectedPixelAndContinue, false)));
            watch.Children.Add(CreateAction("", CreateButton("Next X", WatchNextPixelAndContinue, false)));
            watch.Children.Add(CreateAction("", CreateButton("Next Y", WatchNextLineAndContinue, false)));
            watch.Children.Add(CreateField("", keepHardwareWatchArmed));
            watch.Children.Add(CreateAction("", CreateButton("Clear", ClearHardwareWatch, false)));
            var watchPanel = new StackPanel();
            watchPanel.Children.Add(watch);
            watchPanel.Children.Add(hardwareWatchInfo);
            auxiliary.Children.Add(CreateDisclosure("Hardware watch · resumes debugger execution", watchPanel, false));
            var stats = new StackPanel { Margin = new Thickness(8) };
            stats.Children.Add(CreateField("STATISTICS SCOPE", statisticsScope));
            stats.Children.Add(statisticsSummary);
            stats.Children.Add(statisticsChannels);
            statisticsPanel.Children.Add(stats);
            auxiliary.Children.Add(new ScrollViewer { Content = statisticsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 160 });
            AddWorkspaceCell(inspection, auxiliary, 2);
            var stage = new DockPanel();
            DockPanel.SetDock(map, Dock.Right);
            stage.Children.Add(map);
            stage.Children.Add(scrollViewer);
            AddWorkspaceCell(inspection, stage, 3);
            AddWorkspaceCell(root, inspection, 3);
            AddWorkspaceCell(root, CreateFooter(), 4);
            return root;
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
