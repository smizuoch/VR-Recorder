#include "ffmpeg_h264_media_foundation_configuration.hpp"

#include <cstdint>

extern "C" {
#include <libavcodec/avcodec.h>
#include <libavutil/dict.h>
}

namespace vrrecorder::native {
namespace {

bool IsCanonicalConfig(const H264VideoEncoderConfig &config) noexcept
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

}

vrrec_status_t ConfigureFfmpegH264MediaFoundationContext(
    const H264VideoEncoderConfig &config,
    AVCodecContext &context,
    AVDictionary *&options) noexcept
{
    if (!IsCanonicalConfig(config) || options != nullptr) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    AVDictionary *created_options = nullptr;
    auto result = av_dict_set(
        &created_options,
        "hw_encoding",
        "0",
        0);
    if (result >= 0) {
        result = av_dict_set(
            &created_options,
            "rate_control",
            "quality",
            0);
    }
    if (result < 0) {
        av_dict_free(&created_options);
        return VRREC_STATUS_OUT_OF_MEMORY;
    }

    context.codec_type = AVMEDIA_TYPE_VIDEO;
    context.codec_id = AV_CODEC_ID_H264;
    context.width = static_cast<int>(config.width);
    context.height = static_cast<int>(config.height);
    context.pix_fmt = AV_PIX_FMT_NV12;
    context.time_base = {
        1,
        static_cast<int>(config.frames_per_second),
    };
    context.framerate = {
        static_cast<int>(config.frames_per_second),
        1,
    };
    context.bit_rate = static_cast<std::int64_t>(
        config.target_bitrate_bits_per_second);
    context.rc_max_rate = static_cast<std::int64_t>(
        config.maximum_bitrate_bits_per_second);
    context.gop_size = static_cast<int>(config.gop_frame_count);
    context.max_b_frames = static_cast<int>(config.maximum_b_frame_count);
    context.profile = config.profile == H264Profile::High
        ? AV_PROFILE_H264_HIGH
        : AV_PROFILE_H264_MAIN;
    context.flags |= AV_CODEC_FLAG_GLOBAL_HEADER;

    options = created_options;
    return VRREC_STATUS_OK;
}

}
