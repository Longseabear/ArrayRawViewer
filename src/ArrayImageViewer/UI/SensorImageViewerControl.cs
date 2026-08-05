using System;
using System.Collections.Generic;
using System.Globalization;
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

        public SensorImageViewerControl()
        {
            pixelOrder.ItemsSource = Enum.GetValues(typeof(PixelOrder));
            pixelOrder.SelectedItem = PixelOrder.GRFirst;
            pixelType.ItemsSource = Enum.GetValues(typeof(PixelType));
            pixelType.SelectedItem = PixelType.Bayer;
            visualizeChannel.ItemsSource = Enum.GetValues(typeof(VisualizeChannel));
            visualizeChannel.SelectedItem = VisualizeChannel.BayerRaw;
            availablePointers.SelectionChanged += AvailablePointerChanged;
            widthValueSource.SelectionChanged += WidthValueSelected;
            heightValueSource.SelectionChanged += HeightValueSelected;
            strideValueSource.SelectionChanged += StrideValueSelected;
            xValueSource.SelectionChanged += XValueSelected;
            yValueSource.SelectionChanged += YValueSelected;

            canvas.Children.Add(image);
            canvas.Children.Add(emptyState);
            canvas.Children.Add(roiRectangle);
            canvas.MouseMove += CanvasMouseMove;
            canvas.MouseLeftButtonDown += CanvasMouseLeftButtonDown;
            canvas.MouseLeftButtonUp += CanvasMouseLeftButtonUp;
            canvas.MouseDown += CanvasMouseDown;
            canvas.MouseUp += CanvasMouseUp;
            canvas.PreviewMouseWheel += CanvasMouseWheel;
            scrollViewer.ScrollChanged += ScrollViewerChanged;
            scrollViewer.SizeChanged += ScrollViewerSizeChanged;
            scrollViewer.Content = canvas;

            var root = new DockPanel { Background = RootBrush, LastChildFill = true };
            var header = CreateHeader();
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
            var footer = CreateFooter();
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);
            root.Children.Add(scrollViewer);
            Content = root;
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
            pointerRow.Children.Add(CreateAction("", CreateButton("Load ROI", LoadExpression, true)));
            panel.Children.Add(CreateSection("SOURCE", "Choose a pointer or type an expression, then load only the requested ROI", pointerRow));

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
            panel.Children.Add(CreateSection("FRAME", "Full-frame dimensions; pixel order, physical type, and channel are independent", formatRow));

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
            return panel;
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

        private void CaptureSelection(object sender, RoutedEventArgs e)
        {
            try
            {
                expression.Text = DebugExpressionFrameReader.GetActiveEditorSelection();
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
                availablePointers.ItemsSource = pointers;
                if (pointers.Count == 0)
                {
                    SetStatus("No pointer locals found. Pause in the function that owns A or B, then refresh.");
                    return;
                }

                availablePointers.SelectedIndex = 0;
                SetStatus("Found " + pointers.Count.ToString(CultureInfo.InvariantCulture) + " pointer variable(s). Choose A or B from the list.");
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

            expression.Text = pointer.Name;
            signed.IsChecked = pointer.IsSigned;
            SetStatus("Selected " + pointer.Name + " as " + pointer.Type + ".");
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
                ApplyFrame(source, FrameRenderer.Render(source), configuration.Width, configuration.Height, roi.CenterX, roi.CenterY);
                ApplyZoom(Math.Max(18.0, zoom));
            }
            catch (Exception exception)
            {
                SetStatus("Cannot read pointer: " + exception.Message);
            }
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
            return new RoiBounds(x, y, actualWidth, actualHeight, centerX, centerY);
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
            selectedX.Text = x.ToString(CultureInfo.InvariantCulture);
            selectedY.Text = y.ToString(CultureInfo.InvariantCulture);
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

            roiRectangle.Width = Math.Max(0, canvas.Width - 2);
            roiRectangle.Height = Math.Max(0, canvas.Height - 2);
            Canvas.SetLeft(roiRectangle, 1);
            Canvas.SetTop(roiRectangle, 1);
            roiRectangle.Visibility = Visibility.Visible;
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
                "Pixel ({0}, {1})   RAW {2}   Q {3} ({4}, range {5}..{6})   Bayer {7}",
                x, y, raw, QFormat.Format(raw, frame.Configuration.FractionalBits),
                QFormat.FormatSpecification(frame.Configuration.IntegerBits, frame.Configuration.FractionalBits),
                QFormat.Format(frame.Configuration.RawMinimum, frame.Configuration.FractionalBits),
                QFormat.Format(frame.Configuration.RawMaximum, frame.Configuration.FractionalBits), site);
        }

        private void SetStatus(string text)
        {
            status.Text = text;
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
