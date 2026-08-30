cmake_minimum_required(VERSION 3.20)
project(ArrayRawViewerNativeTests LANGUAGES CXX)

include(FetchContent)
FetchContent_Declare(
  googletest
  URL https://github.com/google/googletest/archive/refs/tags/v1.14.0.zip
  DOWNLOAD_EXTRACT_TIMESTAMP TRUE
)
set(gtest_force_shared_crt ON CACHE BOOL "" FORCE)
FetchContent_MakeAvailable(googletest)

enable_testing()
add_executable(ImageSimulatorTests ImageSimulatorTests.cpp ../../samples/SensorRawDebuggee/ImageSimulator.cpp)
target_include_directories(ImageSimulatorTests PRIVATE ../../samples/SensorRawDebuggee)
target_compile_features(ImageSimulatorTests PRIVATE cxx_std_11)
target_link_libraries(ImageSimulatorTests PRIVATE GTest::gtest_main)
include(GoogleTest)
gtest_discover_tests(ImageSimulatorTests)
