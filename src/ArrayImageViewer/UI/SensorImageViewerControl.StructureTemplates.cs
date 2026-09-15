using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ArrayImageViewer.Core;
using ArrayImageViewer.Debugging;
using ArrayImageViewer.Options;

namespace ArrayImageViewer.UI
{
    internal sealed partial class SensorImageViewerControl
    {
        private const int StructureTemplateFormatVersion = 1;
        private bool structureSearchRunning;
        private bool structureSearchCancelled;
        private Button structureSearchButton;
        private readonly TextBlock structureSearchState = new TextBlock
        {
            Text = "검색 준비", Width = 135, VerticalAlignment = VerticalAlignment.Center,
            Foreground = System.Windows.Media.Brushes.LightGray, Margin = new Thickness(6, 0, 4, 0)
        };
        private int structureSearchEpoch;
        private string structureTemplateSolutionIdentity;
        private bool structureSettingsSubscribed;
        private int appliedStructureSettingsRevision;

        private void StructureOptionsLoaded(object sender, RoutedEventArgs e)
        {
            if (structureSettingsSubscribed) return;
            structureSettingsSubscribed = true;
            StructureTemplateStore.SettingsChanged += StructureOptionsChanged;
            try { RestoreStructureTemplates(); }
            catch (Exception exception) { SetStatus("Cannot load structure templates: " + exception.Message); }
        }

        private void StructureOptionsUnloaded(object sender, RoutedEventArgs e)
        {
            StructureTemplateStore.SettingsChanged -= StructureOptionsChanged;
            structureSettingsSubscribed = false;
        }

        private void StructureOptionsChanged(object sender, EventArgs e)
        {
            var settings = e as StructureTemplateSettingsChangedEventArgs;
            if (settings == null || Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (!structureSettingsSubscribed || settings.Revision <= appliedStructureSettingsRevision ||
                    !String.Equals(settings.SolutionIdentity, structureTemplateSolutionIdentity, StringComparison.Ordinal)) return;
                try
                {
                appliedStructureSettingsRevision = settings.Revision;
                InvalidateStructureSearchCache();
                capturedStructureCandidates.Clear();
                capturedStructurePicker.ItemsSource = null;
                ApplyStructureTemplateDefinitions(settings.Templates);
                structureSearchState.Text = "템플릿 적용됨 · 검색 준비";
                }
                catch (Exception exception) { SetStatus("Cannot refresh structure templates: " + exception.Message); }
                // Do not touch focus, root, capture expression, cached frame or
                // zoom. Applying definitions never starts a search/memory read.
            }));
        }
        private void SetSearchBusy(bool busy)
        {
            SetIconContent(structureSearchButton, busy ? "Cancel search" : "Search objects",
                busy ? "검색 취소 · 현재 디버거 호출이 끝나면 중단합니다" : "객체 검색 · 지정한 루트의 멤버만 탐색합니다 (기반 클래스 제외)",
                busy ? "M4,4 L12,4 L12,12 L4,12 Z" : "M11,6 A5,5 0 1 1 1,6 A5,5 0 1 1 11,6 M10,10 L15,15", false);
            structureSearchState.Text = busy ? "검색 중… ■ 취소" : "검색 준비";
        }

        private void ReloadStructureTemplates(object sender, RoutedEventArgs e)
        {
            try
            {
                InvalidateStructureSearchCache();
                structureTemplates.Clear();
                RestoreStructureTemplates();
                SetStatus("Reloaded " + structureTemplates.Count.ToString(CultureInfo.InvariantCulture) +
                    " structure template(s) from Tools > Options > Array RAW Viewer > Structure Templates.");
            }
            catch (Exception exception)
            {
                SetStatus("Cannot reload structure templates: " + exception.Message);
            }
        }

        private void NewStructureTemplate(object sender, RoutedEventArgs e)
        {
            isApplyingProfile = true;
            structureTemplatePicker.SelectedItem = null;
            structureTemplateName.Text = "MyStructure";
            structureTemplateData.Text = "D";
            structureTemplateWidth.Text = "W";
            structureTemplateHeight.Text = "H";
            isApplyingProfile = false;
            structureTemplateName.Focus();
            structureTemplateName.SelectAll();
            SetStatus("New structure template: name the class and enter its D/W/H member access. Capture root remains the live Viewer input.");
        }

        private void StructureTemplatePickerChanged(object sender, RoutedEventArgs e)
        {
            if (isApplyingProfile)
            {
                return;
            }

            var template = structureTemplatePicker.SelectedItem as StructureTemplate;
            if (template != null)
            {
                ApplyStructureTemplateFields(template);
                SetStatus("Loaded structure template '" + template.ClassName + "'. Set Root for the current object, then choose Bind current object.");
            }
        }

        private void BindStructureTemplate(object sender, RoutedEventArgs e)
        {
            BindStructureTemplate(null);
        }

        private void BindStructureTemplate(string bindingRoot)
        {
            try
            {
                ClearAllInputErrors();
                var template = ReadStructureTemplateFields(true);
                if (bindingRoot != null) template.RootExpression = bindingRoot;
                var dataExpression = StructureTemplateExpressions.Compose(template.RootExpression, template.DataAccess);
                var widthExpression = StructureTemplateExpressions.Compose(template.RootExpression, template.WidthAccess);
                var heightExpression = StructureTemplateExpressions.Compose(template.RootExpression, template.HeightAccess);

                var boundWidth = DebugExpressionFrameReader.EvaluateInt32(widthExpression);
                var boundHeight = DebugExpressionFrameReader.EvaluateInt32(heightExpression);
                if (boundWidth <= 0 || boundHeight <= 0)
                {
                    throw new ArgumentException("Structure template width and height must evaluate to positive integers.");
                }

                SourceElementType elementType;
                string typeError;
                if (!DebugExpressionFrameReader.TryGetIntegralPointerType(dataExpression, out elementType, out typeError))
                {
                    throw new ArgumentException("Structure template RAW data binding failed: " + typeError);
                }

                isApplyingProfile = true;
                expression.Text = dataExpression;
                width.Text = widthExpression;
                height.Text = heightExpression;
                stride.Text = widthExpression;
                sourceElementType.SelectedItem = elementType;
                signed.IsChecked = elementType == SourceElementType.Int8 || elementType == SourceElementType.Int16 || elementType == SourceElementType.Int32;
                isApplyingProfile = false;
                strideFollowsWidth = true;
                lastWidthText = width.Text;
                activeProfileExpression = dataExpression;
                EnsureProfile(dataExpression);
                SaveCurrentProfile();
                RebuildProfilePicker(dataExpression);
                PersistCurrentSessionIfRequested();
                SetStatus("Bound '" + template.ClassName + "': RAW=" + dataExpression + ", W=" + boundWidth.ToString(CultureInfo.InvariantCulture) +
                    ", H=" + boundHeight.ToString(CultureInfo.InvariantCulture) + ", stride=W. Select Capture to read the current object.");
                ScheduleAutoRefresh();
            }
            catch (Exception exception)
            {
                SetInputError("Cannot bind structure template: " + exception.Message);
            }
        }

        private async void CaptureStructureObjects(object sender, RoutedEventArgs e)
        {
            if (structureSearchRunning)
            {
                structureSearchCancelled = true;
                structureSearchState.Text = "취소 요청 중…";
                return;
            }
            structureSearchRunning = true;
            structureSearchCancelled = false;
            StructureSearchTrace trace = null;
            try
            {
                trace = new StructureSearchTrace(StructureTemplateStore.LoadSearchDebug());
                SetSearchBusy(true);
                var timeoutSeconds = StructureTemplateStore.LoadSearchSeconds();
                SetStatus("Searching objects... (limit " + timeoutSeconds + " seconds)");
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                ClearAllInputErrors();
                InvalidateStructureSearchCache();
                RestoreStructureTemplates();
                capturedStructureCandidates.Clear();
                capturedStructurePicker.ItemsSource = null;
                var searchEpoch = structureSearchEpoch;
                var templateRevision = StructureTemplateStore.Revision;
                var solutionAtStart = DebugExpressionFrameReader.GetActiveSolutionIdentity();
                var root = (structureTemplateRoot.Text ?? String.Empty).Trim();
                trace.Write("ROOT " + root);
                var interestTypes = new List<string>();
                foreach (var template in structureTemplates.Values)
                {
                    if (!String.IsNullOrWhiteSpace(template.ClassName))
                    {
                        interestTypes.Add(template.ClassName);
                    }
                }

                string searchWarning = null;
                var captured = await DebugExpressionFrameReader.CaptureInterestedStructures(root, interestTypes, 12, 512,
                    timeoutSeconds, delegate(string warning) { searchWarning = warning; },
                    delegate { return searchEpoch == structureSearchEpoch && !structureSearchCancelled && templateRevision == StructureTemplateStore.Revision && root == structureTemplateRoot.Text.Trim(); }, trace);
                if (solutionAtStart != DebugExpressionFrameReader.GetActiveSolutionIdentity())
                    throw new OperationCanceledException("Solution changed during search. Search again.");
                capturedStructureCandidates.Clear();
                foreach (var candidate in captured)
                {
                    capturedStructureCandidates.Add(candidate);
                }

                // Searching must not replace the expression the user entered as
                // the search root.  A result becomes the active binding only
                // after the explicit "Use captured object" action.
                isApplyingProfile = true;
                capturedStructurePicker.ItemsSource = new List<DebugExpressionFrameReader.StructureCandidate>(capturedStructureCandidates);
                capturedStructurePicker.SelectedItem = capturedStructureCandidates.Count == 0 ? null : capturedStructureCandidates[0];
                isApplyingProfile = false;
                if (capturedStructureCandidates.Count == 0)
                {
                    SetStatus(searchWarning != null
                        ? "Search incomplete: " + searchWarning + " No matches found in the inspected portion. Try a closer root such as this->buffer."
                        : "No registered interest types were found below '" + root + "'. Check the template class name and pause where the object is alive.");
                    return;
                }

                SetStatus("Captured " + capturedStructureCandidates.Count.ToString(CultureInfo.InvariantCulture) +
                    " registered structure(s) below '" + root + "'. " +
                    (searchWarning != null ? "Partial results: " + searchWarning + " Narrow the root to search further." :
                    "Search limits: depth 12, 512 nodes, 16 matching container elements, " + timeoutSeconds + " seconds."));
            }
            catch (OperationCanceledException exception)
            {
                if (trace != null) trace.Write("CANCEL " + exception.Message);
                SetStatus(structureSearchCancelled ? "Structure search cancelled." : exception.Message);
            }
            catch (Exception exception)
            {
                if (trace != null) trace.Write("SEARCH ERROR " + exception.GetType().Name + " " + exception.Message);
                SetInputError("Cannot capture structures: " + exception.Message);
            }
            finally
            {
                if (trace != null)
                {
                    trace.Write("FINISH");
                    trace.Dispose();
                    if (trace.FilePath != null) status.Text += " Debug dump: " + trace.FilePath;
                }
                SetSearchBusy(false);
                structureSearchState.Text = structureSearchCancelled ? "검색 취소됨" : capturedStructureCandidates.Count + "개 · 아래 결과 확인";
                structureSearchRunning = false;
            }
        }


        private void CapturedStructurePickerChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isApplyingProfile)
            {
                return;
            }

            var candidate = capturedStructurePicker.SelectedItem as DebugExpressionFrameReader.StructureCandidate;
            if (candidate != null)
            {
                SetStatus("Selected '" + candidate.Expression + "'. Choose Use captured object to bind it; the search root is unchanged.");
            }
        }

        private void UseCapturedStructure(object sender, RoutedEventArgs e)
        {
            if (structureSearchRunning) return;
            var candidate = capturedStructurePicker.SelectedItem as DebugExpressionFrameReader.StructureCandidate;
            if (candidate == null)
            {
                SetStatus("Capture structures first, then select a discovered object.");
                return;
            }

            ApplyCapturedStructure(candidate);
            BindStructureTemplate(candidate.PointerRootExpression);
        }

        private void ApplyCapturedStructure(DebugExpressionFrameReader.StructureCandidate candidate)
        {
            if (candidate == null)
            {
                return;
            }

            StructureTemplate matchingTemplate = null;
            foreach (var template in structureTemplates.Values)
            {
                if (String.Equals(template.ClassName, candidate.TemplateClassName, StringComparison.OrdinalIgnoreCase))
                {
                    matchingTemplate = template;
                    break;
                }
            }

            isApplyingProfile = true;
            if (matchingTemplate != null)
            {
                structureTemplatePicker.SelectedItem = matchingTemplate;
                ApplyStructureTemplateFields(matchingTemplate);
            }
            isApplyingProfile = false;
        }

        private void InvalidateStructureSearchCache()
        {
            structureSearchEpoch++;
        }

        private void SaveStructureTemplate(object sender, RoutedEventArgs e)
        {
            try
            {
                var template = ReadStructureTemplateFields(true);
                var definition = CreateStructureTemplateDefinition(template);
                structureTemplates[definition.ClassName] = definition;
                PersistStructureTemplate(definition);
                RebuildStructureTemplatePicker(definition.ClassName);
                SetStatus("Saved structure template '" + template.ClassName + "' for this solution. Export JSON to share it with another solution or developer.");
            }
            catch (Exception exception)
            {
                SetInputError("Cannot save structure template: " + exception.Message);
            }
        }

        private void DeleteStructureTemplate(object sender, RoutedEventArgs e)
        {
            var selected = structureTemplatePicker.SelectedItem as StructureTemplate;
            var name = selected == null ? (structureTemplateName.Text ?? String.Empty).Trim() : selected.ClassName;
            if (name.Length == 0 || !structureTemplates.ContainsKey(name))
            {
                SetStatus("Choose a saved structure template to delete.");
                return;
            }

            structureTemplates.Remove(name);
            DeletePersistedStructureTemplate(name);
            RebuildStructureTemplatePicker(null);
            SetStatus("Deleted structure template '" + name + "' from this solution.");
        }

        private void ExportStructureTemplates(object sender, RoutedEventArgs e)
        {
            if (structureTemplates.Count == 0)
            {
                SetStatus("Save at least one structure template before exporting JSON.");
                return;
            }

            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Export Array RAW structure templates",
                    Filter = "Array RAW structure templates (*.json)|*.json|All files (*.*)|*.*",
                    FileName = "ArrayRawViewer.structure-templates.json",
                    AddExtension = true,
                    DefaultExt = ".json"
                };
                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                var exportedTemplates = new List<StructureTemplate>();
                foreach (var template in structureTemplates.Values)
                {
                    exportedTemplates.Add(CreateStructureTemplateDefinition(template));
                }
                var document = new StructureTemplateDocument { FormatVersion = StructureTemplateFormatVersion, Templates = exportedTemplates };
                File.WriteAllText(dialog.FileName, SerializeStructureTemplateDocument(document), Encoding.UTF8);
                SetStatus("Exported " + document.Templates.Count.ToString(CultureInfo.InvariantCulture) + " structure template(s) to " + dialog.FileName + ".");
            }
            catch (Exception exception)
            {
                SetStatus("Cannot export structure templates: " + exception.Message);
            }
        }

        private void ImportStructureTemplates(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Import Array RAW structure templates",
                    Filter = "Array RAW structure templates (*.json)|*.json|All files (*.*)|*.*",
                    CheckFileExists = true,
                    Multiselect = false
                };
                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                var document = DeserializeStructureTemplateDocument(File.ReadAllText(dialog.FileName, Encoding.UTF8));
                if (document == null || document.FormatVersion != StructureTemplateFormatVersion || document.Templates == null)
                {
                    throw new InvalidDataException("This is not a supported Array RAW structure-template JSON file.");
                }

                var imported = 0;
                for (var index = 0; index < document.Templates.Count; index++)
                {
                    var template = document.Templates[index];
                    ValidateStructureTemplate(template);
                    var definition = CreateStructureTemplateDefinition(template);
                    structureTemplates[definition.ClassName] = definition;
                    PersistStructureTemplate(definition);
                    imported++;
                }
                RebuildStructureTemplatePicker(imported > 0 ? document.Templates[document.Templates.Count - 1].ClassName : null);
                SetStatus("Imported " + imported.ToString(CultureInfo.InvariantCulture) + " structure template(s). Existing class-name matches were updated.");
            }
            catch (Exception exception)
            {
                SetInputError("Cannot import structure templates: " + exception.Message);
            }
        }

        private StructureTemplate ReadStructureTemplateFields(bool requireName)
        {
            var template = new StructureTemplate
            {
                ClassName = (structureTemplateName.Text ?? String.Empty).Trim(),
                RootExpression = (structureTemplateRoot.Text ?? String.Empty).Trim(),
                DataAccess = (structureTemplateData.Text ?? String.Empty).Trim(),
                WidthAccess = (structureTemplateWidth.Text ?? String.Empty).Trim(),
                HeightAccess = (structureTemplateHeight.Text ?? String.Empty).Trim()
            };
            if (!requireName && template.ClassName.Length == 0)
            {
                template.ClassName = "Unnamed";
            }
            ValidateStructureTemplate(template);
            return template;
        }

        private static void ValidateStructureTemplate(StructureTemplate template)
        {
            if (template == null || String.IsNullOrWhiteSpace(template.ClassName)) throw new ArgumentException("Enter a structure template class name.");
            if (String.IsNullOrWhiteSpace(template.DataAccess)) throw new ArgumentException("Enter a structure template RAW data access expression.");
            if (String.IsNullOrWhiteSpace(template.WidthAccess)) throw new ArgumentException("Enter a structure template width access expression.");
            if (String.IsNullOrWhiteSpace(template.HeightAccess)) throw new ArgumentException("Enter a structure template height access expression.");
        }

        private void ApplyStructureTemplateFields(StructureTemplate template)
        {
            var wasApplyingProfile = isApplyingProfile;
            isApplyingProfile = true;
            try
            {
            structureTemplateName.Text = template.ClassName;
            structureTemplateData.Text = template.DataAccess;
            structureTemplateWidth.Text = template.WidthAccess;
            structureTemplateHeight.Text = template.HeightAccess;
            }
            finally { isApplyingProfile = wasApplyingProfile; }
        }

        private void RebuildStructureTemplatePicker(string selectedName)
        {
            var wasApplyingProfile = isApplyingProfile;
            isApplyingProfile = true;
            try
            {
            structureTemplatePicker.ItemsSource = new List<StructureTemplate>(structureTemplates.Values);
            structureTemplatePicker.SelectedItem = null;
            foreach (StructureTemplate template in structureTemplates.Values)
            {
                if (String.Equals(template.ClassName, selectedName, StringComparison.OrdinalIgnoreCase))
                {
                    structureTemplatePicker.SelectedItem = template;
                    break;
                }
            }
            }
            finally { isApplyingProfile = wasApplyingProfile; }
        }

        private static string PersistedStructureTemplatesPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArrayImageViewer", "structure-templates-v1.txt");
            }
        }

        private string GetStructureTemplateKey(string className)
        {
            return DebugExpressionFrameReader.GetActiveSolutionIdentity() + "\nstructure-template\n" + className;
        }

        private void RestoreStructureTemplates()
        {
            // The Options page owns the persisted definition format. Reading
            // through the same store is important: it also exposes the
            // built-in ImageStream mapping for this sample solution.
            structureTemplateSolutionIdentity = DebugExpressionFrameReader.GetActiveSolutionIdentity();
            var savedTemplates = StructureTemplateStore.Load(structureTemplateSolutionIdentity);
            ApplyStructureTemplateDefinitions(savedTemplates);
        }

        private void ApplyStructureTemplateDefinitions(IList<StructureTemplateItem> savedTemplates)
        {
            var selected = structureTemplatePicker.SelectedItem as StructureTemplate;
            string selectedName = selected == null ? null : selected.ClassName;
            structureTemplates.Clear();
            foreach (var item in savedTemplates)
            {
                var template = new StructureTemplate
                {
                    ClassName = item.ClassName,
                    DataAccess = item.DataAccess,
                    WidthAccess = item.WidthAccess,
                    HeightAccess = item.HeightAccess
                };
                try
                {
                    ValidateStructureTemplate(template);
                    var definition = CreateStructureTemplateDefinition(template);
                    structureTemplates[definition.ClassName] = definition;
                }
                catch (Exception)
                {
                    // Ignore an individual invalid persisted template.
                }
            }
            RebuildStructureTemplatePicker(selectedName);
            var updated = structureTemplatePicker.SelectedItem as StructureTemplate;
            if (updated != null) ApplyStructureTemplateFields(updated);
            else if (selected != null)
            {
                ApplyStructureTemplateFields(new StructureTemplate());
            }
        }

        private void PersistStructureTemplate(StructureTemplate template)
        {
            if (rememberForSolution.IsChecked != true)
            {
                return;
            }

            var records = ReadPersistedRecords(PersistedStructureTemplatesPath);
            var definition = CreateStructureTemplateDefinition(template);
            records[GetStructureTemplateKey(definition.ClassName)] = SerializeStructureTemplateDocument(
                new StructureTemplateDocument { FormatVersion = StructureTemplateFormatVersion, Templates = new List<StructureTemplate> { definition } });
            WritePersistedRecords(PersistedStructureTemplatesPath, records);
        }

        // Root expressions are debugger-frame specific. Keep one only while
        // binding/searching in this Viewer, never in a saved template.
        private static StructureTemplate CreateStructureTemplateDefinition(StructureTemplate template)
        {
            return new StructureTemplate
            {
                ClassName = template == null ? null : template.ClassName,
                DataAccess = template == null ? null : template.DataAccess,
                WidthAccess = template == null ? null : template.WidthAccess,
                HeightAccess = template == null ? null : template.HeightAccess
            };
        }

        private void DeletePersistedStructureTemplate(string className)
        {
            if (rememberForSolution.IsChecked != true)
            {
                return;
            }

            var records = ReadPersistedRecords(PersistedStructureTemplatesPath);
            records.Remove(GetStructureTemplateKey(className));
            WritePersistedRecords(PersistedStructureTemplatesPath, records);
        }

        private static string SerializeStructureTemplateDocument(StructureTemplateDocument document)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(StructureTemplateDocument)).WriteObject(stream, document);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static StructureTemplateDocument DeserializeStructureTemplateDocument(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? String.Empty)))
            {
                return new DataContractJsonSerializer(typeof(StructureTemplateDocument)).ReadObject(stream) as StructureTemplateDocument;
            }
        }

        private sealed class StructureTemplateDocument
        {
            public int FormatVersion { get; set; }
            public List<StructureTemplate> Templates { get; set; }
        }

        private sealed class StructureTemplate
        {
            public string ClassName { get; set; }
            public string RootExpression { get; set; }
            public string DataAccess { get; set; }
            public string WidthAccess { get; set; }
            public string HeightAccess { get; set; }

            public override string ToString()
            {
                return ClassName + "  |  " + DataAccess + "  |  W=" + WidthAccess + ", H=" + HeightAccess;
            }
        }
    }
}
