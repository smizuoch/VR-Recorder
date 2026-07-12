#include "video_processing_plan.hpp"

#include <cstdlib>
#include <iostream>

namespace {

#define CHECK(condition)                                                        \
    do {                                                                        \
        if (!(condition)) {                                                     \
            std::cerr << "check failed at " << __FILE__ << ':' << __LINE__      \
                      << ": " #condition << '\n';                              \
            std::abort();                                                       \
        }                                                                       \
    } while (false)

using namespace vrrecorder::native;

VideoSurfaceDescriptor Source(
    std::uint32_t width,
    std::uint32_t height,
    vrrec_source_pixel_format_t format =
        VRREC_SOURCE_PIXEL_FORMAT_BGRA8)
{
    return {42, width, height, format};
}

void PadsOddTextureEdgesBeforeChroma420Composition()
{
    VideoProcessingPlan plan {};
    CHECK(CreateSingleFileVideoProcessingPlan(
              Source(1'919, 1'079),
              1'920,
              1'080,
              plan) == VRREC_STATUS_OK);
    CHECK(plan.normalized_source_width == 1'920);
    CHECK(plan.normalized_source_height == 1'080);
    CHECK(plan.pad_right == 1);
    CHECK(plan.pad_bottom == 1);
    CHECK(plan.destination_width == 1'920);
    CHECK(plan.destination_height == 1'080);
    CHECK(plan.offset_x == 0);
    CHECK(plan.offset_y == 0);
    CHECK(plan.output_pixel_format == VRREC_SOURCE_PIXEL_FORMAT_NV12);
}

void ContainsPortraitVideoWithoutCropping()
{
    VideoProcessingPlan plan {};
    CHECK(CreateSingleFileVideoProcessingPlan(
              Source(1'080, 1'920),
              1'920,
              1'080,
              plan) == VRREC_STATUS_OK);
    CHECK(plan.destination_width == 606);
    CHECK(plan.destination_height == 1'080);
    CHECK(plan.offset_x == 657);
    CHECK(plan.offset_y == 0);
}

void MarksRgbaInputForRedBlueChannelNormalization()
{
    VideoProcessingPlan plan {};
    CHECK(CreateSingleFileVideoProcessingPlan(
              Source(1'280, 720, VRREC_SOURCE_PIXEL_FORMAT_RGBA8),
              1'920,
              1'080,
              plan) == VRREC_STATUS_OK);
    CHECK(plan.swap_red_blue_channels);
    CHECK(plan.input_pixel_format == VRREC_SOURCE_PIXEL_FORMAT_RGBA8);
}

void RejectsOddOrZeroOutputCanvas()
{
    VideoProcessingPlan plan {};
    CHECK(CreateSingleFileVideoProcessingPlan(
              Source(1'920, 1'080),
              1'919,
              1'080,
              plan) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(CreateSingleFileVideoProcessingPlan(
              Source(1'920, 1'080),
              0,
              1'080,
              plan) == VRREC_STATUS_INVALID_ARGUMENT);
}

}

int main()
{
    PadsOddTextureEdgesBeforeChroma420Composition();
    ContainsPortraitVideoWithoutCropping();
    MarksRgbaInputForRedBlueChannelNormalization();
    RejectsOddOrZeroOutputCanvas();
    return 0;
}
