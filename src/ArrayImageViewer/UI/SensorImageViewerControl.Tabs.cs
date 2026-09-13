using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArrayImageViewer.Core;

namespace ArrayImageViewer.UI
{
    internal sealed partial class SensorImageViewerControl
    {
        private int nextViewerTabId = 1;
        private ViewerTab activeViewerTab;

        private UIElement CreateViewerTabStrip()
        {
            var strip = new Border
            {
                Background = PanelBrush,
                BorderBrush = PanelBorderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 5, 10, 5)
            };
            viewerTabPanel.VerticalAlignment = VerticalAlignment.Center;
            strip.Child = viewerTabPanel;
            return strip;
        }

        private void InitializeViewerTabs()
        {
            if (activeViewerTab != null)
            {
                return;
            }

            activeViewerTab = new ViewerTab(nextViewerTabId++, CreateCurrentProfile());
            viewerTabs.Add(activeViewerTab);
            RefreshViewerTabs();
        }

        private void RefreshViewerTabs()
        {
            viewerTabPanel.Children.Clear();
            for (var index = 0; index < viewerTabs.Count; index++)
            {
                var tab = viewerTabs[index];
                var select = CreateButton(tab.Caption, SelectViewerTab, tab == activeViewerTab);
                select.Tag = tab;
                select.ToolTip = tab.Profile == null ? tab.Caption : tab.Profile.Expression;
                select.Margin = new Thickness(0, 0, 2, 0);
                select.MinWidth = 105;
                select.MaxWidth = 210;

                var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 7, 0) };
                header.Children.Add(select);
                if (viewerTabs.Count > 1)
                {
                    var close = CreateButton("×", CloseViewerTab, false);
                    close.Tag = tab;
                    close.Width = 28;
                    close.Padding = new Thickness(0, 2, 0, 3);
                    header.Children.Add(close);
                }
                viewerTabPanel.Children.Add(header);
            }

            var add = CreateButton("+ Array", AddViewerTab, false);
            add.Margin = new Thickness(2, 0, 0, 0);
            viewerTabPanel.Children.Add(add);
        }

        private void AddViewerTab(object sender, RoutedEventArgs e)
        {
            CaptureActiveViewerTab();
            BeginNewReadRequest();
            ClearHardwareWatch(false);
            var copiedSettings = CreateCurrentProfile(String.Empty);
            var tab = new ViewerTab(nextViewerTabId++, copiedSettings);
            viewerTabs.Add(tab);
            activeViewerTab = tab;
            RestoreViewerTab(tab);
            RefreshViewerTabs();
            SetStatus("New array tab created. Choose a pointer or enter an expression, then load its View ROI.");
        }

        private void SelectViewerTab(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var tab = button == null ? null : button.Tag as ViewerTab;
            if (tab == null || tab == activeViewerTab)
            {
                return;
            }

            CaptureActiveViewerTab();
            BeginNewReadRequest();
            ClearHardwareWatch(false);
            activeViewerTab = tab;
            RestoreViewerTab(tab);
            RefreshViewerTabs();
            SetStatus(tab.Frame == null
                ? "Switched to " + tab.Caption + ". Load its View ROI when ready."
                : "Switched to " + tab.Caption + " using its cached ROI; no debugger read was made.");
        }

        private void CloseViewerTab(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var tab = button == null ? null : button.Tag as ViewerTab;
            if (tab == null || viewerTabs.Count <= 1)
            {
                return;
            }

            var removedIndex = viewerTabs.IndexOf(tab);
            if (tab == activeViewerTab)
            {
                BeginNewReadRequest();
                ClearHardwareWatch(false);
                viewerTabs.Remove(tab);
                activeViewerTab = viewerTabs[Math.Max(0, removedIndex - 1)];
                RestoreViewerTab(activeViewerTab);
            }
            else
            {
                viewerTabs.Remove(tab);
            }

            RefreshViewerTabs();
            SetStatus("Closed " + tab.Caption + ". Cached samples for that tab were discarded.");
        }

        private void CaptureActiveViewerTab()
        {
            if (activeViewerTab == null || isApplyingProfile)
            {
                return;
            }

            activeViewerTab.Profile = CreateCurrentProfile();
            activeViewerTab.SelectedX = currentX;
            activeViewerTab.SelectedY = currentY;
            activeViewerTab.Frame = frame;
            activeViewerTab.Bitmap = image.Source;
            activeViewerTab.Normalization = activeNormalization;
            activeViewerTab.FullFrameWidth = fullFrameWidth;
            activeViewerTab.FullFrameHeight = fullFrameHeight;
            activeViewerTab.ShowsFullFrameContext = showsFullFrameContext;
            activeViewerTab.Zoom = zoom;
            activeViewerTab.HorizontalOffset = scrollViewer.HorizontalOffset;
            activeViewerTab.VerticalOffset = scrollViewer.VerticalOffset;
            activeViewerTab.NavigatorPreview = navigatorPreview.Source;
            activeViewerTab.NavigatorPreviewKey = navigatorPreviewKey;
            activeViewerTab.NavigatorSourceConfiguration = navigatorSourceConfiguration;
        }

        private void RestoreViewerTab(ViewerTab tab)
        {
            ApplyProfile(tab.Profile);
            activeNormalization = tab.Normalization;
            navigatorPreview.Source = tab.NavigatorPreview;
            navigatorPreviewKey = tab.NavigatorPreviewKey;
            navigatorSourceConfiguration = tab.NavigatorSourceConfiguration;
            zoom = tab.Zoom > 0 ? tab.Zoom : 0.15;

            if (tab.Frame != null && tab.Bitmap != null)
            {
                ApplyFrame(tab.Frame, tab.Bitmap, tab.FullFrameWidth, tab.FullFrameHeight,
                    tab.SelectedX, tab.SelectedY, tab.ShowsFullFrameContext);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    scrollViewer.ScrollToHorizontalOffset(tab.HorizontalOffset);
                    scrollViewer.ScrollToVerticalOffset(tab.VerticalOffset);
                }));
                return;
            }

            frame = null;
            image.Source = null;
            fullFrameWidth = 0;
            fullFrameHeight = 0;
            showsFullFrameContext = false;
            ClearValueOverlay();
            unloadedFrame.Visibility = Visibility.Collapsed;
            selectedCellRectangle.Visibility = Visibility.Collapsed;
            roiRectangle.Visibility = Visibility.Collapsed;
            emptyState.Visibility = Visibility.Visible;
            UpdateNavigator();
        }

        private sealed class ViewerTab
        {
            public ViewerTab(int idValue, ViewerProfile profileValue)
            {
                Id = idValue;
                Profile = profileValue;
                Normalization = new NormalizationRange(0, 1);
                Zoom = 0.15;
            }

            public int Id { get; private set; }
            public ViewerProfile Profile { get; set; }
            public FrameBuffer Frame { get; set; }
            public int SelectedX { get; set; }
            public int SelectedY { get; set; }
            public ImageSource Bitmap { get; set; }
            public NormalizationRange Normalization { get; set; }
            public int FullFrameWidth { get; set; }
            public int FullFrameHeight { get; set; }
            public bool ShowsFullFrameContext { get; set; }
            public double Zoom { get; set; }
            public double HorizontalOffset { get; set; }
            public double VerticalOffset { get; set; }
            public ImageSource NavigatorPreview { get; set; }
            public string NavigatorPreviewKey { get; set; }
            public FrameConfiguration NavigatorSourceConfiguration { get; set; }

            public string Caption
            {
                get
                {
                    var expression = Profile == null ? String.Empty : Profile.Expression;
                    if (String.IsNullOrWhiteSpace(expression))
                    {
                        return "Array " + Id.ToString(CultureInfo.InvariantCulture);
                    }
                    return CompactTabCaption(expression);
                }
            }
        }

        // Display-only shortening. Never feed this back into the debugger expression.
        private static string CompactTabCaption(string expression)
        {
            var text = expression.Trim().Replace("->", ".").Replace("(", "").Replace(")", "").Replace("&", "");
            var parts = text.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1) text = parts[parts.Length - 2] + "." + parts[parts.Length - 1];
            return text.Length > 32 ? "…" + text.Substring(text.Length - 31) : text;
        }
    }
}
