#include <cstdint>
#include <intrin.h>
#include <iostream>
#include "ImageSimulator.h"

namespace
{
    constexpr int ImageWidth = 4096;
    constexpr int ImageHeight = 3072;
    constexpr int PaddedWidth = 640;
    constexpr int PaddedHeight = 480;
    constexpr int PaddedStride = 672;
    constexpr int SmallWidth = 320;
    constexpr int SmallHeight = 240;

    uint32_t ChannelOffsetForGrbg(int x, int y)
    {
        const bool evenX = (x & 1) == 0;
        const bool evenY = (y & 1) == 0;

        // GRBG: Gr, R / B, Gb. Make every site visibly different in the
        // BayerRaw viewer while still leaving a smooth image-wide ramp.
        return evenY ? (evenX ? 700U : 2600U) : (evenX ? 250U : 1200U);
    }

    uint32_t MakeGrbg13BitSample(int x, int y, int width, int height)
    {
        const uint32_t xRamp = static_cast<uint32_t>(x * 4095 / (width - 1));
        const uint32_t yRamp = static_cast<uint32_t>(y * 4095 / (height - 1));
        return (xRamp / 3 + yRamp / 3 + ChannelOffsetForGrbg(x, y)) & 0x1FFF;
    }

    uint16_t MakeGroupedGrbg12BitSample(int x, int y, int groupSize)
    {
        // Tetra treats each Bayer site as a groupSize x groupSize block.
        // groupSize=2 is Tetra; groupSize=4 is TetraSquare.
        const int bayerX = x / groupSize;
        const int bayerY = y / groupSize;
        const uint32_t ramp = static_cast<uint32_t>((x * 11 + y * 7) & 0x7FF);
        return static_cast<uint16_t>((ramp + ChannelOffsetForGrbg(bayerX, bayerY)) & 0x0FFF);
    }

    int MakeSignedScoreQ8_8(int x, int y)
    {
        // A signed Q8.8 Gray map. Its negative diagonal and positive center
        // make signed decoding and Loaded-data normalization easy to check.
        const int distance = (x - SmallWidth / 2) * (x - SmallWidth / 2) +
                             (y - SmallHeight / 2) * (y - SmallHeight / 2);
        return 24000 - distance * 2 + ((x * 37 + y * 19) & 0xFF) - 128;
    }

    uint8_t MakeDefectMask(int x, int y)
    {
        const bool gridLine = (x % 31) == 0 || (y % 29) == 0;
        const bool hotPixel = ((x * 97 + y * 71) % 211) == 0;
        return static_cast<uint8_t>(hotPixel ? 255 : (gridLine ? 96 : 8));
    }

    void ProcessRawFrame(const uint32_t* inputBuffer, uint16_t* outputBuffer,
        int width, int height, int stride, int blackLevel, int gainQ8)
    {
        // A deliberately simple processing stage: black-level subtraction,
        // Q8 digital gain, then 14-bit clipping. The viewer can compare the
        // input and output pointers at the same coordinates after this returns.
        for (int y = 0; y < height; ++y)
        {
            for (int x = 0; x < width; ++x)
            {
                const size_t offset = static_cast<size_t>(y) * stride + x;
                const int blackCorrected = static_cast<int>(inputBuffer[offset]) - blackLevel;
                const int gained = blackCorrected > 0 ? (blackCorrected * gainQ8 + 128) / 256 : 0;
                outputBuffer[offset] = static_cast<uint16_t>(gained > 0x3FFF ? 0x3FFF : gained);
            }
        }
    }
}

int main()
{
    // All dimensions and centers are locals on purpose: choose them from
    // LOCAL VALUES or enter the expressions directly in the viewer.
    volatile int imageWidth = ImageWidth;
    volatile int imageHeight = ImageHeight;
    volatile int rowStride = imageWidth;
    volatile int inputWidth = imageWidth;
    volatile int inputHeight = imageHeight;
    volatile int inputStride = rowStride;
    volatile int outputWidth = imageWidth;
    volatile int outputHeight = imageHeight;
    volatile int outputStride = rowStride;
    volatile int blackLevel = 64;
    volatile int digitalGainQ8 = 384;
    volatile int centerX = 1978;
    volatile int centerY = 369;
    volatile int roiWidth = 5;
    volatile int roiHeight = 5;

    volatile int paddedWidth = PaddedWidth;
    volatile int paddedHeight = PaddedHeight;
    volatile int paddedStride = PaddedStride;
    volatile int paddedCenterX = PaddedWidth / 2;
    volatile int paddedCenterY = PaddedHeight / 2;

    volatile int smallWidth = SmallWidth;
    volatile int smallHeight = SmallHeight;
    volatile int smallStride = SmallWidth;

    // 1. Full-size before/after pipeline pair. The input deliberately uses
    // UInt32 storage while the processed output uses UInt16 storage.
    uint32_t* inputBuffer = new uint32_t[static_cast<size_t>(imageWidth) * imageHeight];
    uint16_t* outputBuffer = new uint16_t[static_cast<size_t>(imageWidth) * imageHeight];
    uint32_t* sensorRaw = inputBuffer;

    // 2. Padded rows: only paddedWidth samples are pixels. Padding contains
    // a conspicuous sentinel and must not appear in a rendered image.
    uint16_t* paddedRaw = new uint16_t[static_cast<size_t>(paddedStride) * paddedHeight];

    // 3. Gray generic data: an int* signed Q8.8 score/loss-like map.
    int* scoreMap = new int[static_cast<size_t>(smallWidth) * smallHeight];

    // 4. Physical Bayer grouping examples, both 12-bit unsigned values.
    uint16_t* tetraRaw = new uint16_t[static_cast<size_t>(smallWidth) * smallHeight];
    uint16_t* tetraSquareRaw = new uint16_t[static_cast<size_t>(smallWidth) * smallHeight];

    // 5. A small unsigned 8-bit Gray map for Element-type verification.
    uint8_t* defectMask = new uint8_t[static_cast<size_t>(smallWidth) * smallHeight];

    // 6. Object-graph capture sample. Its breakpoint lives inside
    // ImageSimulator::ProcessAndBreakForCapture, where the debugger expression "this"
    // denotes this ImageSimulator instance.
    ImageSimulator imageSimulator;

    for (int y = 0; y < imageHeight; ++y)
    {
        for (int x = 0; x < imageWidth; ++x)
        {
            inputBuffer[static_cast<size_t>(y) * rowStride + x] = MakeGrbg13BitSample(x, y, imageWidth, imageHeight);
        }
    }

    ProcessRawFrame(inputBuffer, outputBuffer, imageWidth, imageHeight, rowStride, blackLevel, digitalGainQ8);

    for (int y = 0; y < paddedHeight; ++y)
    {
        for (int x = 0; x < paddedStride; ++x)
        {
            paddedRaw[static_cast<size_t>(y) * paddedStride + x] =
                x < paddedWidth ? static_cast<uint16_t>(MakeGrbg13BitSample(x, y, paddedWidth, paddedHeight) >> 1) : 0xDEAD;
        }
    }

    for (int y = 0; y < smallHeight; ++y)
    {
        for (int x = 0; x < smallWidth; ++x)
        {
            const size_t offset = static_cast<size_t>(y) * smallStride + x;
            scoreMap[offset] = MakeSignedScoreQ8_8(x, y);
            tetraRaw[offset] = MakeGroupedGrbg12BitSample(x, y, 2);
            tetraSquareRaw[offset] = MakeGroupedGrbg12BitSample(x, y, 4);
            defectMask[offset] = MakeDefectMask(x, y);
        }
    }

    // Direct aliases make the original int* A / unsigned int* B workflow
    // visible in the Pointer list at the breakpoint.
    int* A = scoreMap;
    unsigned int* B = inputBuffer;

    // Never make the viewer evaluate a function such as owner.GetPointer().
    // Keep a normal pointer local (for example cachedSensorPointer) instead.
    uint32_t* cachedSensorPointer = inputBuffer;

    // Break inside a non-static class method so the debugger expression
    // "this" denotes the ImageSimulator instance for structure capture.
    imageSimulator.ProcessAndBreakForCapture();

    std::cout << "Break now. Useful viewer expressions:\n"
              << "  B, inputBuffer or sensorRaw: 4096x3072, stride 4096, UInt32, 13.0b, GRBG BayerRaw\n"
              << "  outputBuffer: processed 4096x3072, UInt16, 14.0b, GRBG BayerRaw\n"
              << "  paddedRaw: 640x480, stride 672, UInt16, 12.0b, GRBG BayerRaw\n"
              << "  A or scoreMap: 320x240, stride 320, Int32, signed 8.8b, Gray\n"
              << "  tetraRaw: 320x240, UInt16, 12.0b, GRBG Tetra, BayerRaw\n"
              << "  tetraSquareRaw: 320x240, UInt16, 12.0b, GRBG TetraSquare, BayerRaw\n"
              << "  defectMask: 320x240, UInt8, 8.0b, Gray\n"
              << "  Structure capture: at the earlier ImageSimulator breakpoint, create template ImageStream\n"
              << "    RAW=m_data, Width=m_width, Height=m_height; root=this; Capture structures.\n";
    __debugbreak();

    // Keep every allocation and alias alive after the breakpoint, including
    // when this project is built in Release configuration.
    const int localValueChecksum = imageWidth + imageHeight + rowStride + inputWidth + inputHeight + inputStride +
        outputWidth + outputHeight + outputStride + blackLevel + digitalGainQ8 + centerX + centerY + roiWidth + roiHeight +
        paddedWidth + paddedHeight + paddedStride + paddedCenterX + paddedCenterY + smallWidth + smallHeight + smallStride;
    const unsigned int checksum = B[0] + sensorRaw[1] + cachedSensorPointer[1] + outputBuffer[2] + paddedRaw[2] +
        static_cast<unsigned int>(A[3]) + tetraRaw[4] + tetraSquareRaw[5] + defectMask[6] +
        imageSimulator.output.m_data[3] + imageSimulator.input_aux_stream[1].m_data[4] +
        imageSimulator.output_aux_stream[0].m_data[5] + static_cast<unsigned int>(localValueChecksum);
    std::cout << "Checksum: " << checksum << std::endl;

    delete[] defectMask;
    delete[] tetraSquareRaw;
    delete[] tetraRaw;
    delete[] scoreMap;
    delete[] paddedRaw;
    delete[] outputBuffer;
    delete[] inputBuffer;
    return 0;
}
