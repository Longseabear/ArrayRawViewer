using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ArrayImageViewer.Core;
using ArrayImageViewer.Debugging;

namespace ArrayImageViewer.UI
{
    internal sealed class SensorImageViewerControl : UserControl
    {
        private static readonly Brush RootBrush = new SolidColorBrush(Color.FromRgb(13, 19, 30));
        private static readonly Brush PanelBrush = new SolidColorBrush(Color.FromRgb(24, 33, 48));
        private static readonly Brush ControlBrush = new SolidColorBrush(Color.FromRgb(15, 23, 38));
        private static readonly Brush CanvasBrush = new SolidColorBrush(Color.FromRgb(6, 10, 17));
        private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(67, 214, 177));
        private static readonly Brush AccentSoftBrush = new SolidColorBrush(Color.FromRgb(24, 65, 64));
        private static readonly Brush PanelBorderBrush = new SolidColorBrush(Color.FromRgb(58, 75, 98));
        private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(154, 171, 194));
        private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(237, 242, 249));

        private readonly TextBox expression = CreateTextBox("sensorRaw", 240);
        private readonly ComboBox availablePointers = CreateComboBox(190);
        private readonly ComboBox profilePicker = CreateComboBox(185);
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
        private readonly ComboBox pixelOrder = CreateComboBox(108);
        private readonly ComboBox pixelType = CreateComboBox(108);
        private readonly ComboBox visualizeChannel = CreateComboBox(116);
        private readonly TextBox selectedX = CreateTextBox("0", 60);
        private readonly TextBox selectedY = CreateTextBox("0", 60);
        private readonly TextBox roiWidth = CreateTextBox("5", 54);
        private readonly TextBox roiHeight = CreateTextBox("5", 54);
        private readonly TextBlock status = new TextBlock { Foreground = TextBrush, TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock viewport = new TextBlock { Foreground = MutedBrush, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly ScrollViewer scrollViewer = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = CanvasBrush, Focusable = true };
        private readonly Canvas canvas = new Canvas { Background = CanvasBrush, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, MinWidth = 520, MinHeight = 260 };
        private readonly System.Windows.Controls.Image image = new System.Windows.Controls.Image { Stretch = Stretch.Fill, SnapsToDevicePixels = true };
        private readonly Border emptyState = CreateEmptyState();
        private readonly Rectangle roiRectangle = new Rectangle { Stroke = Brushes.OrangeRed, StrokeThickness = 2, Fill = Brushes.Transparent, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        private readonly Rectangle selectedCellRectangle = new Rectangle { Stroke = new SolidColorBrush(Color.FromRgb(255, 211, 82)), StrokeThickness = 2, Fill = Brushes.Transparent, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        private readonly Canvas navigatorCanvas = new Canvas { Width = 230, Height = 156, Background = ControlBrush, ClipToBounds = true, Cursor = Cursors.Cross };
        private readonly Rectangle navigatorFrame = new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(19, 31, 47)), Stroke = PanelBorderBrush, StrokeThickness = 1, IsHitTestVisible = false };
        private readonly Rectangle navigatorRoi = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(70, 67, 214, 177)), Stroke = AccentBrush, StrokeThickness = 2, IsHitTestVisible = false };
        private readonly Line navigatorHorizontal = new Line { Stroke = new SolidColorBrush(Color.FromArgb(130, 255, 126, 69)), StrokeThickness = 1, IsHitTestVisible = false };
        private readonly Line navigatorVertical = new Line { Stroke = new SolidColorBrush(Color.FromArgb(130, 255, 126, 69)), StrokeThickness = 1, IsHitTestVisible = false };
        private readonly TextBlock navigatorInfo = new TextBlock { Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap, FontSize = 11, LineHeight = 17 };
        private readonly List<UIElement> valueOverlay = new List<UIElement>();

        private FrameBuffer frame;
        private double zoom = 0.15;
        private int currentX;
        private int currentY;
        private int fullFrameWidth;
        private int fullFrameHeight;
        private bool isRoiPanning;
        private Point roiPanStart;
        private int roiPanStartX;
        private int roiPanStartY;
        private readonly Dictionary<string, ViewerProfile> profiles = new Dictionary<string, ViewerProfile>(StringComparer.OrdinalIgnoreCase);
        private readonly List<DebugExpressionFrameReader.PointerExpression> pointerCandidates = new List<DebugExpressionFrameReader.PointerExpression>();
        private bool isApplyingProfile;
        private string activeProfileExpression;
        private bool isNavigatorSelecting;
        private int navigatorStartX;
        private int navigatorStartY;
        private Point navigatorDragStartPoint;
        private Rect navigatorFrameBounds;
        private string lastReadPath = "expression fallback";
        private int renderGeneration;

        public SensorImageViewerControl()
        {
            pixelOrder.ItemsSource = Enum.GetValues(typeof(PixelOrder));
            pixelOrder.SelectedItem = PixelOrder.GRFirst;
            pixelType.ItemsSource = Enum.GetValues(typeof(PixelType));
            pixelType.SelectedItem = PixelType.Bayer;
            visualizeChannel.ItemsSource = Enum.GetValues(typeof(VisualizeChannel));
            visualizeChannel.SelectedItem = VisualizeChannel.BayerRaw;
            availablePointers.SelectionChanged += AvailablePointerChanged;
            profilePicker.SelectionChanged += ProfilePickerChanged;
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

            canvas.Children.Add(image);
            canvas.Children.Add(emptyState);
            canvas.Children.Add(roiRectangle);
            canvas.Children.Add(selectedCellRectangle);
            canvas.MouseMove += CanvasMouseMove;
            canvas.MouseLeftButtonDown += CanvasMouseLeftButtonDown;
            canvas.MouseLeftButtonUp += CanvasMouseLeftButtonUp;
            canvas.MouseDown += CanvasMouseDown;
            canvas.MouseUp += CanvasMouseUp;
            canvas.PreviewMouseWheel += CanvasMouseWheel;
            scrollViewer.ScrollChanged += ScrollViewerChanged;
            scrollViewer.SizeChanged += ScrollViewerSizeChanged;
            scrollViewer.Content = canvas;

            navigatorCanvas.Children.Add(navigatorFrame);
            navigatorCanvas.Children.Add(navigatorHorizontal);
            navigatorCanvas.Children.Add(navigatorVertical);
            navigatorCanvas.Children.Add(navigatorRoi);
            navigatorCanvas.MouseLeftButtonDown += NavigatorMouseLeftButtonDown;
            navigatorCanvas.MouseMove += NavigatorMouseMove;
            navigatorCanvas.MouseLeftButtonUp += NavigatorMouseLeftButtonUp;

            var root = new DockPanel { Background = RootBrush, LastChildFill = true };
            var header = CreateHeader();
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
            var footer = CreateFooter();
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);
            root.Children.Add(scrollViewer);
            Content = root;
            UpdateNavigator();
            SetStatus("Pause the debuggee, refresh pointers, then load a small ROI around the requested X/Y.");
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

            var pointerRow = CreateRow();
            pointerRow.Children.Add(CreateField("POINTER", availablePointers));
            pointerRow.Children.Add(CreateAction("LOCALS", CreateButton("Refresh", RefreshPointers, false)));
            pointerRow.Children.Add(CreateField("EXPRESSION", expression));
            pointerRow.Children.Add(CreateAction("EDITOR", CreateButton("Capture", CaptureSelection, false)));
            pointerRow.Children.Add(CreateField("SESSION PROFILE", profilePicker));
            pointerRow.Children.Add(CreateAction("", CreateButton("Save settings", SaveProfileSettings, false)));
            pointerRow.Children.Add(CreateAction("", CreateButton("Load ROI", LoadExpression, true)));
            pointerRow.Children.Add(CreateAction("", CreateButton("Full preview", LoadFullPreview, false)));
            panel.Children.Add(CreateSection("SOURCE", "Type while paused for local-pointer suggestions. A session profile saves interpretation settings only (never RAW memory) for this Viewer window.", pointerRow));

            var formatRow = CreateRow();
            formatRow.Children.Add(CreateField("WIDTH", width));
            formatRow.Children.Add(CreateField("HEIGHT", height));
            formatRow.Children.Add(CreateField("ROW STRIDE", stride));
            formatRow.Children.Add(CreateField("Q FORMAT", qFormat));
            formatRow.Children.Add(CreateField("VALUE TYPE", signed));
            formatRow.Children.Add(CreateField("PIXEL ORDER", pixelOrder));
            formatRow.Children.Add(CreateField("PIXEL TYPE", pixelType));
            formatRow.Children.Add(CreateField("VISUALIZE", visualizeChannel));
            formatRow.Children.Add(CreateAction("LOCAL STACK", CreateButton("Auto-fill", RefreshScalarValues, false)));
            panel.Children.Add(CreateSection("FRAME", "Full-frame dimensions; Composite is a lightweight nearest-site Bayer preview, while pixel inspection always shows the original sample", formatRow));

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

            var inspectRow = CreateRow();
            inspectRow.Children.Add(CreateField("GO TO X", selectedX));
            inspectRow.Children.Add(CreateField("GO TO Y", selectedY));
            inspectRow.Children.Add(CreateField("ROI W", roiWidth));
            inspectRow.Children.Add(CreateField("ROI H", roiHeight));
            inspectRow.Children.Add(CreateAction("", CreateButton("Center view", JumpToCoordinate, false)));
            inspectRow.Children.Add(CreateAction("", CreateButton("Load ROI cells", InspectCells, true)));
            inspectRow.Children.Add(CreateAction("PAN", CreateButton("Left", PanLeft, false)));
            inspectRow.Children.Add(CreateAction("", CreateButton("Right", PanRight, false)));
            inspectRow.Children.Add(CreateAction("", CreateButton("Up", PanUp, false)));
            inspectRow.Children.Add(CreateAction("", CreateButton("Down", PanDown, false)));
            inspectRow.Children.Add(new TextBlock { Text = "Middle-drag or Shift+drag pans the ROI; release reloads it. Wheel zooms; click selects.", Foreground = MutedBrush, Margin = new Thickness(12, 23, 0, 0), FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(CreateSection("ROI", "Move the inspection window without manually typing new coordinates", inspectRow));
            panel.Children.Add(CreateNavigatorSection());
            return panel;
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
            detail.Children.Add(new TextBlock { Text = "DRAG TO DEFINE ROI", Foreground = AccentBrush, FontSize = 11, FontWeight = FontWeights.SemiBold });
            detail.Children.Add(new TextBlock { Text = "The navigator represents the entire frame, not sampled pixel values. Drag from one ROI corner to the other; on release, only that rectangle is read from the paused debuggee.", Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap, FontSize = 11, LineHeight = 17, Margin = new Thickness(0, 5, 0, 8) });
            detail.Children.Add(navigatorInfo);
            Grid.SetColumn(detail, 1);
            content.Children.Add(detail);
            return CreateSection("FRAME NAVIGATOR", "Mouse-select an ROI anywhere in the full frame without loading the whole buffer", content);
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
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
            var label = new TextBlock { Text = title, Foreground = AccentBrush, FontSize = 10, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
            row.Children.Add(new TextBlock { Text = detail, Foreground = MutedBrush, FontSize = 10, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
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

        private void ConfigureExpressionSuggestions()
        {
            var popupBorder = new Border
            {
                Background = ControlBrush,
                BorderBrush = AccentBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(2),
                Child = expressionSuggestionList
            };
            expressionSuggestions.Child = popupBorder;
            expressionSuggestions.PlacementTarget = expression;
            expressionSuggestions.Closed += delegate { expressionSuggestionList.SelectedItem = null; };
        }

        private void CaptureSelection(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveCurrentProfile();
                expression.Text = DebugExpressionFrameReader.GetActiveEditorSelection();
                EnsureProfile(expression.Text);
                activeProfileExpression = expression.Text.Trim();
                RebuildProfilePicker(expression.Text);
                SetStatus("Captured pointer expression: " + expression.Text);
            }
            catch (Exception exception)
            {
                SetStatus("Cannot capture selection: " + exception.Message);
            }
        }

        private void RefreshPointers(object sender, RoutedEventArgs e)
        {
            try
            {
                var pointers = DebugExpressionFrameReader.GetCurrentFramePointers();
                pointerCandidates.Clear();
                pointerCandidates.AddRange(pointers);
                availablePointers.ItemsSource = pointers;
                if (pointers.Count == 0)
                {
                    SetStatus("No pointer locals found. Pause in the function that owns A or B, then refresh.");
                    return;
                }

                UpdateExpressionSuggestions();
                SetStatus("Found " + pointers.Count.ToString(CultureInfo.InvariantCulture) + " pointer variable(s). Type to filter or choose A/B from the list.");
            }
            catch (Exception exception)
            {
                SetStatus("Cannot list pointer locals: " + exception.Message);
            }
        }

        private void AvailablePointerChanged(object sender, SelectionChangedEventArgs e)
        {
            var pointer = availablePointers.SelectedItem as DebugExpressionFrameReader.PointerExpression;
            if (pointer == null)
            {
                return;
            }

            ActivatePointer(pointer);
        }

        private void ExpressionGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (pointerCandidates.Count == 0)
            {
                RefreshPointers(null, null);
            }

            UpdateExpressionSuggestions();
        }

        private void ExpressionTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!isApplyingProfile)
            {
                UpdateExpressionSuggestions();
            }
        }

        private void ExpressionPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!expressionSuggestions.IsOpen)
            {
                return;
            }

            if (e.Key == Key.Down)
            {
                expressionSuggestionList.SelectedIndex = Math.Min(expressionSuggestionList.Items.Count - 1,
                    Math.Max(0, expressionSuggestionList.SelectedIndex + 1));
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                expressionSuggestions.IsOpen = false;
                e.Handled = true;
            }
        }

        private void UpdateExpressionSuggestions()
        {
            var typed = expression.Text == null ? String.Empty : expression.Text.Trim();
            var matches = new List<DebugExpressionFrameReader.PointerExpression>();
            for (var index = 0; index < pointerCandidates.Count; index++)
            {
                var candidate = pointerCandidates[index];
                if (typed.Length == 0 || candidate.Name.IndexOf(typed, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matches.Add(candidate);
                }
            }

            expressionSuggestionList.ItemsSource = matches;
            expressionSuggestions.IsOpen = expression.IsKeyboardFocusWithin && matches.Count > 0;
        }

        private void ExpressionSuggestionSelected(object sender, SelectionChangedEventArgs e)
        {
            var pointer = expressionSuggestionList.SelectedItem as DebugExpressionFrameReader.PointerExpression;
            if (pointer == null)
            {
                return;
            }

            ActivatePointer(pointer);
            expressionSuggestions.IsOpen = false;
        }

        private void ActivatePointer(DebugExpressionFrameReader.PointerExpression pointer)
        {
            SaveCurrentProfile();
            expressionSuggestions.IsOpen = false;
            ViewerProfile profile;
            if (profiles.TryGetValue(pointer.Name, out profile))
            {
                ApplyProfile(profile);
            }
            else
            {
                isApplyingProfile = true;
                expression.Text = pointer.Name;
                signed.IsChecked = pointer.IsSigned;
                isApplyingProfile = false;
                EnsureProfile(pointer.Name);
            }

            activeProfileExpression = pointer.Name;
            RebuildProfilePicker(pointer.Name);
            SetStatus("Selected " + pointer.Name + " as " + pointer.Type + ". Its profile is retained for this viewer window.");
        }

        private void ProfilePickerChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isApplyingProfile)
            {
                return;
            }

            var profile = profilePicker.SelectedItem as ViewerProfile;
            if (profile == null)
            {
                return;
            }

            SaveCurrentProfile();
            ApplyProfile(profile);
            SetStatus("Restored profile for " + profile.Expression + ". Select Load ROI to read its current debugger values.");
        }

        private void SaveProfileSettings(object sender, RoutedEventArgs e)
        {
            var sourceExpression = expression.Text == null ? String.Empty : expression.Text.Trim();
            if (sourceExpression.Length == 0)
            {
                SetStatus("Choose a pointer or capture an expression before saving a session profile.");
                return;
            }

            EnsureProfile(sourceExpression);
            activeProfileExpression = sourceExpression;
            SaveCurrentProfile();
            RebuildProfilePicker(sourceExpression);
            SetStatus("Saved interpretation settings for " + sourceExpression + " in this Viewer window. RAW samples were not retained.");
        }

        private void ConfigureProfileAutoSave()
        {
            width.TextChanged += ProfileInputChanged;
            height.TextChanged += ProfileInputChanged;
            stride.TextChanged += ProfileInputChanged;
            qFormat.TextChanged += ProfileInputChanged;
            selectedX.TextChanged += ProfileInputChanged;
            selectedY.TextChanged += ProfileInputChanged;
            roiWidth.TextChanged += ProfileInputChanged;
            roiHeight.TextChanged += ProfileInputChanged;
            signed.Checked += ProfileOptionChanged;
            signed.Unchecked += ProfileOptionChanged;
            pixelOrder.SelectionChanged += ProfileOptionChanged;
            pixelType.SelectionChanged += ProfileOptionChanged;
            visualizeChannel.SelectionChanged += ProfileOptionChanged;
        }

        private void ProfileInputChanged(object sender, TextChangedEventArgs e)
        {
            SaveCurrentProfile();
        }

        private void ProfileOptionChanged(object sender, RoutedEventArgs e)
        {
            SaveCurrentProfile();
        }

        private void ProfileOptionChanged(object sender, SelectionChangedEventArgs e)
        {
            SaveCurrentProfile();
        }

        private void EnsureProfile(string sourceExpression)
        {
            if (String.IsNullOrWhiteSpace(sourceExpression) || profiles.ContainsKey(sourceExpression.Trim()))
            {
                return;
            }

            profiles.Add(sourceExpression.Trim(), ViewerProfile.Create(sourceExpression.Trim(), width.Text, height.Text, stride.Text, qFormat.Text,
                signed.IsChecked == true, (PixelOrder)pixelOrder.SelectedItem, (PixelType)pixelType.SelectedItem,
                (VisualizeChannel)visualizeChannel.SelectedItem, selectedX.Text, selectedY.Text, roiWidth.Text, roiHeight.Text));
        }

        private void SaveCurrentProfile()
        {
            if (isApplyingProfile || String.IsNullOrWhiteSpace(expression.Text))
            {
                return;
            }

            var sourceExpression = expression.Text.Trim();
            if (!String.Equals(activeProfileExpression, sourceExpression, StringComparison.OrdinalIgnoreCase) || !profiles.ContainsKey(sourceExpression))
            {
                return;
            }

            profiles[sourceExpression] = ViewerProfile.Create(sourceExpression, width.Text, height.Text, stride.Text, qFormat.Text,
                signed.IsChecked == true, (PixelOrder)pixelOrder.SelectedItem, (PixelType)pixelType.SelectedItem,
                (VisualizeChannel)visualizeChannel.SelectedItem, selectedX.Text, selectedY.Text, roiWidth.Text, roiHeight.Text);
        }

        private void RebuildProfilePicker(string selectedExpression)
        {
            isApplyingProfile = true;
            var items = new List<ViewerProfile>();
            foreach (var profile in profiles.Values)
            {
                items.Add(profile);
            }

            profilePicker.ItemsSource = items;
            profilePicker.SelectedItem = null;
            for (var index = 0; index < items.Count; index++)
            {
                if (String.Equals(items[index].Expression, selectedExpression, StringComparison.OrdinalIgnoreCase))
                {
                    profilePicker.SelectedItem = items[index];
                    break;
                }
            }

            isApplyingProfile = false;
        }

        private void ApplyProfile(ViewerProfile profile)
        {
            isApplyingProfile = true;
            expression.Text = profile.Expression;
            width.Text = profile.Width;
            height.Text = profile.Height;
            stride.Text = profile.Stride;
            qFormat.Text = profile.QFormat;
            signed.IsChecked = profile.IsSigned;
            pixelOrder.SelectedItem = profile.PixelOrder;
            pixelType.SelectedItem = profile.PixelType;
            visualizeChannel.SelectedItem = profile.VisualizeChannel;
            selectedX.Text = profile.SelectedX;
            selectedY.Text = profile.SelectedY;
            roiWidth.Text = profile.RoiWidth;
            roiHeight.Text = profile.RoiHeight;
            isApplyingProfile = false;
            activeProfileExpression = profile.Expression;
            RebuildProfilePicker(profile.Expression);
        }

        private void RefreshScalarValues(object sender, RoutedEventArgs e)
        {
            try
            {
                var values = DebugExpressionFrameReader.GetCurrentFrameScalars();
                widthValueSource.ItemsSource = values;
                heightValueSource.ItemsSource = values;
                strideValueSource.ItemsSource = values;
                xValueSource.ItemsSource = values;
                yValueSource.ItemsSource = values;
                AutoFillScalar(values, widthValueSource, width, "width", "imagewidth", "framewidth", "w");
                AutoFillScalar(values, heightValueSource, height, "height", "imageheight", "frameheight", "h");
                AutoFillScalar(values, strideValueSource, stride, "stride", "pitch", "rowstride", "imagepitch");
                AutoFillScalar(values, xValueSource, selectedX, "centerx", "roi_x", "x");
                AutoFillScalar(values, yValueSource, selectedY, "centery", "roi_y", "y");
                SetStatus(values.Count == 0
                    ? "No integer locals found. Pause in the function that owns width, height, X, and Y."
                    : "Loaded " + values.Count.ToString(CultureInfo.InvariantCulture) + " numeric locals and filled matching W/H/stride/X/Y names.");
            }
            catch (Exception exception)
            {
                SetStatus("Cannot list numeric locals: " + exception.Message);
            }
        }

        private void WidthValueSelected(object sender, SelectionChangedEventArgs e)
        {
            CopyScalarTo(widthValueSource, width, "width");
        }

        private void HeightValueSelected(object sender, SelectionChangedEventArgs e)
        {
            CopyScalarTo(heightValueSource, height, "height");
        }

        private void StrideValueSelected(object sender, SelectionChangedEventArgs e)
        {
            CopyScalarTo(strideValueSource, stride, "stride");
        }

        private void XValueSelected(object sender, SelectionChangedEventArgs e)
        {
            CopyScalarTo(xValueSource, selectedX, "X");
        }

        private void YValueSelected(object sender, SelectionChangedEventArgs e)
        {
            CopyScalarTo(yValueSource, selectedY, "Y");
        }

        private void CopyScalarTo(ComboBox source, TextBox target, string targetName)
        {
            var scalar = source.SelectedItem as DebugExpressionFrameReader.ScalarExpression;
            if (scalar == null)
            {
                return;
            }

            try
            {
                target.Text = DebugExpressionFrameReader.EvaluateInt32(scalar.Name).ToString(CultureInfo.InvariantCulture);
                SetStatus("Copied " + scalar.Name + " into " + targetName + ".");
            }
            catch (Exception exception)
            {
                SetStatus("Cannot read " + scalar.Name + ": " + exception.Message);
            }
        }

        private static void AutoFillScalar(IList<DebugExpressionFrameReader.ScalarExpression> values, ComboBox source, TextBox target, params string[] preferredNames)
        {
            for (var preferredIndex = 0; preferredIndex < preferredNames.Length; preferredIndex++)
            {
                for (var valueIndex = 0; valueIndex < values.Count; valueIndex++)
                {
                    if (String.Equals(values[valueIndex].Name, preferredNames[preferredIndex], StringComparison.OrdinalIgnoreCase))
                    {
                        source.SelectedItem = values[valueIndex];
                        target.Text = values[valueIndex].Name;
                        return;
                    }
                }
            }
        }

        private void LoadExpression(object sender, RoutedEventArgs e)
        {
            try
            {
                var configuration = ReadConfiguration();
                var roi = GetRoiBounds(configuration);
                SetStatus("Reading ROI " + roi.Width + "x" + roi.Height + " from the debugger.");
                var source = DebugExpressionFrameReader.ReadRoi(expression.Text, configuration, roi.X, roi.Y, roi.Width, roi.Height);
                ApplyLoadedFrame(source, configuration.Width, configuration.Height, roi.CenterX, roi.CenterY, false, expression.Text.Trim());
            }
            catch (Exception exception)
            {
                SetStatus("Cannot read pointer: " + exception.Message);
            }
        }

        private void LoadFullPreview(object sender, RoutedEventArgs e)
        {
            try
            {
                var configuration = ReadConfiguration();
                SetStatus("Reading full " + configuration.Width + "x" + configuration.Height + " preview from the debugger.");
                var source = DebugExpressionFrameReader.ReadRoi(expression.Text, configuration, 0, 0, configuration.Width, configuration.Height);
                if (!DebugExpressionFrameReader.LastRoiReadUsedMemory)
                {
                    throw new InvalidOperationException("Full preview requires the native debugger-memory reader. Use Load ROI on this debug engine.");
                }

                var centerX = Math.Max(0, Math.Min(configuration.Width - 1, ResolveInteger(selectedX, "ROI center X")));
                var centerY = Math.Max(0, Math.Min(configuration.Height - 1, ResolveInteger(selectedY, "ROI center Y")));
                ApplyLoadedFrame(source, configuration.Width, configuration.Height, centerX, centerY, true, expression.Text.Trim());
            }
            catch (Exception exception)
            {
                SetStatus("Cannot read full preview: " + exception.Message);
            }
        }

        private void ApplyLoadedFrame(FrameBuffer source, int sourceWidth, int sourceHeight, int selectedGlobalX, int selectedGlobalY, bool isFullPreview, string sourceExpression)
        {
            var readPath = DebugExpressionFrameReader.LastRoiReadUsedMemory ? "debugger memory" : "expression fallback";
            var sampleCount = (long)source.Configuration.Width * source.Configuration.Height;
            var generation = ++renderGeneration;
            if (sampleCount <= 262144)
            {
                ApplyRenderedFrame(source, FrameRenderer.Render(source), sourceWidth, sourceHeight, selectedGlobalX, selectedGlobalY, isFullPreview, sourceExpression, readPath);
                return;
            }

            SetStatus("Rendering " + source.Configuration.Width.ToString(CultureInfo.InvariantCulture) + "x" + source.Configuration.Height.ToString(CultureInfo.InvariantCulture) + " preview in the background. The viewer remains usable.");
            var renderThread = new Thread(delegate()
            {
                try
                {
                    var bitmap = FrameRenderer.Render(source);
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (generation == renderGeneration)
                        {
                            ApplyRenderedFrame(source, bitmap, sourceWidth, sourceHeight, selectedGlobalX, selectedGlobalY, isFullPreview, sourceExpression, readPath);
                        }
                    }));
                }
                catch (Exception exception)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (generation == renderGeneration)
                        {
                            SetStatus("Cannot render preview: " + exception.Message);
                        }
                    }));
                }
            });
            renderThread.IsBackground = true;
            renderThread.SetApartmentState(ApartmentState.STA);
            renderThread.Start();
        }

        private void ApplyRenderedFrame(FrameBuffer source, ImageSource bitmap, int sourceWidth, int sourceHeight, int selectedGlobalX, int selectedGlobalY,
            bool isFullPreview, string sourceExpression, string readPath)
        {
            lastReadPath = readPath;
            ApplyFrame(source, bitmap, sourceWidth, sourceHeight, selectedGlobalX, selectedGlobalY);
            ApplyZoom(isFullPreview ? GetFullPreviewZoom(source.Configuration.Width, source.Configuration.Height) : Math.Max(18.0, zoom));
            EnsureProfile(sourceExpression);
            activeProfileExpression = sourceExpression;
            SaveCurrentProfile();
            RebuildProfilePicker(sourceExpression);
        }

        private double GetFullPreviewZoom(int imageWidth, int imageHeight)
        {
            var availableWidth = Math.Max(320.0, scrollViewer.ActualWidth - 24);
            var availableHeight = Math.Max(220.0, scrollViewer.ActualHeight - 24);
            var fit = Math.Min(availableWidth / imageWidth, availableHeight / imageHeight);
            return Math.Max(0.05, Math.Min(1.0, fit));
        }

        private FrameConfiguration ReadConfiguration()
        {
            var parsedWidth = ResolveInteger(width, "width");
            var parsedHeight = ResolveInteger(height, "height");
            var parsedStride = ResolveInteger(stride, "stride");
            int parsedIntegerBits;
            int parsedFractionalBits;
            if (!QFormat.TryParse(qFormat.Text, out parsedIntegerBits, out parsedFractionalBits))
            {
                throw new ArgumentException("Use Q format such as 8.8b.");
            }

            return new FrameConfiguration(parsedWidth, parsedHeight, parsedStride, parsedIntegerBits, parsedFractionalBits, signed.IsChecked == true,
                (PixelOrder)pixelOrder.SelectedItem, (PixelType)pixelType.SelectedItem, (VisualizeChannel)visualizeChannel.SelectedItem);
        }

        private static int ResolveInteger(TextBox field, string fieldName)
        {
            int value;
            if (Int32.TryParse(field.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            if (String.IsNullOrWhiteSpace(field.Text))
            {
                throw new ArgumentException("Enter a " + fieldName + " integer or a current-stack integer variable.");
            }

            return DebugExpressionFrameReader.EvaluateInt32(field.Text.Trim());
        }

        private RoiBounds GetRoiBounds(FrameConfiguration configuration)
        {
            var requestedX = ResolveInteger(selectedX, "ROI center X");
            var requestedY = ResolveInteger(selectedY, "ROI center Y");
            var requestedWidth = ResolveInteger(roiWidth, "ROI width");
            var requestedHeight = ResolveInteger(roiHeight, "ROI height");
            if (requestedWidth <= 0 || requestedHeight <= 0)
            {
                throw new ArgumentException("ROI W and H must be positive.");
            }

            var actualWidth = Math.Min(requestedWidth, configuration.Width);
            var actualHeight = Math.Min(requestedHeight, configuration.Height);
            var x = Math.Max(0, Math.Min(configuration.Width - actualWidth, requestedX - actualWidth / 2));
            var y = Math.Max(0, Math.Min(configuration.Height - actualHeight, requestedY - actualHeight / 2));
            var centerX = x + actualWidth / 2;
            var centerY = y + actualHeight / 2;
            selectedX.Text = centerX.ToString(CultureInfo.InvariantCulture);
            selectedY.Text = centerY.ToString(CultureInfo.InvariantCulture);
            roiWidth.Text = actualWidth.ToString(CultureInfo.InvariantCulture);
            roiHeight.Text = actualHeight.ToString(CultureInfo.InvariantCulture);
            UpdateNavigator();
            return new RoiBounds(x, y, actualWidth, actualHeight, centerX, centerY);
        }

        private void NavigatorMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!EnsureNavigatorFrame())
            {
                return;
            }

            int x;
            int y;
            if (!TryGetNavigatorCoordinate(e.GetPosition(navigatorCanvas), out x, out y))
            {
                return;
            }

            isNavigatorSelecting = true;
            navigatorStartX = x;
            navigatorStartY = y;
            navigatorDragStartPoint = e.GetPosition(navigatorCanvas);
            navigatorCanvas.CaptureMouse();
            SetStatus("Click to move the current ROI, or drag to set a new ROI size.");
            e.Handled = true;
        }

        private void NavigatorMouseMove(object sender, MouseEventArgs e)
        {
            if (!isNavigatorSelecting)
            {
                return;
            }

            int x;
            int y;
            var point = e.GetPosition(navigatorCanvas);
            if (HasNavigatorDragDistance(point) && TryGetNavigatorCoordinate(point, out x, out y))
            {
                PreviewNavigatorRoi(x, y);
            }
        }

        private void NavigatorMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!isNavigatorSelecting)
            {
                return;
            }

            int x;
            int y;
            var point = e.GetPosition(navigatorCanvas);
            if (TryGetNavigatorCoordinate(point, out x, out y))
            {
                if (HasNavigatorDragDistance(point))
                {
                    PreviewNavigatorRoi(x, y);
                }
                else
                {
                    selectedX.Text = x.ToString(CultureInfo.InvariantCulture);
                    selectedY.Text = y.ToString(CultureInfo.InvariantCulture);
                    UpdateNavigator();
                    SetStatus("ROI moved to (" + x + ", " + y + "). Reading its current size.");
                }
            }

            isNavigatorSelecting = false;
            navigatorCanvas.ReleaseMouseCapture();
            LoadExpression(null, null);
            e.Handled = true;
        }

        private bool HasNavigatorDragDistance(Point point)
        {
            return Math.Abs(point.X - navigatorDragStartPoint.X) >= 3 || Math.Abs(point.Y - navigatorDragStartPoint.Y) >= 3;
        }

        private bool EnsureNavigatorFrame()
        {
            if (fullFrameWidth > 0 && fullFrameHeight > 0)
            {
                return true;
            }

            try
            {
                var configuration = ReadConfiguration();
                fullFrameWidth = configuration.Width;
                fullFrameHeight = configuration.Height;
                UpdateNavigator();
                return true;
            }
            catch (Exception exception)
            {
                SetStatus("Enter valid W/H before selecting an ROI: " + exception.Message);
                return false;
            }
        }

        private bool TryGetNavigatorFrame(out int frameWidth, out int frameHeight)
        {
            frameWidth = fullFrameWidth;
            frameHeight = fullFrameHeight;
            if (frameWidth > 0 && frameHeight > 0)
            {
                return true;
            }

            return Int32.TryParse(width.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out frameWidth) && frameWidth > 0 &&
                   Int32.TryParse(height.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out frameHeight) && frameHeight > 0;
        }

        private bool TryGetNavigatorCoordinate(Point point, out int x, out int y)
        {
            x = 0;
            y = 0;
            int frameWidth;
            int frameHeight;
            if (!TryGetNavigatorFrame(out frameWidth, out frameHeight) || navigatorFrameBounds.Width <= 0 || navigatorFrameBounds.Height <= 0)
            {
                return false;
            }

            var normalizedX = (point.X - navigatorFrameBounds.X) / navigatorFrameBounds.Width;
            var normalizedY = (point.Y - navigatorFrameBounds.Y) / navigatorFrameBounds.Height;
            x = Math.Max(0, Math.Min(frameWidth - 1, (int)Math.Floor(normalizedX * frameWidth)));
            y = Math.Max(0, Math.Min(frameHeight - 1, (int)Math.Floor(normalizedY * frameHeight)));
            return true;
        }

        private void PreviewNavigatorRoi(int endX, int endY)
        {
            var left = Math.Min(navigatorStartX, endX);
            var top = Math.Min(navigatorStartY, endY);
            var right = Math.Max(navigatorStartX, endX);
            var bottom = Math.Max(navigatorStartY, endY);
            selectedX.Text = (left + (right - left) / 2).ToString(CultureInfo.InvariantCulture);
            selectedY.Text = (top + (bottom - top) / 2).ToString(CultureInfo.InvariantCulture);
            roiWidth.Text = (right - left + 1).ToString(CultureInfo.InvariantCulture);
            roiHeight.Text = (bottom - top + 1).ToString(CultureInfo.InvariantCulture);
            UpdateNavigator();
            SetStatus("ROI preview: x=" + left + ".." + right + ", y=" + top + ".." + bottom + ". Release to read it.");
        }

        private void UpdateNavigator()
        {
            int frameWidth;
            int frameHeight;
            if (!TryGetNavigatorFrame(out frameWidth, out frameHeight))
            {
                navigatorFrame.Visibility = Visibility.Collapsed;
                navigatorRoi.Visibility = Visibility.Collapsed;
                navigatorHorizontal.Visibility = Visibility.Collapsed;
                navigatorVertical.Visibility = Visibility.Collapsed;
                navigatorInfo.Text = "Enter W/H (or load a profile) to enable the frame navigator.";
                return;
            }

            const double margin = 8;
            var availableWidth = Math.Max(1, navigatorCanvas.Width - 2 * margin);
            var availableHeight = Math.Max(1, navigatorCanvas.Height - 2 * margin);
            var scale = Math.Min(availableWidth / frameWidth, availableHeight / frameHeight);
            var displayWidth = Math.Max(1, frameWidth * scale);
            var displayHeight = Math.Max(1, frameHeight * scale);
            navigatorFrameBounds = new Rect((navigatorCanvas.Width - displayWidth) / 2, (navigatorCanvas.Height - displayHeight) / 2, displayWidth, displayHeight);
            navigatorFrame.Visibility = Visibility.Visible;
            navigatorRoi.Visibility = Visibility.Visible;
            navigatorHorizontal.Visibility = Visibility.Visible;
            navigatorVertical.Visibility = Visibility.Visible;
            navigatorFrame.Width = displayWidth;
            navigatorFrame.Height = displayHeight;
            Canvas.SetLeft(navigatorFrame, navigatorFrameBounds.X);
            Canvas.SetTop(navigatorFrame, navigatorFrameBounds.Y);

            var centerX = ParseNavigatorValue(selectedX.Text, frameWidth / 2);
            var centerY = ParseNavigatorValue(selectedY.Text, frameHeight / 2);
            var requestedWidth = Math.Max(1, ParseNavigatorValue(roiWidth.Text, 1));
            var requestedHeight = Math.Max(1, ParseNavigatorValue(roiHeight.Text, 1));
            var actualWidth = Math.Min(frameWidth, requestedWidth);
            var actualHeight = Math.Min(frameHeight, requestedHeight);
            var roiX = Math.Max(0, Math.Min(frameWidth - actualWidth, centerX - actualWidth / 2));
            var roiY = Math.Max(0, Math.Min(frameHeight - actualHeight, centerY - actualHeight / 2));
            navigatorRoi.Width = Math.Max(2, actualWidth * scale);
            navigatorRoi.Height = Math.Max(2, actualHeight * scale);
            Canvas.SetLeft(navigatorRoi, navigatorFrameBounds.X + roiX * scale);
            Canvas.SetTop(navigatorRoi, navigatorFrameBounds.Y + roiY * scale);

            var markerX = navigatorFrameBounds.X + (roiX + actualWidth / 2.0) * scale;
            var markerY = navigatorFrameBounds.Y + (roiY + actualHeight / 2.0) * scale;
            navigatorHorizontal.X1 = navigatorFrameBounds.X;
            navigatorHorizontal.X2 = navigatorFrameBounds.Right;
            navigatorHorizontal.Y1 = markerY;
            navigatorHorizontal.Y2 = markerY;
            navigatorVertical.X1 = markerX;
            navigatorVertical.X2 = markerX;
            navigatorVertical.Y1 = navigatorFrameBounds.Y;
            navigatorVertical.Y2 = navigatorFrameBounds.Bottom;
            navigatorInfo.Text = String.Format(CultureInfo.InvariantCulture,
                "Frame  {0} x {1}\nROI  x={2}..{3}, y={4}..{5}  ({6} x {7})",
                frameWidth, frameHeight, roiX, roiX + actualWidth - 1, roiY, roiY + actualHeight - 1, actualWidth, actualHeight);
            if (frame != null)
            {
                UpdateRoiRectangle();
            }
        }

        private static int ParseNavigatorValue(string value, int fallback)
        {
            int parsed;
            return Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private void ApplyFrame(FrameBuffer source, ImageSource bitmap, int sourceWidth, int sourceHeight, int selectedGlobalX, int selectedGlobalY)
        {
            frame = source;
            fullFrameWidth = sourceWidth;
            fullFrameHeight = sourceHeight;
            image.Source = bitmap;
            emptyState.Visibility = Visibility.Collapsed;
            currentX = selectedGlobalX;
            currentY = selectedGlobalY;
            UpdateNavigator();
            ApplyZoom(zoom);
            UpdateSelection(currentX, currentY, false);
        }

        private void CanvasMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (frame == null)
            {
                return;
            }

            ApplyZoom(Math.Max(0.05, Math.Min(64.0, zoom * (e.Delta > 0 ? 1.35 : 1.0 / 1.35))));
            e.Handled = true;
        }

        private void CanvasMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (frame == null)
            {
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                BeginRoiPan(e.GetPosition(canvas));
                e.Handled = true;
                return;
            }

            var point = e.GetPosition(canvas);
            UpdateSelection(frame.Configuration.OriginX + (int)(point.X / zoom), frame.Configuration.OriginY + (int)(point.Y / zoom), false);
        }

        private void CanvasMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isRoiPanning)
            {
                FinishRoiPan(e.GetPosition(canvas));
                e.Handled = true;
            }
        }

        private void CanvasMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                BeginRoiPan(e.GetPosition(canvas));
                e.Handled = true;
            }
        }

        private void CanvasMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (isRoiPanning && e.ChangedButton == MouseButton.Middle)
            {
                FinishRoiPan(e.GetPosition(canvas));
                e.Handled = true;
            }
        }

        private void CanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (frame == null)
            {
                return;
            }

            if (isRoiPanning)
            {
                PreviewRoiPan(e.GetPosition(canvas));
                return;
            }

            var point = e.GetPosition(canvas);
            var x = (int)(point.X / zoom);
            var y = (int)(point.Y / zoom);
            if (x >= 0 && y >= 0 && x < frame.Configuration.Width && y < frame.Configuration.Height)
            {
                SetStatus(Describe(frame.Configuration.OriginX + x, frame.Configuration.OriginY + y));
            }
        }

        private void BeginRoiPan(Point point)
        {
            if (frame == null)
            {
                return;
            }

            isRoiPanning = true;
            roiPanStart = point;
            roiPanStartX = frame.Configuration.OriginX + frame.Configuration.Width / 2;
            roiPanStartY = frame.Configuration.OriginY + frame.Configuration.Height / 2;
            canvas.CaptureMouse();
            canvas.Cursor = Cursors.SizeAll;
            SetStatus("Drag the ROI, then release to read the new debugger cells.");
        }

        private void PreviewRoiPan(Point point)
        {
            var targetX = roiPanStartX + (int)Math.Round((point.X - roiPanStart.X) / zoom, MidpointRounding.AwayFromZero);
            var targetY = roiPanStartY + (int)Math.Round((point.Y - roiPanStart.Y) / zoom, MidpointRounding.AwayFromZero);
            targetX = Math.Max(0, Math.Min(fullFrameWidth - 1, targetX));
            targetY = Math.Max(0, Math.Min(fullFrameHeight - 1, targetY));
            selectedX.Text = targetX.ToString(CultureInfo.InvariantCulture);
            selectedY.Text = targetY.ToString(CultureInfo.InvariantCulture);
            UpdateNavigator();
            SetStatus("New ROI center: (" + targetX + ", " + targetY + "). Release to read it.");
        }

        private void FinishRoiPan(Point point)
        {
            PreviewRoiPan(point);
            isRoiPanning = false;
            canvas.ReleaseMouseCapture();
            canvas.Cursor = null;
            LoadExpression(null, null);
        }

        private void ScrollViewerChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdateViewportAndOverlay();
        }

        private void ScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateViewportAndOverlay();
        }

        private void JumpToCoordinate(object sender, RoutedEventArgs e)
        {
            try
            {
                var configuration = ReadConfiguration();
                var roi = GetRoiBounds(configuration);
                currentX = roi.CenterX;
                currentY = roi.CenterY;
                if (frame != null && IsLoadedCoordinate(currentX, currentY))
                {
                    UpdateSelection(currentX, currentY, true);
                }
                else
                {
                    SetStatus("ROI " + roi.Width + "x" + roi.Height + " is centered at (" + currentX + ", " + currentY + "). Select Load ROI to read its debugger values.");
                }
            }
            catch (Exception exception)
            {
                SetStatus("Cannot resolve ROI: " + exception.Message);
            }
        }

        private void InspectCells(object sender, RoutedEventArgs e)
        {
            LoadExpression(sender, e);
            if (frame != null)
            {
                ApplyZoom(48.0);
                Dispatcher.BeginInvoke(new Action(CenterOnSelection));
            }
        }

        private void PanLeft(object sender, RoutedEventArgs e)
        {
            PanRoi(-1, 0);
        }

        private void PanRight(object sender, RoutedEventArgs e)
        {
            PanRoi(1, 0);
        }

        private void PanUp(object sender, RoutedEventArgs e)
        {
            PanRoi(0, -1);
        }

        private void PanDown(object sender, RoutedEventArgs e)
        {
            PanRoi(0, 1);
        }

        private void PanRoi(int horizontalDirection, int verticalDirection)
        {
            try
            {
                var configuration = ReadConfiguration();
                var roi = GetRoiBounds(configuration);
                var xStep = Math.Max(1, roi.Width / 2);
                var yStep = Math.Max(1, roi.Height / 2);
                var targetX = Math.Max(0, Math.Min(configuration.Width - 1, roi.CenterX + horizontalDirection * xStep));
                var targetY = Math.Max(0, Math.Min(configuration.Height - 1, roi.CenterY + verticalDirection * yStep));
                selectedX.Text = targetX.ToString(CultureInfo.InvariantCulture);
                selectedY.Text = targetY.ToString(CultureInfo.InvariantCulture);
                LoadExpression(null, null);
            }
            catch (Exception exception)
            {
                SetStatus("Cannot move ROI: " + exception.Message);
            }
        }

        private void UpdateSelection(int x, int y, bool centerViewport)
        {
            if (fullFrameWidth <= 0 || fullFrameHeight <= 0 || x < 0 || y < 0 || x >= fullFrameWidth || y >= fullFrameHeight)
            {
                SetStatus("The requested coordinate is outside the full frame.");
                return;
            }

            currentX = x;
            currentY = y;
            UpdateSelectedCellRectangle();
            selectedX.Text = x.ToString(CultureInfo.InvariantCulture);
            selectedY.Text = y.ToString(CultureInfo.InvariantCulture);
            UpdateNavigator();
            if (frame != null && IsLoadedCoordinate(x, y))
            {
                UpdateViewportAndOverlay();
                SetStatus(Describe(x, y));
                if (centerViewport)
                {
                    Dispatcher.BeginInvoke(new Action(CenterOnSelection));
                }
            }
            else
            {
                SetStatus("Pixel (" + x + ", " + y + ") is outside the loaded ROI. Select Load ROI to inspect it.");
            }
        }

        private void ApplyZoom(double newZoom)
        {
            zoom = newZoom;
            if (frame == null)
            {
                return;
            }

            canvas.Width = frame.Configuration.Width * zoom;
            canvas.Height = frame.Configuration.Height * zoom;
            image.Width = canvas.Width;
            image.Height = canvas.Height;
            RenderOptions.SetBitmapScalingMode(image, zoom >= 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.Fant);
            UpdateSelectedCellRectangle();
            UpdateViewportAndOverlay();
            if (IsLoadedCoordinate(currentX, currentY))
            {
                SetStatus(Describe(currentX, currentY));
            }
        }

        private void UpdateViewportAndOverlay()
        {
            if (frame == null)
            {
                return;
            }

            var firstX = Math.Max(0, (int)Math.Floor(scrollViewer.HorizontalOffset / zoom));
            var firstY = Math.Max(0, (int)Math.Floor(scrollViewer.VerticalOffset / zoom));
            var lastX = Math.Min(frame.Configuration.Width - 1, (int)Math.Ceiling((scrollViewer.HorizontalOffset + scrollViewer.ViewportWidth) / zoom));
            var lastY = Math.Min(frame.Configuration.Height - 1, (int)Math.Ceiling((scrollViewer.VerticalOffset + scrollViewer.ViewportHeight) / zoom));
            viewport.Text = String.Format(CultureInfo.InvariantCulture, "X lim [{0}, {1}]  Y lim [{2}, {3}]  |  {4:0.##}x",
                frame.Configuration.OriginX + firstX, frame.Configuration.OriginX + lastX,
                frame.Configuration.OriginY + firstY, frame.Configuration.OriginY + lastY, zoom);
            ClearValueOverlay();

            if (zoom >= 18 && lastX >= firstX && lastY >= firstY && (lastX - firstX + 1) * (lastY - firstY + 1) <= 900)
            {
                for (var y = firstY; y <= lastY; y++)
                {
                    for (var x = firstX; x <= lastX; x++)
                    {
                        AddValueCell(x, y);
                    }
                }
            }

            UpdateRoiRectangle();
        }

        private void AddValueCell(int x, int y)
        {
            var raw = frame.GetRaw(x, y);
            var text = zoom >= 28
                ? raw + "\nQ " + QFormat.Format(raw, frame.Configuration.FractionalBits)
                : raw.ToString(CultureInfo.InvariantCulture);
            var label = new TextBlock
            {
                Text = text,
                Foreground = TextBrush,
                FontSize = Math.Max(8, Math.Min(13, zoom / (zoom >= 28 ? 5 : 3.2))),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Width = zoom,
                Height = zoom,
                Padding = new Thickness(1),
                IsHitTestVisible = false
            };
            var cell = new Border { Child = label, BorderBrush = new SolidColorBrush(Color.FromArgb(110, 190, 205, 230)), BorderThickness = new Thickness(0.5), Width = zoom, Height = zoom, IsHitTestVisible = false };
            Canvas.SetLeft(cell, x * zoom);
            Canvas.SetTop(cell, y * zoom);
            canvas.Children.Add(cell);
            valueOverlay.Add(cell);
        }

        private void ClearValueOverlay()
        {
            for (var index = 0; index < valueOverlay.Count; index++)
            {
                canvas.Children.Remove(valueOverlay[index]);
            }

            valueOverlay.Clear();
        }

        private bool IsLoadedCoordinate(int x, int y)
        {
            return frame != null && x >= frame.Configuration.OriginX && y >= frame.Configuration.OriginY &&
                   x < frame.Configuration.OriginX + frame.Configuration.Width && y < frame.Configuration.OriginY + frame.Configuration.Height;
        }

        private void UpdateRoiRectangle()
        {
            if (frame == null)
            {
                return;
            }

            var localX = 0;
            var localY = 0;
            var widthInSamples = frame.Configuration.Width;
            var heightInSamples = frame.Configuration.Height;
            if (frame.Configuration.OriginX == 0 && frame.Configuration.OriginY == 0 &&
                frame.Configuration.Width == fullFrameWidth && frame.Configuration.Height == fullFrameHeight)
            {
                var requestedWidth = Math.Max(1, ParseNavigatorValue(roiWidth.Text, 1));
                var requestedHeight = Math.Max(1, ParseNavigatorValue(roiHeight.Text, 1));
                widthInSamples = Math.Min(frame.Configuration.Width, requestedWidth);
                heightInSamples = Math.Min(frame.Configuration.Height, requestedHeight);
                var centerX = ParseNavigatorValue(selectedX.Text, frame.Configuration.Width / 2);
                var centerY = ParseNavigatorValue(selectedY.Text, frame.Configuration.Height / 2);
                localX = Math.Max(0, Math.Min(frame.Configuration.Width - widthInSamples, centerX - widthInSamples / 2));
                localY = Math.Max(0, Math.Min(frame.Configuration.Height - heightInSamples, centerY - heightInSamples / 2));
            }

            roiRectangle.Width = Math.Max(1, widthInSamples * zoom - 2);
            roiRectangle.Height = Math.Max(1, heightInSamples * zoom - 2);
            Canvas.SetLeft(roiRectangle, localX * zoom + 1);
            Canvas.SetTop(roiRectangle, localY * zoom + 1);
            roiRectangle.Visibility = Visibility.Visible;
        }

        private void UpdateSelectedCellRectangle()
        {
            if (frame == null || !IsLoadedCoordinate(currentX, currentY))
            {
                selectedCellRectangle.Visibility = Visibility.Collapsed;
                return;
            }

            var localX = currentX - frame.Configuration.OriginX;
            var localY = currentY - frame.Configuration.OriginY;
            selectedCellRectangle.Width = Math.Max(1, zoom);
            selectedCellRectangle.Height = Math.Max(1, zoom);
            Canvas.SetLeft(selectedCellRectangle, localX * zoom);
            Canvas.SetTop(selectedCellRectangle, localY * zoom);
            selectedCellRectangle.Visibility = Visibility.Visible;
        }

        private void CenterOnSelection()
        {
            if (!IsLoadedCoordinate(currentX, currentY))
            {
                return;
            }

            scrollViewer.ScrollToHorizontalOffset(Math.Max(0, (currentX - frame.Configuration.OriginX + 0.5) * zoom - scrollViewer.ViewportWidth / 2));
            scrollViewer.ScrollToVerticalOffset(Math.Max(0, (currentY - frame.Configuration.OriginY + 0.5) * zoom - scrollViewer.ViewportHeight / 2));
        }

        private string Describe(int x, int y)
        {
            var localX = x - frame.Configuration.OriginX;
            var localY = y - frame.Configuration.OriginY;
            var raw = frame.GetRaw(localX, localY);
            var site = frame.Configuration.GetBayerSite(localX, localY);
            return String.Format(CultureInfo.InvariantCulture,
                "Pixel ({0}, {1})   RAW {2}   Q {3} ({4}, range {5}..{6})   Bayer {7}   Read {8}",
                x, y, raw, QFormat.Format(raw, frame.Configuration.FractionalBits),
                QFormat.FormatSpecification(frame.Configuration.IntegerBits, frame.Configuration.FractionalBits),
                QFormat.Format(frame.Configuration.RawMinimum, frame.Configuration.FractionalBits),
                QFormat.Format(frame.Configuration.RawMaximum, frame.Configuration.FractionalBits), site, lastReadPath);
        }

        private void SetStatus(string text)
        {
            status.Text = text;
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
            public PixelOrder PixelOrder { get; private set; }
            public PixelType PixelType { get; private set; }
            public VisualizeChannel VisualizeChannel { get; private set; }
            public string SelectedX { get; private set; }
            public string SelectedY { get; private set; }
            public string RoiWidth { get; private set; }
            public string RoiHeight { get; private set; }

            public static ViewerProfile Create(string expressionValue, string widthValue, string heightValue, string strideValue, string qFormatValue,
                bool signedValue, PixelOrder pixelOrderValue, PixelType pixelTypeValue, VisualizeChannel visualizeChannelValue,
                string selectedXValue, string selectedYValue, string roiWidthValue, string roiHeightValue)
            {
                return new ViewerProfile
                {
                    Expression = expressionValue,
                    Width = widthValue,
                    Height = heightValue,
                    Stride = strideValue,
                    QFormat = qFormatValue,
                    IsSigned = signedValue,
                    PixelOrder = pixelOrderValue,
                    PixelType = pixelTypeValue,
                    VisualizeChannel = visualizeChannelValue,
                    SelectedX = selectedXValue,
                    SelectedY = selectedYValue,
                    RoiWidth = roiWidthValue,
                    RoiHeight = roiHeightValue
                };
            }

            public override string ToString()
            {
                return Expression + "  |  " + Width + "x" + Height + "  |  " + QFormat + "  |  " + PixelType + "/" + PixelOrder;
            }
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
