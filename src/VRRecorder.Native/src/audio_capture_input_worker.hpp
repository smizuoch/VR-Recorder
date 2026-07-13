#ifndef VRRECORDER_NATIVE_AUDIO_CAPTURE_INPUT_WORKER_HPP
#define VRRECORDER_NATIVE_AUDIO_CAPTURE_INPUT_WORKER_HPP

#include <atomic>
#include <condition_variable>
#include <mutex>
#include <thread>

#include "audio_capture_input_runner.hpp"
#include "native_thread_factory.hpp"

namespace vrrecorder::native {

class AudioCaptureInputWorker final : private AudioCaptureInputStartSink {
public:
    AudioCaptureInputWorker(
        AudioCaptureSourceProvider &provider,
        AudioCaptureRecoveryWaiter &waiter,
        StereoCaptureTimeline &timeline,
        AudioCaptureAvailabilitySink *availability_sink = nullptr) noexcept;
    AudioCaptureInputWorker(
        AudioCaptureSourceProvider &provider,
        AudioCaptureRecoveryWaiter &waiter,
        StereoCaptureTimeline &timeline,
        NativeThreadFactoryPort &thread_factory,
        AudioCaptureAvailabilitySink *availability_sink = nullptr) noexcept;
    ~AudioCaptureInputWorker();

    AudioCaptureInputWorker(const AudioCaptureInputWorker &) = delete;
    AudioCaptureInputWorker &operator=(
        const AudioCaptureInputWorker &) = delete;

    vrrec_status_t Start(
        const AudioCaptureSourceConfig &config) noexcept;
    void Abort() noexcept;
    AudioCaptureInputResult Join() noexcept;

private:
    void Started(vrrec_status_t status) noexcept override;
    static void RunEntry(void *context) noexcept;
    void Run() noexcept;
    void PublishStartFailure(
        vrrec_status_t status,
        AudioCaptureInputResult result) noexcept;
    void JoinThread() noexcept;

    AudioCaptureInputRunner runner_;
    NativeThreadFactoryPort &thread_factory_;
    std::mutex state_mutex_;
    std::condition_variable state_changed_;
    std::mutex join_mutex_;
    std::thread thread_;
    AudioCaptureSourceConfig config_ {};
    vrrec_status_t start_status_ = VRREC_STATUS_INVALID_STATE;
    AudioCaptureInputResult result_ = AudioCaptureInputResult::InvalidState;
    bool start_reported_ = false;
    bool started_ = false;
    bool finished_ = false;
    bool runner_launched_ = false;
    std::atomic_bool abort_requested_ = false;
};

}

#endif
