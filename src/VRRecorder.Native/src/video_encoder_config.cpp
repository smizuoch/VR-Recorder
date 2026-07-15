#include "video_encoder_config.hpp"

#include <algorithm>
#include <cstdint>

namespace vrrecorder::native {

bool IsH264VideoEncoderConfigValid(
    const H264VideoEncoderConfig &config) noexcept
{
    constexpr std::uint32_t maximum_dimension = 16'384;
    constexpr std::uint32_t minimum_frames_per_second = 30;
    constexpr std::uint32_t maximum_frames_per_second = 120;
    constexpr std::uint64_t minimum_bitrate = 8'000'000;
    constexpr std::uint64_t maximum_bitrate = 80'000'000;

    const auto profile_is_supported =
        config.profile == H264Profile::Main ||
        config.profile == H264Profile::High;
    return config.width != 0 && config.height != 0 &&
        config.width <= maximum_dimension &&
        config.height <= maximum_dimension &&
        (config.width & 1U) == 0 && (config.height & 1U) == 0 &&
        config.frames_per_second >= minimum_frames_per_second &&
        config.frames_per_second <= maximum_frames_per_second &&
        config.gop_frame_count == config.frames_per_second * 2U &&
        config.target_bitrate_bits_per_second >= minimum_bitrate &&
        config.target_bitrate_bits_per_second <= maximum_bitrate &&
        config.maximum_bitrate_bits_per_second ==
            config.target_bitrate_bits_per_second * 3U / 2U &&
        config.input_pixel_format == VRREC_SOURCE_PIXEL_FORMAT_NV12 &&
        profile_is_supported &&
        config.rate_control == VideoRateControl::QualityVbr &&
        config.maximum_b_frame_count == 0;
}

vrrec_status_t CreateH264VideoEncoderConfig(
    std::uint32_t width,
    std::uint32_t height,
    std::uint32_t frames_per_second,
    bool high_profile_supported,
    H264VideoEncoderConfig &config) noexcept
{
    config = H264VideoEncoderConfig {};
    constexpr std::uint32_t maximum_dimension = 16'384;
    if (width == 0 || height == 0 ||
        width > maximum_dimension || height > maximum_dimension ||
        (width & 1U) != 0 || (height & 1U) != 0 ||
        frames_per_second < 30 || frames_per_second > 120) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    constexpr std::uint64_t minimum_bitrate = 8'000'000;
    constexpr std::uint64_t maximum_bitrate = 80'000'000;
    const auto calculated_bitrate =
        static_cast<std::uint64_t>(width) * height *
        frames_per_second * 14 / 100;
    const auto target_bitrate = std::clamp(
        calculated_bitrate,
        minimum_bitrate,
        maximum_bitrate);

    config = {
        width,
        height,
        frames_per_second,
        frames_per_second * 2,
        target_bitrate,
        target_bitrate * 3 / 2,
        VRREC_SOURCE_PIXEL_FORMAT_NV12,
        high_profile_supported ? H264Profile::High : H264Profile::Main,
        VideoRateControl::QualityVbr,
        0,
    };
    return VRREC_STATUS_OK;
}

}
