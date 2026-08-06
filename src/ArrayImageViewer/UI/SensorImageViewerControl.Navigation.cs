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
            SetStatus("Drag or click to move the visible View ROI. Kernel size and cursor remain unchanged.");
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
                PreviewNavigatorView(x, y);
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
            var hasCoordinate = TryGetNavigatorCoordinate(point, out x, out y);
            if (hasCoordinate)
            {
                if (HasNavigatorDragDistance(point))
                {
                    PreviewNavigatorView(x, y);
                }
                else
                {
                    MoveViewTo(x, y, false);
                }
            }

            isNavigatorSelecting = false;
            navigatorCanvas.ReleaseMouseCapture();
            if (hasCoordinate && HasNavigatorDragDistance(point))
            {
                MoveViewTo(x, y, false);
            }
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

        private void PreviewNavigatorView(int endX, int endY)
        {
            viewCenterX = endX;
            viewCenterY = endY;
            UpdateNavigator();
            SetStatus("View preview centered at (" + endX.ToString(CultureInfo.InvariantCulture) + ", " + endY.ToString(CultureInfo.InvariantCulture) +
                "). Release to load this visible area once.");
        }

        private void MoveViewTo(int x, int y, bool centerCursor)
        {
            viewCenterX = x;
            viewCenterY = y;
            if (centerCursor)
            {
                currentX = x;
                currentY = y;
            }

            UpdateNavigator();
            SaveCurrentProfile();
            LoadContextPreview(null, null);
        }

        private void UpdateNavigator()
        {
            int frameWidth;
            int frameHeight;
            if (!TryGetNavigatorFrame(out frameWidth, out frameHeight))
            {
                navigatorFrame.Visibility = Visibility.Collapsed;
                navigatorRoi.Visibility = Visibility.Collapsed;
                navigatorKernel.Visibility = Visibility.Collapsed;
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
            navigatorKernel.Visibility = Visibility.Visible;
            navigatorHorizontal.Visibility = Visibility.Visible;
            navigatorVertical.Visibility = Visibility.Visible;
            navigatorFrame.Width = displayWidth;
            navigatorFrame.Height = displayHeight;
            Canvas.SetLeft(navigatorFrame, navigatorFrameBounds.X);
            Canvas.SetTop(navigatorFrame, navigatorFrameBounds.Y);

            var viewWidth = Math.Max(1, ParseNavigatorValue(renderWidth.Text, 1));
            var viewHeight = Math.Max(1, ParseNavigatorValue(renderHeight.Text, 1));
            var view = RoiGeometry.ClampCentered(frameWidth, frameHeight,
                viewCenterX >= 0 ? viewCenterX : currentX, viewCenterY >= 0 ? viewCenterY : currentY, viewWidth, viewHeight);
            navigatorRoi.Width = Math.Max(2, view.Width * scale);
            navigatorRoi.Height = Math.Max(2, view.Height * scale);
            Canvas.SetLeft(navigatorRoi, navigatorFrameBounds.X + view.X * scale);
            Canvas.SetTop(navigatorRoi, navigatorFrameBounds.Y + view.Y * scale);

            var kernelWidth = Math.Max(1, ParseNavigatorValue(roiWidth.Text, 1));
            var kernelHeight = Math.Max(1, ParseNavigatorValue(roiHeight.Text, 1));
            var kernel = RoiGeometry.ClampCentered(frameWidth, frameHeight,
                kernelCenterX >= 0 ? kernelCenterX : currentX, kernelCenterY >= 0 ? kernelCenterY : currentY, kernelWidth, kernelHeight);
            navigatorKernel.Width = Math.Max(2, kernel.Width * scale);
            navigatorKernel.Height = Math.Max(2, kernel.Height * scale);
            Canvas.SetLeft(navigatorKernel, navigatorFrameBounds.X + kernel.X * scale);
            Canvas.SetTop(navigatorKernel, navigatorFrameBounds.Y + kernel.Y * scale);

            var markerX = navigatorFrameBounds.X + (Math.Max(0, Math.Min(frameWidth - 1, currentX)) + 0.5) * scale;
            var markerY = navigatorFrameBounds.Y + (Math.Max(0, Math.Min(frameHeight - 1, currentY)) + 0.5) * scale;
            navigatorHorizontal.X1 = navigatorFrameBounds.X;
            navigatorHorizontal.X2 = navigatorFrameBounds.Right;
            navigatorHorizontal.Y1 = markerY;
            navigatorHorizontal.Y2 = markerY;
            navigatorVertical.X1 = markerX;
            navigatorVertical.X2 = markerX;
            navigatorVertical.Y1 = navigatorFrameBounds.Y;
            navigatorVertical.Y2 = navigatorFrameBounds.Bottom;
            navigatorInfo.Text = String.Format(CultureInfo.InvariantCulture,
                "Frame  {0} x {1}\nView  x={2}..{3}, y={4}..{5}  ({6} x {7})\nKernel  x={8}..{9}, y={10}..{11}  ({12} x {13})",
                frameWidth, frameHeight, view.X, view.X + view.Width - 1, view.Y, view.Y + view.Height - 1, view.Width, view.Height,
                kernel.X, kernel.X + kernel.Width - 1, kernel.Y, kernel.Y + kernel.Height - 1, kernel.Width, kernel.Height);
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
            if (viewCenterX < 0 || viewCenterY < 0)
            {
                viewCenterX = selectedGlobalX;
                viewCenterY = selectedGlobalY;
            }
            if (kernelCenterX < 0 || kernelCenterY < 0)
            {
                kernelCenterX = selectedGlobalX;
                kernelCenterY = selectedGlobalY;
            }
            UpdateNavigator();
            ApplyZoom(zoom);
            UpdateSelection(currentX, currentY, false);
        }
    }
}
