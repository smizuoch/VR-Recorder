#ifndef VRRECORDER_NATIVE_FFMPEG_H264_SOFTWARE_CODEC_SESSION_HPP
#define VRRECORDER_NATIVE_FFMPEG_H264_SOFTWARE_CODEC_SESSION_HPP

#include <memory>

#include "ffmpeg_h264_packet_encoder.hpp"

struct AVFrame;

namespace vrrecorder::native {

struct FfmpegH264SoftwareCodecSessionCreateResult final {
    vrrec_status_t status = VRREC_STATUS_INTERNAL_ERROR;
    std::unique_ptr<FfmpegH264CodecSession> session;
    AVFrame *owned_frame = nullptr;
};

// Opens the exact software h264_mf contract. On success the caller owns both
// resources and must pass owned_frame to an owner that calls av_frame_free.
FfmpegH264SoftwareCodecSessionCreateResult
CreateFfmpegH264SoftwareCodecSession(
    const H264VideoEncoderConfig &config) noexcept;

}

#endif
