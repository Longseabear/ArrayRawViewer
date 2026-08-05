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
        private const int ElementSizeInBytes = 4;
        private const uint ParseExpression = 1;
        private const uint EvaluateWithoutFunctionCalls = 0x00000002 | 0x00000004 | 0x00000080;

        public static bool TryReadRoi(object currentStackFrame, string expression, FrameConfiguration sourceConfiguration,
            int originX, int originY, int roiWidth, int roiHeight, out FrameBuffer frame)
        {
            frame = null;
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

                var roiConfiguration = new FrameConfiguration(roiWidth, roiHeight, roiWidth,
                    sourceConfiguration.IntegerBits, sourceConfiguration.FractionalBits, sourceConfiguration.IsSigned,
                    sourceConfiguration.PixelOrder, sourceConfiguration.PixelType, sourceConfiguration.VisualizeChannel,
                    originX, originY);
                var samples = new long[checked(roiWidth * roiHeight)];
                var bytesPerRow = checked(roiWidth * ElementSizeInBytes);
                for (var y = 0; y < roiHeight; y++)
                {
                    var byteOffset = checked(((long)(originY + y) * sourceConfiguration.Stride + originX) * ElementSizeInBytes);
                    IDebugMemoryContext2 rowContext;
                    if (byteOffset < 0 || Failed(memoryContext.Add((ulong)byteOffset, out rowContext)) || rowContext == null)
                    {
                        return false;
                    }

                    var row = new byte[bytesPerRow];
                    uint bytesRead;
                    uint unreadable = 0;
                    if (Failed(memoryBytes.ReadAt(rowContext, (uint)bytesPerRow, row, out bytesRead, ref unreadable)) ||
                        bytesRead != (uint)bytesPerRow || unreadable != 0)
                    {
                        return false;
                    }

                    for (var x = 0; x < roiWidth; x++)
                    {
                        samples[y * roiWidth + x] = DecodeInt32(row, x * ElementSizeInBytes, sourceConfiguration.IsSigned);
                    }
                }

                frame = new FrameBuffer(roiConfiguration, samples);
                return true;
            }
            catch (Exception)
            {
                // Engines vary considerably in their IDebug* support. Falling
                // back keeps the viewer usable for older/third-party engines.
                frame = null;
                return false;
            }
        }

        private static long DecodeInt32(byte[] bytes, int offset, bool isSigned)
        {
            var value = (uint)(bytes[offset] |
                (bytes[offset + 1] << 8) |
                (bytes[offset + 2] << 16) |
                (bytes[offset + 3] << 24));
            return isSigned ? unchecked((int)value) : (long)value;
        }

        private static bool Failed(int hr)
        {
            return hr < 0;
        }
    }
}
