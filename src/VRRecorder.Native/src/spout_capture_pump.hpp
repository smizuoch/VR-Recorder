#ifndef VRRECORDER_NATIVE_SPOUT_CAPTURE_PUMP_HPP
#define VRRECORDER_NATIVE_SPOUT_CAPTURE_PUMP_HPP

#include <atomic>
#include <chrono>
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

class SpoutCapturePump final {
public:
    SpoutCapturePump(
        SpoutSourceBackend &backend,
        VideoCfrScheduler &scheduler,
        std::string selected_sender_id);

    SpoutCaptureResult PollOne(
        std::chrono::milliseconds timeout) noexcept;
    void Abort() noexcept;

private:
    static bool IsFrameValid(const SpoutFrame &frame) noexcept;

    SpoutSourceBackend &backend_;
    VideoCfrScheduler &scheduler_;
    std::string selected_sender_id_;
    std::atomic_bool aborted_ = false;
};

}

#endif
