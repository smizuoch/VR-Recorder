#ifndef VRRECORDER_NATIVE_FFMPEG_H264_PACKET_ENCODER_HPP
#define VRRECORDER_NATIVE_FFMPEG_H264_PACKET_ENCODER_HPP

#include <cstddef>
#include <memory>
#include <vector>

#include "ffmpeg_encoder_state_machine.hpp"
#include "ffmpeg_h264_nv12_frame.hpp"
#include "h264_packet_normalizer.hpp"

struct AVFrame;

namespace vrrecorder::native {

class FfmpegH264CodecSession {
public:
    virtual ~FfmpegH264CodecSession() = default;
    virtual vrrec_status_t PrepareFrame(const AVFrame &frame) noexcept = 0;
    virtual FfmpegEncodeBatch EncodePreparedFrame() noexcept = 0;
    virtual FfmpegEncodeBatch Finish() noexcept = 0;
    virtual vrrec_status_t CopyCodecExtradata(
        std::vector<std::byte> &extradata) const noexcept = 0;
    virtual void Abort() noexcept = 0;
};

struct FfmpegH264PacketEncoderWrite final {
    vrrec_status_t status = VRREC_STATUS_INTERNAL_ERROR;
    bool descriptor_became_ready = false;
    std::vector<EncodedMediaPacket> packets;
};

struct FfmpegH264PacketEncoderCreateResult;

class FfmpegH264PacketEncoder final {
public:
    ~FfmpegH264PacketEncoder();

    FfmpegH264PacketEncoder(const FfmpegH264PacketEncoder &) = delete;
    FfmpegH264PacketEncoder &operator=(
        const FfmpegH264PacketEncoder &) = delete;

    static FfmpegH264PacketEncoderCreateResult Create(
        const H264VideoEncoderConfig &config) noexcept;
#if defined(VRRECORDER_NATIVE_TESTING)
    static FfmpegH264PacketEncoderCreateResult CreateForTesting(
        const H264VideoEncoderConfig &config,
        std::unique_ptr<FfmpegH264CodecSession> session,
        AVFrame *frame) noexcept;
#endif

    FfmpegH264PacketEncoderWrite EncodeNv12(
        const SystemMemoryNv12FrameView &frame) noexcept;
    FfmpegH264PacketEncoderWrite Finish() noexcept;
    void Abort() noexcept;
    const H264StreamDescriptor *Descriptor() const noexcept;

private:
    class Impl;
    explicit FfmpegH264PacketEncoder(std::unique_ptr<Impl> impl) noexcept;
    static FfmpegH264PacketEncoderCreateResult CreateWithSession(
        const H264VideoEncoderConfig &config,
        std::unique_ptr<FfmpegH264CodecSession> session,
        AVFrame *frame) noexcept;

    std::unique_ptr<Impl> impl_;
};

struct FfmpegH264PacketEncoderCreateResult final {
    vrrec_status_t status = VRREC_STATUS_INTERNAL_ERROR;
    std::unique_ptr<FfmpegH264PacketEncoder> encoder;
};

}

#endif
