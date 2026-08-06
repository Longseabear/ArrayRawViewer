#include <cstdint>
#include <intrin.h>
#include <iostream>

namespace
{
    constexpr int ImageWidth = 4096;
    constexpr int ImageHeight = 3072;
    constexpr int PreviewWidth = 128;
    constexpr int PreviewHeight = 128;

    uint32_t MakeSample(int x, int y)
    {
        // A deterministic 13-bit GRBG test pattern. The channel offset makes
        // Bayer placement immediately visible in raw and composite modes.
        const auto ramp = static_cast<uint32_t>((x * 4095 / (ImageWidth - 1)) +
                                                (y * 4095 / (ImageHeight - 1)));
        const bool evenX = (x & 1) == 0;
        const bool evenY = (y & 1) == 0;
        const uint32_t channelOffset = evenY ? (evenX ? 700 : 1800) : (evenX ? 250 : 900);
        return (ramp / 2 + channelOffset) & 0x1FFF;
    }
}

int main()
{
    // These volatile locals deliberately remain materialized at __debugbreak.
    // The viewer can discover them from LOCAL VALUES and reevaluate them on
    // every update; namespace constexpr values are often optimized away.
    volatile int imageWidth = ImageWidth;
    volatile int imageHeight = ImageHeight;
    volatile int rowStride = imageWidth;
    volatile int centerX = imageWidth / 2;
    volatile int centerY = imageHeight / 2;

    // Keep these pointers in scope at the breakpoint. In Visual Studio, use
    // sensorRaw for the 4096x3072 source and previewRaw for the immediately
    // readable 128x128 preview supported by the draft extension reader.
    uint32_t* sensorRaw = new uint32_t[static_cast<size_t>(imageWidth) * imageHeight];
    uint32_t* previewRaw = new uint32_t[static_cast<size_t>(PreviewWidth) * PreviewHeight];

    for (int y = 0; y < imageHeight; ++y)
    {
        for (int x = 0; x < imageWidth; ++x)
        {
            sensorRaw[static_cast<size_t>(y) * rowStride + x] = MakeSample(x, y);
        }
    }

    for (int y = 0; y < PreviewHeight; ++y)
    {
        for (int x = 0; x < PreviewWidth; ++x)
        {
            previewRaw[static_cast<size_t>(y) * PreviewWidth + x] = sensorRaw[static_cast<size_t>(y) * rowStride + x];
        }
    }

    std::cout << "Break now. sensorRaw is " << imageWidth << "x" << imageHeight
              << " GRBG; previewRaw is 128x128." << std::endl;
    __debugbreak();

    // Prevent the compiler from considering the allocations unused after the
    // breakpoint in optimized configurations.
    std::cout << "First sample: " << sensorRaw[0] << std::endl;
    delete[] previewRaw;
    delete[] sensorRaw;
    return 0;
}
