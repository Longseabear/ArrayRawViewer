using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ArrayImageViewer.Core;
using ArrayImageViewer.Debugging;

namespace ArrayImageViewer.UI
{
    internal sealed partial class SensorImageViewerControl
    {
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
                if (!AutoFillScalar(values, strideValueSource, stride, "stride", "pitch", "rowstride", "imagepitch"))
                {
                    SetStrideFromWidth();
                }
                AutoFillScalar(values, xValueSource, selectedX, "centerx", "roi_x", "x");
                AutoFillScalar(values, yValueSource, selectedY, "centery", "roi_y", "y");
                SetStatus(values.Count == 0
                    ? "No integer locals found. Pause in the function that owns width, height, X, and Y."
                    : "Loaded " + values.Count.ToString(CultureInfo.InvariantCulture) + " numeric locals. Matching W/H/stride/X/Y fields now retain the variable expressions.");
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

            // Keep the expression rather than a one-time integer snapshot.
            // ResolveInteger evaluates it afresh on each viewer update.
            target.Text = scalar.Name;
            SetStatus("Using current-stack variable " + scalar.Name + " for " + targetName + ".");
            ScheduleAutoRefresh();
        }

        private bool AutoFillScalar(IList<DebugExpressionFrameReader.ScalarExpression> values, ComboBox source, TextBox target, params string[] preferredNames)
        {
            for (var preferredIndex = 0; preferredIndex < preferredNames.Length; preferredIndex++)
            {
                for (var valueIndex = 0; valueIndex < values.Count; valueIndex++)
                {
                    if (String.Equals(values[valueIndex].Name, preferredNames[preferredIndex], StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            // Do not replace a working literal with a name
                            // the debugger cannot evaluate (for example an
                            // optimized-away constexpr).
                            DebugExpressionFrameReader.EvaluateInt32(values[valueIndex].Name);
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        source.SelectedItem = values[valueIndex];
                        target.Text = values[valueIndex].Name;
                        return true;
                    }
                }
            }

            return false;
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
            expressionSuggestions.Closed += delegate { expressionSuggestionList.SelectedItem = null; expressionSuggestionsRequested = false; };
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
                SetStatus("Expression set from the editor selection: " + expression.Text + ". Select Capture to inspect it.");
                ScheduleAutoRefresh();
            }
            catch (Exception exception)
            {
                SetStatus("Cannot capture selection: " + exception.Message);
            }
        }

        internal void CaptureActiveEditorSelection()
        {
            CaptureSelection(null, null);
        }

        private void RefreshPointers(object sender, RoutedEventArgs e)
        {
            try
            {
                var pointers = DebugExpressionFrameReader.GetCurrentFramePointers();
                pointerCandidates.Clear();
                pointerCandidates.AddRange(pointers);
                var currentExpression = expression.Text == null ? String.Empty : expression.Text.Trim();
                isApplyingProfile = true;
                availablePointers.ItemsSource = pointers;
                availablePointers.SelectedItem = null;
                isApplyingProfile = false;
                if (pointers.Count == 0)
                {
                    SetStatus("No pointer locals found. Pause in the function that owns A or B, then refresh.");
                    return;
                }

                // A member expression such as this->D is not itself a local
                // list item when the debugger exposes only `this`. Refresh
                // must never overwrite such an expression with the first
                // pointer candidate.
                var hasActiveCandidate = false;
                for (var index = 0; index < pointers.Count; index++)
                {
                    if (String.Equals(pointers[index].Name, currentExpression, StringComparison.OrdinalIgnoreCase))
                    {
                        hasActiveCandidate = true;
                        break;
                    }
                }

                if (hasActiveCandidate)
                {
                    isApplyingProfile = true;
                    for (var index = 0; index < pointers.Count; index++)
                    {
                        if (String.Equals(pointers[index].Name, currentExpression, StringComparison.OrdinalIgnoreCase))
                        {
                            availablePointers.SelectedItem = pointers[index];
                            break;
                        }
                    }
                    isApplyingProfile = false;
                }

                UpdateExpressionSuggestions();
                SetStatus("Found " + pointers.Count.ToString(CultureInfo.InvariantCulture) + " pointer variable(s). " +
                    (hasActiveCandidate ? "The current pointer remains selected." : "Your current expression was preserved; choose a pointer from the list to replace it."));
            }
            catch (Exception exception)
            {
                SetStatus("Cannot list pointer locals: " + exception.Message);
            }
        }

        private void AvailablePointerChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isApplyingProfile)
            {
                return;
            }

            var pointer = availablePointers.SelectedItem as DebugExpressionFrameReader.PointerExpression;
            if (pointer == null)
            {
                return;
            }

            ActivatePointer(pointer);
        }

        private bool expressionSuggestionsRequested;

        private void DismissExpressionSuggestions()
        {
            expressionSuggestionsRequested = false;
            expressionSuggestions.IsOpen = false;
        }

        private void ExpressionGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // VS may restore focus here while activating the tool window.
            // Focus alone is not an editing request and must not read locals.
            DismissExpressionSuggestions();
        }

        private void DismissExpressionSuggestionsOutside(object sender, MouseButtonEventArgs e)
        {
            var target = e.OriginalSource as DependencyObject;
            if (target == null || (target != expression && !expression.IsAncestorOf(target) &&
                target != expressionSuggestionList && !expressionSuggestionList.IsAncestorOf(target)))
                DismissExpressionSuggestions();
            // Intentionally leave e.Handled false so the original button acts.
        }

        private void ExpressionLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (!expression.IsKeyboardFocusWithin && !expressionSuggestionList.IsKeyboardFocusWithin)
                    DismissExpressionSuggestions();
            }));
        }

        private void ExpressionTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!isApplyingProfile)
            {
                expressionSuggestionsRequested = expression.IsKeyboardFocusWithin;
                if (expressionSuggestionsRequested && pointerCandidates.Count == 0) RefreshPointers(null, null);
                UpdateExpressionSuggestions();
                PersistCurrentSessionIfRequested();
            }
        }

        private void ExpressionPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                expressionSuggestionsRequested = true;
                if (pointerCandidates.Count == 0) RefreshPointers(null, null);
                UpdateExpressionSuggestions();
                e.Handled = true;
                return;
            }
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
                DismissExpressionSuggestions();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                var pointer = expressionSuggestionList.SelectedItem as DebugExpressionFrameReader.PointerExpression;
                if (pointer != null)
                {
                    ActivatePointer(pointer);
                }
                else
                {
                    expressionSuggestions.IsOpen = false;
                }
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
            expressionSuggestions.IsOpen = expressionSuggestionsRequested && expression.IsKeyboardFocusWithin && matches.Count > 0;
        }

        private void ExpressionSuggestionSelected(object sender, SelectionChangedEventArgs e)
        {
            // The popup reselects items while its source is rebuilt. Applying
            // a profile here used to overwrite a typed member expression with
            // the first local, commonly `this`. Activation is explicit.
        }

        private void ExpressionSuggestionDoubleClicked(object sender, MouseButtonEventArgs e)
        {
            var pointer = expressionSuggestionList.SelectedItem as DebugExpressionFrameReader.PointerExpression;
            if (pointer != null)
            {
                ActivatePointer(pointer);
            }
        }

        private void ActivatePointer(DebugExpressionFrameReader.PointerExpression pointer)
        {
            SaveCurrentProfile();
            expressionSuggestions.IsOpen = false;
            ViewerProfile profile;
            if (profiles.TryGetValue(pointer.Name, out profile) || TryLoadPersistedProfile(pointer.Name, pointer.SourceElementType, out profile))
            {
                profiles[pointer.Name] = profile;
                ApplyProfile(profile);
            }
            else
            {
                isApplyingProfile = true;
                expression.Text = pointer.Name;
                signed.IsChecked = pointer.IsSigned;
                sourceElementType.SelectedItem = pointer.SourceElementType;
                isApplyingProfile = false;
                EnsureProfile(pointer.Name);
            }

            activeProfileExpression = pointer.Name;
            RebuildProfilePicker(pointer.Name);
            SetStatus("Selected " + pointer.Name + " as " + pointer.Type + ". Its profile is retained for this viewer window.");
            ScheduleAutoRefresh();
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
            SetStatus("Restored profile for " + profile.Expression + ". Select Capture to read its current debugger values.");
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
            SetStatus("Saved interpretation settings for " + sourceExpression + ". RAW samples were not retained.");
        }

        private void ProfileSetPickerChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isApplyingProfile)
            {
                return;
            }

            var profileSet = profileSetPicker.SelectedItem as NamedProfileSet;
            if (profileSet == null)
            {
                return;
            }

            SaveCurrentProfile();
            profileSetName.Text = profileSet.Name;
            profiles[profileSet.Profile.Expression] = profileSet.Profile;
            ApplyProfile(profileSet.Profile);
            PersistLastSession(profileSet.Profile);
            SetStatus("Restored profile set '" + profileSet.Name + "'. The current View ROI will refresh after the debugger pauses.");
            ScheduleAutoRefresh();
        }

        private void SaveProfileSet(object sender, RoutedEventArgs e)
        {
            var name = profileSetName.Text == null ? String.Empty : profileSetName.Text.Trim();
            if (name.Length == 0)
            {
                SetInputError("Enter a name before saving a profile set.");
                return;
            }

            var profile = CreateCurrentProfile();
            if (String.IsNullOrWhiteSpace(profile.Expression))
            {
                SetInputError("Choose a pointer or enter an expression before saving a profile set.");
                return;
            }

            profileSets[name] = new NamedProfileSet(name, profile);
            profiles[profile.Expression] = profile;
            activeProfileExpression = profile.Expression;
            PersistProfile(profile.Expression, profile);
            PersistProfileSet(profileSets[name]);
            PersistLastSession(profile);
            RebuildProfilePicker(profile.Expression);
            RebuildProfileSetPicker(name);
            SetStatus("Saved profile set '" + name + "' for this solution. It contains the pointer, frame, Bayer/Q, View, and Kernel settings.");
        }

        private void DeleteProfileSet(object sender, RoutedEventArgs e)
        {
            var profileSet = profileSetPicker.SelectedItem as NamedProfileSet;
            var name = profileSet == null ? (profileSetName.Text == null ? String.Empty : profileSetName.Text.Trim()) : profileSet.Name;
            if (name.Length == 0 || !profileSets.ContainsKey(name))
            {
                SetStatus("Choose a saved profile set to delete.");
                return;
            }

            profileSets.Remove(name);
            DeletePersistedProfileSet(name);
            profileSetName.Text = String.Empty;
            RebuildProfileSetPicker(null);
            SetStatus("Deleted profile set '" + name + "' from this solution. The active viewer settings were kept.");
        }

        private void ConfigureProfileAutoSave()
        {
            width.TextChanged += ProfileInputChanged;
            height.TextChanged += ProfileInputChanged;
            stride.TextChanged += ProfileInputChanged;
            qFormat.TextChanged += ProfileInputChanged;
            normalizationMinimum.TextChanged += ProfileInputChanged;
            normalizationMaximum.TextChanged += ProfileInputChanged;
            selectedX.TextChanged += ProfileInputChanged;
            selectedY.TextChanged += ProfileInputChanged;
            renderWidth.TextChanged += ProfileInputChanged;
            renderHeight.TextChanged += ProfileInputChanged;
            roiWidth.TextChanged += ProfileInputChanged;
            roiHeight.TextChanged += ProfileInputChanged;
            signed.Checked += SignedInterpretationChanged;
            signed.Unchecked += SignedInterpretationChanged;
            sourceElementType.SelectionChanged += SourceElementTypeChanged;
            pixelOrder.SelectionChanged += ProfileOptionChanged;
            pixelType.SelectionChanged += ProfileOptionChanged;
            visualizeChannel.SelectionChanged += ProfileOptionChanged;
            normalizationMode.SelectionChanged += NormalizationModeChanged;
            rememberForSolution.Checked += RememberForSolutionChanged;
            rememberForSolution.Unchecked += RememberForSolutionChanged;
        }

        private void ProfileInputChanged(object sender, TextChangedEventArgs e)
        {
            if (isUpdatingWatchSelection) return;
            var changed = sender as TextBox;
            ClearInputError(changed);
            if (isNavigatorSelecting && (changed == renderWidth || changed == renderHeight))
            {
                // Overview drag may change these hundreds of times per
                // second. Do not persist or queue a debugger read until
                // mouse-up commits the final View ROI.
                UpdateNavigator();
                return;
            }

            SaveCurrentProfile();
            PersistCurrentSessionIfRequested();
            if (changed == selectedX || changed == selectedY)
            {
                ScheduleCoordinateUpdate();
                return;
            }

            if (changed == roiWidth || changed == roiHeight)
            {
                UpdateNavigator();
                UpdateRoiRectangle();
                return;
            }

            if (changed == width)
            {
                if (strideFollowsWidth)
                {
                    SetStrideFromWidth();
                }
                lastWidthText = width.Text;
            }
            else if (changed == stride && !isSynchronizingStride)
            {
                strideFollowsWidth = false;
            }

            ScheduleAutoRefresh();
        }

        private void ConfigureAutoUpdate()
        {
            var fields = new[] { expression, width, height, stride, qFormat, normalizationMinimum, normalizationMaximum,
                selectedX, selectedY, renderWidth, renderHeight, roiWidth, roiHeight };
            for (var index = 0; index < fields.Length; index++)
            {
                fields[index].LostKeyboardFocus += InputFieldLostKeyboardFocus;
            }

            autoUpdate.Checked += delegate { ScheduleAutoRefresh(); };
            autoUpdate.Unchecked += delegate { autoRefreshTimer.Stop(); debuggerBreakRefreshTimer.Stop(); };
        }

        private void RememberForSolutionChanged(object sender, RoutedEventArgs e)
        {
            if (rememberForSolution.IsChecked == true)
            {
                PersistCurrentSessionIfRequested();
            }
        }

        private void InputFieldLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (isUpdatingWatchSelection) return;
            var field = sender as TextBox;
            if (field == selectedX || field == selectedY)
            {
                ScheduleCoordinateUpdate();
            }
            else if (field != roiWidth && field != roiHeight)
            {
                ScheduleAutoRefresh();
            }
        }

        private void ScheduleAutoRefresh()
        {
            if (isApplyingProfile || autoUpdate.IsChecked != true || String.IsNullOrWhiteSpace(expression.Text))
            {
                return;
            }

            autoRefreshTimer.Stop();
            autoRefreshTimer.Start();
        }

        private void AutoRefreshTimerTick(object sender, EventArgs e)
        {
            if (structureSearchRunning) return;
            autoRefreshTimer.Stop();
            if (autoUpdate.IsChecked != true)
            {
                return;
            }

            LoadContextPreview(null, null);
        }

        internal void DebuggerReturnedToBreakMode()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(DebuggerReturnedToBreakMode));
                return;
            }

            InvalidateStructureSearchCache();
            var hardwareWatchHit = HardwareWatchReturnedToBreakMode();
            if ((!hardwareWatchHit && autoUpdate.IsChecked != true) || String.IsNullOrWhiteSpace(expression.Text))
            {
                return;
            }

            debuggerBreakRefreshTimer.Stop();
            debuggerBreakRefreshTimer.Start();
            SetStatus(hardwareWatchHit
                ? "Watched pixel changed; refreshing the current View ROI."
                : "Debugger paused; refreshing the current View ROI after the step.");
        }

        private void DebuggerBreakRefreshTimerTick(object sender, EventArgs e)
        {
            if (structureSearchRunning) return;
            debuggerBreakRefreshTimer.Stop();
            if ((autoUpdate.IsChecked == true || centerViewAfterHardwareWatch) && !String.IsNullOrWhiteSpace(expression.Text))
            {
                // Full previews are intentionally not reread after each F10/F11.
                // This keeps automatic updates bounded to the current View ROI.
                RefreshCurrentViewAfterDebuggerBreak();
            }
        }

        private void RefreshCurrentViewAfterDebuggerBreak()
        {
            try
            {
                ClearAllInputErrors();
                BeginNewReadRequest();
                var configuration = ReadConfiguration();
                // A hardware watch may have advanced the in-memory cursor
                // while selectedX/Y intentionally retain local expressions.
                // Refresh the existing View ROI without reevaluating those
                // expressions or moving the viewport away from the watch stop.
                var selectedGlobalX = currentX >= 0 && currentX < configuration.Width
                    ? currentX : ResolveCurrentSelection(configuration).X;
                var selectedGlobalY = currentY >= 0 && currentY < configuration.Height
                    ? currentY : ResolveCurrentSelection(configuration).Y;
                preserveViewportOnNextRender = !centerViewAfterHardwareWatch;
                centerViewAfterHardwareWatch = false;
                LoadContextPreviewAt(configuration, selectedGlobalX, selectedGlobalY);
            }
            catch (Exception exception)
            {
                preserveViewportOnNextRender = false;
                SetInputError("Cannot refresh the current View ROI after the debugger break: " + exception.Message);
            }
        }

        private void ScheduleCoordinateUpdate()
        {
            if (isApplyingProfile)
            {
                return;
            }

            coordinateUpdateTimer.Stop();
            coordinateUpdateTimer.Start();
        }

        private void CoordinateUpdateTimerTick(object sender, EventArgs e)
        {
            if (structureSearchRunning) return;
            coordinateUpdateTimer.Stop();
            try
            {
                var configuration = ReadConfiguration();
                var selection = ResolveCurrentSelection(configuration);
                // X/Y is a coordinate search, not merely a cursor edit.
                // Move the visible View ROI to the resolved full-frame point
                // so entering a literal or local centerX/centerY immediately
                // shows the requested location.
                MoveViewTo(selection.X, selection.Y, true);
            }
            catch (Exception exception)
            {
                SetInputError("Cannot resolve selected coordinate: " + exception.Message);
            }
        }

        private void SetStrideFromWidth()
        {
            isSynchronizingStride = true;
            strideFollowsWidth = true;
            stride.Text = width.Text;
            isSynchronizingStride = false;
        }

        private void ProfileOptionChanged(object sender, RoutedEventArgs e)
        {
            SaveCurrentProfile();
            PersistCurrentSessionIfRequested();
            ScheduleAutoRefresh();
        }

        private void ProfileOptionChanged(object sender, SelectionChangedEventArgs e)
        {
            SaveCurrentProfile();
            PersistCurrentSessionIfRequested();
            ScheduleAutoRefresh();
        }

        private void NormalizationModeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isApplyingProfile || switchingViewerTab)
            {
                if (tabTrace != null) tabTrace.Write("SUPPRESS NormalizationModeChanged during profile restore");
                return;
            }
            try
            {
                var selectedMode = GetSelectedNormalizationMode();
                if (selectedMode == NormalizationMode.QFormatRange)
                {
                    var configuration = ReadConfiguration();
                    UpdateNormalizationFields(new NormalizationRange(configuration.RawMinimum, configuration.RawMaximum));
                }
                else if (selectedMode == NormalizationMode.LoadedDataRange && frame != null)
                {
                    UpdateNormalizationFields(frame.GetLoadedDataRange());
                }
            }
            catch (Exception)
            {
                // The normal validation path reports invalid frame values when
                // the user loads or applies a display range.
            }

            SaveCurrentProfile();
            PersistCurrentSessionIfRequested();
            ScheduleAutoRefresh();
        }

        private NormalizationMode GetSelectedNormalizationMode()
        {
            var choice = normalizationMode.SelectedItem as NormalizationModeChoice;
            return choice == null ? NormalizationMode.QFormatRange : choice.Mode;
        }

        private void SetSelectedNormalizationMode(NormalizationMode mode)
        {
            for (var index = 0; index < normalizationMode.Items.Count; index++)
            {
                var choice = normalizationMode.Items[index] as NormalizationModeChoice;
                if (choice != null && choice.Mode == mode)
                {
                    normalizationMode.SelectedItem = choice;
                    return;
                }
            }
        }

        private void SourceElementTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isApplyingProfile || switchingViewerTab) return;
            var selected = sourceElementType.SelectedItem;
            if (selected == null)
            {
                return;
            }

            var elementType = (SourceElementType)selected;
            signed.IsChecked = elementType == SourceElementType.Int8 || elementType == SourceElementType.Int16 || elementType == SourceElementType.Int32;
            SaveCurrentProfile();
            PersistCurrentSessionIfRequested();
            ScheduleAutoRefresh();
        }

        private void SignedInterpretationChanged(object sender, RoutedEventArgs e)
        {
            if (isApplyingProfile || switchingViewerTab) return;
            var selected = sourceElementType.SelectedItem;
            if (selected == null)
            {
                return;
            }

            var targetSigned = signed.IsChecked == true;
            var current = (SourceElementType)selected;
            var currentSigned = current == SourceElementType.Int8 || current == SourceElementType.Int16 || current == SourceElementType.Int32;
            if (targetSigned != currentSigned)
            {
                sourceElementType.SelectedItem = ToSignedVariant(current, targetSigned);
            }

            SaveCurrentProfile();
            PersistCurrentSessionIfRequested();
            ScheduleAutoRefresh();
        }

        private static SourceElementType ToSignedVariant(SourceElementType sourceElementTypeValue, bool signedValue)
        {
            switch (sourceElementTypeValue)
            {
                case SourceElementType.Int8:
                case SourceElementType.UInt8:
                    return signedValue ? SourceElementType.Int8 : SourceElementType.UInt8;
                case SourceElementType.Int16:
                case SourceElementType.UInt16:
                    return signedValue ? SourceElementType.Int16 : SourceElementType.UInt16;
                default:
                    return signedValue ? SourceElementType.Int32 : SourceElementType.UInt32;
            }
        }

        private void EnsureProfile(string sourceExpression)
        {
            if (String.IsNullOrWhiteSpace(sourceExpression) || profiles.ContainsKey(sourceExpression.Trim()))
            {
                return;
            }

            profiles.Add(sourceExpression.Trim(), CreateCurrentProfile(sourceExpression.Trim()));
            PersistProfile(sourceExpression.Trim(), profiles[sourceExpression.Trim()]);
            PersistLastSession(profiles[sourceExpression.Trim()]);
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

            profiles[sourceExpression] = CreateCurrentProfile(sourceExpression);
            PersistProfile(sourceExpression, profiles[sourceExpression]);
            PersistLastSession(profiles[sourceExpression]);
        }

        private ViewerProfile CreateCurrentProfile()
        {
            return CreateCurrentProfile(expression.Text == null ? String.Empty : expression.Text.Trim());
        }

        private ViewerProfile CreateCurrentProfile(string sourceExpression)
        {
            return ViewerProfile.Create(sourceExpression, width.Text, height.Text, stride.Text, qFormat.Text,
                signed.IsChecked == true, (SourceElementType)sourceElementType.SelectedItem, (PixelOrder)pixelOrder.SelectedItem, (PixelType)pixelType.SelectedItem,
                (VisualizeChannel)visualizeChannel.SelectedItem, GetSelectedNormalizationMode(), normalizationMinimum.Text, normalizationMaximum.Text,
                selectedX.Text, selectedY.Text, renderWidth.Text, renderHeight.Text, roiWidth.Text, roiHeight.Text,
                kernelCenterX, kernelCenterY, viewCenterX, viewCenterY);
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
            TraceTabStep("Profile.Signed", delegate { signed.IsChecked = profile.IsSigned; });
            TraceTabStep("Profile.ElementType", delegate { sourceElementType.SelectedItem = profile.SourceElementType; });
            pixelOrder.SelectedItem = profile.PixelOrder;
            pixelType.SelectedItem = profile.PixelType;
            visualizeChannel.SelectedItem = profile.VisualizeChannel;
            TraceTabStep("Profile.Normalization", delegate { SetSelectedNormalizationMode(profile.NormalizationMode); });
            normalizationMinimum.Text = profile.NormalizationMinimum;
            normalizationMaximum.Text = profile.NormalizationMaximum;
            selectedX.Text = profile.SelectedX;
            selectedY.Text = profile.SelectedY;
            renderWidth.Text = profile.RenderWidth;
            renderHeight.Text = profile.RenderHeight;
            roiWidth.Text = profile.RoiWidth;
            roiHeight.Text = profile.RoiHeight;
            kernelCenterX = profile.KernelCenterX;
            kernelCenterY = profile.KernelCenterY;
            viewCenterX = profile.ViewCenterX;
            viewCenterY = profile.ViewCenterY;
            strideFollowsWidth = String.Equals(stride.Text, width.Text, StringComparison.Ordinal);
            lastWidthText = width.Text;
            isApplyingProfile = false;
            activeProfileExpression = profile.Expression;
            RebuildProfilePicker(profile.Expression);
        }

        private void RebuildProfileSetPicker(string selectedName)
        {
            isApplyingProfile = true;
            var items = new List<NamedProfileSet>();
            foreach (var profileSet in profileSets.Values)
            {
                items.Add(profileSet);
            }

            profileSetPicker.ItemsSource = items;
            profileSetPicker.SelectedItem = null;
            for (var index = 0; index < items.Count; index++)
            {
                if (String.Equals(items[index].Name, selectedName, StringComparison.OrdinalIgnoreCase))
                {
                    profileSetPicker.SelectedItem = items[index];
                    break;
                }
            }
            isApplyingProfile = false;
        }

        private void RestorePersistentWorkspaceState()
        {
            if (rememberForSolution.IsChecked != true)
            {
                return;
            }

            try
            {
                RestoreStructureTemplates();
                var solutionIdentity = DebugExpressionFrameReader.GetActiveSolutionIdentity();
                var profileSetPrefix = solutionIdentity + "\nprofile-set\n";
                var profileSetRecords = ReadPersistedRecords(PersistedProfileSetsPath);
                foreach (var pair in profileSetRecords)
                {
                    if (!pair.Key.StartsWith(profileSetPrefix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var profile = DeserializeProfile(pair.Value);
                    var name = pair.Key.Substring(profileSetPrefix.Length);
                    if (profile != null && !String.IsNullOrWhiteSpace(name))
                    {
                        profileSets[name] = new NamedProfileSet(name, profile);
                    }
                }
                RebuildProfileSetPicker(null);

                string serialized;
                if (ReadPersistedRecords(PersistedLastSessionPath).TryGetValue(solutionIdentity + "\nlast-session", out serialized))
                {
                    var restored = DeserializeProfile(serialized);
                    if (restored != null && !String.IsNullOrWhiteSpace(restored.Expression))
                    {
                        profiles[restored.Expression] = restored;
                        ApplyProfile(restored);
                        SetStatus("Restored the last viewer state for this solution. Pause the debuggee to refresh its current View ROI.");
                    }
                }
            }
            catch (Exception)
            {
                // Persisted settings are optional. A corrupt user file must
                // never prevent the viewer from opening.
            }
        }

        private void PersistCurrentSessionIfRequested()
        {
            if (isApplyingProfile || rememberForSolution.IsChecked != true || String.IsNullOrWhiteSpace(expression.Text))
            {
                return;
            }

            PersistLastSession(CreateCurrentProfile());
        }

        // Profiles are kept below LocalAppData rather than in the solution
        // directory, so opening the same solution restores its interpretation
        // without creating untracked project files. The key includes the
        // absolute solution path, expression, and decoded element type; an A
        // pointer in another solution (or with a different underlying type)
        // therefore cannot silently inherit this profile.
        private static string PersistedProfilesPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArrayImageViewer", "profiles-v1.txt");
            }
        }

        private static string PersistedProfileSetsPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArrayImageViewer", "profile-sets-v1.txt");
            }
        }

        private static string PersistedLastSessionPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArrayImageViewer", "last-session-v1.txt");
            }
        }

        private string GetPersistedProfileKey(string sourceExpression, SourceElementType elementType)
        {
            return DebugExpressionFrameReader.GetActiveSolutionIdentity() + "\n" + sourceExpression.Trim() + "\n" + elementType.ToString();
        }

        private void PersistProfile(string sourceExpression, ViewerProfile profile)
        {
            if (rememberForSolution.IsChecked != true)
            {
                return;
            }

            try
            {
                var key = GetPersistedProfileKey(sourceExpression, profile.SourceElementType);
                var records = ReadPersistedRecords(PersistedProfilesPath);
                records[key] = SerializeProfile(profile);
                WritePersistedRecords(PersistedProfilesPath, records);
            }
            catch (Exception)
            {
                // Persistence is a convenience only; a readonly or damaged
                // user profile must never stop debugger inspection.
            }
        }

        private bool TryLoadPersistedProfile(string sourceExpression, SourceElementType elementType, out ViewerProfile profile)
        {
            profile = null;
            try
            {
                if (rememberForSolution.IsChecked != true)
                {
                    return false;
                }

                var key = GetPersistedProfileKey(sourceExpression, elementType);
                string serialized;
                if (ReadPersistedRecords(PersistedProfilesPath).TryGetValue(key, out serialized))
                {
                    profile = DeserializeProfile(serialized);
                    return profile != null;
                }
            }
            catch (Exception)
            {
                profile = null;
            }

            return false;
        }

        private void PersistProfileSet(NamedProfileSet profileSet)
        {
            if (rememberForSolution.IsChecked != true)
            {
                return;
            }

            try
            {
                var records = ReadPersistedRecords(PersistedProfileSetsPath);
                records[GetProfileSetKey(profileSet.Name)] = SerializeProfile(profileSet.Profile);
                WritePersistedRecords(PersistedProfileSetsPath, records);
            }
            catch (Exception)
            {
                // The in-memory set remains usable if local persistence fails.
            }
        }

        private void DeletePersistedProfileSet(string name)
        {
            if (rememberForSolution.IsChecked != true)
            {
                return;
            }

            try
            {
                var records = ReadPersistedRecords(PersistedProfileSetsPath);
                records.Remove(GetProfileSetKey(name));
                WritePersistedRecords(PersistedProfileSetsPath, records);
            }
            catch (Exception)
            {
                // The UI already removed the in-memory entry.
            }
        }

        private void PersistLastSession(ViewerProfile profile)
        {
            if (rememberForSolution.IsChecked != true || profile == null || String.IsNullOrWhiteSpace(profile.Expression))
            {
                return;
            }

            try
            {
                var records = ReadPersistedRecords(PersistedLastSessionPath);
                records[DebugExpressionFrameReader.GetActiveSolutionIdentity() + "\nlast-session"] = SerializeProfile(profile);
                WritePersistedRecords(PersistedLastSessionPath, records);
            }
            catch (Exception)
            {
                // Persistence must not interfere with debugger inspection.
            }
        }

        private string GetProfileSetKey(string name)
        {
            return DebugExpressionFrameReader.GetActiveSolutionIdentity() + "\nprofile-set\n" + name;
        }

        private static Dictionary<string, string> ReadPersistedRecords(string path)
        {
            var records = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!File.Exists(path))
            {
                return records;
            }

            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                var split = lines[index].IndexOf('\t');
                if (split > 0)
                {
                    records[DecodeStoredValue(lines[index].Substring(0, split))] = lines[index].Substring(split + 1);
                }
            }
            return records;
        }

        private static void WritePersistedRecords(string path, Dictionary<string, string> records)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var output = new List<string>();
            foreach (var pair in records)
            {
                output.Add(EncodeStoredValue(pair.Key) + "\t" + pair.Value);
            }
            File.WriteAllLines(path, output.ToArray());
        }

        private static string SerializeProfile(ViewerProfile profile)
        {
            var values = new[]
            {
                profile.Expression, profile.Width, profile.Height, profile.Stride, profile.QFormat,
                profile.IsSigned ? "1" : "0", profile.SourceElementType.ToString(), profile.PixelOrder.ToString(), profile.PixelType.ToString(), profile.VisualizeChannel.ToString(),
                profile.NormalizationMode.ToString(), profile.NormalizationMinimum, profile.NormalizationMaximum, profile.SelectedX, profile.SelectedY,
                profile.RenderWidth, profile.RenderHeight, profile.RoiWidth, profile.RoiHeight,
                profile.KernelCenterX.ToString(CultureInfo.InvariantCulture), profile.KernelCenterY.ToString(CultureInfo.InvariantCulture),
                profile.ViewCenterX.ToString(CultureInfo.InvariantCulture), profile.ViewCenterY.ToString(CultureInfo.InvariantCulture)
            };
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = EncodeStoredValue(values[index]);
            }
            return String.Join("|", values);
        }

        private static ViewerProfile DeserializeProfile(string value)
        {
            var fields = value.Split('|');
            if (fields.Length != 23)
            {
                return null;
            }

            for (var index = 0; index < fields.Length; index++)
            {
                fields[index] = DecodeStoredValue(fields[index]);
            }

            SourceElementType elementType;
            PixelOrder order;
            PixelType type;
            VisualizeChannel channel;
            NormalizationMode mode;
            int kernelX;
            int kernelY;
            int viewX;
            int viewY;
            if (!Enum.TryParse(fields[6], out elementType) || !Enum.TryParse(fields[7], out order) || !Enum.TryParse(fields[8], out type) ||
                !Enum.TryParse(fields[9], out channel) || !Enum.TryParse(fields[10], out mode) ||
                !Int32.TryParse(fields[19], NumberStyles.Integer, CultureInfo.InvariantCulture, out kernelX) ||
                !Int32.TryParse(fields[20], NumberStyles.Integer, CultureInfo.InvariantCulture, out kernelY) ||
                !Int32.TryParse(fields[21], NumberStyles.Integer, CultureInfo.InvariantCulture, out viewX) ||
                !Int32.TryParse(fields[22], NumberStyles.Integer, CultureInfo.InvariantCulture, out viewY))
            {
                return null;
            }

            return ViewerProfile.Create(fields[0], fields[1], fields[2], fields[3], fields[4], fields[5] == "1", elementType, order, type, channel, mode,
                fields[11], fields[12], fields[13], fields[14], fields[15], fields[16], fields[17], fields[18], kernelX, kernelY, viewX, viewY);
        }

        private static string EncodeStoredValue(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? String.Empty));
        }

        private static string DecodeStoredValue(string value)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
    }
}
