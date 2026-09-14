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
            tools.Children.Add(WorkspaceField("X", selectedX));
            tools.Children.Add(WorkspaceField("Y", selectedY));
            var selectPixel = CreateButton("Select X/Y", GoToEnteredCoordinate, false);
            selectPixel.ToolTip = "입력한 X/Y 픽셀을 선택합니다. 디버거를 실행하지 않습니다.";
            tools.Children.Add(WorkspaceAction("", selectPixel));
            tools.Children.Add(WorkspaceAction("", CreateIconButton("Center view", "Center view · 선택한 픽셀을 화면 중앙으로 이동합니다. 디버거를 실행하지 않습니다.", "M8,1 L8,4 M8,12 L8,15 M1,8 L4,8 M12,8 L15,8 M8,4 A4,4 0 1 1 8,12 A4,4 0 1 1 8,4", JumpToCoordinate)));
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
            movement.Children.Add(WorkspaceAction("", CreateIconButton("Left", "뷰를 왼쪽으로 이동", "M14,8 L2,8 M7,3 L2,8 L7,13", PanLeft)));
            movement.Children.Add(WorkspaceAction("", CreateIconButton("Right", "뷰를 오른쪽으로 이동", "M2,8 L14,8 M9,3 L14,8 L9,13", PanRight)));
            movement.Children.Add(WorkspaceAction("", CreateIconButton("Up", "뷰를 위로 이동", "M8,14 L8,2 M3,7 L8,2 L13,7", PanUp)));
            movement.Children.Add(WorkspaceAction("", CreateIconButton("Down", "뷰를 아래로 이동", "M8,2 L8,14 M3,9 L8,14 L13,9", PanDown)));
            mapContents.Children.Add(movement);
            var mapToggle = CreateIconButton("Frame map", "프레임 맵 숨기기", "M1,2 L15,2 L15,14 L1,14 Z M3,4 L8,4 L8,8 L3,8 Z M10,10 L13,10 M10,12 L13,12", null);
            mapToggle.Content = CreateFrameMapImage();
            mapToggle.Padding = new Thickness(3, 2, 3, 2);
            mapToggle.Click += delegate {
                map.Visibility = map.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                mapToggle.ToolTip = map.Visibility == Visibility.Visible ? "프레임 맵 숨기기" : "프레임 맵 표시";
            };
            tools.Children.Add(WorkspaceAction("", mapToggle));
            AddWorkspaceCell(inspection, WorkspaceBand(tools), 1);

            var auxiliary = new StackPanel();
            var watch = CreateRow();
            watch.Children.Add(new TextBlock { Text = "디버거 실행", Foreground = MutedBrush,
                FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6) });
            watch.Children.Add(WorkspaceAction("", CreateRunButton("Run to write X/Y", "입력한 X/Y 픽셀에 쓰기가 발생할 때까지 디버거를 실행합니다.", WatchSelectedPixelAndContinue)));
            watch.Children.Add(WorkspaceAction("", CreateRunButton("Run to next X", "다음 X 픽셀에 쓰기가 발생할 때까지 디버거를 실행합니다.", WatchNextPixelAndContinue)));
            watch.Children.Add(WorkspaceAction("", CreateRunButton("Run to next Y", "다음 Y 픽셀에 쓰기가 발생할 때까지 디버거를 실행합니다.", WatchNextLineAndContinue)));
            watch.Children.Add(WorkspaceField("", keepHardwareWatchArmed));
            watch.Children.Add(WorkspaceAction("", CreateButton("Clear", ClearHardwareWatch, false)));
            var watchPanel = new StackPanel();
            watchPanel.Children.Add(watch);
            watchPanel.Children.Add(hardwareWatchInfo);
            watchPanel.ToolTip = "하드웨어 데이터 중단점으로 대상 픽셀의 쓰기를 기다립니다. Run 버튼은 디버거 실행을 재개합니다.";
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
