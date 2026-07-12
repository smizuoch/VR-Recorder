#ifndef VRRECORDER_NATIVE_AUDIO_CAPTURE_PLATFORM_SUPPORT_HPP
#define VRRECORDER_NATIVE_AUDIO_CAPTURE_PLATFORM_SUPPORT_HPP

#include <condition_variable>
#include <mutex>

#include "audio_capture_input_runner.hpp"

namespace vrrecorder::native {

class PlatformAudioCaptureSourceProvider final
    : public AudioCaptureSourceProvider {
public:
    vrrec_status_t Create(
        std::unique_ptr<AudioCaptureSource> &source) noexcept override;
};

class ConditionVariableAudioCaptureRecoveryWaiter final
    : public AudioCaptureRecoveryWaiter {
public:
    bool Wait(
        std::chrono::milliseconds duration) noexcept override;
    void Abort() noexcept override;

private:
    std::mutex mutex_;
    std::condition_variable condition_;
    bool aborted_ = false;
};

}

#endif
