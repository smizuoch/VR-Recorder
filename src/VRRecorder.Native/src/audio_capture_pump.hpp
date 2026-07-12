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

class AudioCapturePump final {
public:
    AudioCapturePump(
        AudioCaptureSource &source,
        StereoCaptureTimeline &timeline) noexcept;

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
    std::vector<float> silent_samples_;
    std::atomic_bool aborted_ = false;
    bool started_ = false;
    bool recovering_ = false;
};

}

#endif
