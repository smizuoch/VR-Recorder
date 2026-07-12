#include "audio_capture_input_runner.hpp"

#include <algorithm>

namespace vrrecorder::native {
namespace {

class RunningGuard final {
public:
    explicit RunningGuard(std::atomic_bool &running) noexcept
        : running_(running)
    {
    }

    ~RunningGuard()
    {
        running_.store(false);
    }

private:
    std::atomic_bool &running_;
};

class NullAudioCaptureInputStartSink final
    : public AudioCaptureInputStartSink {
public:
    void Started(vrrec_status_t) noexcept override
    {
    }
};

bool TryCreateRecoveryConfig(
    const AudioCaptureSourceConfig &config,
    AudioCaptureSourceConfig &recovered) noexcept
{
    try {
        recovered = config;
        recovered.endpoint_id_utf8 =
            config.role == AudioCaptureRole::DesktopLoopback
            ? "default-render"
            : "default-capture";
        return true;
    } catch (...) {
        return false;
    }
}

}

AudioCaptureInputRunner::AudioCaptureInputRunner(
    AudioCaptureSourceProvider &provider,
    AudioCaptureRecoveryWaiter &waiter,
    StereoCaptureTimeline &timeline,
    AudioCaptureAvailabilitySink *availability_sink) noexcept
    : provider_(provider),
      waiter_(waiter),
      timeline_(timeline),
      availability_sink_(availability_sink)
{
}

AudioCaptureInputResult AudioCaptureInputRunner::Run(
    const AudioCaptureSourceConfig &config) noexcept
{
    NullAudioCaptureInputStartSink start_sink;
    return Run(config, start_sink);
}

AudioCaptureInputResult AudioCaptureInputRunner::Run(
    const AudioCaptureSourceConfig &config,
    AudioCaptureInputStartSink &start_sink) noexcept
{
    if (running_.exchange(true) || aborted_.load()) {
        start_sink.Started(VRREC_STATUS_INVALID_STATE);
        return AudioCaptureInputResult::InvalidState;
    }

    const RunningGuard running_guard(running_);
    AudioCaptureSourceConfig recovery_config {};
    if (!TryCreateRecoveryConfig(config, recovery_config)) {
        start_sink.Started(VRREC_STATUS_INTERNAL_ERROR);
        return AudioCaptureInputResult::Failed;
    }

    auto recovering = false;
    auto initial_start_reported = false;
    std::chrono::milliseconds recovery_waited {0};
    while (true) {
        if (aborted_.load()) {
            if (!initial_start_reported) {
                start_sink.Started(VRREC_STATUS_INVALID_STATE);
            }

            return AudioCaptureInputResult::Aborted;
        }

        std::unique_ptr<AudioCaptureSource> source;
        const auto create_status = provider_.Create(source);
        if (create_status != VRREC_STATUS_OK || source == nullptr) {
            if (!recovering) {
                start_sink.Started(create_status != VRREC_STATUS_OK
                    ? create_status
                    : VRREC_STATUS_INTERNAL_ERROR);
                return AudioCaptureInputResult::Failed;
            }

            const auto wait_result = WaitBeforeRetry(recovery_waited);
            if (wait_result != AudioCaptureInputResult::Failed) {
                return wait_result;
            }

            continue;
        }

        AudioCapturePump pump(*source, timeline_, availability_sink_);
        SetActivePump(&pump);
        const auto start_status = recovering
            ? pump.StartRecovery(recovery_config)
            : pump.Start(config);
        if (start_status != VRREC_STATUS_OK) {
            SetActivePump(nullptr);
            if (aborted_.load()) {
                if (!initial_start_reported) {
                    start_sink.Started(VRREC_STATUS_INVALID_STATE);
                }

                return AudioCaptureInputResult::Aborted;
            }

            if (!recovering) {
                start_sink.Started(start_status);
                return AudioCaptureInputResult::Failed;
            }

            const auto wait_result = WaitBeforeRetry(recovery_waited);
            if (wait_result != AudioCaptureInputResult::Failed) {
                return wait_result;
            }

            continue;
        }

        if (!initial_start_reported) {
            start_sink.Started(VRREC_STATUS_OK);
            initial_start_reported = true;
        }

        auto restart = false;
        while (!aborted_.load()) {
            const auto pump_result = pump.PumpOne();
            if (pump_result == AudioCapturePumpResult::PacketAccepted) {
                if (recovering) {
                    recovering = false;
                    recovery_waited = std::chrono::milliseconds {0};
                }

                continue;
            }

            SetActivePump(nullptr);
            if (pump_result == AudioCapturePumpResult::Aborted ||
                aborted_.load()) {
                return AudioCaptureInputResult::Aborted;
            }

            if (pump_result != AudioCapturePumpResult::DeviceLost) {
                return AudioCaptureInputResult::Failed;
            }

            if (!recovering) {
                recovering = true;
                recovery_waited = std::chrono::milliseconds {0};
            }

            restart = true;
            break;
        }

        SetActivePump(nullptr);
        if (!restart) {
            return AudioCaptureInputResult::Aborted;
        }
    }

    return AudioCaptureInputResult::Aborted;
}

void AudioCaptureInputRunner::Abort() noexcept
{
    if (aborted_.exchange(true)) {
        return;
    }

    waiter_.Abort();
    const std::lock_guard lock(active_mutex_);
    if (active_pump_ != nullptr) {
        active_pump_->Abort();
    } else {
        timeline_.Abort();
    }
}

AudioCaptureInputResult AudioCaptureInputRunner::WaitBeforeRetry(
    std::chrono::milliseconds &waited) noexcept
{
    if (aborted_.load()) {
        return AudioCaptureInputResult::Aborted;
    }

    const auto remaining = std::chrono::duration_cast<
        std::chrono::milliseconds>(RecoveryWindow) - waited;
    if (remaining <= std::chrono::milliseconds {0}) {
        return AudioCaptureInputResult::RecoveryTimedOut;
    }

    const auto duration = std::min(RetryInterval, remaining);
    if (!waiter_.Wait(duration) || aborted_.load()) {
        return AudioCaptureInputResult::Aborted;
    }

    waited += duration;
    return waited >= RecoveryWindow
        ? AudioCaptureInputResult::RecoveryTimedOut
        : AudioCaptureInputResult::Failed;
}

void AudioCaptureInputRunner::SetActivePump(
    AudioCapturePump *pump) noexcept
{
    const std::lock_guard lock(active_mutex_);
    active_pump_ = pump;
    if (active_pump_ != nullptr && aborted_.load()) {
        active_pump_->Abort();
    }
}

}
