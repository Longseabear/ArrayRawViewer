using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
        private readonly ComboBox pattern = CreateComboBox(88);
        private readonly ComboBox display = CreateComboBox(108);
        private readonly TextBox selectedX = CreateTextBox("0", 60);
        private readonly TextBox selectedY = CreateTextBox("0", 60);
        private readonly TextBlock status = new TextBlock { Foreground = TextBrush, TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock viewport = new TextBlock { Foreground = MutedBrush, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly ScrollViewer scrollViewer = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = ControlBrush, Focusable = true };
        private readonly Canvas canvas = new Canvas { Background = new SolidColorBrush(Color.FromRgb(6, 10, 17)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, MinWidth = 520, MinHeight = 260 };
        private readonly System.Windows.Controls.Image image = new System.Windows.Controls.Image { Stretch = Stretch.Fill, SnapsToDevicePixels = true };
        private readonly Border emptyState = CreateEmptyState();
        private readonly Rectangle verticalCrosshair = new Rectangle { Fill = Brushes.OrangeRed, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        private readonly Rectangle horizontalCrosshair = new Rectangle { Fill = Brushes.OrangeRed, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        private readonly List<UIElement> valueOverlay = new List<UIElement>();

        private FrameBuffer frame;
        private double zoom = 0.15;
        private int currentX;
        private int currentY;

        public SensorImageViewerControl()
        {
            pattern.ItemsSource = Enum.GetValues(typeof(BayerPattern));
            pattern.SelectedItem = BayerPattern.GRBG;
            display.ItemsSource = Enum.GetValues(typeof(DisplayMode));
            display.SelectedItem = DisplayMode.Composite;
            availablePointers.SelectionChanged += AvailablePointerChanged;
            widthValueSource.SelectionChanged += WidthValueSelected;
            heightValueSource.SelectionChanged += HeightValueSelected;
            strideValueSource.SelectionChanged += StrideValueSelected;
            xValueSource.SelectionChanged += XValueSelected;
            yValueSource.SelectionChanged += YValueSelected;

            canvas.Children.Add(image);
            canvas.Children.Add(emptyState);
            canvas.Children.Add(verticalCrosshair);
            canvas.Children.Add(horizontalCrosshair);
            canvas.MouseMove += CanvasMouseMove;
            canvas.MouseLeftButtonDown += CanvasMouseLeftButtonDown;
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
            SetStatus("Select a pointer in the editor, then use Capture selection. Pause the debuggee before Load pointer.");
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
            pointerRow.Children.Add(CreateAction("", CreateButton("Load frame", LoadExpression, true)));
            pointerRow.Children.Add(CreateAction("", CreateButton("Synthetic", RenderSyntheticClick, false)));
            panel.Children.Add(CreateSection("SOURCE", "Choose a pointer or type an expression", pointerRow));

            var formatRow = CreateRow();
            formatRow.Children.Add(CreateField("WIDTH", width));
            formatRow.Children.Add(CreateField("HEIGHT", height));
            formatRow.Children.Add(CreateField("ROW STRIDE", stride));
            formatRow.Children.Add(CreateField("Q FORMAT", qFormat));
            formatRow.Children.Add(CreateField("VALUE TYPE", signed));
            formatRow.Children.Add(CreateField("PIXEL LAYOUT", pattern));
            formatRow.Children.Add(CreateField("RENDER", display));
            panel.Children.Add(CreateSection("FRAME", "Full-frame interpretation and color mapping", formatRow));

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
            inspectRow.Children.Add(CreateAction("", CreateButton("Center view", JumpToCoordinate, false)));
            inspectRow.Children.Add(CreateAction("", CreateButton("Inspect cells", InspectCells, true)));
            inspectRow.Children.Add(new TextBlock { Text = "Mouse wheel zooms  |  Click selects a pixel", Foreground = MutedBrush, Margin = new Thickness(12, 23, 0, 0), FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(CreateSection("NAVIGATE", "Jump directly to a full-frame coordinate", inspectRow));
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
            return new ComboBox { Width = width, Height = 27, Background = ControlBrush, Foreground = TextBrush, BorderBrush = PanelBorderBrush, Padding = new Thickness(5, 2, 5, 2), VerticalContentAlignment = VerticalAlignment.Center };
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
                Text = "NO FRAME LOADED\n\nChoose a pointer, enter its dimensions, then load the frame.\nSynthetic preview is available without a paused debuggee.",
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

        private async void RenderSyntheticClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var configuration = ReadConfiguration();
                SetStatus("Rendering synthetic Bayer frame...");
                var rendered = await Task.Run(() =>
                {
                    var source = FrameBuffer.CreateSynthetic(configuration);
                    return new RenderedFrame(source, FrameRenderer.Render(source));
                });
                ApplyFrame(rendered.Frame, rendered.Bitmap);
            }
            catch (Exception exception)
            {
                SetStatus("Cannot render: " + exception.Message);
            }
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
                SetStatus(values.Count == 0
                    ? "No integer locals found. Pause in the function that owns width, height, X, and Y."
                    : "Choose a local variable beside W, H, stride, X, or Y to copy its debugger value.");
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

        private void LoadExpression(object sender, RoutedEventArgs e)
        {
            try
            {
                var configuration = ReadConfiguration();
                SetStatus("Reading debugger expression. This draft is limited to 16,384 samples.");
                var source = DebugExpressionFrameReader.Read(expression.Text, configuration);
                ApplyFrame(source, FrameRenderer.Render(source));
            }
            catch (Exception exception)
            {
                SetStatus("Cannot read pointer: " + exception.Message);
            }
        }

        private FrameConfiguration ReadConfiguration()
        {
            int parsedWidth;
            int parsedHeight;
            int parsedStride;
            int parsedIntegerBits;
            int parsedFractionalBits;
            if (!Int32.TryParse(width.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedWidth) ||
                !Int32.TryParse(height.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedHeight) ||
                !Int32.TryParse(stride.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedStride) ||
                !QFormat.TryParse(qFormat.Text, out parsedIntegerBits, out parsedFractionalBits))
            {
                throw new ArgumentException("Enter W, H, and stride as integers; use Q format such as 8.8b.");
            }

            return new FrameConfiguration(parsedWidth, parsedHeight, parsedStride, parsedIntegerBits, parsedFractionalBits, signed.IsChecked == true,
                (BayerPattern)pattern.SelectedItem, (DisplayMode)display.SelectedItem);
        }

        private void ApplyFrame(FrameBuffer source, ImageSource bitmap)
        {
            frame = source;
            image.Source = bitmap;
            emptyState.Visibility = Visibility.Collapsed;
            currentX = Math.Min(currentX, frame.Configuration.Width - 1);
            currentY = Math.Min(currentY, frame.Configuration.Height - 1);
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

            var point = e.GetPosition(canvas);
            UpdateSelection((int)(point.X / zoom), (int)(point.Y / zoom), false);
        }

        private void CanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (frame == null)
            {
                return;
            }

            var point = e.GetPosition(canvas);
            var x = (int)(point.X / zoom);
            var y = (int)(point.Y / zoom);
            if (x >= 0 && y >= 0 && x < frame.Configuration.Width && y < frame.Configuration.Height)
            {
                SetStatus(Describe(x, y));
            }
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
            int x;
            int y;
            if (!Int32.TryParse(selectedX.Text, out x) || !Int32.TryParse(selectedY.Text, out y))
            {
                SetStatus("Jump coordinates must be integers.");
                return;
            }

            UpdateSelection(x, y, true);
        }

        private void InspectCells(object sender, RoutedEventArgs e)
        {
            JumpToCoordinate(sender, e);
            if (frame != null)
            {
                ApplyZoom(48.0);
                Dispatcher.BeginInvoke(new Action(CenterOnSelection));
            }
        }

        private void UpdateSelection(int x, int y, bool centerViewport)
        {
            if (frame == null || x < 0 || y < 0 || x >= frame.Configuration.Width || y >= frame.Configuration.Height)
            {
                SetStatus("The requested coordinate is outside the full frame.");
                return;
            }

            currentX = x;
            currentY = y;
            selectedX.Text = x.ToString(CultureInfo.InvariantCulture);
            selectedY.Text = y.ToString(CultureInfo.InvariantCulture);
            UpdateViewportAndOverlay();
            SetStatus(Describe(x, y));
            if (centerViewport)
            {
                Dispatcher.BeginInvoke(new Action(CenterOnSelection));
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
            SetStatus(Describe(currentX, currentY));
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
            viewport.Text = String.Format(CultureInfo.InvariantCulture, "Viewport  X {0}..{1}  Y {2}..{3}  |  {4:0.##}x", firstX, lastX, firstY, lastY, zoom);
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

            UpdateCrosshair();
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

        private void UpdateCrosshair()
        {
            if (frame == null)
            {
                return;
            }

            canvas.Children.Remove(verticalCrosshair);
            canvas.Children.Remove(horizontalCrosshair);
            var thickness = Math.Max(1, Math.Min(3, zoom / 4));
            verticalCrosshair.Width = thickness;
            verticalCrosshair.Height = canvas.Height;
            Canvas.SetLeft(verticalCrosshair, Math.Max(0, (currentX + 0.5) * zoom - thickness / 2));
            Canvas.SetTop(verticalCrosshair, 0);
            horizontalCrosshair.Width = canvas.Width;
            horizontalCrosshair.Height = thickness;
            Canvas.SetLeft(horizontalCrosshair, 0);
            Canvas.SetTop(horizontalCrosshair, Math.Max(0, (currentY + 0.5) * zoom - thickness / 2));
            verticalCrosshair.Visibility = Visibility.Visible;
            horizontalCrosshair.Visibility = Visibility.Visible;
            canvas.Children.Add(verticalCrosshair);
            canvas.Children.Add(horizontalCrosshair);
        }

        private void CenterOnSelection()
        {
            scrollViewer.ScrollToHorizontalOffset(Math.Max(0, (currentX + 0.5) * zoom - scrollViewer.ViewportWidth / 2));
            scrollViewer.ScrollToVerticalOffset(Math.Max(0, (currentY + 0.5) * zoom - scrollViewer.ViewportHeight / 2));
        }

        private string Describe(int x, int y)
        {
            var raw = frame.GetRaw(x, y);
            var site = BayerLayout.GetSite(frame.Configuration.BayerPattern, x, y);
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

        private sealed class RenderedFrame
        {
            public RenderedFrame(FrameBuffer frame, ImageSource bitmap)
            {
                Frame = frame;
                Bitmap = bitmap;
            }

            public FrameBuffer Frame { get; private set; }
            public ImageSource Bitmap { get; private set; }
        }
    }
}
