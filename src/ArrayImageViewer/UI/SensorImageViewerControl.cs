using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ArrayImageViewer.Core;
using ArrayImageViewer.Debugging;

namespace ArrayImageViewer.UI
{
    internal sealed partial class SensorImageViewerControl : UserControl
    {
        private const long NormalFullPreviewSampleLimit = 4L * 1024L * 1024L;
        private static readonly Brush RootBrush = new SolidColorBrush(Color.FromRgb(13, 19, 30));
        private static readonly Brush PanelBrush = new SolidColorBrush(Color.FromRgb(24, 33, 48));
        private static readonly Brush ControlBrush = new SolidColorBrush(Color.FromRgb(15, 23, 38));
        private static readonly Brush CanvasBrush = new SolidColorBrush(Color.FromRgb(6, 10, 17));
        private static readonly Brush UnloadedFrameBrush = new SolidColorBrush(Color.FromRgb(29, 41, 57));
        private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(67, 214, 177));
        private static readonly Brush AccentSoftBrush = new SolidColorBrush(Color.FromRgb(24, 65, 64));
        private static readonly Brush PanelBorderBrush = new SolidColorBrush(Color.FromRgb(58, 75, 98));
        private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(255, 111, 111));
        private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(154, 171, 194));
        private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(237, 242, 249));

        private readonly TextBox expression = CreateTextBox("sensorRaw", 240);
        private readonly ComboBox availablePointers = CreateComboBox(190);
        private readonly ComboBox profilePicker = CreateComboBox(185);
        private readonly TextBox profileSetName = CreateTextBox("", 120);
        private readonly ComboBox profileSetPicker = CreateComboBox(200);
        private readonly CheckBox rememberForSolution = new CheckBox { Content = "Remember for this solution", Foreground = TextBrush, IsChecked = true, Height = 27, VerticalAlignment = VerticalAlignment.Center };
        private readonly Popup expressionSuggestions = new Popup { AllowsTransparency = true, Placement = PlacementMode.Bottom, StaysOpen = false };
        private readonly ListBox expressionSuggestionList = new ListBox { Background = ControlBrush, Foreground = TextBrush, BorderThickness = new Thickness(0), MaxHeight = 220, MinWidth = 240 };
        private readonly ComboBox widthValueSource = CreateComboBox(145);
        private readonly ComboBox heightValueSource = CreateComboBox(145);
        private readonly ComboBox strideValueSource = CreateComboBox(145);
        private readonly ComboBox xValueSource = CreateComboBox(145);
        private readonly ComboBox yValueSource = CreateComboBox(145);
        private readonly TextBox width = CreateTextBox("4096", 60);
        private readonly TextBox height = CreateTextBox("3072", 60);
        private readonly TextBox stride = CreateTextBox("4096", 60);
        private readonly TextBox qFormat = CreateTextBox("13.0b", 58);
        private readonly CheckBox signed = new CheckBox { Content = "Signed", Foreground = TextBrush, Height = 27, VerticalAlignment = VerticalAlignment.Center };
        private readonly ComboBox sourceElementType = CreateComboBox(82);
        private readonly ComboBox pixelOrder = CreateComboBox(108);
        private readonly ComboBox pixelType = CreateComboBox(108);
        private readonly ComboBox visualizeChannel = CreateComboBox(116);
        private readonly ComboBox normalizationMode = CreateComboBox(130);
        private readonly TextBox normalizationMinimum = CreateTextBox("0", 88);
        private readonly TextBox normalizationMaximum = CreateTextBox("8191", 88);
        private readonly TextBox selectedX = CreateTextBox("0", 60);
        private readonly TextBox selectedY = CreateTextBox("0", 60);
        private readonly TextBox renderWidth = CreateTextBox("128", 54);
        private readonly TextBox renderHeight = CreateTextBox("128", 54);
        private readonly TextBox roiWidth = CreateTextBox("5", 54);
        private readonly TextBox roiHeight = CreateTextBox("5", 54);
        private readonly CheckBox autoUpdate = new CheckBox { Content = "Auto update", Foreground = TextBrush, IsChecked = true, Height = 27, VerticalAlignment = VerticalAlignment.Center };
        private readonly CheckBox keepHardwareWatchArmed = new CheckBox { Content = "Keep armed", Foreground = TextBrush, IsChecked = false, Height = 27, VerticalAlignment = VerticalAlignment.Center };
        private readonly ComboBox statisticsScope = CreateComboBox(118);
        private readonly Button statisticsToggle;
        private readonly StackPanel statisticsPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
        private readonly TextBlock statisticsSummary = new TextBlock { Foreground = TextBrush, FontFamily = new FontFamily("Consolas"), FontSize = 11, TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock statisticsChannels = new TextBlock { Foreground = MutedBrush, FontFamily = new FontFamily("Consolas"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        private readonly TextBlock hardwareWatchInfo = new TextBlock { Foreground = MutedBrush, Text = "No hardware watch armed.", Margin = new Thickness(0, 8, 0, 0), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        private readonly TextBox status = new TextBox
        {
            Foreground = TextBrush,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Select text and press Ctrl+C to copy."
        };
        private readonly TextBlock viewport = new TextBlock { Foreground = MutedBrush, Text = "No frame loaded", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly ScrollViewer scrollViewer = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = CanvasBrush, Focusable = true };
        private readonly Canvas canvas = new Canvas { Background = CanvasBrush, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, MinWidth = 520, MinHeight = 260, Focusable = true };
        private readonly Rectangle unloadedFrame = new Rectangle { Fill = UnloadedFrameBrush, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        private readonly System.Windows.Controls.Image image = new System.Windows.Controls.Image { Stretch = Stretch.Fill, SnapsToDevicePixels = true };
        private readonly Border emptyState = CreateEmptyState();
        private readonly Rectangle roiRectangle = new Rectangle { Stroke = Brushes.OrangeRed, StrokeThickness = 2, Fill = Brushes.Transparent, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        private readonly Rectangle mouseRoiRectangle = new Rectangle { Stroke = AccentBrush, StrokeThickness = 2, StrokeDashArray = new DoubleCollection(new double[] { 3, 2 }), Fill = new SolidColorBrush(Color.FromArgb(45, 67, 214, 177)), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        private readonly Rectangle selectedCellRectangle = new Rectangle { Stroke = new SolidColorBrush(Color.FromRgb(255, 211, 82)), StrokeThickness = 2, Fill = Brushes.Transparent, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        private readonly Canvas navigatorCanvas = new Canvas { Width = 230, Height = 156, Background = ControlBrush, ClipToBounds = true, Cursor = Cursors.Cross };
        private readonly Rectangle navigatorFrame = new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(19, 31, 47)), Stroke = PanelBorderBrush, StrokeThickness = 1, IsHitTestVisible = false };
        private readonly System.Windows.Controls.Image navigatorPreview = new System.Windows.Controls.Image { Stretch = Stretch.Fill, SnapsToDevicePixels = true, IsHitTestVisible = false };
        private readonly Rectangle navigatorRoi = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(70, 67, 214, 177)), Stroke = AccentBrush, StrokeThickness = 2, IsHitTestVisible = false };
        private readonly Rectangle navigatorKernel = new Rectangle { Fill = Brushes.Transparent, Stroke = Brushes.OrangeRed, StrokeThickness = 2, IsHitTestVisible = false };
        private readonly Line navigatorHorizontal = new Line { Stroke = new SolidColorBrush(Color.FromArgb(130, 255, 126, 69)), StrokeThickness = 1, IsHitTestVisible = false };
        private readonly Line navigatorVertical = new Line { Stroke = new SolidColorBrush(Color.FromArgb(130, 255, 126, 69)), StrokeThickness = 1, IsHitTestVisible = false };
        private readonly TextBlock navigatorInfo = new TextBlock { Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap, FontSize = 11, LineHeight = 17 };
        private readonly List<UIElement> valueOverlay = new List<UIElement>();
        private readonly WrapPanel viewerTabPanel = new WrapPanel();
        private readonly List<ViewerTab> viewerTabs = new List<ViewerTab>();

        private FrameBuffer frame;
        private double zoom = 0.15;
        private int currentX;
        private int currentY;
        private int kernelCenterX = -1;
        private int kernelCenterY = -1;
        private int viewCenterX = -1;
        private int viewCenterY = -1;
        private bool preserveViewportOnNextRender;
        private int fullFrameWidth;
        private int fullFrameHeight;
        // Sample-space margins around the physical frame. They let a border
        // pixel sit at the screen center while the area outside the sensor is
        // rendered as the unloaded-frame color.
        private int virtualCanvasPaddingX;
        private int virtualCanvasPaddingY;
        private bool isRoiPanning;
        private Point roiPanStart;
        private double roiPanStartHorizontalOffset;
        private double roiPanStartVerticalOffset;
        private bool isMouseRoiSelecting;
        private bool isFixedMouseRoiSelecting;
        private int mouseRoiStartX;
        private int mouseRoiStartY;
        private int mouseRoiEndX;
        private int mouseRoiEndY;
        private readonly Dictionary<string, ViewerProfile> profiles = new Dictionary<string, ViewerProfile>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, NamedProfileSet> profileSets = new Dictionary<string, NamedProfileSet>(StringComparer.OrdinalIgnoreCase);
        private readonly List<DebugExpressionFrameReader.PointerExpression> pointerCandidates = new List<DebugExpressionFrameReader.PointerExpression>();
        private bool isApplyingProfile;
        private string activeProfileExpression;
        private bool isNavigatorSelecting;
        private int navigatorStartX;
        private int navigatorStartY;
        private Point navigatorDragStartPoint;
        private Rect navigatorFrameBounds;
        private string lastReadPath = "expression fallback";
        private FrameConfiguration navigatorSourceConfiguration;
        private string navigatorPreviewKey;
        private NormalizationRange activeNormalization = new NormalizationRange(0, 8191);
        private int renderGeneration;
        private int statisticsGeneration;
        private readonly DispatcherTimer memoryReadTimer;
        private readonly DispatcherTimer autoRefreshTimer;
        private readonly DispatcherTimer debuggerBreakRefreshTimer;
        private readonly DispatcherTimer coordinateUpdateTimer;
        private readonly DispatcherTimer viewportOverlayTimer;
        private PendingMemoryRead pendingMemoryRead;
        private TextBox invalidInput;
        private bool showsFullFrameContext;
        private bool strideFollowsWidth = true;
        private string lastWidthText = "4096";
        private bool isSynchronizingStride;
        private bool isLeftPanPending;
        private Point leftPanStartPoint;

        public SensorImageViewerControl()
        {
            statisticsToggle = CreateButton("Stats", ToggleStatistics, false);
            pixelOrder.ItemsSource = Enum.GetValues(typeof(PixelOrder));
            pixelOrder.SelectedItem = PixelOrder.GRFirst;
            pixelType.ItemsSource = Enum.GetValues(typeof(PixelType));
            pixelType.SelectedItem = PixelType.Bayer;
            visualizeChannel.ItemsSource = Enum.GetValues(typeof(VisualizeChannel));
            visualizeChannel.SelectedItem = VisualizeChannel.BayerRaw;
            normalizationMode.ItemsSource = new[]
            {
                new NormalizationModeChoice(NormalizationMode.QFormatRange, "Q-format range"),
                new NormalizationModeChoice(NormalizationMode.LoadedDataRange, "Loaded data range"),
                new NormalizationModeChoice(NormalizationMode.ManualRawRange, "Manual raw range")
            };
            normalizationMode.SelectedIndex = 0;
            sourceElementType.ItemsSource = Enum.GetValues(typeof(SourceElementType));
            sourceElementType.SelectedItem = SourceElementType.UInt32;
            statisticsScope.ItemsSource = new[] { "View", "Kernel", "Loaded ROI", "Filter match" };
            statisticsScope.SelectedIndex = 0;
            statisticsScope.SelectionChanged += StatisticsScopeChanged;
            visualizeChannel.SelectionChanged += StatisticsFilterChanged;
            availablePointers.SelectionChanged += AvailablePointerChanged;
            profilePicker.SelectionChanged += ProfilePickerChanged;
            profileSetPicker.SelectionChanged += ProfileSetPickerChanged;
            expression.TextChanged += ExpressionTextChanged;
            expression.GotKeyboardFocus += ExpressionGotKeyboardFocus;
            expression.PreviewKeyDown += ExpressionPreviewKeyDown;
            expressionSuggestionList.SelectionChanged += ExpressionSuggestionSelected;
            ConfigureExpressionSuggestions();
            profilePicker.ItemsSource = new object[] { "No session profiles yet" };
            profilePicker.SelectedIndex = 0;
            widthValueSource.SelectionChanged += WidthValueSelected;
            heightValueSource.SelectionChanged += HeightValueSelected;
            strideValueSource.SelectionChanged += StrideValueSelected;
            xValueSource.SelectionChanged += XValueSelected;
            yValueSource.SelectionChanged += YValueSelected;
            ConfigureProfileAutoSave();
            memoryReadTimer = new DispatcherTimer(DispatcherPriority.Background);
            memoryReadTimer.Interval = TimeSpan.FromMilliseconds(1);
            memoryReadTimer.Tick += MemoryReadTimerTick;
            autoRefreshTimer = new DispatcherTimer(DispatcherPriority.Background);
            autoRefreshTimer.Interval = TimeSpan.FromMilliseconds(450);
            autoRefreshTimer.Tick += AutoRefreshTimerTick;
            debuggerBreakRefreshTimer = new DispatcherTimer(DispatcherPriority.Background);
            debuggerBreakRefreshTimer.Interval = TimeSpan.FromMilliseconds(180);
            debuggerBreakRefreshTimer.Tick += DebuggerBreakRefreshTimerTick;
            coordinateUpdateTimer = new DispatcherTimer(DispatcherPriority.Background);
            coordinateUpdateTimer.Interval = TimeSpan.FromMilliseconds(220);
            coordinateUpdateTimer.Tick += CoordinateUpdateTimerTick;
            viewportOverlayTimer = new DispatcherTimer(DispatcherPriority.Background);
            // Wait for scroll/pan to settle before rebuilding hundreds of
            // high-zoom value labels. The bitmap itself remains responsive.
            viewportOverlayTimer.Interval = TimeSpan.FromMilliseconds(240);
            viewportOverlayTimer.Tick += ViewportOverlayTimerTick;
            ConfigureAutoUpdate();
            Unloaded += ViewerUnloaded;
            Focusable = true;
            PreviewKeyDown += ViewerPreviewKeyDown;

            canvas.Children.Add(unloadedFrame);
            canvas.Children.Add(image);
            canvas.Children.Add(emptyState);
            canvas.Children.Add(roiRectangle);
            canvas.Children.Add(mouseRoiRectangle);
            canvas.Children.Add(selectedCellRectangle);
            canvas.MouseMove += CanvasMouseMove;
            canvas.MouseLeftButtonDown += CanvasMouseLeftButtonDown;
            canvas.MouseLeftButtonUp += CanvasMouseLeftButtonUp;
            canvas.MouseRightButtonDown += CanvasMouseRightButtonDown;
            canvas.MouseRightButtonUp += CanvasMouseRightButtonUp;
            canvas.MouseDown += CanvasMouseDown;
            canvas.MouseUp += CanvasMouseUp;
            canvas.PreviewMouseWheel += CanvasMouseWheel;
            scrollViewer.ScrollChanged += ScrollViewerChanged;
            scrollViewer.SizeChanged += ScrollViewerSizeChanged;
            scrollViewer.Content = canvas;

            navigatorCanvas.Children.Add(navigatorFrame);
            navigatorCanvas.Children.Add(navigatorPreview);
            navigatorCanvas.Children.Add(navigatorHorizontal);
            navigatorCanvas.Children.Add(navigatorVertical);
            navigatorCanvas.Children.Add(navigatorRoi);
            navigatorCanvas.Children.Add(navigatorKernel);
            navigatorCanvas.MouseLeftButtonDown += NavigatorMouseLeftButtonDown;
            navigatorCanvas.MouseMove += NavigatorMouseMove;
            navigatorCanvas.MouseLeftButtonUp += NavigatorMouseLeftButtonUp;

            var root = new Grid { Background = RootBrush };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(260), MinHeight = 118 });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(7) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 180 });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = CreateHeader();
            var configurationScroll = new ScrollViewer
            {
                Content = header,
                Background = RootBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = PanelBorderBrush,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(configurationScroll, 0);
            root.Children.Add(configurationScroll);
            var splitter = new GridSplitter
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = PanelBorderBrush,
                ResizeDirection = GridResizeDirection.Rows,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ShowsPreview = true,
                Cursor = Cursors.SizeNS
            };
            Grid.SetRow(splitter, 1);
            root.Children.Add(splitter);
            var tabStrip = CreateViewerTabStrip();
            Grid.SetRow(tabStrip, 2);
            root.Children.Add(tabStrip);
            var footer = CreateFooter();
            Grid.SetRow(footer, 4);
            root.Children.Add(footer);
            Grid.SetRow(scrollViewer, 3);
            root.Children.Add(scrollViewer);
            Content = root;
            RestorePersistentWorkspaceState();
            InitializeViewerTabs();
            UpdateNavigator();
            if (String.IsNullOrWhiteSpace(expression.Text) || activeProfileExpression == null)
            {
                SetStatus("Pause the debuggee, choose a pointer or editor selection, then show ROI + context around X/Y.");
            }
        }

        private UIElement CreateHeader()
        {
            var panel = new StackPanel { Margin = new Thickness(14, 12, 14, 10) };
            panel.Children.Add(new TextBlock
            {
                Text = "ARRAY RAW VIEWER",
                Foreground = TextBrush,
                FontSize = 19,
                FontWeight = FontWeights.SemiBold
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Pointer buffer inspector  |  Bayer phase follows full-frame coordinates",
                Foreground = MutedBrush,
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 11)
            });

            // Keep the actual inspection flow together: choose/capture a
            // pointer, then immediately read it. Profiles are supporting
            // state, so they live on a quieter second line instead of forcing
            // the primary actions to wrap on ordinary tool-window widths.
            var sourceRow = CreateRow();
            sourceRow.Children.Add(CreateField("POINTER", availablePointers));
            sourceRow.Children.Add(CreateAction("LOCALS", CreateButton("Refresh", RefreshPointers, false)));
            sourceRow.Children.Add(CreateField("EXPRESSION", expression));
            sourceRow.Children.Add(CreateAction("EDITOR", CreateButton("Use selection", CaptureSelection, false)));
            sourceRow.Children.Add(CreateAction("SHOW", CreateButton("ROI + context", LoadContextPreview, true)));
            sourceRow.Children.Add(CreateAction("PIXELS", CreateButton("Exact ROI", LoadExpression, false)));
            sourceRow.Children.Add(CreateAction("", CreateButton("Full preview", LoadFullPreview, false)));
            sourceRow.Children.Add(CreateAction("RAW", CreateButton("Save full", SaveFullRaw, false)));
            sourceRow.Children.Add(CreateAction("", CreateButton("Cancel read", CancelRead, false)));
            sourceRow.Children.Add(CreateField("UPDATE", autoUpdate));
            var profileRow = CreateRow();
            profileRow.Margin = new Thickness(0, 8, 0, 0);
            profileRow.Children.Add(CreateField("SESSION PROFILE", profilePicker));
            profileRow.Children.Add(CreateAction("", CreateButton("Save settings", SaveProfileSettings, false)));
            profileRow.Children.Add(CreateField("PROFILE SET", profileSetPicker));
            profileRow.Children.Add(CreateField("SET NAME", profileSetName));
            profileRow.Children.Add(CreateAction("", CreateButton("Save set", SaveProfileSet, false)));
            profileRow.Children.Add(CreateAction("", CreateButton("Delete set", DeleteProfileSet, false)));
            profileRow.Children.Add(CreateField("PERSIST", rememberForSolution));
            profileRow.Children.Add(new TextBlock
            {
                Text = "Profile sets and the last state are local to this solution; RAW samples are never saved.",
                Foreground = MutedBrush,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 20, 0, 0)
            });
            var sourceContent = new StackPanel();
            sourceContent.Children.Add(sourceRow);
            sourceContent.Children.Add(profileRow);
            panel.Children.Add(CreateSection("SOURCE", "Choose a pointer, or use the selected editor expression. ROI + context is the normal first view; Exact ROI is for close-up values.", sourceContent));

            var formatRow = CreateRow();
            formatRow.Children.Add(CreateField("WIDTH", width));
            formatRow.Children.Add(CreateField("HEIGHT", height));
            formatRow.Children.Add(CreateField("ROW STRIDE", stride));
            formatRow.Children.Add(CreateField("Q FORMAT", qFormat));
            formatRow.Children.Add(CreateField("ELEMENT", sourceElementType));
            formatRow.Children.Add(CreateField("VALUE TYPE", signed));
            formatRow.Children.Add(CreateField("PIXEL ORDER", pixelOrder));
            formatRow.Children.Add(CreateField("PIXEL TYPE", pixelType));
            formatRow.Children.Add(CreateField("VISUALIZE", visualizeChannel));
            formatRow.Children.Add(CreateAction("LOCAL STACK", CreateButton("Auto-fill", RefreshScalarValues, false)));
            var normalizationRow = CreateRow();
            normalizationRow.Children.Add(CreateField("NORMALIZE", normalizationMode));
            normalizationRow.Children.Add(CreateField("MIN RAW", normalizationMinimum));
            normalizationRow.Children.Add(CreateField("MAX RAW", normalizationMaximum));
            normalizationRow.Children.Add(CreateAction("", CreateButton("Apply display range", ApplyNormalization, false)));
            normalizationRow.Children.Add(new TextBlock { Text = "Q-format range is the default. Apply changes recolors cached samples without a debugger read.", Foreground = MutedBrush, Margin = new Thickness(12, 23, 0, 0), FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            var formatContent = new StackPanel();
            formatContent.Children.Add(formatRow);
            formatContent.Children.Add(normalizationRow);
            panel.Children.Add(CreateSection("FRAME", "Full-frame dimensions; Composite is a lightweight nearest-site Bayer preview, while pixel inspection always shows the original sample", formatContent));

            var localValuesRow = CreateRow();
            localValuesRow.Children.Add(CreateAction("", CreateButton("Refresh numeric locals", RefreshScalarValues, false)));
            localValuesRow.Children.Add(CreateField("WIDTH FROM", widthValueSource));
            localValuesRow.Children.Add(CreateField("HEIGHT FROM", heightValueSource));
            localValuesRow.Children.Add(CreateField("STRIDE FROM", strideValueSource));
            localValuesRow.Children.Add(CreateField("X FROM", xValueSource));
            localValuesRow.Children.Add(CreateField("Y FROM", yValueSource));
            panel.Children.Add(new Expander
            {
                Header = new TextBlock { Text = "LOCAL VALUES  Pull dimensions and coordinates from the current stack frame", Foreground = MutedBrush, FontSize = 11 },
                Foreground = TextBrush,
                IsExpanded = false,
                Margin = new Thickness(2, 0, 2, 6),
                Content = new Border { Background = PanelBrush, BorderBrush = PanelBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(9, 8, 9, 8), Margin = new Thickness(0, 5, 0, 0), Child = localValuesRow }
            });

            panel.Children.Add(CreateInspectionSection());
            panel.Children.Add(CreateNavigatorSection());
            return panel;
        }

        // This surface intentionally uses independently wrapping command
        // groups. A single WrapPanel for every inspection control caused
        // labels such as RAW/STATS/WATCH to become separated from their
        // buttons when a tool window was narrowed.
        private UIElement CreateInspectionSection()
        {
            var content = new StackPanel();

            var positionRow = CreateRow();
            positionRow.Children.Add(CreateField("GO TO X", selectedX));
            positionRow.Children.Add(CreateField("GO TO Y", selectedY));
            positionRow.Children.Add(CreateField("VIEW W", renderWidth));
            positionRow.Children.Add(CreateField("VIEW H", renderHeight));
            positionRow.Children.Add(CreateField("KERNEL W", roiWidth));
            positionRow.Children.Add(CreateField("KERNEL H", roiHeight));
            positionRow.Children.Add(CreateAction("FOCUS", CreateButton("Center cursor", JumpToCoordinate, false)));
            content.Children.Add(CreateCommandGroup("POSITION", positionRow));

            var pixelActions = CreateRow();
            pixelActions.Children.Add(CreateCompactAction(CreateButton("Read exact cells", InspectCells, true)));
            pixelActions.Children.Add(CreateCompactAction(CreateButton("Copy kernel (Ctrl+C)", CopyKernelToClipboard, false)));
            pixelActions.Children.Add(CreateCompactAction(CreateButton("Save View", SaveViewRaw, false)));
            pixelActions.Children.Add(CreateCompactAction(CreateButton("Save Kernel", SaveKernelRaw, false)));
            pixelActions.Children.Add(CreateCompactAction(statisticsToggle));
            content.Children.Add(CreateCommandGroup("PIXEL · RAW · STATS", pixelActions));

            var statisticsRow = CreateRow();
            statisticsRow.Children.Add(CreateField("SCOPE", statisticsScope));
            statisticsRow.Children.Add(statisticsSummary);
            statisticsRow.Children.Add(statisticsChannels);
            statisticsPanel.Children.Clear();
            statisticsPanel.Children.Add(statisticsRow);
            content.Children.Add(statisticsPanel);

            var watchContent = new StackPanel();
            var watchActions = CreateRow();
            watchActions.Children.Add(CreateCompactAction(CreateButton("Next line", NextDebuggerLine, false)));
            watchActions.Children.Add(CreateCompactAction(CreateButton("Watch + Run", WatchSelectedPixelAndContinue, true)));
            watchActions.Children.Add(CreateCompactAction(CreateButton("Watch next", WatchNextPixelAndContinue, false)));
            watchActions.Children.Add(new Border { Child = keepHardwareWatchArmed, Margin = new Thickness(2, 0, 12, 0), Padding = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            watchActions.Children.Add(CreateCompactAction(CreateButton("Clear watch", ClearHardwareWatch, false)));
            watchContent.Children.Add(watchActions);
            watchContent.Children.Add(hardwareWatchInfo);
            watchContent.Children.Add(new TextBlock
            {
                Text = "Next line is a normal F10 step. Watch + Run stops at the selected sample's next write and removes the one-shot watch. Watch next moves to the next sample first. Enable Keep armed only for repeated changes at the same sample.",
                Foreground = MutedBrush,
                FontSize = 10,
                LineHeight = 15,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 0)
            });
            content.Children.Add(CreateCommandGroup("HARDWARE WATCH", watchContent));

            var viewActions = new UniformGrid { Columns = 4, Rows = 1, HorizontalAlignment = HorizontalAlignment.Left };
            viewActions.Children.Add(CreateButton("Left", PanLeft, false));
            viewActions.Children.Add(CreateButton("Right", PanRight, false));
            viewActions.Children.Add(CreateButton("Up", PanUp, false));
            viewActions.Children.Add(CreateButton("Down", PanDown, false));
            content.Children.Add(CreateCommandGroup("MOVE VIEW", viewActions));

            return CreateSection("VIEW + KERNEL", "View is the loaded display range; Kernel is the orange analysis rectangle. Frame overview moves View, and right-drag on the image changes Kernel.", content);
        }

        private static Border CreateCommandGroup(string label, UIElement contents)
        {
            var group = new Border
            {
                Background = ControlBrush,
                BorderBrush = PanelBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 7),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = AccentBrush,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 5)
            });
            stack.Children.Add(contents);
            group.Child = stack;
            return group;
        }

        private static Border CreateCompactAction(Button button)
        {
            return new Border { Child = button, Margin = new Thickness(0, 0, 8, 0) };
        }

        private UIElement CreateNavigatorSection()
        {
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var navigatorBorder = new Border
            {
                Background = CanvasBrush,
                BorderBrush = PanelBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4),
                Child = navigatorCanvas
            };
            content.Children.Add(navigatorBorder);
            var detail = new StackPanel { Margin = new Thickness(14, 2, 4, 2), VerticalAlignment = VerticalAlignment.Center };
            detail.Children.Add(new TextBlock { Text = "OPTIONAL FRAME MAP", Foreground = AccentBrush, FontSize = 11, FontWeight = FontWeights.SemiBold });
            detail.Children.Add(new TextBlock { Text = "This is a position overview, not the primary selector. Use the image click-hold or Ctrl+drag interaction for pixel-accurate ROI selection.", Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap, FontSize = 11, LineHeight = 17, Margin = new Thickness(0, 5, 0, 8) });
            detail.Children.Add(navigatorInfo);
            Grid.SetColumn(detail, 1);
            content.Children.Add(detail);
            return CreateSection("FRAME OVERVIEW", "Optional full-frame kernel-position map; image selection is the primary workflow", content);
        }

        private UIElement CreateFooter()
        {
            var footer = new Border { Background = PanelBrush, BorderBrush = PanelBorderBrush, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(14, 9, 14, 9) };
            var row = new DockPanel();
            var viewportBadge = new Border { Background = ControlBrush, BorderBrush = PanelBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 3, 8, 3), Child = viewport };
            DockPanel.SetDock(viewportBadge, Dock.Right);
            row.Children.Add(viewportBadge);
            row.Children.Add(status);
            footer.Child = row;
            return footer;
        }

        private static Border CreateSection(string title, string detail, UIElement contents)
        {
            var wrapper = new Border { Background = PanelBrush, BorderBrush = PanelBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 9), Margin = new Thickness(0, 0, 0, 7) };
            var stack = new StackPanel();
            var row = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = new TextBlock { Text = title, Foreground = AccentBrush, FontSize = 10, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(label);
            var description = new TextBlock { Text = detail, Foreground = MutedBrush, FontSize = 10, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(description, 1);
            row.Children.Add(description);
            stack.Children.Add(row);
            stack.Children.Add(contents);
            wrapper.Child = stack;
            return wrapper;
        }

        private static WrapPanel CreateRow()
        {
            return new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        }

        private static TextBox CreateTextBox(string value, double width)
        {
            return new TextBox { Text = value, Width = width, Height = 27, Background = ControlBrush, Foreground = TextBrush, BorderBrush = PanelBorderBrush, CaretBrush = TextBrush, Padding = new Thickness(7, 4, 7, 4), VerticalContentAlignment = VerticalAlignment.Center };
        }

        private static ComboBox CreateComboBox(double width)
        {
            var items = new Style(typeof(ComboBoxItem));
            items.Setters.Add(new Setter(Control.BackgroundProperty, ControlBrush));
            items.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
            items.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7, 4, 7, 4)));
            var highlighted = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
            highlighted.Setters.Add(new Setter(Control.BackgroundProperty, AccentSoftBrush));
            highlighted.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
            items.Triggers.Add(highlighted);
            var selected = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, AccentSoftBrush));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
            items.Triggers.Add(selected);

            return new ComboBox
            {
                Width = width,
                Height = 27,
                Background = ControlBrush,
                Foreground = TextBrush,
                BorderBrush = PanelBorderBrush,
                Padding = new Thickness(5, 2, 5, 2),
                VerticalContentAlignment = VerticalAlignment.Center,
                ItemContainerStyle = items,
                ItemTemplate = CreateComboBoxItemTemplate(),
                Template = CreateComboBoxTemplate(width)
            };
        }

        private static DataTemplate CreateComboBoxItemTemplate()
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
            text.SetValue(TextBlock.ForegroundProperty, TextBrush);
            text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            return new DataTemplate { VisualTree = text };
        }

        private static ControlTemplate CreateComboBoxTemplate(double width)
        {
            var template = new ControlTemplate(typeof(ComboBox));
            var root = new FrameworkElementFactory(typeof(Grid));

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));

            var toggle = new FrameworkElementFactory(typeof(ToggleButton));
            toggle.Name = "ToggleButton";
            toggle.SetValue(ToggleButton.BackgroundProperty, Brushes.Transparent);
            toggle.SetValue(ToggleButton.BorderThicknessProperty, new Thickness(0));
            toggle.SetValue(ToggleButton.FocusableProperty, false);
            toggle.SetValue(ToggleButton.ClickModeProperty, ClickMode.Press);
            toggle.SetBinding(ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding("IsDropDownOpen") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent), Mode = System.Windows.Data.BindingMode.TwoWay });

            var toggleGrid = new FrameworkElementFactory(typeof(Grid));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.MarginProperty, new Thickness(7, 0, 25, 0));
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
            content.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemTemplateProperty));
            toggleGrid.AppendChild(content);
            var arrow = new FrameworkElementFactory(typeof(TextBlock));
            arrow.SetValue(TextBlock.TextProperty, "▼");
            arrow.SetValue(TextBlock.ForegroundProperty, MutedBrush);
            arrow.SetValue(TextBlock.FontSizeProperty, 9.0);
            arrow.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            arrow.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 8, 0));
            toggleGrid.AppendChild(arrow);
            toggle.AppendChild(toggleGrid);
            border.AppendChild(toggle);
            root.AppendChild(border);

            var popup = new FrameworkElementFactory(typeof(Popup));
            popup.Name = "PART_Popup";
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetBinding(Popup.IsOpenProperty, new System.Windows.Data.Binding("IsDropDownOpen") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent), Mode = System.Windows.Data.BindingMode.TwoWay });
            var popupBorder = new FrameworkElementFactory(typeof(Border));
            popupBorder.SetValue(Border.BackgroundProperty, ControlBrush);
            popupBorder.SetValue(Border.BorderBrushProperty, AccentBrush);
            popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            popupBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            popupBorder.SetValue(FrameworkElement.MinWidthProperty, width);
            popupBorder.SetValue(FrameworkElement.MaxHeightProperty, 280.0);
            var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.Name = "PART_ScrollViewer";
            scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
            scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            var presenter = new FrameworkElementFactory(typeof(ItemsPresenter));
            scroll.AppendChild(presenter);
            popupBorder.AppendChild(scroll);
            popup.AppendChild(popupBorder);
            root.AppendChild(popup);

            template.VisualTree = root;
            return template;
        }

        private static StackPanel CreateField(string label, UIElement input)
        {
            var field = new StackPanel { Margin = new Thickness(0, 0, 9, 0) };
            field.Children.Add(new TextBlock { Text = label, Foreground = MutedBrush, FontSize = 9, FontWeight = FontWeights.SemiBold, Margin = new Thickness(1, 0, 0, 4) });
            field.Children.Add(input);
            return field;
        }

        private static StackPanel CreateAction(string label, Button button)
        {
            var action = new StackPanel { Margin = new Thickness(0, 0, 7, 0) };
            action.Children.Add(new TextBlock { Text = label, Foreground = MutedBrush, FontSize = 9, FontWeight = FontWeights.SemiBold, Margin = new Thickness(1, 0, 0, 4), Height = 11 });
            action.Children.Add(button);
            return action;
        }

        private static Button CreateButton(string text, RoutedEventHandler action, bool primary)
        {
            var button = new Button { Content = text, Height = 27, Foreground = primary ? RootBrush : TextBrush, Background = primary ? AccentBrush : ControlBrush, BorderBrush = primary ? AccentBrush : PanelBorderBrush, Padding = new Thickness(10, 3, 10, 3), FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal };
            button.Click += action;
            return button;
        }

        private static Border CreateEmptyState()
        {
            var text = new TextBlock
            {
                Text = "NO ROI LOADED\n\nChoose a pointer, enter full-frame dimensions and ROI W/H,\nthen load the requested debugger ROI.",
                Foreground = MutedBrush,
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Width = 420,
                LineHeight = 20
            };
            var state = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(230, 15, 23, 38)),
                BorderBrush = PanelBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(22, 18, 22, 18),
                Child = text,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(state, 40);
            Canvas.SetTop(state, 40);
            return state;
        }

        private sealed class NormalizationModeChoice
        {
            public NormalizationModeChoice(NormalizationMode mode, string label)
            {
                Mode = mode;
                Label = label;
            }

            public NormalizationMode Mode { get; private set; }
            public string Label { get; private set; }

            public override string ToString()
            {
                return Label;
            }
        }

        private sealed class ViewerProfile
        {
            private ViewerProfile()
            {
            }

            public string Expression { get; private set; }
            public string Width { get; private set; }
            public string Height { get; private set; }
            public string Stride { get; private set; }
            public string QFormat { get; private set; }
            public bool IsSigned { get; private set; }
            public SourceElementType SourceElementType { get; private set; }
            public PixelOrder PixelOrder { get; private set; }
            public PixelType PixelType { get; private set; }
            public VisualizeChannel VisualizeChannel { get; private set; }
            public NormalizationMode NormalizationMode { get; private set; }
            public string NormalizationMinimum { get; private set; }
            public string NormalizationMaximum { get; private set; }
            public string SelectedX { get; private set; }
            public string SelectedY { get; private set; }
            public string RenderWidth { get; private set; }
            public string RenderHeight { get; private set; }
            public string RoiWidth { get; private set; }
            public string RoiHeight { get; private set; }
            public int KernelCenterX { get; private set; }
            public int KernelCenterY { get; private set; }
            public int ViewCenterX { get; private set; }
            public int ViewCenterY { get; private set; }

            public static ViewerProfile Create(string expressionValue, string widthValue, string heightValue, string strideValue, string qFormatValue,
                bool signedValue, SourceElementType sourceElementTypeValue, PixelOrder pixelOrderValue, PixelType pixelTypeValue, VisualizeChannel visualizeChannelValue,
                NormalizationMode normalizationModeValue, string normalizationMinimumValue, string normalizationMaximumValue,
                string selectedXValue, string selectedYValue, string renderWidthValue, string renderHeightValue, string roiWidthValue, string roiHeightValue,
                int kernelCenterXValue, int kernelCenterYValue, int viewCenterXValue, int viewCenterYValue)
            {
                return new ViewerProfile
                {
                    Expression = expressionValue,
                    Width = widthValue,
                    Height = heightValue,
                    Stride = strideValue,
                    QFormat = qFormatValue,
                    IsSigned = signedValue,
                    SourceElementType = sourceElementTypeValue,
                    PixelOrder = pixelOrderValue,
                    PixelType = pixelTypeValue,
                    VisualizeChannel = visualizeChannelValue,
                    NormalizationMode = normalizationModeValue,
                    NormalizationMinimum = normalizationMinimumValue,
                    NormalizationMaximum = normalizationMaximumValue,
                    SelectedX = selectedXValue,
                    SelectedY = selectedYValue,
                    RenderWidth = renderWidthValue,
                    RenderHeight = renderHeightValue,
                    RoiWidth = roiWidthValue,
                    RoiHeight = roiHeightValue,
                    KernelCenterX = kernelCenterXValue,
                    KernelCenterY = kernelCenterYValue,
                    ViewCenterX = viewCenterXValue,
                    ViewCenterY = viewCenterYValue
                };
            }

            public override string ToString()
            {
                return Expression + "  |  " + Width + "x" + Height + "  |  " + SourceElementType + "  |  " + QFormat + "  |  " + PixelType + "/" + PixelOrder;
            }
        }

        private sealed class NamedProfileSet
        {
            public NamedProfileSet(string nameValue, ViewerProfile profileValue)
            {
                Name = nameValue;
                Profile = profileValue;
            }

            public string Name { get; private set; }
            public ViewerProfile Profile { get; private set; }

            public override string ToString()
            {
                return Name + "  |  " + Profile.Width + "x" + Profile.Height + "  |  " + Profile.Expression;
            }
        }

        private sealed class PendingMemoryRead
        {
            public PendingMemoryRead(DebugMemoryFrameReader.IFrameReadSession session, int sourceWidth, int sourceHeight,
                int selectedGlobalX, int selectedGlobalY, bool isFullPreview, bool showFullFrameContext, string sourceExpression,
                string rawExportPath, int rawExportBits, bool isNavigatorPreview)
            {
                Session = session;
                SourceWidth = sourceWidth;
                SourceHeight = sourceHeight;
                SelectedGlobalX = selectedGlobalX;
                SelectedGlobalY = selectedGlobalY;
                IsFullPreview = isFullPreview;
                ShowFullFrameContext = showFullFrameContext;
                SourceExpression = sourceExpression;
                RawExportPath = rawExportPath;
                RawExportBits = rawExportBits;
                IsNavigatorPreview = isNavigatorPreview;
            }

            public DebugMemoryFrameReader.IFrameReadSession Session { get; private set; }
            public int SourceWidth { get; private set; }
            public int SourceHeight { get; private set; }
            public int SelectedGlobalX { get; private set; }
            public int SelectedGlobalY { get; private set; }
            public bool IsFullPreview { get; private set; }
            public bool ShowFullFrameContext { get; private set; }
            public string SourceExpression { get; private set; }
            public string RawExportPath { get; private set; }
            public int RawExportBits { get; private set; }
            public bool IsNavigatorPreview { get; private set; }
        }

        private struct RoiBounds
        {
            public RoiBounds(int x, int y, int width, int height, int centerX, int centerY)
            {
                X = x;
                Y = y;
                Width = width;
                Height = height;
                CenterX = centerX;
                CenterY = centerY;
            }

            public int X;
            public int Y;
            public int Width;
            public int Height;
            public int CenterX;
            public int CenterY;
        }
    }
}
