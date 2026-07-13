#ifndef VRRECORDER_NATIVE_VIDEO_PIPELINE_SESSION_HPP
#define VRRECORDER_NATIVE_VIDEO_PIPELINE_SESSION_HPP

#include <atomic>
#include <chrono>
#include <mutex>

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

class VideoPipelineSessionPort {
public:
    virtual ~VideoPipelineSessionPort() = default;
    virtual vrrec_status_t Start(
        std::chrono::milliseconds poll_timeout) noexcept = 0;
    virtual vrrec_status_t RequestStop() noexcept = 0;
    virtual void RequestAbort() noexcept = 0;
    virtual void JoinAfterAbort() noexcept = 0;
    virtual void Abort() noexcept = 0;
    virtual VideoPipelineResult Join() noexcept = 0;
    virtual VideoEncodingStatistics Statistics() const noexcept = 0;
};

class VideoPipelineSession final : public VideoPipelineSessionPort {
public:
    VideoPipelineSession(
        SpoutCaptureWorkerPort &capture,
        VideoEncodingWorkerPort &encoding,
        MediaEventSink &events) noexcept;
    ~VideoPipelineSession();

    VideoPipelineSession(const VideoPipelineSession &) = delete;
    VideoPipelineSession &operator=(const VideoPipelineSession &) = delete;

    vrrec_status_t Start(
        std::chrono::milliseconds poll_timeout) noexcept override;
    vrrec_status_t RequestStop() noexcept override;
    void RequestAbort() noexcept override;
    void JoinAfterAbort() noexcept override;
    void Abort() noexcept override;
    VideoPipelineResult Join() noexcept override;
    VideoEncodingStatistics Statistics() const noexcept override;

private:
    SpoutCaptureWorkerPort &capture_;
    VideoEncodingWorkerPort &encoding_;
    MediaEventSink &events_;
    std::mutex abort_join_mutex_;
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
