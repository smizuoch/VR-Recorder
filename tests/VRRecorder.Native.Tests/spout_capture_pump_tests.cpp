#include "spout_capture_pump.hpp"

#include <chrono>
#include <cstddef>
#include <cstdlib>
#include <deque>
#include <iostream>
#include <utility>
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

struct PollResult final {
    vrrec_status_t status;
    SpoutFrame frame;
};

class ScriptedSpoutBackend final : public SpoutSourceBackend {
public:
    vrrec_status_t Snapshot(
        std::vector<SpoutSenderSnapshot> &senders) override
    {
        senders.clear();
        return VRREC_STATUS_OK;
    }

    vrrec_status_t Poll(
        std::chrono::milliseconds timeout,
        SpoutFrame &frame) override
    {
        last_timeout = timeout;
        ++poll_calls;
        if (polls.empty()) {
            return VRREC_STATUS_TIMEOUT;
        }

        auto result = std::move(polls.front());
        polls.pop_front();
        frame = std::move(result.frame);
        return result.status;
    }

    void Abort() noexcept override
    {
        ++abort_calls;
    }

    std::deque<PollResult> polls;
    std::chrono::milliseconds last_timeout {0};
    std::size_t poll_calls = 0;
    std::size_t abort_calls = 0;
};

SpoutFrame Frame(
    const char *sender,
    std::uint64_t sequence,
    std::int64_t timestamp)
{
    return {
        sender,
        42,
        "gpu",
        VRREC_GPU_VENDOR_NVIDIA,
        1'920,
        1'080,
        VRREC_SOURCE_PIXEL_FORMAT_BGRA8,
        60.0,
        sequence,
        timestamp,
    };
}

void AcceptsOnlyTheSelectedSenderIntoTheScheduler()
{
    ScriptedSpoutBackend backend;
    backend.polls.push_back({
        VRREC_STATUS_OK,
        Frame("selected", 10, 1'000'000),
    });
    VideoCfrScheduler scheduler;
    SpoutCapturePump pump(backend, scheduler, "selected");

    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::FrameAccepted);
    CHECK(backend.last_timeout == std::chrono::milliseconds(100));
    ScheduledVideoFrame output {};
    CHECK(scheduler.Schedule(0, output) == VideoScheduleResult::Ready);
    CHECK(output.source_sequence == 10);
    CHECK(output.source_timestamp_microseconds == 1'000'000);
}

void KeepsTimeoutSeparateFromSenderLoss()
{
    ScriptedSpoutBackend backend;
    backend.polls.push_back({VRREC_STATUS_TIMEOUT, {}});
    backend.polls.push_back({VRREC_STATUS_BACKEND_UNAVAILABLE, {}});
    VideoCfrScheduler scheduler;
    SpoutCapturePump pump(backend, scheduler, "selected");

    CHECK(pump.PollOne(std::chrono::milliseconds(50)) ==
          SpoutCaptureResult::Timeout);
    CHECK(pump.PollOne(std::chrono::milliseconds(50)) ==
          SpoutCaptureResult::SenderLost);
    CHECK(scheduler.Statistics().source_frame_count == 0);
}

void RejectsFramesFromAnotherSender()
{
    ScriptedSpoutBackend backend;
    backend.polls.push_back({
        VRREC_STATUS_OK,
        Frame("another", 20, 2'000'000),
    });
    VideoCfrScheduler scheduler;
    SpoutCapturePump pump(backend, scheduler, "selected");

    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::InvalidFrame);
    CHECK(scheduler.Statistics().source_frame_count == 0);
}

void AbortStopsPollingAndReleasesTheBackend()
{
    ScriptedSpoutBackend backend;
    VideoCfrScheduler scheduler;
    SpoutCapturePump pump(backend, scheduler, "selected");

    pump.Abort();
    pump.Abort();
    CHECK(backend.abort_calls == 1);
    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::Aborted);
    CHECK(backend.poll_calls == 0);
}

}

int main()
{
    AcceptsOnlyTheSelectedSenderIntoTheScheduler();
    KeepsTimeoutSeparateFromSenderLoss();
    RejectsFramesFromAnotherSender();
    AbortStopsPollingAndReleasesTheBackend();
    return 0;
}
