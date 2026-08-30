using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using ArrayImageViewer.Core;
using ArrayImageViewer.Debugging;

namespace ArrayImageViewer.UI
{
    internal sealed partial class SensorImageViewerControl
    {
        private object hardwareWatchBreakpoint;
        private string hardwareWatchExpression;
        private string hardwareWatchAddressExpression;
        private int hardwareWatchX = -1;
        private int hardwareWatchY = -1;
        private int hardwareWatchHitCount = -1;
        private bool hardwareWatchRunning;
        private readonly List<object> retiredHardwareWatchBreakpoints = new List<object>();
        private DispatcherTimer hardwareWatchReleaseTimer;
        private FrameConfiguration pendingHardwareWatchConfiguration;
        private int pendingHardwareWatchX;
        private int pendingHardwareWatchY;
        private string pendingHardwareWatchAdvanceLabel;
        private int pendingHardwareWatchReleaseAttempts;
        private string lastHardwareWatchReleaseError;

        private void WatchSelectedPixelAndContinue(object sender, RoutedEventArgs e)
        {
            Dispatcher.VerifyAccess();
            try
            {
                var configuration = ReadConfiguration();
                var selection = ResolveCurrentSelection(configuration);
                StartHardwareWatchAndContinue(configuration, selection.X, selection.Y, null);
            }
            catch (Exception exception)
            {
                hardwareWatchRunning = false;
                SetInputError("Cannot start hardware watch: " + exception.Message);
            }
        }

        private void WatchNextPixelAndContinue(object sender, RoutedEventArgs e)
        {
            Dispatcher.VerifyAccess();
            try
            {
                var configuration = ReadConfiguration();
                var selection = ResolveCurrentSelection(configuration);
                var nextX = selection.X + 1;
                var nextY = selection.Y;
                if (nextX >= configuration.Width)
                {
                    nextX = 0;
                    nextY++;
                }
                if (nextY >= configuration.Height)
                {
                    throw new InvalidOperationException("The selected pixel is the last sample in the frame; there is no next pixel to watch.");
                }

                if (StartHardwareWatchAndContinue(configuration, nextX, nextY, "X"))
                {
                    UpdateWatchSelectionWithoutChangingView(nextX, nextY);
                }
            }
            catch (Exception exception)
            {
                hardwareWatchRunning = false;
                SetInputError("Cannot start next-X hardware watch: " + exception.Message);
            }
        }

        private void WatchNextLineAndContinue(object sender, RoutedEventArgs e)
        {
            Dispatcher.VerifyAccess();
            try
            {
                var configuration = ReadConfiguration();
                var selection = ResolveCurrentSelection(configuration);
                var nextX = selection.X;
                var nextY = selection.Y + 1;
                if (nextY >= configuration.Height)
                {
                    throw new InvalidOperationException("The selected pixel is on the final image row; there is no next Y sample to watch.");
                }

                if (StartHardwareWatchAndContinue(configuration, nextX, nextY, "Y"))
                {
                    UpdateWatchSelectionWithoutChangingView(nextX, nextY);
                }
            }
            catch (Exception exception)
            {
                hardwareWatchRunning = false;
                SetInputError("Cannot start next-Y hardware watch: " + exception.Message);
            }
        }

        private bool StartHardwareWatchAndContinue(FrameConfiguration configuration, int x, int y, string advanceLabel)
        {
            Dispatcher.VerifyAccess();
            var isNextPixel = !String.IsNullOrEmpty(advanceLabel);
            var sourceExpression = expression.Text == null ? String.Empty : expression.Text.Trim();
            if (String.IsNullOrWhiteSpace(sourceExpression))
            {
                throw new InvalidOperationException("Choose a pointer expression before starting a hardware watch.");
            }
            // Do not give a native data breakpoint a stack-frame expression
            // such as (&((this)->output))->m_data. The native engine can bind
            // it lazily after Continue, when that expression may no longer be
            // evaluated in the capture frame. Resolve the base exactly once
            // while stopped and register a literal byte address instead.
            var breakpointBaseAddress = DebugExpressionFrameReader.ResolvePointerExpressionToAddress(sourceExpression);
            ulong parsedBaseAddress;
            if (!DataWatchExpression.TryParsePointerAddress(breakpointBaseAddress, out parsedBaseAddress))
            {
                throw new InvalidOperationException("The debugger did not return a usable pointer address for the selected RAW expression.");
            }
            var addressExpression = DataWatchExpression.BuildByteAddress(parsedBaseAddress,
                configuration.ElementSizeInBytes, configuration.Stride, x, y);
            var isSameWatch = hardwareWatchBreakpoint != null &&
                String.Equals(hardwareWatchAddressExpression, addressExpression, StringComparison.Ordinal) &&
                hardwareWatchX == x && hardwareWatchY == y;

            if (!isSameWatch)
            {
                CleanupRetiredHardwareWatches();
                ClearHardwareWatch(false);
                CleanupRetiredHardwareWatches();
                if (retiredHardwareWatchBreakpoints.Count > 0)
                {
                    QueueHardwareWatchAfterRelease(configuration, x, y, advanceLabel);
                    return false;
                }
                var dataExpression = addressExpression;
                hardwareWatchBreakpoint = DebugExpressionFrameReader.AddNativeDataBreakpoint(dataExpression,
                    configuration.ElementSizeInBytes);
                hardwareWatchExpression = dataExpression;
                hardwareWatchAddressExpression = addressExpression;
                hardwareWatchX = x;
                hardwareWatchY = y;
                int hitCount;
                hardwareWatchHitCount = DebugExpressionFrameReader.TryGetBreakpointHitCount(hardwareWatchBreakpoint, out hitCount) ? hitCount : -1;
                hardwareWatchInfo.Text = "Watching pixel (" + x.ToString(CultureInfo.InvariantCulture) + ", " +
                    y.ToString(CultureInfo.InvariantCulture) + ") at " + addressExpression + " x " +
                    configuration.ElementSizeInBytes.ToString(CultureInfo.InvariantCulture) +
                    " byte native data breakpoint armed.";
            }

            hardwareWatchRunning = true;
            try
            {
                DebugExpressionFrameReader.ContinueDebuggee();
            }
            catch
            {
                // Do not leave a new data breakpoint behind if Continue could
                // not start the debuggee after arming it.
                if (!isSameWatch)
                {
                    ClearHardwareWatch(false);
                }
                throw;
            }
            SetStatus(isSameWatch
                ? "Hardware watch is already armed for pixel (" + x.ToString(CultureInfo.InvariantCulture) + ", " + y.ToString(CultureInfo.InvariantCulture) + "). Continuing until its next write."
                : (isNextPixel ? "Watch Next " + advanceLabel + " armed for pixel (" : "Watch armed for pixel (") +
                    x.ToString(CultureInfo.InvariantCulture) + ", " + y.ToString(CultureInfo.InvariantCulture) + "). Running until that sample is written.");
            return true;
        }

        private void ClearHardwareWatch(object sender, RoutedEventArgs e)
        {
            CancelPendingHardwareWatch();
            ClearHardwareWatch(true);
        }

        private void ClearHardwareWatch(bool reportStatus)
        {
            Dispatcher.VerifyAccess();
            string failureReason = null;
            var removed = hardwareWatchBreakpoint == null || DebugExpressionFrameReader.DeleteBreakpoint(hardwareWatchBreakpoint, out failureReason);
            if (!removed)
            {
                lastHardwareWatchReleaseError = failureReason;
            }
            if (!removed)
            {
                // The native engine sometimes publishes a data breakpoint
                // only after it resumes. Keep the reference and retry before
                // the next Watch command instead of losing a hardware slot.
                retiredHardwareWatchBreakpoints.Add(hardwareWatchBreakpoint);
            }
            hardwareWatchBreakpoint = null;
            hardwareWatchExpression = null;
            hardwareWatchAddressExpression = null;
            hardwareWatchX = -1;
            hardwareWatchY = -1;
            hardwareWatchHitCount = -1;
            hardwareWatchRunning = false;
            hardwareWatchInfo.Text = "No hardware watch armed.";
            if (reportStatus)
            {
                SetStatus(removed
                    ? "Removed the viewer's native data breakpoint."
                    : "Visual Studio is still releasing the native data breakpoint; another watch will not be added until it is gone. " + lastHardwareWatchReleaseError);
            }
        }

        private void CleanupRetiredHardwareWatches()
        {
            for (var index = retiredHardwareWatchBreakpoints.Count - 1; index >= 0; index--)
            {
                string failureReason;
                if (DebugExpressionFrameReader.DeleteBreakpoint(retiredHardwareWatchBreakpoints[index], out failureReason))
                {
                    retiredHardwareWatchBreakpoints.RemoveAt(index);
                }
                else
                {
                    lastHardwareWatchReleaseError = failureReason;
                }
            }
        }

        private void QueueHardwareWatchAfterRelease(FrameConfiguration configuration, int x, int y, string advanceLabel)
        {
            pendingHardwareWatchConfiguration = configuration;
            pendingHardwareWatchX = x;
            pendingHardwareWatchY = y;
            pendingHardwareWatchAdvanceLabel = advanceLabel;
            pendingHardwareWatchReleaseAttempts = 0;
            if (hardwareWatchReleaseTimer == null)
            {
                hardwareWatchReleaseTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
                hardwareWatchReleaseTimer.Interval = TimeSpan.FromMilliseconds(100);
                hardwareWatchReleaseTimer.Tick += HardwareWatchReleaseTimerTick;
            }
            hardwareWatchReleaseTimer.Start();
            hardwareWatchInfo.Text = "Releasing the previous native data breakpoint...";
            SetStatus((String.IsNullOrEmpty(advanceLabel) ? "Watch" : "Watch Next " + advanceLabel) +
                " is queued and will arm automatically as soon as Visual Studio releases the previous data breakpoint.");
        }

        private void HardwareWatchReleaseTimerTick(object sender, EventArgs e)
        {
            Dispatcher.VerifyAccess();
            CleanupRetiredHardwareWatches();
            if (retiredHardwareWatchBreakpoints.Count > 0)
            {
                pendingHardwareWatchReleaseAttempts++;
                if (pendingHardwareWatchReleaseAttempts < 30)
                {
                    return;
                }

                CancelPendingHardwareWatch();
                SetInputError("Visual Studio did not release the previous native data breakpoint. No new watch was added. " + lastHardwareWatchReleaseError);
                return;
            }

            var configuration = pendingHardwareWatchConfiguration;
            var x = pendingHardwareWatchX;
            var y = pendingHardwareWatchY;
            var advanceLabel = pendingHardwareWatchAdvanceLabel;
            CancelPendingHardwareWatch();
            try
            {
                if (StartHardwareWatchAndContinue(configuration, x, y, advanceLabel) && !String.IsNullOrEmpty(advanceLabel))
                {
                    UpdateWatchSelectionWithoutChangingView(x, y);
                }
            }
            catch (Exception exception)
            {
                hardwareWatchRunning = false;
                SetInputError("Cannot arm the queued hardware watch: " + exception.Message);
            }
        }

        private void CancelPendingHardwareWatch()
        {
            if (hardwareWatchReleaseTimer != null)
            {
                hardwareWatchReleaseTimer.Stop();
            }
            pendingHardwareWatchConfiguration = null;
            pendingHardwareWatchAdvanceLabel = null;
            pendingHardwareWatchReleaseAttempts = 0;
        }

        // Watch next advances the debugger target, not the rendered View ROI
        // or the kernel rectangle. Keep this deliberately narrower than
        // UpdateSelection: that method refreshes navigator/overlay state and
        // can make a large zoomed view appear to pan when its layout updates.
        private void UpdateWatchSelectionWithoutChangingView(int x, int y)
        {
            currentX = x;
            currentY = y;
            kernelCenterX = x;
            kernelCenterY = y;
            UpdateSelectedCellRectangle();
            UpdateRoiRectangle();

            int ignored;
            if (Int32.TryParse(selectedX.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ignored))
            {
                selectedX.Text = x.ToString(CultureInfo.InvariantCulture);
            }
            if (Int32.TryParse(selectedY.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ignored))
            {
                selectedY.Text = y.ToString(CultureInfo.InvariantCulture);
            }
            UpdateNavigator();
        }

        // Uses the debugger's last-hit breakpoint rather than a generic break
        // notification. Other source breakpoints must leave this one-shot watch
        // armed, while its own hit can be removed safely.
        private bool HardwareWatchReturnedToBreakMode()
        {
            Dispatcher.VerifyAccess();
            if (hardwareWatchBreakpoint == null)
            {
                return false;
            }

            var wasRunning = hardwareWatchRunning;
            hardwareWatchRunning = false;
            var didHit = DebugExpressionFrameReader.WasBreakpointLastHit(hardwareWatchBreakpoint);
            int currentHitCount;
            var hasCurrentHitCount = DebugExpressionFrameReader.TryGetBreakpointHitCount(hardwareWatchBreakpoint, out currentHitCount);
            if (!didHit && hardwareWatchHitCount >= 0 && hasCurrentHitCount &&
                currentHitCount > hardwareWatchHitCount)
            {
                // Some engines do not expose BreakpointLastHit for a native
                // data breakpoint. HitCount is a second, engine-independent
                // signal that this one-shot watch fired.
                didHit = true;
            }
            if (didHit && hasCurrentHitCount)
            {
                // Keep armed must compare the next stop with this hit rather
                // than treating the same already-observed hit as new again.
                hardwareWatchHitCount = currentHitCount;
            }

            if (didHit)
            {
                var watchedX = hardwareWatchX;
                var watchedY = hardwareWatchY;
                if (keepHardwareWatchArmed.IsChecked == true)
                {
                    hardwareWatchInfo.Text = "Stopped: pixel (" + watchedX.ToString(CultureInfo.InvariantCulture) + ", " +
                        watchedY.ToString(CultureInfo.InvariantCulture) + ") changed. The watch remains armed for its next change.";
                }
                else
                {
                    ClearHardwareWatch(false);
                    hardwareWatchInfo.Text = "Stopped: pixel (" + watchedX.ToString(CultureInfo.InvariantCulture) + ", " +
                        watchedY.ToString(CultureInfo.InvariantCulture) + ") changed. The one-shot watch was removed.";
                }
            }
            else if (wasRunning)
            {
                hardwareWatchInfo.Text = "Debugger paused before the watched pixel changed; the hardware watch remains armed.";
            }
            return didHit;
        }

        internal void DebuggerSessionEnded()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(DebuggerSessionEnded));
                return;
            }
            CancelPendingHardwareWatch();
            InvalidateStructureSearchCache();
            ClearHardwareWatch(false);
            // Ending the debug session discards all native data breakpoints,
            // including references that were waiting for publication.
            retiredHardwareWatchBreakpoints.Clear();
        }
    }
}
