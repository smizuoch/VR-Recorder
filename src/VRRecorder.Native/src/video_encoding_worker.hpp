#ifndef VRRECORDER_NATIVE_VIDEO_ENCODING_WORKER_HPP
#define VRRECORDER_NATIVE_VIDEO_ENCODING_WORKER_HPP

#include <atomic>
#include <cstdint>
#include <mutex>
#include <thread>

#include "media_backend.hpp"
#include "native_thread_factory.hpp"
#include "video_encoding_pump.hpp"

namespace vrrecorder::native {

enum class VideoCfrClockResult {
    Tick,
    Aborted,
    Failed,
};

class VideoCfrClock {
public:
    virtual ~VideoCfrClock() = default;

    virtual VideoCfrClockResult WaitNext(
        std::uint64_t &tick) noexcept = 0;
    virtual void Abort() noexcept = 0;
};

enum class VideoEncodingWorkerResult {
    Stopped,
    Aborted,
    EncoderFailed,
    ClockFailed,
    InvalidState,
    Failed,
};

class VideoEncodingWorkerPort {
public:
    virtual ~VideoEncodingWorkerPort() = default;

    virtual vrrec_status_t Start() noexcept = 0;
    virtual vrrec_status_t RequestStop() noexcept = 0;
    virtual void RequestAbort() noexcept = 0;
    virtual void JoinAfterAbort() noexcept = 0;
    virtual void Abort() noexcept = 0;
    virtual VideoEncodingWorkerResult Join() noexcept = 0;
    virtual VideoEncodingStatistics Statistics() const noexcept = 0;
};

class VideoEncodingWorker final : public VideoEncodingWorkerPort {
public:
    VideoEncodingWorker(
        VideoCfrScheduler &scheduler,
        VideoCfrClock &clock,
        VideoEncoderSink &sink,
        MediaEventSink &events) noexcept;
    VideoEncodingWorker(
        VideoCfrScheduler &scheduler,
        VideoCfrClock &clock,
        VideoFramePreparingEncoderSink &sink,
        MediaEventSink &events) noexcept;
    VideoEncodingWorker(
        VideoCfrScheduler &scheduler,
        VideoCfrClock &clock,
        VideoEncoderSink &sink,
        MediaEventSink &events,
        NativeThreadFactoryPort &thread_factory) noexcept;
    VideoEncodingWorker(
        VideoCfrScheduler &scheduler,
        VideoCfrClock &clock,
        VideoFramePreparingEncoderSink &sink,
        MediaEventSink &events,
        NativeThreadFactoryPort &thread_factory) noexcept;
    ~VideoEncodingWorker();

    VideoEncodingWorker(const VideoEncodingWorker &) = delete;
    VideoEncodingWorker &operator=(const VideoEncodingWorker &) = delete;

    vrrec_status_t Start() noexcept override;
    vrrec_status_t RequestStop() noexcept override;
    void RequestAbort() noexcept override;
    void JoinAfterAbort() noexcept override;
    void Abort() noexcept override;
    VideoEncodingWorkerResult Join() noexcept override;
    VideoEncodingStatistics Statistics() const noexcept override;

private:
    static void RunEntry(void *context) noexcept;
    void Run() noexcept;
    void Finish() noexcept;
    void Fail(
        VideoEncodingWorkerResult result,
        vrrec_status_t status,
        const char *message,
        bool abort_clock) noexcept;
    bool SetResult(VideoEncodingWorkerResult result) noexcept;
    bool CommitStopped(
        const VideoEncoderWrite &finish,
        bool &report_first_packet) noexcept;
    bool CommitSubmitted(
        const VideoEncodingRead &read,
        bool &report_first_packet) noexcept;
    void JoinThread() noexcept;

    VideoCfrClock &clock_;
    VideoEncoderSink &sink_;
    MediaEventSink &events_;
    NativeThreadFactoryPort &thread_factory_;
    VideoEncodingPump pump_;
    std::mutex state_mutex_;
    std::mutex join_mutex_;
    std::thread thread_;
    VideoEncodingWorkerResult result_ =
        VideoEncodingWorkerResult::InvalidState;
    std::atomic<std::uint64_t> committed_muxed_packet_count_ {0};
    std::atomic<std::uint64_t> committed_latest_latency_microseconds_ {0};
    std::atomic<std::uint64_t> committed_maximum_latency_microseconds_ {0};
    std::atomic<std::uint64_t> flushed_packet_count_ {0};
    std::atomic<std::uint64_t> flushed_latency_microseconds_ {0};
    std::atomic_bool started_ = false;
    std::atomic_bool stop_requested_ = false;
    std::atomic_bool abort_requested_ = false;
    std::atomic_bool abort_cleanup_started_ = false;
    std::atomic_bool finished_ = false;
    std::atomic_bool first_packet_reported_ = false;
};

}

#endif
