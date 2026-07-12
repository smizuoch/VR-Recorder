#ifndef VRRECORDER_NATIVE_AUDIO_CAPTURE_INPUT_WORKER_HPP
#define VRRECORDER_NATIVE_AUDIO_CAPTURE_INPUT_WORKER_HPP

#include <condition_variable>
#include <mutex>
#include <thread>

#include "audio_capture_input_runner.hpp"

namespace vrrecorder::native {

class AudioCaptureInputWorker final : private AudioCaptureInputStartSink {
public:
    AudioCaptureInputWorker(
        AudioCaptureSourceProvider &provider,
        AudioCaptureRecoveryWaiter &waiter,
        StereoCaptureTimeline &timeline,
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
    void Run() noexcept;
    void JoinThread() noexcept;

    AudioCaptureInputRunner runner_;
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
};

}

#endif
