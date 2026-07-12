#ifndef VRRECORDER_NATIVE_AUDIO_CAPTURE_INPUT_RUNNER_HPP
#define VRRECORDER_NATIVE_AUDIO_CAPTURE_INPUT_RUNNER_HPP

#include <atomic>
#include <chrono>
#include <memory>
#include <mutex>

#include "audio_capture_pump.hpp"

namespace vrrecorder::native {

class AudioCaptureSourceProvider {
public:
    virtual ~AudioCaptureSourceProvider() = default;

    virtual vrrec_status_t Create(
        std::unique_ptr<AudioCaptureSource> &source) noexcept = 0;
};

class AudioCaptureRecoveryWaiter {
public:
    virtual ~AudioCaptureRecoveryWaiter() = default;

    virtual bool Wait(
        std::chrono::milliseconds duration) noexcept = 0;
};

enum class AudioCaptureInputResult {
    Aborted,
    RecoveryTimedOut,
    InvalidState,
    Failed,
};

class AudioCaptureInputRunner final {
public:
    static constexpr auto RecoveryWindow = std::chrono::seconds(5);
    static constexpr auto RetryInterval = std::chrono::milliseconds(100);

    AudioCaptureInputRunner(
        AudioCaptureSourceProvider &provider,
        AudioCaptureRecoveryWaiter &waiter,
        StereoCaptureTimeline &timeline) noexcept;

    AudioCaptureInputResult Run(
        const AudioCaptureSourceConfig &config) noexcept;
    void Abort() noexcept;

private:
    AudioCaptureInputResult WaitBeforeRetry(
        std::chrono::milliseconds &waited) noexcept;
    void SetActivePump(AudioCapturePump *pump) noexcept;

    AudioCaptureSourceProvider &provider_;
    AudioCaptureRecoveryWaiter &waiter_;
    StereoCaptureTimeline &timeline_;
    std::mutex active_mutex_;
    AudioCapturePump *active_pump_ = nullptr;
    std::atomic_bool aborted_ = false;
    std::atomic_bool running_ = false;
};

}

#endif
