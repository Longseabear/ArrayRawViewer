#include "ImageSimulator.h"

#if defined(_MSC_VER)
#include <intrin.h>
#endif

ImageSimulator::ImageSimulator()
{
    InitializeStream(input, input_storage, 96, 64, 100U);
    InitializeStream(output, output_storage, 96, 64, 200U);

    input_aux_storage.resize(2);
    input_aux_stream.resize(2);
    InitializeStream(input_aux_stream[0], input_aux_storage[0], 48, 32, 1000U);
    InitializeStream(input_aux_stream[1], input_aux_storage[1], 40, 24, 2000U);
    InitializeStream(output_aux_stream[0], output_aux_storage[0], 32, 20, 3000U);
    InitializeStream(output_aux_stream[1], output_aux_storage[1], 24, 16, 4000U);
}

void ImageSimulator::Process()
{
    // This deliberately mirrors real image processing: the debugger sees x/y
    // and a write to output.m_data[y * width + x] for every output pixel.
    for (int y = 0; y < input.m_height; ++y)
    {
        for (int x = 0; x < input.m_width; ++x)
        {
            const size_t offset = static_cast<size_t>(y) * static_cast<size_t>(input.m_width) + static_cast<size_t>(x);
            output.m_data[offset] = input.m_data[offset] * 2U;
        }
    }
}

void ImageSimulator::ProcessAndBreakForCapture()
{
#if defined(_MSC_VER)
    // A real .cpp source location is intentional: Visual Studio can otherwise
    // map an inline-header breakpoint back to the call site in main().
    // At this stop "this" is the live ImageSimulator object and output has not
    // been written, so a Viewer pixel watch can be armed before Process().
    __debugbreak();
#endif
    Process();
}

void ImageSimulator::InitializeStream(ImageStream& stream, std::vector<unsigned int>& storage,
    int width, int height, unsigned int baseValue)
{
    storage.resize(static_cast<size_t>(width) * height);
    for (size_t index = 0; index < storage.size(); ++index)
    {
        storage[index] = baseValue + static_cast<unsigned int>(index);
    }
    stream.m_width = width;
    stream.m_height = height;
    stream.m_data = storage.empty() ? 0 : &storage[0];
}
