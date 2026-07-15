#include "video_processing_plan.hpp"

#include <cstdint>
#include <limits>

namespace vrrecorder::native {
namespace {

std::uint32_t FloorEven(std::uint64_t value) noexcept
{
    return static_cast<std::uint32_t>(value & ~std::uint64_t {1});
}

}

vrrec_status_t CreateSingleFileVideoProcessingPlan(
    const VideoSurfaceDescriptor &source,
    std::uint32_t output_width,
    std::uint32_t output_height,
    VideoProcessingPlan &plan) noexcept
{
    plan = VideoProcessingPlan {};
    const auto input_format_supported =
        source.pixel_format == VRREC_SOURCE_PIXEL_FORMAT_BGRA8 ||
        source.pixel_format == VRREC_SOURCE_PIXEL_FORMAT_RGBA8 ||
        source.pixel_format == VRREC_SOURCE_PIXEL_FORMAT_NV12;
    if (source.width == 0 || source.height == 0 ||
        source.width == std::numeric_limits<std::uint32_t>::max() ||
        source.height == std::numeric_limits<std::uint32_t>::max() ||
        output_width == 0 || output_height == 0 ||
        (output_width & 1U) != 0 || (output_height & 1U) != 0 ||
        !input_format_supported) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    const auto normalized_width = source.width + (source.width & 1U);
    const auto normalized_height = source.height + (source.height & 1U);
    std::uint32_t destination_width = 0;
    std::uint32_t destination_height = 0;
    const auto width_limited =
        static_cast<std::uint64_t>(output_width) * normalized_height <=
        static_cast<std::uint64_t>(output_height) * normalized_width;
    if (width_limited) {
        destination_width = output_width;
        destination_height = FloorEven(
            static_cast<std::uint64_t>(normalized_height) * output_width /
            normalized_width);
    } else {
        destination_height = output_height;
        destination_width = FloorEven(
            static_cast<std::uint64_t>(normalized_width) * output_height /
            normalized_height);
    }

    if (destination_width == 0 || destination_height == 0) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    plan = {
        source.adapter_luid,
        source.width,
        source.height,
        normalized_width,
        normalized_height,
        output_width,
        output_height,
        destination_width,
        destination_height,
        (output_width - destination_width) / 2,
        (output_height - destination_height) / 2,
        source.width & 1U,
        source.height & 1U,
        source.pixel_format,
        VRREC_SOURCE_PIXEL_FORMAT_NV12,
        source.pixel_format == VRREC_SOURCE_PIXEL_FORMAT_RGBA8,
        source.generation_id,
    };
    return VRREC_STATUS_OK;
}

vrrec_status_t CreateExplicitVideoProcessingPlan(
    const VideoSurfaceDescriptor &source,
    const vrrec_video_layout_v1 &layout,
    VideoProcessingPlan &plan) noexcept
{
    plan = VideoProcessingPlan {};
    if (layout.struct_size < sizeof(vrrec_video_layout_v1)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }
    if (layout.abi_version != VRREC_ABI_V1) {
        return VRREC_STATUS_UNSUPPORTED_ABI;
    }
    if (source.width != layout.source_width ||
        source.height != layout.source_height ||
        layout.destination_width == 0 ||
        layout.destination_height == 0 ||
        (layout.destination_width & 1U) != 0 ||
        (layout.destination_height & 1U) != 0 ||
        layout.destination_x > layout.canvas_width ||
        layout.destination_y > layout.canvas_height ||
        layout.destination_width >
            layout.canvas_width - layout.destination_x ||
        layout.destination_height >
            layout.canvas_height - layout.destination_y ||
        layout.canvas_background != VRREC_CANVAS_BACKGROUND_BLACK ||
        layout.rotation != VRREC_VIDEO_ROTATION_NONE) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    const auto status = CreateSingleFileVideoProcessingPlan(
        source,
        layout.canvas_width,
        layout.canvas_height,
        plan);
    if (status != VRREC_STATUS_OK) {
        return status;
    }

    plan.destination_width = layout.destination_width;
    plan.destination_height = layout.destination_height;
    plan.offset_x = layout.destination_x;
    plan.offset_y = layout.destination_y;
    return VRREC_STATUS_OK;
}

}
