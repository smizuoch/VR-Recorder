#include "steady_video_cfr_clock.hpp"

#include <condition_variable>
#include <limits>

namespace vrrecorder::native {
namespace {

class ConditionVariableVideoCfrDeadlineWaiter final
    : public VideoCfrDeadlineWaiter {
public:
    VideoCfrTimePoint Now() noexcept override
    {
        return std::chrono::steady_clock::now();
    }

    VideoCfrDeadlineWaitResult WaitUntil(
        VideoCfrTimePoint deadline) noexcept override
    {
        std::unique_lock lock(mutex_);
        changed_.wait_until(lock, deadline, [this] { return aborted_; });
        return aborted_
            ? VideoCfrDeadlineWaitResult::Aborted
            : VideoCfrDeadlineWaitResult::Reached;
    }

    void Abort() noexcept override
    {
        const std::lock_guard lock(mutex_);
        aborted_ = true;
        changed_.notify_all();
    }

private:
    std::mutex mutex_;
    std::condition_variable changed_;
    bool aborted_ = false;
};

std::unique_ptr<VideoCfrDeadlineWaiter> CreateDeadlineWaiter()
{
    return std::make_unique<ConditionVariableVideoCfrDeadlineWaiter>();
}

}

SteadyVideoCfrClock::SteadyVideoCfrClock(
    std::uint32_t frames_per_second)
    : owned_waiter_(CreateDeadlineWaiter()),
      waiter_(*owned_waiter_),
      frames_per_second_(frames_per_second)
{
}

SteadyVideoCfrClock::SteadyVideoCfrClock(
    std::uint32_t frames_per_second,
    VideoCfrDeadlineWaiter &waiter) noexcept
    : waiter_(waiter),
      frames_per_second_(frames_per_second)
{
}

VideoCfrClockResult SteadyVideoCfrClock::WaitNext(
    std::uint64_t &tick) noexcept
{
    const std::lock_guard lock(sequence_mutex_);
    if (aborted_.load(std::memory_order_acquire)) {
        return VideoCfrClockResult::Aborted;
    }
    if (frames_per_second_ == 0) {
        return VideoCfrClockResult::Failed;
    }
    if (!initialized_) {
        epoch_ = waiter_.Now();
        initialized_ = true;
    }

    VideoCfrTimePoint deadline {};
    if (!TryGetNextDeadline(deadline)) {
        return VideoCfrClockResult::Failed;
    }

    const auto wait_result = waiter_.WaitUntil(deadline);
    if (wait_result == VideoCfrDeadlineWaitResult::Aborted) {
        return VideoCfrClockResult::Aborted;
    }
    if (wait_result != VideoCfrDeadlineWaitResult::Reached) {
        return VideoCfrClockResult::Failed;
    }

    if (next_tick_ == std::numeric_limits<std::uint64_t>::max()) {
        return VideoCfrClockResult::Failed;
    }
    tick = next_tick_;
    ++next_tick_;
    return VideoCfrClockResult::Tick;
}

void SteadyVideoCfrClock::Abort() noexcept
{
    if (!aborted_.exchange(true, std::memory_order_acq_rel)) {
        waiter_.Abort();
    }
}

bool SteadyVideoCfrClock::TryGetNextDeadline(
    VideoCfrTimePoint &deadline) noexcept
{
    using namespace std::chrono;

    const auto whole_seconds = next_tick_ / frames_per_second_;
    const auto remaining_ticks = next_tick_ % frames_per_second_;
    const auto available_seconds = duration_cast<seconds>(
        VideoCfrTimePoint::max() - epoch_).count();
    if (whole_seconds > static_cast<std::uint64_t>(available_seconds)) {
        return false;
    }

    const auto fractional_nanoseconds =
        remaining_ticks * 1'000'000'000ULL / frames_per_second_;
    const auto offset = seconds(whole_seconds) +
        nanoseconds(fractional_nanoseconds);
    const auto available = VideoCfrTimePoint::max() - epoch_;
    if (duration_cast<VideoCfrTimePoint::duration>(offset) > available) {
        return false;
    }

    deadline = epoch_ +
        duration_cast<VideoCfrTimePoint::duration>(offset);
    return true;
}

}
