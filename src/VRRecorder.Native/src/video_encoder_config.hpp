#ifndef VRRECORDER_NATIVE_VIDEO_ENCODER_CONFIG_HPP
#define VRRECORDER_NATIVE_VIDEO_ENCODER_CONFIG_HPP

#include <cstdint>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

enum class H264Profile {
    Main,
    High,
};

enum class VideoRateControl {
    QualityVbr,
};

struct H264VideoEncoderConfig final {
    std::uint32_t width = 0;
    std::uint32_t height = 0;
    std::uint32_t frames_per_second = 0;
    std::uint32_t gop_frame_count = 0;
    std::uint64_t target_bitrate_bits_per_second = 0;
    std::uint64_t maximum_bitrate_bits_per_second = 0;
    vrrec_source_pixel_format_t input_pixel_format =
        static_cast<vrrec_source_pixel_format_t>(0);
    H264Profile profile = H264Profile::Main;
    VideoRateControl rate_control = VideoRateControl::QualityVbr;
};

vrrec_status_t CreateH264VideoEncoderConfig(
    std::uint32_t width,
    std::uint32_t height,
    std::uint32_t frames_per_second,
    bool high_profile_supported,
    H264VideoEncoderConfig &config) noexcept;

}

#endif
