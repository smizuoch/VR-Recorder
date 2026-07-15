#ifndef VRRECORDER_NATIVE_FFMPEG_H264_SOFTWARE_CODEC_SESSION_HPP
#define VRRECORDER_NATIVE_FFMPEG_H264_SOFTWARE_CODEC_SESSION_HPP

#include <memory>
#include <string>

#include "ffmpeg_h264_packet_encoder.hpp"

struct AVFrame;

namespace vrrecorder::native {

struct FfmpegH264SoftwareOpenedIdentity final {
    std::string codec_name;
    bool hardware_accelerated = true;
    vrrec_encoder_input_format_t opened_input_format = 0;
    H264VideoEncoderConfig opened_config;
    std::string ffmpeg_build_identity;
};

struct FfmpegH264SoftwareCodecSessionCreateResult final {
    vrrec_status_t status = VRREC_STATUS_INTERNAL_ERROR;
    std::unique_ptr<FfmpegH264CodecSession> session;
    AVFrame *owned_frame = nullptr;
    FfmpegH264SoftwareOpenedIdentity opened_identity;
};

// Opens the exact software h264_mf contract. On success the caller owns both
// resources and must pass owned_frame to an owner that calls av_frame_free.
FfmpegH264SoftwareCodecSessionCreateResult
CreateFfmpegH264SoftwareCodecSession(
    const H264VideoEncoderConfig &config) noexcept;

class FfmpegH264SoftwareCodecSessionFactoryPort {
public:
    virtual ~FfmpegH264SoftwareCodecSessionFactoryPort() = default;

    virtual FfmpegH264SoftwareCodecSessionCreateResult Create(
        const H264VideoEncoderConfig &config) noexcept = 0;
};

class LibavcodecH264SoftwareCodecSessionFactory final
    : public FfmpegH264SoftwareCodecSessionFactoryPort {
public:
    FfmpegH264SoftwareCodecSessionCreateResult Create(
        const H264VideoEncoderConfig &config) noexcept override;
};

}

#endif
