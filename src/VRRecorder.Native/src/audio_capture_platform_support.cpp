#include "audio_capture_platform_support.hpp"

#include "audio_capture_source_factory.hpp"

namespace vrrecorder::native {

vrrec_status_t PlatformAudioCaptureSourceProvider::Create(
    std::unique_ptr<AudioCaptureSource> &source) noexcept
{
    return CreateWasapiAudioCaptureSource(source);
}

bool ConditionVariableAudioCaptureRecoveryWaiter::Wait(
    std::chrono::milliseconds duration) noexcept
{
    std::unique_lock lock(mutex_);
    if (aborted_) {
        return false;
    }

    return !condition_.wait_for(
        lock,
        duration,
        [this] { return aborted_; });
}

void ConditionVariableAudioCaptureRecoveryWaiter::Abort() noexcept
{
    {
        const std::lock_guard lock(mutex_);
        if (aborted_) {
            return;
        }

        aborted_ = true;
    }

    condition_.notify_all();
}

}
