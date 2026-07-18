#ifndef VRRECORDER_NATIVE_FFMPEG_H264_HARDWARE_CODEC_SESSION_HPP
#define VRRECORDER_NATIVE_FFMPEG_H264_HARDWARE_CODEC_SESSION_HPP

#include <memory>

#include "ffmpeg_h264_packet_encoder.hpp"
#include "production_video_encoder_route.hpp"

struct AVFrame;

namespace vrrecorder::native {

struct FfmpegH264HardwareCodecSessionCreateResult final {
    vrrec_status_t status = VRREC_STATUS_INTERNAL_ERROR;
    std::unique_ptr<FfmpegH264CodecSession> session;
    AVFrame *owned_frame = nullptr;
};

FfmpegH264HardwareCodecSessionCreateResult
CreateFfmpegH264HardwareCodecSession(
    const H264VideoEncoderConfig &config,
    const ProductionVideoEncoderRoute &route,
    void *d3d11_device) noexcept;

}

#endif
