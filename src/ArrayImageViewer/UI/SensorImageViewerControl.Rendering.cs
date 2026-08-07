using System;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ArrayImageViewer.Core;
using ArrayImageViewer.Debugging;

namespace ArrayImageViewer.UI
{
    internal sealed partial class SensorImageViewerControl
    {
        private void LoadExpression(object sender, RoutedEventArgs e)
        {
            try
            {
                ClearAllInputErrors();
                BeginNewReadRequest();
                var configuration = ReadConfiguration();
                navigatorSourceConfiguration = configuration;
                var roi = GetRoiBounds(configuration);
                var selection = ResolveCurrentSelection(configuration);
                var sampleCount = checked((long)roi.Width * roi.Height);
                DebugMemoryFrameReader.RoiReadSession memoryRead;
                if (sampleCount > 32768 && DebugExpressionFrameReader.TryStartMemoryRoiRead(expression.Text, configuration,
                    roi.X, roi.Y, roi.Width, roi.Height, out memoryRead))
                {
                    BeginMemoryRead(memoryRead, configuration.Width, configuration.Height, selection.X, selection.Y, false, false, expression.Text.Trim());
                    return;
                }

                SetStatus("Reading exact ROI " + roi.Width + "x" + roi.Height + " from the debugger.");
                var source = DebugExpressionFrameReader.ReadRoi(expression.Text, configuration, roi.X, roi.Y, roi.Width, roi.Height);
                ApplyLoadedFrame(source, configuration.Width, configuration.Height, selection.X, selection.Y, false, false, expression.Text.Trim());
            }
            catch (Exception exception)
            {
                SetInputError("Cannot read exact ROI: " + exception.Message);
            }
        }

        private void LoadContextPreview(object sender, RoutedEventArgs e)
        {
            try
            {
                ClearAllInputErrors();
                BeginNewReadRequest();
                var configuration = ReadConfiguration();
                navigatorSourceConfiguration = configuration;
                var requestedRoi = GetRoiBounds(configuration);
                var context = GetRenderBounds(configuration, requestedRoi);
                var selection = ResolveCurrentSelection(configuration);
                DebugMemoryFrameReader.RoiReadSession memoryRead;
                if (DebugExpressionFrameReader.TryStartMemoryRoiRead(expression.Text, configuration,
                    context.X, context.Y, context.Width, context.Height, out memoryRead))
                {
                    BeginMemoryRead(memoryRead, configuration.Width, configuration.Height, selection.X, selection.Y, false, true, expression.Text.Trim());
                    return;
                }

                SetStatus("Reading " + context.Width + "x" + context.Height + " context around the ROI from the debugger.");
                var source = DebugExpressionFrameReader.ReadRoi(expression.Text, configuration, context.X, context.Y, context.Width, context.Height);
                ApplyLoadedFrame(source, configuration.Width, configuration.Height, selection.X, selection.Y, false, true, expression.Text.Trim());
            }
            catch (Exception exception)
            {
                SetInputError("Cannot show ROI + context: " + exception.Message);
            }
        }

        private void LoadFullPreview(object sender, RoutedEventArgs e)
        {
            try
            {
                BeginNewReadRequest();
                var configuration = ReadConfiguration();
                navigatorSourceConfiguration = configuration;
                var fullPreviewSamples = checked((long)configuration.Width * configuration.Height);
                if (fullPreviewSamples > NormalFullPreviewSampleLimit)
                {
                    var bytes = checked(fullPreviewSamples * configuration.ElementSizeInBytes);
                    var confirmation = MessageBox.Show(
                        String.Format(CultureInfo.InvariantCulture,
                            "Full preview will read {0:N0} samples ({1:N1} MiB) from the paused debuggee and render a {2}x{3} image. Continue?",
                            fullPreviewSamples, bytes / (1024.0 * 1024.0), configuration.Width, configuration.Height),
                        "Sensor RAW Array Viewer", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                    if (confirmation != MessageBoxResult.Yes)
                    {
                        SetStatus("Full preview canceled. Load a smaller ROI to inspect cells without reading the complete frame.");
                        return;
                    }
                }

                DebugMemoryFrameReader.RoiReadSession memoryRead;
                if (!DebugExpressionFrameReader.TryStartMemoryRoiRead(expression.Text, configuration, 0, 0, configuration.Width, configuration.Height, out memoryRead))
                {
                    throw new InvalidOperationException("Full preview requires the native debugger-memory reader. Use ROI + context on this debug engine.");
                }

                var selection = ResolveCurrentSelection(configuration);
                var centerX = selection.X;
                var centerY = selection.Y;
                BeginMemoryRead(memoryRead, configuration.Width, configuration.Height, centerX, centerY, true, false, expression.Text.Trim());
            }
            catch (Exception exception)
            {
                SetInputError("Cannot read full preview: " + exception.Message);
            }
        }

        private void BeginMemoryRead(DebugMemoryFrameReader.IFrameReadSession memoryRead, int sourceWidth, int sourceHeight,
            int selectedGlobalX, int selectedGlobalY, bool isFullPreview, bool showFullFrameContext, string sourceExpression,
            string rawExportPath = null, int rawExportBits = 0, bool isNavigatorPreview = false)
        {
            pendingMemoryRead = new PendingMemoryRead(memoryRead, sourceWidth, sourceHeight, selectedGlobalX, selectedGlobalY, isFullPreview, showFullFrameContext, sourceExpression,
                rawExportPath, rawExportBits, isNavigatorPreview);
            SetStatus(isNavigatorPreview
                ? "Reading a downsampled Gray frame-map preview (0%)."
                : "Reading " + memoryRead.TotalRows.ToString(CultureInfo.InvariantCulture) + " debugger-memory rows in responsive batches (0%).");
            memoryReadTimer.Start();
        }

        private void BeginNavigatorMemoryRead(DebugMemoryFrameReader.IFrameReadSession memoryRead, int sourceWidth, int sourceHeight, string sourceExpression)
        {
            BeginMemoryRead(memoryRead, sourceWidth, sourceHeight, currentX, currentY, false, false, sourceExpression, null, 0, true);
        }

        private void MemoryReadTimerTick(object sender, EventArgs e)
        {
            var pending = pendingMemoryRead;
            if (pending == null)
            {
                memoryReadTimer.Stop();
                return;
            }

            // Limit a single UI turn to roughly 512 KiB of debuggee memory.
            // This keeps mouse/paint operations responsive for a 4096-wide frame.
            var rowsPerBatch = Math.Max(1, Math.Min(64, 131072 / Math.Max(1, pending.Session.BytesPerRow)));
            if (!pending.Session.TryReadRows(rowsPerBatch))
            {
                memoryReadTimer.Stop();
                pendingMemoryRead = null;
                SetStatus(pending.IsNavigatorPreview
                    ? "Frame-map image preview is unavailable: " + (pending.Session.ErrorMessage ?? "unknown debugger read error")
                    : "Cannot read debugger memory: " + (pending.Session.ErrorMessage ?? "unknown native debugger read error"));
                return;
            }

            if (!pending.Session.IsComplete)
            {
                var percent = pending.Session.RowsRead * 100 / Math.Max(1, pending.Session.TotalRows);
                SetStatus(pending.IsNavigatorPreview
                    ? "Reading downsampled Gray frame-map preview (" + percent.ToString(CultureInfo.InvariantCulture) + "%)."
                    : "Reading " + pending.Session.RowsRead.ToString(CultureInfo.InvariantCulture) + "/" + pending.Session.TotalRows.ToString(CultureInfo.InvariantCulture) +
                        " debugger-memory rows in responsive batches (" + percent.ToString(CultureInfo.InvariantCulture) + "%).");
                return;
            }

            memoryReadTimer.Stop();
            pendingMemoryRead = null;
            DebugExpressionFrameReader.MarkMemoryReadComplete();
            var completedFrame = pending.Session.CreateFrame();
            if (pending.IsNavigatorPreview)
            {
                ApplyNavigatorPreview(completedFrame);
                SetStatus("Updated downsampled Gray frame-map preview. View and Kernel overlays remain full-frame accurate.");
                return;
            }
            if (!String.IsNullOrWhiteSpace(pending.RawExportPath))
            {
                WriteRawFrameAsync(completedFrame, pending.RawExportPath, pending.RawExportBits);
                return;
            }

            ApplyLoadedFrame(completedFrame, pending.SourceWidth, pending.SourceHeight,
                pending.SelectedGlobalX, pending.SelectedGlobalY, pending.IsFullPreview, pending.ShowFullFrameContext, pending.SourceExpression);
        }

        private void CancelPendingMemoryRead()
        {
            if (pendingMemoryRead != null)
            {
                pendingMemoryRead = null;
                memoryReadTimer.Stop();
            }
        }

        // A memory read and a background bitmap conversion are both cancellable
        // only at their next UI handoff. Incrementing the generation makes an
        // older renderer harmless if it finishes after a newer request starts.
        private void BeginNewReadRequest()
        {
            CancelPendingMemoryRead();
            renderGeneration++;
        }

        private void CancelRead(object sender, RoutedEventArgs e)
        {
            var hadPendingRead = pendingMemoryRead != null || autoRefreshTimer.IsEnabled;
            autoRefreshTimer.Stop();
            BeginNewReadRequest();
            SetStatus(hadPendingRead
                ? "Canceled the pending debugger read. The current rendered frame remains available."
                : "No debugger read is pending.");
        }

        private void ViewerUnloaded(object sender, RoutedEventArgs e)
        {
            autoRefreshTimer.Stop();
            coordinateUpdateTimer.Stop();
            viewportOverlayTimer.Stop();
            BeginNewReadRequest();
        }

        private void ApplyLoadedFrame(FrameBuffer source, int sourceWidth, int sourceHeight, int selectedGlobalX, int selectedGlobalY,
            bool isFullPreview, bool showFullFrameContext, string sourceExpression)
        {
            var readPath = DebugExpressionFrameReader.LastRoiReadUsedMemory ? "debugger memory" : "expression fallback";
            NormalizationRange normalization;
            try
            {
                normalization = ResolveNormalizationRange(source);
            }
            catch (Exception exception)
            {
                SetInputError("Cannot apply display range: " + exception.Message);
                return;
            }

            var sampleCount = (long)source.Configuration.Width * source.Configuration.Height;
            var generation = ++renderGeneration;
            if (sampleCount <= 262144)
            {
                ApplyRenderedFrame(source, FrameRenderer.Render(source, normalization), normalization, sourceWidth, sourceHeight, selectedGlobalX, selectedGlobalY, isFullPreview, showFullFrameContext, sourceExpression, readPath);
                return;
            }

            SetStatus("Rendering " + source.Configuration.Width.ToString(CultureInfo.InvariantCulture) + "x" + source.Configuration.Height.ToString(CultureInfo.InvariantCulture) + " preview in the background. The viewer remains usable.");
            var renderThread = new Thread(delegate()
            {
                try
                {
                    var bitmap = FrameRenderer.Render(source, normalization);
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (generation == renderGeneration)
                        {
                            ApplyRenderedFrame(source, bitmap, normalization, sourceWidth, sourceHeight, selectedGlobalX, selectedGlobalY, isFullPreview, showFullFrameContext, sourceExpression, readPath);
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

        private void ApplyRenderedFrame(FrameBuffer source, ImageSource bitmap, NormalizationRange normalization, int sourceWidth, int sourceHeight, int selectedGlobalX, int selectedGlobalY,
            bool isFullPreview, bool showFullFrameContext, string sourceExpression, string readPath)
        {
            var preserveContextZoom = showFullFrameContext && showsFullFrameContext && frame != null;
            lastReadPath = readPath;
            activeNormalization = normalization;
            ApplyFrame(source, bitmap, sourceWidth, sourceHeight, selectedGlobalX, selectedGlobalY, showFullFrameContext);
            ApplyZoom(isFullPreview ? GetFullPreviewZoom(source.Configuration.Width, source.Configuration.Height) :
                (showFullFrameContext ? (preserveContextZoom ? zoom : GetContextPreviewZoom(source.Configuration.Width, source.Configuration.Height)) : Math.Max(18.0, zoom)));
            if (showFullFrameContext)
            {
                Dispatcher.BeginInvoke(new Action(CenterOnView));
            }
            EnsureProfile(sourceExpression);
            activeProfileExpression = sourceExpression;
            SaveCurrentProfile();
            RebuildProfilePicker(sourceExpression);
        }

        private NormalizationRange ResolveNormalizationRange(FrameBuffer source)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            var selectedMode = GetSelectedNormalizationMode();
            if (selectedMode == NormalizationMode.QFormatRange)
            {
                var qFormatRange = new NormalizationRange(source.Configuration.RawMinimum, source.Configuration.RawMaximum);
                UpdateNormalizationFields(qFormatRange);
                return qFormatRange;
            }

            if (selectedMode == NormalizationMode.LoadedDataRange)
            {
                var loadedDataRange = source.GetLoadedDataRange();
                UpdateNormalizationFields(loadedDataRange);
                return loadedDataRange;
            }

            return new NormalizationRange(ParseRawNormalizationValue(normalizationMinimum, "minimum"),
                ParseRawNormalizationValue(normalizationMaximum, "maximum"));
        }

        private static long ParseRawNormalizationValue(TextBox field, string label)
        {
            long value;
            if (!Int64.TryParse(field.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                throw new ArgumentException("Enter a signed raw " + label + " integer.");
            }

            return value;
        }

        private void UpdateNormalizationFields(NormalizationRange range)
        {
            normalizationMinimum.Text = range.Minimum.ToString(CultureInfo.InvariantCulture);
            normalizationMaximum.Text = range.Maximum.ToString(CultureInfo.InvariantCulture);
        }

        private void ApplyNormalization(object sender, RoutedEventArgs e)
        {
            if (frame == null)
            {
                SetStatus("Load an ROI or full preview before applying a display range.");
                return;
            }

            try
            {
                var normalization = ResolveNormalizationRange(frame);
                SaveCurrentProfile();
                RenderCachedFrame(frame, normalization);
            }
            catch (Exception exception)
            {
                SetInputError("Cannot apply display range: " + exception.Message);
            }
        }

        private void RenderCachedFrame(FrameBuffer source, NormalizationRange normalization)
        {
            var sampleCount = (long)source.Configuration.Width * source.Configuration.Height;
            var generation = ++renderGeneration;
            if (sampleCount <= 262144)
            {
                ApplyCachedRender(source, FrameRenderer.Render(source, normalization), normalization);
                return;
            }

            SetStatus("Recoloring cached " + source.Configuration.Width.ToString(CultureInfo.InvariantCulture) + "x" + source.Configuration.Height.ToString(CultureInfo.InvariantCulture) + " frame. No debugger memory is being read.");
            var renderThread = new Thread(delegate()
            {
                try
                {
                    var bitmap = FrameRenderer.Render(source, normalization);
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (generation == renderGeneration)
                        {
                            ApplyCachedRender(source, bitmap, normalization);
                        }
                    }));
                }
                catch (Exception exception)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (generation == renderGeneration)
                        {
                            SetStatus("Cannot recolor cached frame: " + exception.Message);
                        }
                    }));
                }
            });
            renderThread.IsBackground = true;
            renderThread.SetApartmentState(ApartmentState.STA);
            renderThread.Start();
        }

        private void ApplyCachedRender(FrameBuffer source, ImageSource bitmap, NormalizationRange normalization)
        {
            if (!Object.ReferenceEquals(frame, source))
            {
                return;
            }

            activeNormalization = normalization;
            image.Source = bitmap;
            UpdateSelectedCellRectangle();
            UpdateViewportAndOverlay();
            SetStatus(String.Format(CultureInfo.InvariantCulture, "Recolored cached frame with raw range {0}..{1}. No debugger memory was read.", normalization.Minimum, normalization.Maximum));
        }

        private double GetFullPreviewZoom(int imageWidth, int imageHeight)
        {
            var availableWidth = Math.Max(320.0, scrollViewer.ActualWidth - 24);
            var availableHeight = Math.Max(220.0, scrollViewer.ActualHeight - 24);
            var fit = Math.Min(availableWidth / imageWidth, availableHeight / imageHeight);
            return Math.Max(0.05, Math.Min(1.0, fit));
        }

        private double GetContextPreviewZoom(int imageWidth, int imageHeight)
        {
            var longestSide = Math.Max(imageWidth, imageHeight);
            return Math.Max(2.0, Math.Min(8.0, 640.0 / Math.Max(1, longestSide)));
        }

        private FrameConfiguration ReadConfiguration()
        {
            var parsedWidth = ResolveInteger(width, "width");
            var parsedHeight = ResolveInteger(height, "height");
            var parsedStride = ResolveInteger(stride, "stride");
            if (parsedWidth <= 0)
            {
                throw new ArgumentException("Width must be positive.");
            }
            if (parsedHeight <= 0)
            {
                throw new ArgumentException("Height must be positive.");
            }
            if (parsedStride < parsedWidth)
            {
                throw new ArgumentException("Stride must be at least Width.");
            }
            int parsedIntegerBits;
            int parsedFractionalBits;
            if (!QFormat.TryParse(qFormat.Text, out parsedIntegerBits, out parsedFractionalBits))
            {
                throw new ArgumentException("Use Q format such as 8.8b.");
            }

            return new FrameConfiguration(parsedWidth, parsedHeight, parsedStride, parsedIntegerBits, parsedFractionalBits, signed.IsChecked == true,
                (PixelOrder)pixelOrder.SelectedItem, (PixelType)pixelType.SelectedItem, (VisualizeChannel)visualizeChannel.SelectedItem, 0, 0,
                (SourceElementType)sourceElementType.SelectedItem);
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

            try
            {
                return DebugExpressionFrameReader.EvaluateInt32(field.Text.Trim());
            }
            catch (Exception exception)
            {
                throw new ArgumentException("Cannot evaluate " + fieldName + ": " + exception.Message);
            }
        }

        private RoiBounds ResolveCurrentSelection(FrameConfiguration configuration)
        {
            var x = Math.Max(0, Math.Min(configuration.Width - 1, ResolveInteger(selectedX, "selected X")));
            var y = Math.Max(0, Math.Min(configuration.Height - 1, ResolveInteger(selectedY, "selected Y")));
            currentX = x;
            currentY = y;
            return new RoiBounds(x, y, 1, 1, x, y);
        }

        private RoiBounds GetRoiBounds(FrameConfiguration configuration)
        {
            var requestedWidth = ResolveInteger(roiWidth, "ROI width");
            var requestedHeight = ResolveInteger(roiHeight, "ROI height");
            if (requestedWidth <= 0)
            {
                throw new ArgumentException("Kernel width must be positive.");
            }
            if (requestedHeight <= 0)
            {
                throw new ArgumentException("Kernel height must be positive.");
            }

            if (kernelCenterX < 0 || kernelCenterY < 0)
            {
                kernelCenterX = ResolveInteger(selectedX, "selected X");
                kernelCenterY = ResolveInteger(selectedY, "selected Y");
            }

            var roi = RoiGeometry.ClampCentered(configuration.Width, configuration.Height, kernelCenterX, kernelCenterY, requestedWidth, requestedHeight);
            kernelCenterX = roi.CenterX;
            kernelCenterY = roi.CenterY;
            UpdateNavigator();
            return new RoiBounds(roi.X, roi.Y, roi.Width, roi.Height, roi.CenterX, roi.CenterY);
        }

        private RoiBounds GetRenderBounds(FrameConfiguration configuration, RoiBounds kernel)
        {
            var requestedWidth = ResolveInteger(renderWidth, "view width");
            var requestedHeight = ResolveInteger(renderHeight, "view height");
            if (requestedWidth <= 0)
            {
                throw new ArgumentException("View width must be positive.");
            }
            if (requestedHeight <= 0)
            {
                throw new ArgumentException("View height must be positive.");
            }

            var samples = checked((long)requestedWidth * requestedHeight);
            if (samples > 262144)
            {
                throw new ArgumentException("View W x H is limited to 262,144 samples. Use Full preview for the entire frame.");
            }

            if (viewCenterX < 0 || viewCenterY < 0)
            {
                viewCenterX = currentX >= 0 ? currentX : kernel.CenterX;
                viewCenterY = currentY >= 0 ? currentY : kernel.CenterY;
            }

            var view = RoiGeometry.ClampCentered(configuration.Width, configuration.Height, viewCenterX, viewCenterY, requestedWidth, requestedHeight);
            viewCenterX = view.CenterX;
            viewCenterY = view.CenterY;
            return new RoiBounds(view.X, view.Y, view.Width, view.Height, view.CenterX, view.CenterY);
        }
    }
}
