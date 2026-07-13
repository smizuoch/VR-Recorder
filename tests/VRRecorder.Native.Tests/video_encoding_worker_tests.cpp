#include "video_encoding_worker.hpp"

#include <condition_variable>
#include <cstddef>
#include <cstdint>
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

class ScriptedCfrClock final : public VideoCfrClock {
public:
    VideoCfrClockResult WaitNext(std::uint64_t &tick) noexcept override
    {
        std::unique_lock lock(mutex);
        if (block_next_tick) {
            wait_entered = true;
            changed.notify_all();
            changed.wait(lock, [&] { return release_tick; });
            tick = next_tick++;
            return VideoCfrClockResult::Tick;
        }
        if (fail_unexpectedly) {
            return VideoCfrClockResult::Aborted;
        }

        if (next_tick < ready_tick_count) {
            tick = next_tick++;
            changed.notify_all();
            return VideoCfrClockResult::Tick;
        }

        changed.wait(lock, [&] { return aborted; });
        return VideoCfrClockResult::Aborted;
    }

    void Abort() noexcept override
    {
        {
            const std::lock_guard lock(mutex);
            if (aborted) {
                return;
            }

            aborted = true;
            ++abort_calls;
        }

        changed.notify_all();
    }

    void WaitForTicks(std::uint64_t count)
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return next_tick >= count; });
    }

    void WaitUntilBlocked()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return wait_entered; });
    }

    void WaitUntilAborted()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return aborted; });
    }

    void ReleaseTick()
    {
        {
            const std::lock_guard lock(mutex);
            release_tick = true;
        }
        changed.notify_all();
    }

    std::mutex mutex;
    std::condition_variable changed;
    std::uint64_t ready_tick_count = 2;
    std::uint64_t next_tick = 0;
    std::size_t abort_calls = 0;
    bool aborted = false;
    bool fail_unexpectedly = false;
    bool block_next_tick = false;
    bool wait_entered = false;
    bool release_tick = false;
};

class ScriptedVideoSink final : public VideoEncoderSink {
public:
    VideoEncoderWrite Write(
        const ScheduledVideoFrame &) noexcept override
    {
        ++write_calls;
        if (next_write >= writes.size()) {
            return {VRREC_STATUS_INTERNAL_ERROR, 0, 0};
        }

        return writes[next_write++];
    }

    VideoEncoderWrite Finish() noexcept override
    {
        ++finish_calls;
        return finish;
    }

    void Abort() noexcept override
    {
        ++abort_calls;
    }

    std::vector<VideoEncoderWrite> writes;
    VideoEncoderWrite finish {VRREC_STATUS_OK, 0, 0};
    std::size_t next_write = 0;
    std::size_t write_calls = 0;
    std::size_t finish_calls = 0;
    std::size_t abort_calls = 0;
};

class RecordingMediaEvents final : public MediaEventSink {
public:
    void FirstVideoPacketMuxed() noexcept override
    {
        ++first_packet_calls;
    }

    void Stopped(std::uint64_t, std::uint64_t) noexcept override
    {
    }

    void Faulted(
        vrrec_status_t status,
        const char *message_utf8) noexcept override
    {
        ++fault_calls;
        fault_status = status;
        fault_message = message_utf8;
    }

    void AudioEndpointAvailabilityChanged(
        AudioEndpointRole,
        bool,
        std::uint64_t) noexcept override
    {
    }

    std::size_t first_packet_calls = 0;
    std::size_t fault_calls = 0;
    vrrec_status_t fault_status = VRREC_STATUS_OK;
    const char *fault_message = nullptr;
};

void GracefulStopFlushesAndReportsTheFirstPacketOnce()
{
    VideoCfrScheduler scheduler;
    CHECK(scheduler.Push({10, 1'000'000}) == VRREC_STATUS_OK);
    ScriptedCfrClock clock;
    ScriptedVideoSink sink;
    sink.writes.push_back({VRREC_STATUS_OK, 0, 100});
    sink.writes.push_back({VRREC_STATUS_OK, 1, 200});
    sink.finish = {VRREC_STATUS_OK, 1, 150};
    RecordingMediaEvents events;
    VideoEncodingWorker worker(scheduler, clock, sink, events);

    CHECK(worker.Start() == VRREC_STATUS_OK);
    clock.WaitForTicks(2);
    CHECK(worker.RequestStop() == VRREC_STATUS_OK);
    CHECK(worker.RequestStop() == VRREC_STATUS_OK);
    CHECK(worker.Join() == VideoEncodingWorkerResult::Stopped);
    CHECK(events.first_packet_calls == 1);
    CHECK(events.fault_calls == 0);
    CHECK(sink.write_calls == 2);
    CHECK(sink.finish_calls == 1);
    CHECK(sink.abort_calls == 0);
    const auto statistics = worker.Statistics();
    CHECK(statistics.muxed_packet_count == 2);
    CHECK(statistics.latest_encode_latency_microseconds == 150);
    CHECK(statistics.maximum_encode_latency_microseconds == 200);
}

void RuntimeEncoderFailureRaisesFaultAndDoesNotFlush()
{
    VideoCfrScheduler scheduler;
    CHECK(scheduler.Push({20, 2'000'000}) == VRREC_STATUS_OK);
    ScriptedCfrClock clock;
    clock.ready_tick_count = 1;
    ScriptedVideoSink sink;
    sink.writes.push_back({VRREC_STATUS_INTERNAL_ERROR, 0, 999});
    RecordingMediaEvents events;
    VideoEncodingWorker worker(scheduler, clock, sink, events);

    CHECK(worker.Start() == VRREC_STATUS_OK);
    CHECK(worker.Join() == VideoEncodingWorkerResult::EncoderFailed);
    CHECK(worker.RequestStop() == VRREC_STATUS_INVALID_STATE);
    CHECK(events.first_packet_calls == 0);
    CHECK(events.fault_calls == 1);
    CHECK(events.fault_status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(events.fault_message != nullptr);
    CHECK(sink.finish_calls == 0);
    CHECK(sink.abort_calls == 1);
    CHECK(clock.abort_calls == 1);
}

void ForcedAbortDoesNotFlushOrRaiseFault()
{
    VideoCfrScheduler scheduler;
    ScriptedCfrClock clock;
    clock.ready_tick_count = 0;
    ScriptedVideoSink sink;
    RecordingMediaEvents events;
    VideoEncodingWorker worker(scheduler, clock, sink, events);

    CHECK(worker.Start() == VRREC_STATUS_OK);
    worker.Abort();
    worker.Abort();
    CHECK(worker.Join() == VideoEncodingWorkerResult::Aborted);
    CHECK(sink.finish_calls == 0);
    CHECK(sink.abort_calls == 1);
    CHECK(events.fault_calls == 0);
}

void UnexpectedClockAbortReleasesTheEncoderSinkAndRaisesFault()
{
    VideoCfrScheduler scheduler;
    ScriptedCfrClock clock;
    clock.fail_unexpectedly = true;
    ScriptedVideoSink sink;
    RecordingMediaEvents events;
    VideoEncodingWorker worker(scheduler, clock, sink, events);

    CHECK(worker.Start() == VRREC_STATUS_OK);
    CHECK(worker.Join() == VideoEncodingWorkerResult::ClockFailed);
    CHECK(clock.abort_calls == 0);
    CHECK(sink.abort_calls == 1);
    CHECK(sink.finish_calls == 0);
    CHECK(events.fault_calls == 1);
    CHECK(events.fault_status == VRREC_STATUS_INTERNAL_ERROR);
}

void AbortPreventsATickReturnedByAnInFlightClockWaitFromEncoding()
{
    VideoCfrScheduler scheduler;
    CHECK(scheduler.Push({30, 3'000'000}) == VRREC_STATUS_OK);
    ScriptedCfrClock clock;
    clock.block_next_tick = true;
    ScriptedVideoSink sink;
    sink.writes.push_back({VRREC_STATUS_OK, 1, 100});
    RecordingMediaEvents events;
    VideoEncodingWorker worker(scheduler, clock, sink, events);

    CHECK(worker.Start() == VRREC_STATUS_OK);
    clock.WaitUntilBlocked();
    std::thread aborting([&] { worker.Abort(); });
    clock.WaitUntilAborted();
    clock.ReleaseTick();
    aborting.join();

    CHECK(worker.Join() == VideoEncodingWorkerResult::Aborted);
    CHECK(sink.write_calls == 0);
    CHECK(events.first_packet_calls == 0);
    CHECK(events.fault_calls == 0);
}

}

int main()
{
    GracefulStopFlushesAndReportsTheFirstPacketOnce();
    RuntimeEncoderFailureRaisesFaultAndDoesNotFlush();
    ForcedAbortDoesNotFlushOrRaiseFault();
    UnexpectedClockAbortReleasesTheEncoderSinkAndRaisesFault();
    AbortPreventsATickReturnedByAnInFlightClockWaitFromEncoding();
    return 0;
}
