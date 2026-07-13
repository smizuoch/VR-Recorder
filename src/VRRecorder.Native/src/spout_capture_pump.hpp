#ifndef VRRECORDER_NATIVE_SPOUT_CAPTURE_PUMP_HPP
#define VRRECORDER_NATIVE_SPOUT_CAPTURE_PUMP_HPP

#include <atomic>
#include <chrono>
#include <mutex>
#include <string>

#include "spout_source_backend.hpp"
#include "video_cfr_scheduler.hpp"

namespace vrrecorder::native {

enum class SpoutCaptureResult {
    FrameAccepted,
    Timeout,
    SenderLost,
    Aborted,
    InvalidFrame,
    Failed,
};

class SpoutCaptureSource {
public:
    virtual ~SpoutCaptureSource() = default;

    virtual SpoutCaptureResult PollOne(
        std::chrono::milliseconds timeout) noexcept = 0;
    virtual void Abort() noexcept = 0;
};

class SpoutCapturePump final : public SpoutCaptureSource {
public:
    SpoutCapturePump(
        SpoutSourceBackend &backend,
        VideoCfrScheduler &scheduler,
        std::string selected_sender_id);

    SpoutCaptureResult PollOne(
        std::chrono::milliseconds timeout) noexcept override;
    void Abort() noexcept override;

private:
    static bool IsFrameValid(const SpoutFrame &frame) noexcept;

    SpoutSourceBackend &backend_;
    VideoCfrScheduler &scheduler_;
    std::string selected_sender_id_;
    std::mutex lifecycle_mutex_;
    std::atomic_bool aborted_ = false;
};

}

#endif
