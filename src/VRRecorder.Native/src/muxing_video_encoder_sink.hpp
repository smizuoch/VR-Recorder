#ifndef VRRECORDER_NATIVE_MUXING_VIDEO_ENCODER_SINK_HPP
#define VRRECORDER_NATIVE_MUXING_VIDEO_ENCODER_SINK_HPP

#include <atomic>
#include <cstdint>
#include <mutex>
#include <vector>

#include "encoded_media_packet_submission_port.hpp"
#include "h264_descriptor_packet_submission_port.hpp"
#include "video_encoding_pump.hpp"

namespace vrrecorder::native {

struct PacketVideoEncoderWrite final {
    vrrec_status_t status;
    std::uint64_t encode_latency_microseconds;
    std::vector<EncodedMediaPacket> packets;
    bool descriptor_became_ready = false;
    const void *encoder_identity = nullptr;
    const H264StreamDescriptor *descriptor = nullptr;
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
        EncodedMediaPacketSubmissionPort &mux) noexcept;
    MuxingVideoEncoderSink(
        PacketVideoEncoder &encoder,
        EncodedMediaPacketSubmissionPort &mux,
        H264DescriptorPacketSubmissionPort &descriptor_mux) noexcept;

    VideoEncoderWrite Write(
        const ScheduledVideoFrame &frame) noexcept override;
    VideoEncoderWrite Finish() noexcept override;
    void Abort() noexcept override;

private:
    VideoEncoderWrite Commit(
        PacketVideoEncoderWrite encoded) noexcept;
    void AbortEncoderOnce() noexcept;

    PacketVideoEncoder &encoder_;
    EncodedMediaPacketSubmissionPort &mux_;
    H264DescriptorPacketSubmissionPort *descriptor_mux_;
    std::mutex operation_mutex_;
    std::atomic_bool aborted_ = false;
    std::atomic_bool encoder_aborted_ = false;
    std::atomic_bool finished_ = false;
    std::uint64_t committed_muxed_packet_count_ = 0;
};

}

#endif
