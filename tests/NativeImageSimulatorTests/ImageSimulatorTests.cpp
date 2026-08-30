#include "ImageSimulator.h"
#include <gtest/gtest.h>

TEST(ImageSimulatorCaptureSample, ExposesDirectVectorAndArrayImageStreams)
{
    ImageSimulator simulator;

    EXPECT_EQ(96, simulator.input.m_width);
    EXPECT_EQ(64, simulator.input.m_height);
    ASSERT_NE(static_cast<unsigned int*>(0), simulator.input.m_data);
    EXPECT_EQ(100U, simulator.input.m_data[0]);

    ASSERT_EQ(static_cast<size_t>(2), simulator.input_aux_stream.size());
    EXPECT_EQ(48, simulator.input_aux_stream[0].m_width);
    EXPECT_EQ(32, simulator.input_aux_stream[0].m_height);
    EXPECT_EQ(1000U, simulator.input_aux_stream[0].m_data[0]);
    EXPECT_EQ(40, simulator.input_aux_stream[1].m_width);
    EXPECT_EQ(24, simulator.input_aux_stream[1].m_height);
    EXPECT_EQ(2000U, simulator.input_aux_stream[1].m_data[0]);

    EXPECT_EQ(32, simulator.output_aux_stream[0].m_width);
    EXPECT_EQ(20, simulator.output_aux_stream[0].m_height);
    EXPECT_EQ(3000U, simulator.output_aux_stream[0].m_data[0]);
    EXPECT_EQ(24, simulator.output_aux_stream[1].m_width);
    EXPECT_EQ(16, simulator.output_aux_stream[1].m_height);
    EXPECT_EQ(4000U, simulator.output_aux_stream[1].m_data[0]);
}

TEST(ImageSimulatorCaptureSample, ProcessingWritesOutputPointer)
{
    ImageSimulator simulator;
    simulator.Process();

    EXPECT_EQ(simulator.input.m_data[0] * 2U, simulator.output.m_data[0]);
    EXPECT_EQ(simulator.input.m_data[123] * 2U, simulator.output.m_data[123]);
    const int x = 27;
    const int y = 11;
    const size_t offset = static_cast<size_t>(y) * static_cast<size_t>(simulator.input.m_width) + static_cast<size_t>(x);
    EXPECT_EQ(simulator.input.m_data[offset] * 2U, simulator.output.m_data[offset]);
}
