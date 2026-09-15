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
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddWorkspaceCell(root, CreateViewerTabStrip(), 0);
            AddWorkspaceCell(root, CreateWorkspaceSource(), 1);
            AddWorkspaceCell(root, CreateWorkspaceTarget(), 2);

            // Settings and map share one inspector column. This splitter changes
            // layout only; no control or debugger handler is recreated on resize.
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 240 });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280), MinWidth = 260, MaxWidth = 460 });
            var inspector = new Grid { Background = RootBrush };
            inspector.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 90 });
            inspector.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddWorkspaceCell(inspector, CreateHeader(), 0);

            var map = CreateInspectorMap();
            var details = new StackPanel();
            details.Children.Add(map);
            var stats = new StackPanel { Margin = new Thickness(12, 6, 12, 8) };
            stats.Children.Add(WorkspaceField("STATISTICS SCOPE", statisticsScope));
            stats.Children.Add(statisticsSummary);
            stats.Children.Add(statisticsChannels);
            statisticsPanel.Children.Add(stats);
            details.Children.Add(statisticsPanel);
            var detailsScroll = new ScrollViewer { Content = details, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            inspector.SizeChanged += delegate {
                detailsScroll.MaxHeight = Math.Max(0, inspector.ActualHeight - 140);
            };
            AddWorkspaceCell(inspector, detailsScroll, 1);

            var imageArea = new Grid();
            imageArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            imageArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            AddWorkspaceCell(imageArea, CreateWorkspaceImageTools(map), 0);
            AddWorkspaceCell(imageArea, scrollViewer, 1);
            body.Children.Add(imageArea);
            var splitter = new GridSplitter { Background = PanelBorderBrush,
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ShowsPreview = true, ToolTip = "Drag to resize the settings panel and image." };
            Grid.SetColumn(splitter, 1);
            body.Children.Add(splitter);
            Grid.SetColumn(inspector, 2);
            body.Children.Add(inspector);
            AddWorkspaceCell(root, body, 3);
            AddWorkspaceCell(root, CreateFooter(), 4);
            return root;
        }

        private UIElement CreateWorkspaceSource()
        {
            var source = new Grid();
            source.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            source.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            expression.Width = Double.NaN;
            expression.HorizontalAlignment = HorizontalAlignment.Stretch;
            var input = new DockPanel { Margin = new Thickness(0, 0, 10, 6) };
            var label = new TextBlock { Text = "Buffer", Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(label, Dock.Left);
            input.Children.Add(label);
            input.Children.Add(expression);
            source.Children.Add(input);
            var actions = CreateRow();
            availablePointers.Width = 130;
            availablePointers.ToolTip = "Pointers from the current stack frame";
            System.Windows.Automation.AutomationProperties.SetName(availablePointers, "Pointers");
            actions.Children.Add(WorkspaceAction("", CreateButton("Capture", LoadContextPreview, true)));
            var pointers = new Grid();
            pointers.Children.Add(availablePointers);
            var pointerHintStyle = new Style(typeof(TextBlock));
            pointerHintStyle.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
            var noPointer = new DataTrigger { Binding = new System.Windows.Data.Binding("SelectedItem") { Source = availablePointers }, Value = null };
            noPointer.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible));
            pointerHintStyle.Triggers.Add(noPointer);
            pointers.Children.Add(new TextBlock { Text = "Pointers", Foreground = MutedBrush, Margin = new Thickness(8, 0, 25, 0),
                VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Style = pointerHintStyle });
            actions.Children.Add(WorkspaceField("", pointers));
            actions.Children.Add(WorkspaceField("", autoUpdate));
            actions.Children.Add(WorkspaceAction("", CreateCommandMenu("Source",
                new[] { "Refresh pointers", "Use editor selection", "Read exact kernel area", "Full-frame preview", "Refresh exact cells", "Cancel read" },
                new RoutedEventHandler[] { RefreshPointers, CaptureSelection, LoadExpression, LoadFullPreview, InspectCells, CancelRead })));
            Grid.SetColumn(actions, 1);
            source.Children.Add(actions);
            return WorkspaceBand(source);
        }

        private UIElement CreateWorkspaceTarget()
        {
            var groups = CreateRow();
            var target = CreateRow();
            target.Children.Add(WorkspaceField("X", selectedX));
            target.Children.Add(WorkspaceField("Y", selectedY));
            var select = CreateButton("Select X/Y", GoToEnteredCoordinate, false);
            select.ToolTip = "입력한 X/Y 픽셀을 선택합니다. 디버거를 실행하지 않습니다.";
            target.Children.Add(WorkspaceAction("", select));
            target.Children.Add(WorkspaceAction("", CreateIconButton("Center view",
                "Center view · 선택한 픽셀을 화면 중앙으로 이동합니다. 디버거를 실행하지 않습니다.",
                "M8,1 L8,4 M8,12 L8,15 M1,8 L4,8 M12,8 L15,8 M8,4 A4,4 0 1 1 8,12 A4,4 0 1 1 8,4", JumpToCoordinate)));
            groups.Children.Add(new Border { Child = target, Margin = new Thickness(0, 0, 12, 0) });

            var watch = CreateRow();
            watch.Children.Add(WorkspaceAction("", CreateRunButton("Run to write X/Y", "입력한 X/Y 픽셀에 쓰기가 발생할 때까지 디버거를 실행합니다.", WatchSelectedPixelAndContinue)));
            watch.Children.Add(WorkspaceAction("", CreateRunButton("Run to next X", "다음 X 픽셀에 쓰기가 발생할 때까지 디버거를 실행합니다.", WatchNextPixelAndContinue)));
            watch.Children.Add(WorkspaceAction("", CreateRunButton("Run to next Y", "다음 Y 픽셀에 쓰기가 발생할 때까지 디버거를 실행합니다.", WatchNextLineAndContinue)));
            watch.Children.Add(WorkspaceField("", keepHardwareWatchArmed));
            watch.Children.Add(WorkspaceAction("", CreateButton("Clear", ClearHardwareWatch, false)));
            groups.Children.Add(watch);
            var panel = new StackPanel();
            panel.Children.Add(groups);
            hardwareWatchInfo.Margin = new Thickness(0, 0, 0, 5);
            hardwareWatchInfo.MaxHeight = 32;
            hardwareWatchInfo.SetBinding(TextBlock.ToolTipProperty, new System.Windows.Data.Binding("Text") { Source = hardwareWatchInfo });
            panel.Children.Add(hardwareWatchInfo);
            return WorkspaceBand(panel);
        }

        private UIElement CreateWorkspaceImageTools(UIElement map)
        {
            var tools = CreateRow();
            roiWidth.Width = 42;
            roiHeight.Width = 42;
            tools.Children.Add(WorkspaceField("Kernel W", roiWidth));
            tools.Children.Add(WorkspaceField("H", roiHeight));
            tools.Children.Add(WorkspaceField("", visualizeChannel));
            tools.Children.Add(WorkspaceAction("", CreateCommandMenu("Zoom", new[] { "Fit loaded view", "100%", "Zoom in", "Zoom out" },
                new RoutedEventHandler[] { FitLoadedView, delegate { SetWorkspaceZoom(1); },
                    delegate { ZoomAroundSelection(1.25); }, delegate { ZoomAroundSelection(0.8); } })));
            tools.Children.Add(WorkspaceAction("", CreateCommandMenu("Export", new[] { "Copy kernel text  ·  Ctrl+C", "Save full RAW…", "Save View RAW…", "Save Kernel RAW…" },
                new RoutedEventHandler[] { CopyKernelToClipboard, SaveFullRaw, SaveViewRaw, SaveKernelRaw })));
            tools.Children.Add(WorkspaceAction("", statisticsToggle));
            var mapToggle = CreateIconButton("Frame map", "프레임 맵 숨기기", "M1,2 L15,2 L15,14 L1,14 Z M3,4 L8,4 L8,8 L3,8 Z M10,10 L13,10 M10,12 L13,12", null);
            mapToggle.Content = CreateFrameMapImage();
            mapToggle.Padding = new Thickness(3, 2, 3, 2);
            mapToggle.Click += delegate {
                map.Visibility = map.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                mapToggle.ToolTip = map.Visibility == Visibility.Visible ? "프레임 맵 숨기기" : "프레임 맵 표시";
            };
            tools.Children.Add(WorkspaceAction("", mapToggle));
            return WorkspaceBand(tools);
        }

        private UIElement CreateInspectorMap()
        {
            navigatorCanvas.Width = 230;
            navigatorCanvas.Height = 128;
            navigatorCanvas.HorizontalAlignment = HorizontalAlignment.Center;
            navigatorCanvas.ToolTip = "Drag to set View ROI. Orange rectangle = Kernel.";
            var content = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
            content.SizeChanged += delegate {
                navigatorCanvas.Width = Math.Max(1, Math.Min(230, content.ActualWidth));
                UpdateNavigator();
            };
            content.Children.Add(new TextBlock { Text = "Frame map", Foreground = TextBrush, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6) });
            content.Children.Add(navigatorCanvas);
            navigatorInfo.Margin = new Thickness(0, 5, 0, 5);
            content.Children.Add(navigatorInfo);
            var viewSize = CreateRow();
            viewSize.Children.Add(WorkspaceField("View W", renderWidth));
            viewSize.Children.Add(WorkspaceField("H", renderHeight));
            content.Children.Add(viewSize);
            var movement = CreateRow();
            movement.Children.Add(WorkspaceAction("", CreateIconButton("Left", "뷰를 왼쪽으로 이동", "M14,8 L2,8 M7,3 L2,8 L7,13", PanLeft)));
            movement.Children.Add(WorkspaceAction("", CreateIconButton("Right", "뷰를 오른쪽으로 이동", "M2,8 L14,8 M9,3 L14,8 L9,13", PanRight)));
            movement.Children.Add(WorkspaceAction("", CreateIconButton("Up", "뷰를 위로 이동", "M8,14 L8,2 M3,7 L8,2 L13,7", PanUp)));
            movement.Children.Add(WorkspaceAction("", CreateIconButton("Down", "뷰를 아래로 이동", "M8,2 L8,14 M3,9 L8,14 L13,9", PanDown)));
            content.Children.Add(movement);
            return new Border { Child = content, BorderBrush = PanelBorderBrush, BorderThickness = new Thickness(0, 1, 0, 0) };
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
            if (title == "Zoom") SetIconContent(button, "Zoom ▾", "확대/축소 · 화면 맞춤 · 100%", "M7,1 A6,6 0 1 1 7,13 A6,6 0 1 1 7,1 M11,11 L15,15", true);
            if (title == "Export") SetIconContent(button, "Export ▾", "내보내기 · Kernel 텍스트 복사(Ctrl+C) · RAW 저장", "M8,1 L8,10 M4,6 L8,10 L12,6 M2,10 L2,15 L14,15 L14,10", true);
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

        private static Image CreateFrameMapImage()
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            using (var stream = typeof(SensorImageViewerControl).Assembly.GetManifestResourceStream("ArrayImageViewer.UI.Assets.FrameMap.png"))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 80; // 20 DIP icon, up to 400% DPI.
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }
            bitmap.Freeze();
            var image = new Image { Source = bitmap, Width = 20, Height = 20, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }

        private static Button CreateRunButton(string name, string tooltip, RoutedEventHandler handler)
        {
            var button = CreateButton("▶ " + name, handler, false);
            button.ToolTip = tooltip;
            System.Windows.Automation.AutomationProperties.SetName(button, name);
            System.Windows.Automation.AutomationProperties.SetHelpText(button, tooltip);
            return button;
        }

        // WPF vectors scale with DPI and do not depend on a Windows icon font
        // or a newer Visual Studio image service. Keep handlers/menus unchanged.
        private static Button CreateIconButton(string name, string tooltip, string geometry, RoutedEventHandler handler)
        {
            var button = CreateButton(name, delegate { }, false);
            if (handler != null) button.Click += handler;
            SetIconContent(button, name, tooltip, geometry, false);
            return button;
        }

        private static void SetIconContent(Button button, string name, string tooltip, string geometry, bool menu)
        {
            var canvas = new Canvas { Width = 16, Height = 16, IsHitTestVisible = false };
            var path = new System.Windows.Shapes.Path {
                Data = Geometry.Parse(geometry), StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round };
            path.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,
                new System.Windows.Data.Binding("Foreground") { Source = button });
            canvas.Children.Add(path);
            var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(canvas);
            if (menu) content.Children.Add(new TextBlock { Text = "▾", Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            button.Content = content;
            button.Width = menu ? 43 : 30;
            button.Padding = new Thickness(5, 3, 5, 3);
            button.ToolTip = tooltip;
            System.Windows.Automation.AutomationProperties.SetName(button, name);
            System.Windows.Automation.AutomationProperties.SetHelpText(button, tooltip);
            ToolTipService.SetShowOnDisabled(button, true);
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
