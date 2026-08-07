using System;
using Microsoft.VisualStudio.Debugger.Interop;
using ArrayImageViewer.Core;

namespace ArrayImageViewer.Debugging
{
    // Uses the debugger's memory interface when the active engine exposes the
    // current stack frame as IDebugStackFrame2. This avoids one expression
    // evaluation per sample while retaining DebugExpressionFrameReader as a
    // fallback for engines that do not support memory contexts.
    internal static class DebugMemoryFrameReader
    {
        private const uint ParseExpression = 1;
        private const uint EvaluateWithoutFunctionCalls = 0x00000002 | 0x00000004 | 0x00000080;

        internal interface IFrameReadSession
        {
            int RowsRead { get; }
            int TotalRows { get; }
            int Width { get; }
            int BytesPerRow { get; }
            bool IsComplete { get; }
            string ErrorMessage { get; }
            bool TryReadRows(int maximumRows);
            FrameBuffer CreateFrame();
        }

        public static bool TryStartRoiRead(object currentStackFrame, string expression, FrameConfiguration sourceConfiguration,
            int originX, int originY, int roiWidth, int roiHeight, out RoiReadSession session)
        {
            session = null;
            var stackFrame = currentStackFrame as IDebugStackFrame2;
            if (stackFrame == null)
            {
                return false;
            }

            try
            {
                IDebugExpressionContext2 expressionContext;
                if (Failed(stackFrame.GetExpressionContext(out expressionContext)) || expressionContext == null)
                {
                    return false;
                }

                IDebugExpression2 debugExpression;
                string parseError;
                uint errorIndex;
                if (Failed(expressionContext.ParseText(expression, ParseExpression, 10, out debugExpression, out parseError, out errorIndex)) || debugExpression == null)
                {
                    return false;
                }

                IDebugProperty2 property;
                if (Failed(debugExpression.EvaluateSync(EvaluateWithoutFunctionCalls, 2000, null, out property)) || property == null)
                {
                    return false;
                }

                IDebugMemoryBytes2 memoryBytes;
                IDebugMemoryContext2 memoryContext;
                if (Failed(property.GetMemoryBytes(out memoryBytes)) || memoryBytes == null ||
                    Failed(property.GetMemoryContext(out memoryContext)) || memoryContext == null)
                {
                    return false;
                }

                session = new RoiReadSession(memoryBytes, memoryContext, sourceConfiguration, originX, originY, roiWidth, roiHeight);
                return true;
            }
            catch (Exception)
            {
                // Engines vary considerably in their IDebug* support. Falling
                // back keeps the viewer usable for older/third-party engines.
                session = null;
                return false;
            }
        }

        // Some native engines expose the evaluated DTE expression as the
        // underlying IDebugProperty2 even when their DTE StackFrame wrapper
        // cannot be queried for IDebugStackFrame2.
        public static bool TryStartRoiReadFromProperty(object evaluatedExpression, FrameConfiguration sourceConfiguration,
            int originX, int originY, int roiWidth, int roiHeight, out RoiReadSession session)
        {
            session = null;
            var property = evaluatedExpression as IDebugProperty2;
            if (property == null)
            {
                return false;
            }

            try
            {
                IDebugMemoryBytes2 memoryBytes;
                IDebugMemoryContext2 memoryContext;
                if (Failed(property.GetMemoryBytes(out memoryBytes)) || memoryBytes == null ||
                    Failed(property.GetMemoryContext(out memoryContext)) || memoryContext == null)
                {
                    return false;
                }

                session = new RoiReadSession(memoryBytes, memoryContext, sourceConfiguration, originX, originY, roiWidth, roiHeight);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool TryStartDecimatedRead(object currentStackFrame, string expression, FrameConfiguration sourceConfiguration,
            int targetWidth, int targetHeight, out IFrameReadSession session)
        {
            session = null;
            var stackFrame = currentStackFrame as IDebugStackFrame2;
            if (stackFrame == null)
            {
                return false;
            }

            try
            {
                IDebugExpressionContext2 expressionContext;
                if (Failed(stackFrame.GetExpressionContext(out expressionContext)) || expressionContext == null)
                {
                    return false;
                }

                IDebugExpression2 debugExpression;
                string parseError;
                uint errorIndex;
                if (Failed(expressionContext.ParseText(expression, ParseExpression, 10, out debugExpression, out parseError, out errorIndex)) || debugExpression == null)
                {
                    return false;
                }

                IDebugProperty2 property;
                if (Failed(debugExpression.EvaluateSync(EvaluateWithoutFunctionCalls, 2000, null, out property)) || property == null)
                {
                    return false;
                }

                return TryStartDecimatedReadFromProperty(property, sourceConfiguration, targetWidth, targetHeight, out session);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool TryStartDecimatedReadFromProperty(object evaluatedExpression, FrameConfiguration sourceConfiguration,
            int targetWidth, int targetHeight, out IFrameReadSession session)
        {
            session = null;
            var property = evaluatedExpression as IDebugProperty2;
            if (property == null)
            {
                return false;
            }

            try
            {
                IDebugMemoryBytes2 memoryBytes;
                IDebugMemoryContext2 memoryContext;
                if (Failed(property.GetMemoryBytes(out memoryBytes)) || memoryBytes == null ||
                    Failed(property.GetMemoryContext(out memoryContext)) || memoryContext == null)
                {
                    return false;
                }

                session = new DecimatedPreviewSession(memoryBytes, memoryContext, sourceConfiguration, targetWidth, targetHeight);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool TryReadRoi(object currentStackFrame, string expression, FrameConfiguration sourceConfiguration,
            int originX, int originY, int roiWidth, int roiHeight, out FrameBuffer frame)
        {
            frame = null;
            RoiReadSession session;
            if (!TryStartRoiRead(currentStackFrame, expression, sourceConfiguration, originX, originY, roiWidth, roiHeight, out session))
            {
                return false;
            }

            if (!session.TryReadRows(roiHeight))
            {
                return false;
            }

            frame = session.CreateFrame();
            return true;
        }

        // Debugger memory interfaces are generally apartment-bound.  The session
        // keeps every read on the VS UI thread, but lets the caller yield between
        // row batches so a full-frame preview does not monopolize that thread.
        internal sealed class RoiReadSession : IFrameReadSession
        {
            private readonly IDebugMemoryBytes2 memoryBytes;
            private readonly IDebugMemoryContext2 memoryContext;
            private readonly FrameConfiguration sourceConfiguration;
            private readonly FrameConfiguration roiConfiguration;
            private readonly int originX;
            private readonly int originY;
            private readonly int width;
            private readonly int height;
            private readonly int bytesPerRow;
            private readonly long[] samples;
            private int nextRow;
            private string errorMessage;

            internal RoiReadSession(IDebugMemoryBytes2 memoryBytesValue, IDebugMemoryContext2 memoryContextValue,
                FrameConfiguration sourceConfigurationValue, int originXValue, int originYValue, int widthValue, int heightValue)
            {
                memoryBytes = memoryBytesValue;
                memoryContext = memoryContextValue;
                sourceConfiguration = sourceConfigurationValue;
                originX = originXValue;
                originY = originYValue;
                width = widthValue;
                height = heightValue;
                roiConfiguration = new FrameConfiguration(width, height, width,
                    sourceConfiguration.IntegerBits, sourceConfiguration.FractionalBits, sourceConfiguration.IsSigned,
                    sourceConfiguration.PixelOrder, sourceConfiguration.PixelType, sourceConfiguration.VisualizeChannel,
                    originX, originY, sourceConfiguration.SourceElementType);
                samples = new long[checked(width * height)];
                bytesPerRow = checked(width * sourceConfiguration.ElementSizeInBytes);
            }

            public int RowsRead { get { return nextRow; } }
            public int TotalRows { get { return height; } }
            public int Width { get { return width; } }
            public int BytesPerRow { get { return bytesPerRow; } }
            public bool IsComplete { get { return nextRow == height; } }
            public string ErrorMessage { get { return errorMessage; } }

            public bool TryReadRows(int maximumRows)
            {
                if (maximumRows <= 0)
                {
                    throw new ArgumentOutOfRangeException("maximumRows");
                }

                try
                {
                    var finalRow = Math.Min(height, checked(nextRow + maximumRows));
                    while (nextRow < finalRow)
                    {
                        var byteOffset = checked(((long)(originY + nextRow) * sourceConfiguration.Stride + originX) * sourceConfiguration.ElementSizeInBytes);
                        IDebugMemoryContext2 rowContext;
                        if (byteOffset < 0 || Failed(memoryContext.Add((ulong)byteOffset, out rowContext)) || rowContext == null)
                        {
                            errorMessage = "The debugger could not address a requested image row.";
                            return false;
                        }

                        var row = new byte[bytesPerRow];
                        uint bytesRead;
                        uint unreadable = 0;
                        if (Failed(memoryBytes.ReadAt(rowContext, (uint)bytesPerRow, row, out bytesRead, ref unreadable)) ||
                            bytesRead != (uint)bytesPerRow || unreadable != 0)
                        {
                            errorMessage = "The debugger reported unreadable memory in row " + nextRow + ".";
                            return false;
                        }

                        for (var x = 0; x < width; x++)
                        {
                            samples[nextRow * width + x] = SourceElementCodec.DecodeLittleEndian(row,
                                x * sourceConfiguration.ElementSizeInBytes, sourceConfiguration.SourceElementType);
                        }

                        nextRow++;
                    }

                    return true;
                }
                catch (Exception exception)
                {
                    errorMessage = "Native debugger-memory read failed: " + exception.Message;
                    return false;
                }
            }

            public FrameBuffer CreateFrame()
            {
                if (!IsComplete)
                {
                    throw new InvalidOperationException("The debugger-memory read is not complete.");
                }

                return new FrameBuffer(roiConfiguration, samples);
            }
        }

        // Reads one source row for each navigator row, then picks evenly-spaced
        // samples from it. This keeps the overview to a few MiB even for a
        // multi-megapixel frame while preserving full-frame position.
        private sealed class DecimatedPreviewSession : IFrameReadSession
        {
            private readonly IDebugMemoryBytes2 memoryBytes;
            private readonly IDebugMemoryContext2 memoryContext;
            private readonly FrameConfiguration sourceConfiguration;
            private readonly FrameConfiguration previewConfiguration;
            private readonly int previewWidth;
            private readonly int previewHeight;
            private readonly int sourceRowBytes;
            private readonly long[] samples;
            private int nextRow;
            private string errorMessage;

            public DecimatedPreviewSession(IDebugMemoryBytes2 memoryBytesValue, IDebugMemoryContext2 memoryContextValue,
                FrameConfiguration sourceConfigurationValue, int targetWidth, int targetHeight)
            {
                memoryBytes = memoryBytesValue;
                memoryContext = memoryContextValue;
                sourceConfiguration = sourceConfigurationValue;
                previewWidth = Math.Max(1, Math.Min(targetWidth, sourceConfiguration.Width));
                previewHeight = Math.Max(1, Math.Min(targetHeight, sourceConfiguration.Height));
                previewConfiguration = new FrameConfiguration(previewWidth, previewHeight, previewWidth,
                    sourceConfiguration.IntegerBits, sourceConfiguration.FractionalBits, sourceConfiguration.IsSigned,
                    sourceConfiguration.PixelOrder, sourceConfiguration.PixelType, VisualizeChannel.Gray, 0, 0, sourceConfiguration.SourceElementType);
                sourceRowBytes = checked(sourceConfiguration.Width * sourceConfiguration.ElementSizeInBytes);
                samples = new long[checked(previewWidth * previewHeight)];
            }

            public int RowsRead { get { return nextRow; } }
            public int TotalRows { get { return previewHeight; } }
            public int Width { get { return previewWidth; } }
            public int BytesPerRow { get { return sourceRowBytes; } }
            public bool IsComplete { get { return nextRow == previewHeight; } }
            public string ErrorMessage { get { return errorMessage; } }

            public bool TryReadRows(int maximumRows)
            {
                if (maximumRows <= 0)
                {
                    throw new ArgumentOutOfRangeException("maximumRows");
                }

                try
                {
                    var finalRow = Math.Min(previewHeight, checked(nextRow + maximumRows));
                    while (nextRow < finalRow)
                    {
                        var sourceY = GetSourceCoordinate(nextRow, previewHeight, sourceConfiguration.Height);
                        var byteOffset = checked((long)sourceY * sourceConfiguration.Stride * sourceConfiguration.ElementSizeInBytes);
                        IDebugMemoryContext2 rowContext;
                        if (byteOffset < 0 || Failed(memoryContext.Add((ulong)byteOffset, out rowContext)) || rowContext == null)
                        {
                            errorMessage = "The debugger could not address a frame-map row.";
                            return false;
                        }

                        var row = new byte[sourceRowBytes];
                        uint bytesRead;
                        uint unreadable = 0;
                        if (Failed(memoryBytes.ReadAt(rowContext, (uint)sourceRowBytes, row, out bytesRead, ref unreadable)) ||
                            bytesRead != (uint)sourceRowBytes || unreadable != 0)
                        {
                            errorMessage = "The debugger reported unreadable memory in frame-map row " + sourceY + ".";
                            return false;
                        }

                        for (var previewX = 0; previewX < previewWidth; previewX++)
                        {
                            var sourceX = GetSourceCoordinate(previewX, previewWidth, sourceConfiguration.Width);
                            samples[nextRow * previewWidth + previewX] = SourceElementCodec.DecodeLittleEndian(row,
                                sourceX * sourceConfiguration.ElementSizeInBytes, sourceConfiguration.SourceElementType);
                        }
                        nextRow++;
                    }
                    return true;
                }
                catch (Exception exception)
                {
                    errorMessage = "Frame-map read failed: " + exception.Message;
                    return false;
                }
            }

            public FrameBuffer CreateFrame()
            {
                if (!IsComplete)
                {
                    throw new InvalidOperationException("The frame-map read is not complete.");
                }
                return new FrameBuffer(previewConfiguration, samples);
            }

            private static int GetSourceCoordinate(int previewCoordinate, int previewLength, int sourceLength)
            {
                if (previewLength <= 1)
                {
                    return sourceLength / 2;
                }
                return (int)((long)previewCoordinate * (sourceLength - 1) / (previewLength - 1));
            }
        }

        private static bool Failed(int hr)
        {
            return hr < 0;
        }
    }
}
