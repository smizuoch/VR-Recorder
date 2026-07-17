#include "video_processing_plan.hpp"

#include <cstdlib>
#include <iostream>
#include <limits>

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

void RejectsEverySingleFilePlanBoundary()
{
    VideoProcessingPlan plan {};
    const auto rejects = [&](VideoSurfaceDescriptor source,
                             std::uint32_t width,
                             std::uint32_t height) {
        plan.destination_width = 99;
        CHECK(CreateSingleFileVideoProcessingPlan(
                  source, width, height, plan) ==
              VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(plan.destination_width == 0);
    };
    rejects(Source(0, 1'080), 1'920, 1'080);
    rejects(Source(1'920, 0), 1'920, 1'080);
    rejects(
        Source(std::numeric_limits<std::uint32_t>::max(), 1'080),
        1'920,
        1'080);
    rejects(
        Source(1'920, std::numeric_limits<std::uint32_t>::max()),
        1'920,
        1'080);
    rejects(Source(1'920, 1'080), 1'920, 0);
    rejects(Source(1'920, 1'080), 1'920, 1'079);
    rejects(
        Source(1'920, 1'080, static_cast<vrrec_source_pixel_format_t>(99)),
        1'920,
        1'080);
    rejects(
        Source(1, std::numeric_limits<std::uint32_t>::max() - 1U),
        2,
        2);

    for (const auto format : {
             VRREC_SOURCE_PIXEL_FORMAT_BGRA8,
             VRREC_SOURCE_PIXEL_FORMAT_RGBA8,
             VRREC_SOURCE_PIXEL_FORMAT_NV12,
         }) {
        CHECK(CreateSingleFileVideoProcessingPlan(
                  Source(1'920, 1'080, format),
                  1'920,
                  1'080,
                  plan) == VRREC_STATUS_OK);
        CHECK(plan.input_pixel_format == format);
    }
}

void RejectsExplicitDestinationsOutsideTheCanvas()
{
    VideoProcessingPlan plan {};
    const vrrec_video_layout_v1 layout {
        sizeof(vrrec_video_layout_v1),
        VRREC_ABI_V1,
        1'920,
        1'080,
        1'920,
        1'080,
        1'900,
        0,
        100,
        1'080,
        VRREC_CANVAS_BACKGROUND_BLACK,
        VRREC_VIDEO_ROTATION_NONE,
    };

    CHECK(CreateExplicitVideoProcessingPlan(
              Source(1'920, 1'080),
              layout,
              plan) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(plan.destination_width == 0);
    CHECK(plan.destination_height == 0);
}

void RejectsExplicitLayoutsFromAnUnknownAbiContract()
{
    auto layout = vrrec_video_layout_v1 {
        sizeof(vrrec_video_layout_v1),
        VRREC_ABI_V1,
        1'920,
        1'080,
        1'920,
        1'080,
        0,
        0,
        1'920,
        1'080,
        VRREC_CANVAS_BACKGROUND_BLACK,
        VRREC_VIDEO_ROTATION_NONE,
    };
    VideoProcessingPlan plan {};

    layout.struct_size -= 1;
    CHECK(CreateExplicitVideoProcessingPlan(
              Source(1'920, 1'080),
              layout,
              plan) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(plan.output_width == 0);

    layout.struct_size = sizeof(vrrec_video_layout_v1);
    layout.abi_version = VRREC_ABI_V1 + 1;
    CHECK(CreateExplicitVideoProcessingPlan(
              Source(1'920, 1'080),
              layout,
              plan) == VRREC_STATUS_UNSUPPORTED_ABI);
    CHECK(plan.output_width == 0);
}

void ValidatesEveryExplicitLayoutBoundaryAndOverride()
{
    const auto valid = vrrec_video_layout_v1 {
        sizeof(vrrec_video_layout_v1),
        VRREC_ABI_V1,
        1'920,
        1'080,
        1'920,
        1'080,
        160,
        90,
        1'600,
        900,
        VRREC_CANVAS_BACKGROUND_BLACK,
        VRREC_VIDEO_ROTATION_NONE,
    };
    const auto rejects = [&](const auto mutate) {
        auto layout = valid;
        mutate(layout);
        VideoProcessingPlan plan {};
        plan.destination_width = 99;
        CHECK(CreateExplicitVideoProcessingPlan(
                  Source(1'920, 1'080), layout, plan) ==
              VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(plan.destination_width == 0);
    };

    rejects([](auto &value) { ++value.source_width; });
    rejects([](auto &value) { ++value.source_height; });
    rejects([](auto &value) { value.destination_width = 0; });
    rejects([](auto &value) { value.destination_height = 0; });
    rejects([](auto &value) { --value.destination_width; });
    rejects([](auto &value) { --value.destination_height; });
    rejects([](auto &value) { value.destination_x = value.canvas_width + 1; });
    rejects([](auto &value) { value.destination_y = value.canvas_height + 1; });
    rejects([](auto &value) { value.destination_width = 1'762; });
    rejects([](auto &value) { value.destination_height = 992; });
    rejects([](auto &value) {
        value.canvas_background =
            static_cast<vrrec_canvas_background_t>(99);
    });
    rejects([](auto &value) {
        value.rotation = static_cast<vrrec_video_rotation_t>(99);
    });
    rejects([](auto &value) { --value.canvas_width; });
    rejects([](auto &value) { --value.canvas_height; });

    VideoProcessingPlan invalid_source_plan {};
    CHECK(CreateExplicitVideoProcessingPlan(
              Source(
                  1'920,
                  1'080,
                  static_cast<vrrec_source_pixel_format_t>(99)),
              valid,
              invalid_source_plan) == VRREC_STATUS_INVALID_ARGUMENT);

    VideoProcessingPlan plan {};
    CHECK(CreateExplicitVideoProcessingPlan(
              Source(1'920, 1'080), valid, plan) == VRREC_STATUS_OK);
    CHECK(plan.destination_width == valid.destination_width);
    CHECK(plan.destination_height == valid.destination_height);
    CHECK(plan.offset_x == valid.destination_x);
    CHECK(plan.offset_y == valid.destination_y);
}

}

int main()
{
    PadsOddTextureEdgesBeforeChroma420Composition();
    ContainsPortraitVideoWithoutCropping();
    MarksRgbaInputForRedBlueChannelNormalization();
    RejectsOddOrZeroOutputCanvas();
    RejectsEverySingleFilePlanBoundary();
    RejectsExplicitDestinationsOutsideTheCanvas();
    RejectsExplicitLayoutsFromAnUnknownAbiContract();
    ValidatesEveryExplicitLayoutBoundaryAndOverride();
    return 0;
}
