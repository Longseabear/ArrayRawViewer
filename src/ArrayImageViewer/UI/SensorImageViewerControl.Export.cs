using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using ArrayImageViewer.Core;
using ArrayImageViewer.Debugging;

namespace ArrayImageViewer.UI
{
    internal sealed partial class SensorImageViewerControl
    {
        private void CopyKernelToClipboard(object sender, RoutedEventArgs e)
        {
            if (frame == null)
            {
                SetStatus("Load a view before copying the kernel.");
                return;
            }

            try
            {
                var configuration = ReadConfiguration();
                var kernel = GetRoiBounds(configuration);
                var text = new StringBuilder();
                for (var y = kernel.Y; y < kernel.Y + kernel.Height; y++)
                {
                    for (var x = kernel.X; x < kernel.X + kernel.Width; x++)
                    {
                        if (!IsLoadedCoordinate(x, y))
                        {
                            throw new InvalidOperationException("The kernel is outside the loaded view. Move the View ROI over it, then copy.");
                        }

                        if (x > kernel.X)
                        {
                            text.Append(' ');
                        }

                        text.Append(frame.GetRaw(x - frame.Configuration.OriginX, y - frame.Configuration.OriginY).ToString(CultureInfo.InvariantCulture));
                    }
                    if (y < kernel.Y + kernel.Height - 1)
                    {
                        text.AppendLine();
                    }
                }

                Clipboard.SetText(text.ToString());
                SetStatus("Copied " + kernel.Width.ToString(CultureInfo.InvariantCulture) + "x" + kernel.Height.ToString(CultureInfo.InvariantCulture) +
                    " kernel RAW values to the clipboard (spaces between columns, newlines between rows).");
            }
            catch (Exception exception)
            {
                SetInputError("Cannot copy kernel: " + exception.Message);
            }
        }

        private void SaveFullRaw(object sender, RoutedEventArgs e)
        {
            try
            {
                ClearAllInputErrors();
                var configuration = ReadConfiguration();
                var outputBits = configuration.TotalBits <= 16 ? 16 : 32;
                var dialog = new SaveFileDialog
                {
                    Title = "Save full sensor RAW",
                    Filter = outputBits == 16 ? "16-bit RAW (*.raw)|*.raw|All files (*.*)|*.*" : "32-bit RAW (*.raw)|*.raw|All files (*.*)|*.*",
                    DefaultExt = ".raw",
                    FileName = (expression.Text ?? "sensorRaw").Trim() + "_" + configuration.Width.ToString(CultureInfo.InvariantCulture) + "x" +
                        configuration.Height.ToString(CultureInfo.InvariantCulture) + "_" + outputBits.ToString(CultureInfo.InvariantCulture) + "b.raw"
                };
                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                DebugMemoryFrameReader.RoiReadSession memoryRead;
                if (!DebugExpressionFrameReader.TryStartMemoryRoiRead(expression.Text, configuration, 0, 0, configuration.Width, configuration.Height, out memoryRead))
                {
                    throw new InvalidOperationException("Saving a full RAW requires the native debugger-memory reader for the paused debuggee.");
                }

                BeginNewReadRequest();
                var selection = ResolveCurrentSelection(configuration);
                BeginMemoryRead(memoryRead, configuration.Width, configuration.Height, selection.X, selection.Y, false, false, expression.Text.Trim(), dialog.FileName, outputBits);
                SetStatus("Reading the complete frame once for " + outputBits.ToString(CultureInfo.InvariantCulture) + "-bit RAW export.");
            }
            catch (Exception exception)
            {
                SetInputError("Cannot save full RAW: " + exception.Message);
            }
        }

        private void WriteRawFrameAsync(FrameBuffer source, string path, int outputBits)
        {
            SetStatus("Writing " + outputBits.ToString(CultureInfo.InvariantCulture) + "-bit RAW file in the background.");
            var writer = new Thread(delegate()
            {
                try
                {
                    using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 65536, false))
                    using (var binary = new BinaryWriter(stream))
                    {
                        for (var y = 0; y < source.Configuration.Height; y++)
                        {
                            for (var x = 0; x < source.Configuration.Width; x++)
                            {
                                var raw = source.GetRaw(x, y);
                                if (outputBits == 16)
                                {
                                    binary.Write(unchecked((ushort)raw));
                                }
                                else
                                {
                                    binary.Write(unchecked((uint)raw));
                                }
                            }
                        }
                    }

                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        SetStatus("Saved " + outputBits.ToString(CultureInfo.InvariantCulture) + "-bit RAW: " + path);
                    }));
                }
                catch (Exception exception)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        SetStatus("Cannot save RAW: " + exception.Message);
                    }));
                }
            });
            writer.IsBackground = true;
            writer.Start();
        }
    }
}
