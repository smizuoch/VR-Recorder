#ifndef VRRECORDER_NATIVE_VIDEO_PROCESSING_PLAN_HPP
#define VRRECORDER_NATIVE_VIDEO_PROCESSING_PLAN_HPP

#include <cstdint>

#include "video_surface.hpp"

namespace vrrecorder::native {

struct VideoProcessingPlan final {
    std::uint64_t adapter_luid = 0;
    std::uint32_t source_width = 0;
    std::uint32_t source_height = 0;
    std::uint32_t normalized_source_width = 0;
    std::uint32_t normalized_source_height = 0;
    std::uint32_t output_width = 0;
    std::uint32_t output_height = 0;
    std::uint32_t destination_width = 0;
    std::uint32_t destination_height = 0;
    std::uint32_t offset_x = 0;
    std::uint32_t offset_y = 0;
    std::uint32_t pad_right = 0;
    std::uint32_t pad_bottom = 0;
    vrrec_source_pixel_format_t input_pixel_format =
        static_cast<vrrec_source_pixel_format_t>(0);
    vrrec_source_pixel_format_t output_pixel_format =
        static_cast<vrrec_source_pixel_format_t>(0);
    bool swap_red_blue_channels = false;
};

vrrec_status_t CreateSingleFileVideoProcessingPlan(
    const VideoSurfaceDescriptor &source,
    std::uint32_t output_width,
    std::uint32_t output_height,
    VideoProcessingPlan &plan) noexcept;

vrrec_status_t CreateExplicitVideoProcessingPlan(
    const VideoSurfaceDescriptor &source,
    const vrrec_video_layout_v1 &layout,
    VideoProcessingPlan &plan) noexcept;

}

#endif
