#ifndef VRRECORDER_NATIVE_VIDEO_ENCODING_WORKER_HPP
#define VRRECORDER_NATIVE_VIDEO_ENCODING_WORKER_HPP

#include <atomic>
#include <cstdint>
#include <mutex>
#include <thread>

#include "media_backend.hpp"
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

class VideoEncodingWorker final {
public:
    VideoEncodingWorker(
        VideoCfrScheduler &scheduler,
        VideoCfrClock &clock,
        VideoEncoderSink &sink,
        MediaEventSink &events) noexcept;
    ~VideoEncodingWorker();

    VideoEncodingWorker(const VideoEncodingWorker &) = delete;
    VideoEncodingWorker &operator=(const VideoEncodingWorker &) = delete;

    vrrec_status_t Start() noexcept;
    vrrec_status_t RequestStop() noexcept;
    void Abort() noexcept;
    VideoEncodingWorkerResult Join() noexcept;
    VideoEncodingStatistics Statistics() const noexcept;

private:
    void Run() noexcept;
    void Finish() noexcept;
    void Fail(
        VideoEncodingWorkerResult result,
        vrrec_status_t status,
        const char *message) noexcept;
    void SetResult(VideoEncodingWorkerResult result) noexcept;
    void JoinThread() noexcept;

    VideoCfrClock &clock_;
    VideoEncoderSink &sink_;
    MediaEventSink &events_;
    VideoEncodingPump pump_;
    std::mutex state_mutex_;
    std::mutex join_mutex_;
    std::thread thread_;
    VideoEncodingWorkerResult result_ =
        VideoEncodingWorkerResult::InvalidState;
    std::atomic<std::uint64_t> flushed_packet_count_ {0};
    std::atomic<std::uint64_t> flushed_latency_microseconds_ {0};
    std::atomic_bool started_ = false;
    std::atomic_bool stop_requested_ = false;
    std::atomic_bool abort_requested_ = false;
    std::atomic_bool finished_ = false;
    std::atomic_bool first_packet_reported_ = false;
};

}

#endif
