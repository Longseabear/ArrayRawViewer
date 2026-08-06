using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ArrayImageViewer.Core;

namespace ArrayImageViewer.UI
{
    internal sealed partial class SensorImageViewerControl
    {
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
            LoadContextPreview(null, null);
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
            var roi = RoiGeometry.FromDrag(fullFrameWidth, fullFrameHeight, navigatorStartX, navigatorStartY, endX, endY);
            var left = roi.X;
            var top = roi.Y;
            var right = roi.X + roi.Width - 1;
            var bottom = roi.Y + roi.Height - 1;
            selectedX.Text = roi.CenterX.ToString(CultureInfo.InvariantCulture);
            selectedY.Text = roi.CenterY.ToString(CultureInfo.InvariantCulture);
            roiWidth.Text = roi.Width.ToString(CultureInfo.InvariantCulture);
            roiHeight.Text = roi.Height.ToString(CultureInfo.InvariantCulture);
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

            var centerX = ParseNavigatorValue(selectedX.Text, Math.Max(0, Math.Min(frameWidth - 1, currentX)));
            var centerY = ParseNavigatorValue(selectedY.Text, Math.Max(0, Math.Min(frameHeight - 1, currentY)));
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
                "Frame  {0} x {1}\nKernel  x={2}..{3}, y={4}..{5}  ({6} x {7})",
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

        private void ApplyFrame(FrameBuffer source, ImageSource bitmap, int sourceWidth, int sourceHeight, int selectedGlobalX, int selectedGlobalY,
            bool showFullFrameContext)
        {
            frame = source;
            fullFrameWidth = sourceWidth;
            fullFrameHeight = sourceHeight;
            showsFullFrameContext = showFullFrameContext;
            image.Source = bitmap;
            emptyState.Visibility = Visibility.Collapsed;
            currentX = selectedGlobalX;
            currentY = selectedGlobalY;
            UpdateNavigator();
            ApplyZoom(zoom);
            UpdateSelection(currentX, currentY, false);
        }
    }
}
