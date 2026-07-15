#ifndef VRRECORDER_NATIVE_FFMPEG_H264_MEDIA_FOUNDATION_CONFIGURATION_HPP
#define VRRECORDER_NATIVE_FFMPEG_H264_MEDIA_FOUNDATION_CONFIGURATION_HPP

#include <string_view>

#include "video_encoder_config.hpp"

struct AVCodecContext;
struct AVDictionary;

namespace vrrecorder::native {

inline constexpr std::string_view FfmpegH264MediaFoundationEncoderName =
    "h264_mf";

vrrec_status_t ConfigureFfmpegH264MediaFoundationContext(
    const H264VideoEncoderConfig &config,
    AVCodecContext &context,
    AVDictionary *&options) noexcept;

}

#endif
