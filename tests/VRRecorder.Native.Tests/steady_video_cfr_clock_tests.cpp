#include "steady_video_cfr_clock.hpp"

#include <chrono>
#include <condition_variable>
#include <cstdlib>
#include <iostream>
#include <mutex>
#include <thread>
#include <vector>

namespace {

#define CHECK(condition)                                                        \
    do {                                                                        \
        if (!(condition)) {                                                     \
            std::cerr << "check failed at " << __FILE__ << ':' << __LINE__      \
                      << ": " #condition << '\n';                              \
            std::abort();                                                       \
        }                                                                       \
    } while (false)

using namespace vrrecorder::native;
using namespace std::chrono_literals;

class RecordingDeadlineWaiter final : public VideoCfrDeadlineWaiter {
public:
    VideoCfrTimePoint Now() noexcept override
    {
        return epoch;
    }

    VideoCfrDeadlineWaitResult WaitUntil(
        VideoCfrTimePoint deadline) noexcept override
    {
        deadlines.push_back(deadline);
        if (next_result == VideoCfrDeadlineWaitResult::Failed) {
            next_result = VideoCfrDeadlineWaitResult::Reached;
            return VideoCfrDeadlineWaitResult::Failed;
        }

        return aborted
            ? VideoCfrDeadlineWaitResult::Aborted
            : next_result;
    }

    void Abort() noexcept override
    {
        ++abort_count;
        aborted = true;
    }

    VideoCfrTimePoint epoch {123s};
    VideoCfrDeadlineWaitResult next_result =
        VideoCfrDeadlineWaitResult::Reached;
    std::vector<VideoCfrTimePoint> deadlines;
    std::size_t abort_count = 0;
    bool aborted = false;
};

class BlockingDeadlineWaiter final : public VideoCfrDeadlineWaiter {
public:
    VideoCfrTimePoint Now() noexcept override
    {
        return VideoCfrTimePoint {};
    }

    VideoCfrDeadlineWaitResult WaitUntil(
        VideoCfrTimePoint) noexcept override
    {
        std::unique_lock lock(mutex_);
        entered_ = true;
        changed_.notify_all();
        changed_.wait(lock, [this] { return aborted_; });
        return VideoCfrDeadlineWaitResult::Aborted;
    }

    void Abort() noexcept override
    {
        const std::lock_guard lock(mutex_);
        ++abort_count_;
        aborted_ = true;
        changed_.notify_all();
    }

    void WaitUntilEntered()
    {
        std::unique_lock lock(mutex_);
        CHECK(changed_.wait_for(lock, 1s, [this] { return entered_; }));
    }

    std::size_t AbortCount() const
    {
        const std::lock_guard lock(mutex_);
        return abort_count_;
    }

private:
    mutable std::mutex mutex_;
    std::condition_variable changed_;
    bool entered_ = false;
    bool aborted_ = false;
    std::size_t abort_count_ = 0;
};

void StartsAtTickZeroAndDoesNotAccumulateRoundingDrift()
{
    RecordingDeadlineWaiter waiter;
    SteadyVideoCfrClock clock(60, waiter);

    for (std::uint64_t expected_tick = 0; expected_tick < 4;
         ++expected_tick) {
        std::uint64_t tick = 99;
        CHECK(clock.WaitNext(tick) == VideoCfrClockResult::Tick);
        CHECK(tick == expected_tick);
    }

    CHECK(waiter.deadlines.size() == 4);
    CHECK(waiter.deadlines[0] == waiter.epoch);
    CHECK(waiter.deadlines[1] == waiter.epoch + 16'666'666ns);
    CHECK(waiter.deadlines[2] == waiter.epoch + 33'333'333ns);
    CHECK(waiter.deadlines[3] == waiter.epoch + 50'000'000ns);
}

void RejectsAnInvalidFrameRateWithoutWaiting()
{
    RecordingDeadlineWaiter waiter;
    SteadyVideoCfrClock clock(0, waiter);
    std::uint64_t tick = 41;

    CHECK(clock.WaitNext(tick) == VideoCfrClockResult::Failed);
    CHECK(tick == 41);
    CHECK(waiter.deadlines.empty());
}

void DoesNotConsumeATickWhenTheWaitFails()
{
    RecordingDeadlineWaiter waiter;
    waiter.next_result = VideoCfrDeadlineWaitResult::Failed;
    SteadyVideoCfrClock clock(30, waiter);
    std::uint64_t tick = 77;

    CHECK(clock.WaitNext(tick) == VideoCfrClockResult::Failed);
    CHECK(tick == 77);
    CHECK(clock.WaitNext(tick) == VideoCfrClockResult::Tick);
    CHECK(tick == 0);
    CHECK(waiter.deadlines[0] == waiter.deadlines[1]);
}

void FailsWhenTheNextDeadlineExceedsTheSteadyClockRange()
{
    RecordingDeadlineWaiter waiter;
    waiter.epoch = VideoCfrTimePoint::max() - 1ns;
    SteadyVideoCfrClock clock(60, waiter);
    std::uint64_t tick = 77;

    CHECK(clock.WaitNext(tick) == VideoCfrClockResult::Tick);
    CHECK(tick == 0);
    tick = 77;
    CHECK(clock.WaitNext(tick) == VideoCfrClockResult::Failed);
    CHECK(tick == 77);
    CHECK(waiter.deadlines.size() == 1);
}

void AbortWakesAWaitAndIsForwardedExactlyOnce()
{
    BlockingDeadlineWaiter waiter;
    SteadyVideoCfrClock clock(30, waiter);
    auto result = VideoCfrClockResult::Failed;
    std::uint64_t tick = 55;
    std::thread waiting([&] { result = clock.WaitNext(tick); });

    waiter.WaitUntilEntered();
    clock.Abort();
    clock.Abort();
    waiting.join();

    CHECK(result == VideoCfrClockResult::Aborted);
    CHECK(tick == 55);
    CHECK(waiter.AbortCount() == 1);
    CHECK(clock.WaitNext(tick) == VideoCfrClockResult::Aborted);
}

void ProductionWaiterProvidesTheImmediateFirstTick()
{
    SteadyVideoCfrClock clock(30);
    std::uint64_t tick = 88;

    CHECK(clock.WaitNext(tick) == VideoCfrClockResult::Tick);
    CHECK(tick == 0);
    clock.Abort();
    CHECK(clock.WaitNext(tick) == VideoCfrClockResult::Aborted);
}

}

int main()
{
    StartsAtTickZeroAndDoesNotAccumulateRoundingDrift();
    RejectsAnInvalidFrameRateWithoutWaiting();
    DoesNotConsumeATickWhenTheWaitFails();
    FailsWhenTheNextDeadlineExceedsTheSteadyClockRange();
    AbortWakesAWaitAndIsForwardedExactlyOnce();
    ProductionWaiterProvidesTheImmediateFirstTick();
    return 0;
}
