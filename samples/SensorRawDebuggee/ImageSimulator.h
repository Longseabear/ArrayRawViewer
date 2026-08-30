#pragma once

#include <cstddef>
#include <vector>

// Intentionally minimal viewer target. Register this as:
//   class: ImageStream, RAW: m_data, Width: m_width, Height: m_height
struct ImageStream
{
    int m_width;
    int m_height;
    unsigned int* m_data;
};

struct ImageSimulator
{
    ImageStream input;
    ImageStream output;
    std::vector<ImageStream> input_aux_stream;
    ImageStream output_aux_stream[2];

    // Ownership intentionally belongs to the parent. ImageStream stays a
    // simple width/height/raw-pointer type, just like real integration types.
    std::vector<unsigned int> input_storage;
    std::vector<unsigned int> output_storage;
    std::vector<std::vector<unsigned int> > input_aux_storage;
    std::vector<unsigned int> output_aux_storage[2];

    ImageSimulator();
    void Process();
    void ProcessAndBreakForCapture();

private:
    static void InitializeStream(ImageStream& stream, std::vector<unsigned int>& storage,
        int width, int height, unsigned int baseValue);
};
