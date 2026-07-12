#ifndef VRRECORDER_NATIVE_MUXING_VIDEO_ENCODER_SINK_HPP
#define VRRECORDER_NATIVE_MUXING_VIDEO_ENCODER_SINK_HPP

#include <atomic>
#include <cstdint>
#include <vector>

#include "shared_mux_finalization_session.hpp"
#include "video_encoding_pump.hpp"

namespace vrrecorder::native {

struct PacketVideoEncoderWrite final {
    vrrec_status_t status;
    std::uint64_t encode_latency_microseconds;
    std::vector<EncodedMediaPacket> packets;
};

class PacketVideoEncoder {
public:
    virtual ~PacketVideoEncoder() = default;

    virtual PacketVideoEncoderWrite Encode(
        const ScheduledVideoFrame &frame) noexcept = 0;
    virtual PacketVideoEncoderWrite Finish() noexcept = 0;
    virtual void Abort() noexcept = 0;
};

class MuxingVideoEncoderSink final : public VideoEncoderSink {
public:
    MuxingVideoEncoderSink(
        PacketVideoEncoder &encoder,
        SharedMuxFinalizationSession &mux) noexcept;

    VideoEncoderWrite Write(
        const ScheduledVideoFrame &frame) noexcept override;
    VideoEncoderWrite Finish() noexcept override;
    void Abort() noexcept override;

private:
    VideoEncoderWrite Commit(
        PacketVideoEncoderWrite encoded) noexcept;

    PacketVideoEncoder &encoder_;
    SharedMuxFinalizationSession &mux_;
    std::atomic_bool aborted_ = false;
};

}

#endif
