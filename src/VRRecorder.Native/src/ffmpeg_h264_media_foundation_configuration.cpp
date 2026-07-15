#include "ffmpeg_h264_media_foundation_configuration.hpp"

extern "C" {
#include <libavcodec/avcodec.h>
#include <libavutil/dict.h>
}

namespace vrrecorder::native {
vrrec_status_t ConfigureFfmpegH264MediaFoundationContext(
    const H264VideoEncoderConfig &config,
    AVCodecContext &context,
    AVDictionary *&options) noexcept
{
    if (!IsH264VideoEncoderConfigValid(config) || options != nullptr) {
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
