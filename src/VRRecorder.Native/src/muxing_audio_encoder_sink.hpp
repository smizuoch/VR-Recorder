#ifndef VRRECORDER_NATIVE_MUXING_AUDIO_ENCODER_SINK_HPP
#define VRRECORDER_NATIVE_MUXING_AUDIO_ENCODER_SINK_HPP

#include <atomic>
#include <cstdint>
#include <mutex>
#include <span>
#include <vector>

#include "audio_encoding_pump.hpp"
#include "encoded_media_packet_submission_port.hpp"

namespace vrrecorder::native {

struct PacketAudioEncoderWrite final {
    vrrec_status_t status;
    std::vector<EncodedMediaPacket> packets;
};

class PacketAudioEncoder {
public:
    virtual ~PacketAudioEncoder() = default;

    virtual PacketAudioEncoderWrite EncodePcm48k(
        std::uint64_t start_frame_48k,
        std::span<const float> interleaved_samples) noexcept = 0;
    virtual PacketAudioEncoderWrite Finish() noexcept = 0;
    virtual void Abort() noexcept = 0;
};

class MuxingAudioEncoderSink final : public StereoAudioEncoderSink {
public:
    MuxingAudioEncoderSink(
        PacketAudioEncoder &encoder,
        EncodedMediaPacketSubmissionPort &mux) noexcept;

    StereoAudioEncoderWrite WritePcm48k(
        std::uint64_t start_frame_48k,
        std::span<const float> interleaved_samples) noexcept override;
    StereoAudioEncoderWrite Finish() noexcept override;
    void Abort() noexcept override;

private:
    StereoAudioEncoderWrite Commit(
        PacketAudioEncoderWrite encoded) noexcept;

    PacketAudioEncoder &encoder_;
    EncodedMediaPacketSubmissionPort &mux_;
    std::mutex operation_mutex_;
    std::atomic_bool aborted_ = false;
    std::atomic_bool finished_ = false;
};

}

#endif
