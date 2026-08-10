using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ArrayImageViewer.Core;

namespace ArrayImageViewer.UI
{
    internal sealed partial class SensorImageViewerControl
    {
        private void UpdateSelection(int x, int y, bool centerViewport)
        {
            if (fullFrameWidth <= 0 || fullFrameHeight <= 0 || x < 0 || y < 0 || x >= fullFrameWidth || y >= fullFrameHeight)
            {
                SetStatus("The requested coordinate is outside the full frame.");
                return;
            }

            currentX = x;
            currentY = y;
            // Kernel is an inspection window around the active sample. This
            // avoids a stale orange rectangle after clicks, Go To, or Watch
            // next. Its size remains independently configurable.
            kernelCenterX = x;
            kernelCenterY = y;
            UpdateSelectedCellRectangle();
            // Keep local expressions such as centerX/centerY intact. A click
            // changes the in-view cursor, not the user's coordinate source.
            int literal;
            if (Int32.TryParse(selectedX.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out literal))
            {
                selectedX.Text = x.ToString(CultureInfo.InvariantCulture);
            }
            if (Int32.TryParse(selectedY.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out literal))
            {
                selectedY.Text = y.ToString(CultureInfo.InvariantCulture);
            }
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
                SetStatus("Pixel (" + x + ", " + y + ") is outside the loaded context. Select ROI + context to inspect it.");
            }
        }

        private void ApplyZoom(double newZoom)
        {
            zoom = newZoom;
            if (frame == null)
            {
                return;
            }

            virtualCanvasPaddingX = showsFullFrameContext ? GetVirtualCanvasPadding(renderWidth.Text, roiWidth.Text, scrollViewer.ViewportWidth) : 0;
            virtualCanvasPaddingY = showsFullFrameContext ? GetVirtualCanvasPadding(renderHeight.Text, roiHeight.Text, scrollViewer.ViewportHeight) : 0;
            var canvasWidth = showsFullFrameContext ? fullFrameWidth + 2 * virtualCanvasPaddingX : frame.Configuration.Width;
            var canvasHeight = showsFullFrameContext ? fullFrameHeight + 2 * virtualCanvasPaddingY : frame.Configuration.Height;
            canvas.Width = canvasWidth * zoom;
            canvas.Height = canvasHeight * zoom;
            unloadedFrame.Width = canvas.Width;
            unloadedFrame.Height = canvas.Height;
            unloadedFrame.Visibility = showsFullFrameContext ? Visibility.Visible : Visibility.Collapsed;
            image.Width = frame.Configuration.Width * zoom;
            image.Height = frame.Configuration.Height * zoom;
            Canvas.SetLeft(image, (showsFullFrameContext ? frame.Configuration.OriginX + virtualCanvasPaddingX : 0) * zoom);
            Canvas.SetTop(image, (showsFullFrameContext ? frame.Configuration.OriginY + virtualCanvasPaddingY : 0) * zoom);
            RenderOptions.SetBitmapScalingMode(image, zoom >= 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.Fant);
            UpdateSelectedCellRectangle();
            UpdateViewportAndOverlay();
            if (IsLoadedCoordinate(currentX, currentY))
            {
                SetStatus(Describe(currentX, currentY));
            }
        }

        // Debugger memory reads stay inside the physical sensor.  The canvas
        // has a separate sample-space margin so an edge pixel can still be
        // centered, with unavailable space painted by UnloadedFrameBrush.
        private int GetVirtualCanvasPadding(string viewSizeText, string kernelSizeText, double viewportExtent)
        {
            var viewHalf = (Math.Max(1, ParseNavigatorValue(viewSizeText, 1)) + 1) / 2;
            var kernelHalf = (Math.Max(1, ParseNavigatorValue(kernelSizeText, 1)) + 1) / 2;
            var viewportHalf = zoom > 0 && viewportExtent > 0
                ? (int)Math.Ceiling(viewportExtent / (2.0 * zoom))
                : 0;
            return Math.Max(viewHalf, Math.Max(kernelHalf, viewportHalf));
        }

        private void ZoomAroundPoint(Point pointInCanvas, Point pointInViewport, double requestedZoom)
        {
            if (frame == null || zoom <= 0)
            {
                return;
            }

            var localX = pointInCanvas.X / zoom;
            var localY = pointInCanvas.Y / zoom;
            ApplyZoom(Math.Max(0.05, Math.Min(64.0, requestedZoom)));
            scrollViewer.ScrollToHorizontalOffset(Math.Max(0, localX * zoom - pointInViewport.X));
            scrollViewer.ScrollToVerticalOffset(Math.Max(0, localY * zoom - pointInViewport.Y));
        }

        private void ZoomAroundSelection(double factor)
        {
            if (frame == null)
            {
                return;
            }

            var canvasX = (showsFullFrameContext ? currentX + virtualCanvasPaddingX : currentX - frame.Configuration.OriginX) + 0.5;
            var canvasY = (showsFullFrameContext ? currentY + virtualCanvasPaddingY : currentY - frame.Configuration.OriginY) + 0.5;
            ZoomAroundPoint(new Point(canvasX * zoom, canvasY * zoom),
                new Point(scrollViewer.ViewportWidth / 2.0, scrollViewer.ViewportHeight / 2.0), zoom * factor);
        }

        private void UpdateViewportAndOverlay()
        {
            if (frame == null)
            {
                return;
            }

            var visibleWidth = showsFullFrameContext ? fullFrameWidth + 2 * virtualCanvasPaddingX : frame.Configuration.Width;
            var visibleHeight = showsFullFrameContext ? fullFrameHeight + 2 * virtualCanvasPaddingY : frame.Configuration.Height;
            var firstCanvasX = Math.Max(0, (int)Math.Floor(scrollViewer.HorizontalOffset / zoom));
            var firstCanvasY = Math.Max(0, (int)Math.Floor(scrollViewer.VerticalOffset / zoom));
            var lastCanvasX = Math.Min(visibleWidth - 1, (int)Math.Ceiling((scrollViewer.HorizontalOffset + scrollViewer.ViewportWidth) / zoom));
            var lastCanvasY = Math.Min(visibleHeight - 1, (int)Math.Ceiling((scrollViewer.VerticalOffset + scrollViewer.ViewportHeight) / zoom));
            var firstGlobalX = firstCanvasX - (showsFullFrameContext ? virtualCanvasPaddingX : 0);
            var firstGlobalY = firstCanvasY - (showsFullFrameContext ? virtualCanvasPaddingY : 0);
            var lastGlobalX = lastCanvasX - (showsFullFrameContext ? virtualCanvasPaddingX : 0);
            var lastGlobalY = lastCanvasY - (showsFullFrameContext ? virtualCanvasPaddingY : 0);
            var firstX = Math.Max(0, firstGlobalX - (showsFullFrameContext ? frame.Configuration.OriginX : 0));
            var firstY = Math.Max(0, firstGlobalY - (showsFullFrameContext ? frame.Configuration.OriginY : 0));
            var lastX = Math.Min(frame.Configuration.Width - 1, lastGlobalX - (showsFullFrameContext ? frame.Configuration.OriginX : 0));
            var lastY = Math.Min(frame.Configuration.Height - 1, lastGlobalY - (showsFullFrameContext ? frame.Configuration.OriginY : 0));
            viewport.Text = String.Format(CultureInfo.InvariantCulture, "X lim [{0}, {1}]  Y lim [{2}, {3}]  |  {4:0.##}x",
                firstGlobalX, lastGlobalX, firstGlobalY, lastGlobalY, zoom);
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
            Canvas.SetLeft(cell, (showsFullFrameContext ? frame.Configuration.OriginX + x + virtualCanvasPaddingX : x) * zoom);
            Canvas.SetTop(cell, (showsFullFrameContext ? frame.Configuration.OriginY + y + virtualCanvasPaddingY : y) * zoom);
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

            var requestedWidth = Math.Max(1, ParseNavigatorValue(roiWidth.Text, 1));
            var requestedHeight = Math.Max(1, ParseNavigatorValue(roiHeight.Text, 1));
            // A display rectangle retains its requested centre at an edge.
            // Its off-frame part is shown over the unloaded canvas instead of
            // shifting the kernel rectangle away from the selected coordinate.
            var selectedRoi = RoiGeometry.CenteredUnclipped(
                kernelCenterX >= 0 ? kernelCenterX : currentX, kernelCenterY >= 0 ? kernelCenterY : currentY,
                requestedWidth, requestedHeight);
            var widthInSamples = selectedRoi.Width;
            var heightInSamples = selectedRoi.Height;
            var localX = (showsFullFrameContext ? selectedRoi.X + virtualCanvasPaddingX : selectedRoi.X - frame.Configuration.OriginX);
            var localY = (showsFullFrameContext ? selectedRoi.Y + virtualCanvasPaddingY : selectedRoi.Y - frame.Configuration.OriginY);

            // The kernel can deliberately sit outside the loaded view. In that
            // case keep it visible in the frame overview but do not draw a
            // misleading clipped rectangle over unrelated pixels.
            if (!showsFullFrameContext && (localX + widthInSamples <= 0 || localY + heightInSamples <= 0 ||
                localX >= frame.Configuration.Width || localY >= frame.Configuration.Height))
            {
                roiRectangle.Visibility = Visibility.Collapsed;
                return;
            }

            roiRectangle.Width = Math.Max(1, widthInSamples * zoom - 2);
            roiRectangle.Height = Math.Max(1, heightInSamples * zoom - 2);
            Canvas.SetLeft(roiRectangle, localX * zoom + 1);
            Canvas.SetTop(roiRectangle, localY * zoom + 1);
            roiRectangle.Visibility = Visibility.Visible;
        }

        private void UpdateSelectedCellRectangle()
        {
            if (frame == null || !IsLoadedCoordinate(currentX, currentY) || zoom < 12)
            {
                selectedCellRectangle.Visibility = Visibility.Collapsed;
                return;
            }

            var localX = showsFullFrameContext ? currentX + virtualCanvasPaddingX : currentX - frame.Configuration.OriginX;
            var localY = showsFullFrameContext ? currentY + virtualCanvasPaddingY : currentY - frame.Configuration.OriginY;
            selectedCellRectangle.Width = Math.Max(1, zoom);
            selectedCellRectangle.Height = Math.Max(1, zoom);
            Canvas.SetLeft(selectedCellRectangle, localX * zoom);
            Canvas.SetTop(selectedCellRectangle, localY * zoom);
            selectedCellRectangle.Visibility = Visibility.Visible;
        }

        private void CenterOnSelection()
        {
            CenterOnCoordinate(currentX, currentY);
        }

        private void CenterOnCoordinate(int x, int y)
        {
            if (!IsLoadedCoordinate(x, y))
            {
                return;
            }

            var canvasX = showsFullFrameContext ? x + virtualCanvasPaddingX : x - frame.Configuration.OriginX;
            var canvasY = showsFullFrameContext ? y + virtualCanvasPaddingY : y - frame.Configuration.OriginY;
            scrollViewer.ScrollToHorizontalOffset(Math.Max(0, (canvasX + 0.5) * zoom - scrollViewer.ViewportWidth / 2));
            scrollViewer.ScrollToVerticalOffset(Math.Max(0, (canvasY + 0.5) * zoom - scrollViewer.ViewportHeight / 2));
        }

        private void CenterOnView()
        {
            if (frame == null || !IsLoadedCoordinate(viewCenterX, viewCenterY))
            {
                return;
            }

            var canvasX = showsFullFrameContext ? viewCenterX + virtualCanvasPaddingX : viewCenterX - frame.Configuration.OriginX;
            var canvasY = showsFullFrameContext ? viewCenterY + virtualCanvasPaddingY : viewCenterY - frame.Configuration.OriginY;
            scrollViewer.ScrollToHorizontalOffset(Math.Max(0, (canvasX + 0.5) * zoom - scrollViewer.ViewportWidth / 2));
            scrollViewer.ScrollToVerticalOffset(Math.Max(0, (canvasY + 0.5) * zoom - scrollViewer.ViewportHeight / 2));
        }

        private string Describe(int x, int y)
        {
            var localX = x - frame.Configuration.OriginX;
            var localY = y - frame.Configuration.OriginY;
            var raw = frame.GetRaw(localX, localY);
            var channel = frame.Configuration.VisualizeChannel == VisualizeChannel.Gray
                ? "Gray"
                : frame.Configuration.GetBayerSite(localX, localY).ToString();
            return String.Format(CultureInfo.InvariantCulture,
                "Pixel ({0}, {1})   RAW {2}   Q {3} ({4}, display raw {5}..{6})   Channel {7}   Read {8}",
                x, y, raw, QFormat.Format(raw, frame.Configuration.FractionalBits),
                QFormat.FormatSpecification(frame.Configuration.IntegerBits, frame.Configuration.FractionalBits),
                QFormat.Format(activeNormalization.Minimum, frame.Configuration.FractionalBits),
                QFormat.Format(activeNormalization.Maximum, frame.Configuration.FractionalBits), channel, lastReadPath);
        }

        private void SetStatus(string text)
        {
            status.Foreground = TextBrush;
            status.Text = text;
        }

        private void SetInputError(string message)
        {
            TextBox target = null;
            var lower = message == null ? String.Empty : message.ToLowerInvariant();
            if (lower.Contains("profile set") || lower.Contains("enter a name")) target = profileSetName;
            else if (lower.Contains("view height")) target = renderHeight;
            else if (lower.Contains("view width") || lower.Contains("view")) target = renderWidth;
            else if (lower.Contains("kernel height") || lower.Contains("roi h")) target = roiHeight;
            else if (lower.Contains("kernel width") || lower.Contains("roi w") || lower.Contains("kernel")) target = roiWidth;
            else if (lower.Contains("selected x") || lower.Contains("roi center x")) target = selectedX;
            else if (lower.Contains("selected y") || lower.Contains("roi center y")) target = selectedY;
            else if (lower.Contains("stride")) target = stride;
            else if (lower.Contains("width")) target = width;
            else if (lower.Contains("height")) target = height;
            else if (lower.Contains("q format")) target = qFormat;
            else if (lower.Contains("raw maximum")) target = normalizationMaximum;
            else if (lower.Contains("raw minimum") || lower.Contains("normalization") || lower.Contains("raw range")) target = normalizationMinimum;
            else if (lower.Contains("pointer") || lower.Contains("expression")) target = expression;

            if (target != null)
            {
                ClearAllInputErrors();
                invalidInput = target;
                target.BorderBrush = ErrorBrush;
                target.BorderThickness = new Thickness(2);
                target.ToolTip = message;
                // Keep the user's current editor/focus untouched. Moving
                // focus here interrupts expression typing and also makes the
                // settings ScrollViewer jump to the highlighted field.
            }

            status.Foreground = ErrorBrush;
            status.Text = message;
        }

        private void ClearInputError(TextBox changedField)
        {
            if (changedField == null || invalidInput != changedField)
            {
                return;
            }

            invalidInput.BorderBrush = PanelBorderBrush;
            invalidInput.BorderThickness = new Thickness(1);
            invalidInput.ToolTip = null;
            invalidInput = null;
        }

        private void ClearAllInputErrors()
        {
            if (invalidInput != null)
            {
                invalidInput.BorderBrush = PanelBorderBrush;
                invalidInput.BorderThickness = new Thickness(1);
                invalidInput.ToolTip = null;
                invalidInput = null;
            }
        }
    }
}
