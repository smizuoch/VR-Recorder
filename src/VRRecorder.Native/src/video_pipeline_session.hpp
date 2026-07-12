#ifndef VRRECORDER_NATIVE_VIDEO_PIPELINE_SESSION_HPP
#define VRRECORDER_NATIVE_VIDEO_PIPELINE_SESSION_HPP

#include <atomic>
#include <chrono>

#include "spout_capture_worker.hpp"
#include "video_encoding_worker.hpp"

namespace vrrecorder::native {

enum class VideoPipelineResult {
    Stopped,
    Aborted,
    SenderLost,
    CaptureFailed,
    EncoderFailed,
    InvalidState,
    Failed,
};

class VideoPipelineSession final {
public:
    VideoPipelineSession(
        SpoutCaptureWorkerPort &capture,
        VideoEncodingWorkerPort &encoding,
        MediaEventSink &events) noexcept;
    ~VideoPipelineSession();

    VideoPipelineSession(const VideoPipelineSession &) = delete;
    VideoPipelineSession &operator=(const VideoPipelineSession &) = delete;

    vrrec_status_t Start(
        std::chrono::milliseconds poll_timeout) noexcept;
    vrrec_status_t RequestStop() noexcept;
    void Abort() noexcept;
    VideoPipelineResult Join() noexcept;
    VideoEncodingStatistics Statistics() const noexcept;

private:
    SpoutCaptureWorkerPort &capture_;
    VideoEncodingWorkerPort &encoding_;
    MediaEventSink &events_;
    std::atomic_bool start_attempted_ = false;
    std::atomic_bool capture_started_ = false;
    std::atomic_bool encoding_started_ = false;
    std::atomic_bool stop_requested_ = false;
    std::atomic_bool active_ = false;
    std::atomic_bool aborted_ = false;
    std::atomic_bool finished_ = false;
};

}

#endif
