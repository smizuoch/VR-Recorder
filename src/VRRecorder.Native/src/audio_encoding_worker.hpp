#ifndef VRRECORDER_NATIVE_AUDIO_ENCODING_WORKER_HPP
#define VRRECORDER_NATIVE_AUDIO_ENCODING_WORKER_HPP

#include <atomic>
#include <cstddef>
#include <cstdint>
#include <mutex>
#include <thread>

#include "audio_encoding_pump.hpp"

namespace vrrecorder::native {

enum class StereoAudioEncodingWorkerResult {
    Stopped,
    Aborted,
    CaptureFailed,
    EncoderFailed,
    MuxFailed,
    InvalidState,
    Failed,
};

class StereoAudioEncodingWorker final {
public:
    StereoAudioEncodingWorker(
        StereoAudioMixSource &source,
        StereoAudioEncoderSink &sink) noexcept;
    ~StereoAudioEncodingWorker();

    StereoAudioEncodingWorker(const StereoAudioEncodingWorker &) = delete;
    StereoAudioEncodingWorker &operator=(
        const StereoAudioEncodingWorker &) = delete;

    vrrec_status_t Start(std::size_t frame_count_48k) noexcept;
    vrrec_status_t RequestStop() noexcept;
    void RequestAbort() noexcept;
    void JoinAfterAbort() noexcept;
    void Abort() noexcept;
    StereoAudioEncodingWorkerResult Join() noexcept;

    std::uint64_t SubmittedFrameCount() const noexcept;
    std::uint64_t MuxedPacketCount() const noexcept;
    bool IsFinished() const noexcept;

private:
    void Run() noexcept;
    void JoinThread() noexcept;
    void SetResult(StereoAudioEncodingWorkerResult result) noexcept;

    StereoAudioMixSource &source_;
    StereoAudioEncoderSink &sink_;
    StereoAudioEncodingPump pump_;
    std::mutex state_mutex_;
    std::mutex join_mutex_;
    std::thread thread_;
    std::size_t frame_count_48k_ = 0;
    StereoAudioEncodingWorkerResult result_ =
        StereoAudioEncodingWorkerResult::InvalidState;
    std::atomic<std::uint64_t> flushed_packet_count_ {0};
    std::atomic_bool started_ = false;
    std::atomic_bool stop_requested_ = false;
    std::atomic_bool abort_requested_ = false;
    std::atomic_bool abort_cleanup_started_ = false;
    std::atomic_bool terminal_ = false;
    std::atomic_bool finished_ = false;
};

}

#endif
