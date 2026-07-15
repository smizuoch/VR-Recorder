#ifndef VRRECORDER_NATIVE_STEADY_VIDEO_CFR_CLOCK_HPP
#define VRRECORDER_NATIVE_STEADY_VIDEO_CFR_CLOCK_HPP

#include <atomic>
#include <chrono>
#include <cstdint>
#include <memory>
#include <mutex>

#include "video_encoding_worker.hpp"

namespace vrrecorder::native {

using VideoCfrTimePoint = std::chrono::steady_clock::time_point;

enum class VideoCfrDeadlineWaitResult {
    Reached,
    Aborted,
    Failed,
};

class VideoCfrDeadlineWaiter {
public:
    virtual ~VideoCfrDeadlineWaiter() = default;

    virtual VideoCfrTimePoint Now() noexcept = 0;
    virtual VideoCfrDeadlineWaitResult WaitUntil(
        VideoCfrTimePoint deadline) noexcept = 0;
    virtual void Abort() noexcept = 0;
};

class SteadyVideoCfrClock final : public VideoCfrClock {
public:
    explicit SteadyVideoCfrClock(std::uint32_t frames_per_second);
    SteadyVideoCfrClock(
        std::uint32_t frames_per_second,
        VideoCfrDeadlineWaiter &waiter) noexcept;

    VideoCfrClockResult WaitNext(
        std::uint64_t &tick) noexcept override;
    void Abort() noexcept override;

private:
    bool TryGetNextDeadline(VideoCfrTimePoint &deadline) noexcept;

    std::unique_ptr<VideoCfrDeadlineWaiter> owned_waiter_;
    VideoCfrDeadlineWaiter &waiter_;
    const std::uint32_t frames_per_second_;
    std::mutex sequence_mutex_;
    VideoCfrTimePoint epoch_ {};
    std::uint64_t next_tick_ = 0;
    bool initialized_ = false;
    std::atomic_bool aborted_ = false;
};

}

#endif
