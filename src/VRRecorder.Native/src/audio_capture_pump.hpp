#ifndef VRRECORDER_NATIVE_AUDIO_CAPTURE_PUMP_HPP
#define VRRECORDER_NATIVE_AUDIO_CAPTURE_PUMP_HPP

#include <atomic>
#include <vector>

#include "audio_capture_source.hpp"
#include "audio_capture_timeline.hpp"

namespace vrrecorder::native {

enum class AudioCapturePumpResult {
    PacketAccepted,
    DeviceLost,
    Aborted,
    InvalidState,
    InvalidPacket,
    Discontinuity,
    Overrun,
    Failed,
};

enum class AudioBufferHealth {
    Underrun,
    Overrun,
};

class AudioCaptureAvailabilitySink {
public:
    virtual ~AudioCaptureAvailabilitySink() = default;

    virtual void AvailabilityChanged(
        AudioCaptureRole role,
        bool available,
        std::uint64_t frame_position) noexcept = 0;
    virtual void BufferHealthChanged(
        AudioCaptureRole role,
        AudioBufferHealth health,
        std::uint64_t frame_position) noexcept
    {
        (void)role;
        (void)health;
        (void)frame_position;
    }
};

class AudioCapturePump final {
public:
    AudioCapturePump(
        AudioCaptureSource &source,
        StereoCaptureTimeline &timeline,
        AudioCaptureAvailabilitySink *availability_sink = nullptr) noexcept;

    vrrec_status_t Start(
        const AudioCaptureSourceConfig &config) noexcept;
    vrrec_status_t StartRecovery(
        const AudioCaptureSourceConfig &config) noexcept;
    AudioCapturePumpResult PumpOne() noexcept;
    void Abort() noexcept;

private:
    AudioCapturePumpResult AcceptPacket(
        const CapturedStereoPacket48k &packet) noexcept;
    vrrec_status_t StartCore(
        const AudioCaptureSourceConfig &config,
        bool recovering) noexcept;
    static AudioCapturePumpResult MapTimelineResult(
        AudioTimelineResult result) noexcept;

    AudioCaptureSource &source_;
    StereoCaptureTimeline &timeline_;
    AudioCaptureAvailabilitySink *availability_sink_;
    std::vector<float> silent_samples_;
    std::atomic_bool aborted_ = false;
    AudioCaptureRole role_ = AudioCaptureRole::DesktopLoopback;
    bool started_ = false;
    bool recovering_ = false;
};

}

#endif
