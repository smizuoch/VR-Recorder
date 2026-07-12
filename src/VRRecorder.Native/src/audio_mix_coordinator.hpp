#ifndef VRRECORDER_NATIVE_AUDIO_MIX_COORDINATOR_HPP
#define VRRECORDER_NATIVE_AUDIO_MIX_COORDINATOR_HPP

#include <atomic>
#include <cstddef>
#include <cstdint>
#include <span>
#include <vector>

#include "audio_capture_timeline.hpp"
#include "audio_mixer.hpp"

namespace vrrecorder::native {

enum class StereoAudioMixResult {
    Mixed,
    Aborted,
    InvalidArgument,
    InvalidState,
    Failed,
};

struct StereoAudioMixRead final {
    std::uint64_t start_frame_48k = 0;
    std::size_t frame_count_48k = 0;
    bool desktop_available = false;
    bool microphone_available = false;
    bool desktop_underrun = false;
    bool microphone_underrun = false;
};

class StereoAudioMixCoordinator final {
public:
    StereoAudioMixCoordinator(
        StereoCaptureTimeline &desktop,
        StereoCaptureTimeline &microphone,
        StereoAudioMixer &mixer) noexcept;

    StereoAudioMixResult MixNext(
        std::size_t frame_count_48k,
        std::span<float> output_interleaved,
        StereoAudioMixRead &read) noexcept;
    void Abort() noexcept;

private:
    static StereoAudioMixResult MapTimelineResult(
        AudioTimelineResult result) noexcept;

    StereoCaptureTimeline &desktop_;
    StereoCaptureTimeline &microphone_;
    StereoAudioMixer &mixer_;
    std::vector<float> desktop_samples_;
    std::vector<float> microphone_samples_;
    std::atomic_bool aborted_ = false;
};

}

#endif
