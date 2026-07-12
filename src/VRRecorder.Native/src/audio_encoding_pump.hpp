#ifndef VRRECORDER_NATIVE_AUDIO_ENCODING_PUMP_HPP
#define VRRECORDER_NATIVE_AUDIO_ENCODING_PUMP_HPP

#include <atomic>
#include <cstddef>
#include <cstdint>
#include <span>
#include <vector>

#include "audio_mix_coordinator.hpp"

namespace vrrecorder::native {

struct StereoAudioEncoderWrite final {
    vrrec_status_t status;
    std::uint64_t muxed_packet_count;
};

class StereoAudioEncoderSink {
public:
    virtual ~StereoAudioEncoderSink() = default;

    virtual StereoAudioEncoderWrite WritePcm48k(
        std::uint64_t start_frame_48k,
        std::span<const float> interleaved_samples) noexcept = 0;
    virtual StereoAudioEncoderWrite Finish() noexcept = 0;
    virtual void Abort() noexcept = 0;
};

enum class StereoAudioEncodingResult {
    Submitted,
    Aborted,
    InvalidArgument,
    InvalidState,
    CaptureFailed,
    EncoderFailed,
    Failed,
};

struct StereoAudioEncodingRead final {
    StereoAudioMixRead mix {};
    std::uint64_t muxed_packet_count = 0;
    vrrec_status_t encoder_status = VRREC_STATUS_OK;
};

class StereoAudioEncodingPump final {
public:
    StereoAudioEncodingPump(
        StereoAudioMixSource &source,
        StereoAudioEncoderSink &sink) noexcept;

    StereoAudioEncodingResult PumpNext(
        std::size_t frame_count_48k,
        StereoAudioEncodingRead &read) noexcept;

    std::uint64_t SubmittedFrameCount() const noexcept;
    std::uint64_t MuxedPacketCount() const noexcept;

private:
    static StereoAudioEncodingResult MapMixResult(
        StereoAudioMixResult result) noexcept;

    StereoAudioMixSource &source_;
    StereoAudioEncoderSink &sink_;
    std::vector<float> mixed_samples_;
    std::atomic<std::uint64_t> submitted_frame_count_ {0};
    std::atomic<std::uint64_t> muxed_packet_count_ {0};
};

}

#endif
