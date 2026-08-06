using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ArrayImageViewer.Core;

namespace ArrayImageViewer.UI
{
    internal sealed partial class SensorImageViewerControl
    {
        private void CanvasMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (frame == null)
            {
                return;
            }

            var pointInCanvas = e.GetPosition(canvas);
            var pointInViewport = e.GetPosition(scrollViewer);
            ZoomAroundPoint(pointInCanvas, pointInViewport, zoom * (e.Delta > 0 ? 1.35 : 1.0 / 1.35));
            e.Handled = true;
        }

        private void CanvasMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (frame == null)
            {
                return;
            }

            canvas.Focus();
            if (e.ClickCount >= 2)
            {
                int doubleClickX;
                int doubleClickY;
                if (TryGetGlobalImageCoordinate(e.GetPosition(canvas), out doubleClickX, out doubleClickY))
                {
                    UpdateSelection(doubleClickX, doubleClickY, false);
                    // The pixel is already in the decoded frame. Re-reading
                    // debugger memory here made double-click feel delayed and
                    // could race an auto-update. Center the existing canvas
                    // exactly on the clicked sample instead.
                    viewCenterX = doubleClickX;
                    viewCenterY = doubleClickY;
                    UpdateNavigator();
                    CenterOnCoordinate(doubleClickX, doubleClickY);
                    SaveCurrentProfile();
                    SetStatus("Centered the current view on pixel (" + doubleClickX.ToString(CultureInfo.InvariantCulture) + ", " +
                        doubleClickY.ToString(CultureInfo.InvariantCulture) + "). No debugger memory was read.");
                }
                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                BeginMouseRoiSelect(e.GetPosition(canvas));
                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                BeginRoiPan(e.GetPosition(scrollViewer));
                e.Handled = true;
                return;
            }

            int selectedXValue;
            int selectedYValue;
            if (TryGetGlobalImageCoordinate(e.GetPosition(canvas), out selectedXValue, out selectedYValue))
            {
                // A normal click is deliberately non-destructive: it moves the
                // inspection cursor but leaves the cached view and kernel ROI
                // where they are. This also avoids a debugger-memory read.
                UpdateSelection(selectedXValue, selectedYValue, false);
                isLeftPanPending = true;
                // Keep pan deltas in viewport coordinates. Canvas coordinates
                // move underneath the cursor as the ScrollViewer scrolls and
                // cause a feedback loop / apparent left-right wobble.
                leftPanStartPoint = e.GetPosition(scrollViewer);
                canvas.CaptureMouse();
            }
            e.Handled = true;
        }

        private void CanvasMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isFixedMouseRoiSelecting)
            {
                FinishFixedMouseRoiSelect(e.GetPosition(canvas));
                e.Handled = true;
                return;
            }

            if (isMouseRoiSelecting)
            {
                FinishMouseRoiSelect(e.GetPosition(canvas));
                e.Handled = true;
                return;
            }

            if (isRoiPanning)
            {
                FinishRoiPan(e.GetPosition(scrollViewer));
                e.Handled = true;
                return;
            }

            if (isLeftPanPending)
            {
                isLeftPanPending = false;
                canvas.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void CanvasMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                canvas.Focus();
                BeginRoiPan(e.GetPosition(scrollViewer));
                e.Handled = true;
            }
        }

        private void CanvasMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (frame == null)
            {
                return;
            }

            canvas.Focus();
            BeginMouseRoiSelect(e.GetPosition(canvas));
            e.Handled = true;
        }

        private void CanvasMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isMouseRoiSelecting)
            {
                FinishMouseRoiSelect(e.GetPosition(canvas));
                e.Handled = true;
            }
        }

        private void CanvasMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (isRoiPanning && e.ChangedButton == MouseButton.Middle)
            {
                FinishRoiPan(e.GetPosition(scrollViewer));
                e.Handled = true;
            }
        }

        private void CanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (frame == null)
            {
                return;
            }

            if (isFixedMouseRoiSelecting)
            {
                PreviewFixedMouseRoiSelect(e.GetPosition(canvas));
                return;
            }

            if (isMouseRoiSelecting)
            {
                PreviewMouseRoiSelect(e.GetPosition(canvas));
                return;
            }

            if (isRoiPanning)
            {
                PreviewRoiPan(e.GetPosition(scrollViewer));
                return;
            }

            if (isLeftPanPending && e.LeftButton == MouseButtonState.Pressed)
            {
                var point = e.GetPosition(scrollViewer);
                if (Math.Abs(point.X - leftPanStartPoint.X) >= 4 || Math.Abs(point.Y - leftPanStartPoint.Y) >= 4)
                {
                    isLeftPanPending = false;
                    BeginRoiPan(leftPanStartPoint);
                    PreviewRoiPan(point);
                }
                return;
            }

            int globalX;
            int globalY;
            if (TryGetGlobalImageCoordinate(e.GetPosition(canvas), out globalX, out globalY))
            {
                SetStatus(Describe(globalX, globalY));
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
            roiPanStartHorizontalOffset = scrollViewer.HorizontalOffset;
            roiPanStartVerticalOffset = scrollViewer.VerticalOffset;
            canvas.CaptureMouse();
            canvas.Cursor = Cursors.SizeAll;
            SetStatus("Panning the view only. No debugger memory will be read.");
        }

        private void BeginMouseRoiSelect(Point point)
        {
            int startX;
            int startY;
            if (!TryGetGlobalCanvasCoordinate(point, out startX, out startY))
            {
                SetStatus("Start Ctrl+drag inside the frame canvas.");
                return;
            }

            isMouseRoiSelecting = true;
            mouseRoiStartX = startX;
            mouseRoiStartY = startY;
            mouseRoiEndX = startX;
            mouseRoiEndY = startY;
            canvas.CaptureMouse();
            canvas.Cursor = Cursors.Cross;
            PreviewMouseRoiSelect(point);
        }

        private void BeginFixedMouseRoiSelect(Point point)
        {
            int centerX;
            int centerY;
            if (!TryGetGlobalCanvasCoordinate(point, out centerX, out centerY))
            {
                SetStatus("Start the ROI selection inside the frame canvas.");
                return;
            }

            isFixedMouseRoiSelecting = true;
            mouseRoiStartX = centerX;
            mouseRoiStartY = centerY;
            mouseRoiEndX = centerX;
            mouseRoiEndY = centerY;
            canvas.CaptureMouse();
            canvas.Cursor = Cursors.Cross;
            PreviewFixedMouseRoiSelect(point);
        }

        private void PreviewMouseRoiSelect(Point point)
        {
            int endX;
            int endY;
            if (!TryGetGlobalCanvasCoordinate(point, out endX, out endY))
            {
                return;
            }

            mouseRoiEndX = endX;
            mouseRoiEndY = endY;
            var roi = RoiGeometry.FromDrag(fullFrameWidth, fullFrameHeight, mouseRoiStartX, mouseRoiStartY, endX, endY);
            ShowMouseRoiPreview(roi, "Ctrl ROI size");
        }

        private void FinishMouseRoiSelect(Point point)
        {
            int endX;
            int endY;
            if (!TryGetGlobalCanvasCoordinate(point, out endX, out endY))
            {
                endX = mouseRoiEndX;
                endY = mouseRoiEndY;
            }

            var roi = RoiGeometry.FromDrag(fullFrameWidth, fullFrameHeight, mouseRoiStartX, mouseRoiStartY, endX, endY);
            isMouseRoiSelecting = false;
            EndMouseRoiPreview();
            CommitMouseRoi(roi);
        }

        private void PreviewFixedMouseRoiSelect(Point point)
        {
            int centerX;
            int centerY;
            if (!TryGetGlobalCanvasCoordinate(point, out centerX, out centerY))
            {
                return;
            }

            try
            {
                mouseRoiEndX = centerX;
                mouseRoiEndY = centerY;
                var roi = RoiGeometry.ClampCentered(fullFrameWidth, fullFrameHeight, centerX, centerY,
                    ResolveInteger(roiWidth, "ROI width"), ResolveInteger(roiHeight, "ROI height"));
                ShowMouseRoiPreview(roi, "ROI preview");
            }
            catch (Exception exception)
            {
                SetStatus("Cannot preview ROI: " + exception.Message);
            }
        }

        private void FinishFixedMouseRoiSelect(Point point)
        {
            int centerX;
            int centerY;
            if (!TryGetGlobalCanvasCoordinate(point, out centerX, out centerY))
            {
                centerX = mouseRoiEndX;
                centerY = mouseRoiEndY;
            }

            try
            {
                var roi = RoiGeometry.ClampCentered(fullFrameWidth, fullFrameHeight, centerX, centerY,
                    ResolveInteger(roiWidth, "ROI width"), ResolveInteger(roiHeight, "ROI height"));
                isFixedMouseRoiSelecting = false;
                EndMouseRoiPreview();
                CommitMouseRoi(roi);
            }
            catch (Exception exception)
            {
                isFixedMouseRoiSelecting = false;
                EndMouseRoiPreview();
                SetStatus("Cannot select ROI: " + exception.Message);
            }
        }

        private void ShowMouseRoiPreview(RoiRectangle roi, string prefix)
        {
            var localX = showsFullFrameContext ? roi.X : roi.X - frame.Configuration.OriginX;
            var localY = showsFullFrameContext ? roi.Y : roi.Y - frame.Configuration.OriginY;
            mouseRoiRectangle.Width = Math.Max(1, roi.Width * zoom - 2);
            mouseRoiRectangle.Height = Math.Max(1, roi.Height * zoom - 2);
            Canvas.SetLeft(mouseRoiRectangle, localX * zoom + 1);
            Canvas.SetTop(mouseRoiRectangle, localY * zoom + 1);
            mouseRoiRectangle.Visibility = Visibility.Visible;
            SetStatus(String.Format(CultureInfo.InvariantCulture,
                "{0}  x={1}..{2}, y={3}..{4}  ({5} x {6}). Release to set Kernel; Esc cancels.",
                prefix, roi.X, roi.X + roi.Width - 1, roi.Y, roi.Y + roi.Height - 1, roi.Width, roi.Height));
        }

        private void EndMouseRoiPreview()
        {
            canvas.ReleaseMouseCapture();
            canvas.Cursor = null;
            mouseRoiRectangle.Visibility = Visibility.Collapsed;
        }

        private void CommitMouseRoi(RoiRectangle roi)
        {
            kernelCenterX = roi.CenterX;
            kernelCenterY = roi.CenterY;
            roiWidth.Text = roi.Width.ToString(CultureInfo.InvariantCulture);
            roiHeight.Text = roi.Height.ToString(CultureInfo.InvariantCulture);
            UpdateNavigator();
            UpdateRoiRectangle();
            SaveCurrentProfile();
            SetStatus("Kernel set to " + roi.Width.ToString(CultureInfo.InvariantCulture) + "x" + roi.Height.ToString(CultureInfo.InvariantCulture) +
                " at (" + roi.CenterX.ToString(CultureInfo.InvariantCulture) + ", " + roi.CenterY.ToString(CultureInfo.InvariantCulture) + "). The visible view was not reread.");
        }

        private bool TryGetGlobalImageCoordinate(Point point, out int globalX, out int globalY)
        {
            globalX = 0;
            globalY = 0;
            if (frame == null || zoom <= 0)
            {
                return false;
            }

            var canvasX = (int)Math.Floor(point.X / zoom);
            var canvasY = (int)Math.Floor(point.Y / zoom);
            var localX = canvasX - (showsFullFrameContext ? frame.Configuration.OriginX : 0);
            var localY = canvasY - (showsFullFrameContext ? frame.Configuration.OriginY : 0);
            if (localX < 0 || localY < 0 || localX >= frame.Configuration.Width || localY >= frame.Configuration.Height)
            {
                return false;
            }

            globalX = frame.Configuration.OriginX + localX;
            globalY = frame.Configuration.OriginY + localY;
            return true;
        }

        private bool TryGetGlobalCanvasCoordinate(Point point, out int globalX, out int globalY)
        {
            if (!showsFullFrameContext)
            {
                return TryGetGlobalImageCoordinate(point, out globalX, out globalY);
            }

            globalX = (int)Math.Floor(point.X / zoom);
            globalY = (int)Math.Floor(point.Y / zoom);
            return globalX >= 0 && globalY >= 0 && globalX < fullFrameWidth && globalY < fullFrameHeight;
        }

        private void PreviewRoiPan(Point point)
        {
            scrollViewer.ScrollToHorizontalOffset(Math.Max(0, roiPanStartHorizontalOffset - (point.X - roiPanStart.X)));
            scrollViewer.ScrollToVerticalOffset(Math.Max(0, roiPanStartVerticalOffset - (point.Y - roiPanStart.Y)));
        }

        private void FinishRoiPan(Point point)
        {
            PreviewRoiPan(point);
            isRoiPanning = false;
            canvas.ReleaseMouseCapture();
            canvas.Cursor = null;
            SetStatus("Moved the cached view. No debugger memory was read.");
        }

        private void ScrollViewerChanged(object sender, ScrollChangedEventArgs e)
        {
            ScheduleViewportOverlayUpdate();
        }

        private void ScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
        {
            ScheduleViewportOverlayUpdate();
        }

        private void ScheduleViewportOverlayUpdate()
        {
            if (frame == null)
            {
                return;
            }

            viewportOverlayTimer.Stop();
            viewportOverlayTimer.Start();
        }

        private void ViewportOverlayTimerTick(object sender, EventArgs e)
        {
            viewportOverlayTimer.Stop();
            UpdateViewportAndOverlay();
        }

        private void JumpToCoordinate(object sender, RoutedEventArgs e)
        {
            try
            {
                var configuration = ReadConfiguration();
                var selection = ResolveCurrentSelection(configuration);
                MoveViewTo(selection.X, selection.Y, true);
            }
            catch (Exception exception)
            {
                SetInputError("Cannot center view: " + exception.Message);
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

        private void ViewerPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (frame == null || e.OriginalSource is TextBox || e.OriginalSource is ComboBox || e.OriginalSource is ListBox)
            {
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.C)
            {
                CopyKernelToClipboard(null, null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && (isFixedMouseRoiSelecting || isMouseRoiSelecting))
            {
                isFixedMouseRoiSelecting = false;
                isMouseRoiSelecting = false;
                EndMouseRoiPreview();
                SetStatus("ROI selection canceled.");
            }
            else if (e.Key == Key.Left)
            {
                PanRoi(-1, 0);
            }
            else if (e.Key == Key.Right)
            {
                PanRoi(1, 0);
            }
            else if (e.Key == Key.Up)
            {
                PanRoi(0, -1);
            }
            else if (e.Key == Key.Down)
            {
                PanRoi(0, 1);
            }
            else if (e.Key == Key.Add || e.Key == Key.OemPlus || e.Key == Key.PageUp)
            {
                ZoomAroundSelection(1.35);
            }
            else if (e.Key == Key.Subtract || e.Key == Key.OemMinus || e.Key == Key.PageDown)
            {
                ZoomAroundSelection(1.0 / 1.35);
            }
            else if (e.Key == Key.Home)
            {
                CenterOnSelection();
            }
            else
            {
                return;
            }

            e.Handled = true;
        }

        private void PanRoi(int horizontalDirection, int verticalDirection)
        {
            var horizontalStep = Math.Max(32.0, scrollViewer.ViewportWidth * 0.7);
            var verticalStep = Math.Max(32.0, scrollViewer.ViewportHeight * 0.7);
            scrollViewer.ScrollToHorizontalOffset(Math.Max(0, scrollViewer.HorizontalOffset + horizontalDirection * horizontalStep));
            scrollViewer.ScrollToVerticalOffset(Math.Max(0, scrollViewer.VerticalOffset + verticalDirection * verticalStep));
            SetStatus("Moved the view. No debugger memory was read.");
        }
    }
}
