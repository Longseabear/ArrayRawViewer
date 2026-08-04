using System;
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
        private readonly TextBox expression = new TextBox { MinWidth = 170, Text = "raw" };
        private readonly TextBox width = new TextBox { Width = 52, Text = "4096" };
        private readonly TextBox height = new TextBox { Width = 52, Text = "3072" };
        private readonly TextBox stride = new TextBox { Width = 52, Text = "4096" };
        private readonly TextBox fractionalBits = new TextBox { Width = 36, Text = "0" };
        private readonly CheckBox signed = new CheckBox { Content = "signed", VerticalAlignment = VerticalAlignment.Center };
        private readonly ComboBox pattern = new ComboBox { Width = 70 };
        private readonly ComboBox display = new ComboBox { Width = 84 };
        private readonly TextBox selectedX = new TextBox { Width = 52, Text = "0" };
        private readonly TextBox selectedY = new TextBox { Width = 52, Text = "0" };
        private readonly TextBlock status = new TextBlock { Margin = new Thickness(6, 3, 6, 3), TextWrapping = TextWrapping.Wrap };
        private readonly ScrollViewer scrollViewer = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.Black };
        private readonly Canvas canvas = new Canvas { Background = Brushes.Black };
        private readonly System.Windows.Controls.Image image = new System.Windows.Controls.Image { Stretch = Stretch.Fill, SnapsToDevicePixels = true };
        private readonly Rectangle verticalCrosshair = new Rectangle { Fill = Brushes.OrangeRed, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        private readonly Rectangle horizontalCrosshair = new Rectangle { Fill = Brushes.OrangeRed, IsHitTestVisible = false, Visibility = Visibility.Collapsed };

        private FrameBuffer frame;
        private double zoom = 0.15;
        private int currentX;
        private int currentY;

        public SensorImageViewerControl()
        {
            pattern.ItemsSource = Enum.GetValues(typeof(BayerPattern));
            pattern.SelectedItem = BayerPattern.GRBG;
            display.ItemsSource = Enum.GetValues(typeof(DisplayMode));
            display.SelectedItem = DisplayMode.Raw;

            canvas.Children.Add(image);
            canvas.Children.Add(verticalCrosshair);
            canvas.Children.Add(horizontalCrosshair);
            canvas.MouseMove += CanvasMouseMove;
            canvas.MouseLeftButtonDown += CanvasMouseLeftButtonDown;
            canvas.PreviewMouseWheel += CanvasMouseWheel;
            scrollViewer.Content = canvas;

            var panel = new DockPanel();
            panel.LastChildFill = true;
            var configurationPanel = CreateConfigurationPanel();
            DockPanel.SetDock(configurationPanel, Dock.Top);
            panel.Children.Add(configurationPanel);
            DockPanel.SetDock(status, Dock.Bottom);
            panel.Children.Add(status);
            panel.Children.Add(scrollViewer);
            Content = panel;
            SetStatus("Set frame dimensions, then use Synthetic preview or Load expression while the debuggee is paused.");
        }

        private UIElement CreateConfigurationPanel()
        {
            var area = new WrapPanel { Margin = new Thickness(6) };
            area.Children.Add(Label("expr"));
            area.Children.Add(expression);
            area.Children.Add(Label("W"));
            area.Children.Add(width);
            area.Children.Add(Label("H"));
            area.Children.Add(height);
            area.Children.Add(Label("stride"));
            area.Children.Add(stride);
            area.Children.Add(Label("frac"));
            area.Children.Add(fractionalBits);
            area.Children.Add(signed);
            area.Children.Add(Label("Bayer"));
            area.Children.Add(pattern);
            area.Children.Add(Label("view"));
            area.Children.Add(display);

            var syntheticButton = new Button { Content = "Synthetic preview", Margin = new Thickness(6, 0, 0, 0) };
            syntheticButton.Click += async (sender, args) => await RenderSyntheticAsync();
            area.Children.Add(syntheticButton);
            var expressionButton = new Button { Content = "Load expression", Margin = new Thickness(4, 0, 0, 0) };
            expressionButton.Click += LoadExpression;
            area.Children.Add(expressionButton);

            area.Children.Add(Label("jump X"));
            area.Children.Add(selectedX);
            area.Children.Add(Label("Y"));
            area.Children.Add(selectedY);
            var jumpButton = new Button { Content = "Go", Margin = new Thickness(4, 0, 0, 0) };
            jumpButton.Click += JumpToCoordinate;
            area.Children.Add(jumpButton);
            return area;
        }

        private static TextBlock Label(string text)
        {
            return new TextBlock { Text = text, Margin = new Thickness(6, 3, 2, 3), VerticalAlignment = VerticalAlignment.Center };
        }

        private async Task RenderSyntheticAsync()
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
                SetStatus("Cannot read expression: " + exception.Message);
            }
        }

        private FrameConfiguration ReadConfiguration()
        {
            int parsedWidth;
            int parsedHeight;
            int parsedStride;
            int parsedFractionalBits;
            if (!Int32.TryParse(width.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedWidth) ||
                !Int32.TryParse(height.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedHeight) ||
                !Int32.TryParse(stride.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedStride) ||
                !Int32.TryParse(fractionalBits.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedFractionalBits))
            {
                throw new ArgumentException("Width, height, stride, and fractional bits must be integers.");
            }

            return new FrameConfiguration(parsedWidth, parsedHeight, parsedStride, parsedFractionalBits, signed.IsChecked == true,
                (BayerPattern)pattern.SelectedItem, (DisplayMode)display.SelectedItem);
        }

        private void ApplyFrame(FrameBuffer source, ImageSource bitmap)
        {
            frame = source;
            image.Source = bitmap;
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
            UpdateCrosshair();
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
            UpdateCrosshair();
            SetStatus(Describe(currentX, currentY));
        }

        private void UpdateCrosshair()
        {
            if (frame == null)
            {
                return;
            }

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
                "({0}, {1})  raw={2}  Q={3}  Bayer={4}  zoom={5:0.##}x",
                x, y, raw, QFormat.Format(raw, frame.Configuration.FractionalBits), site, zoom);
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
