#include "video_encoding_worker.hpp"

#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <future>
#include <iostream>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <tuple>
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
        if (fail_clock) {
            return VideoCfrClockResult::Failed;
        }

        if (next_tick < ready_tick_count) {
            tick = next_tick++;
            changed.notify_all();
            return VideoCfrClockResult::Tick;
        }

        waiting_for_abort = true;
        changed.notify_all();
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

    void WaitUntilWaitingForAbort()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return waiting_for_abort; });
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
    bool fail_clock = false;
    bool block_next_tick = false;
    bool wait_entered = false;
    bool waiting_for_abort = false;
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
        {
            const std::lock_guard lock(mutex);
            ++abort_calls;
        }
        changed.notify_all();
    }

    void WaitForAbort()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [this] { return abort_calls > 0; });
    }

    std::mutex mutex;
    std::condition_variable changed;
    std::vector<VideoEncoderWrite> writes;
    VideoEncoderWrite finish {VRREC_STATUS_OK, 0, 0};
    std::size_t next_write = 0;
    std::size_t write_calls = 0;
    std::size_t finish_calls = 0;
    std::size_t abort_calls = 0;
};

class WorkerSurface final : public VideoSurface {
public:
    explicit WorkerSurface(std::vector<int> *order = nullptr) noexcept
        : order_(order)
    {
    }

    VideoSurfaceDescriptor Descriptor() const noexcept override
    {
        return {42, 1'920, 1'080, VRREC_SOURCE_PIXEL_FORMAT_BGRA8};
    }

    void *NativeHandle() const noexcept override
    {
        return reinterpret_cast<void *>(1);
    }

    VideoSurfaceAcquireResult AcquireForRead(
        std::chrono::milliseconds) noexcept override
    {
        if (order_ != nullptr) {
            order_->push_back(1);
        }
        return acquire_result;
    }

    vrrec_status_t ReleaseFromRead() noexcept override
    {
        if (order_ != nullptr) {
            order_->push_back(3);
        }
        return release_status;
    }

    vrrec_status_t release_status = VRREC_STATUS_OK;
    VideoSurfaceAcquireResult acquire_result =
        VideoSurfaceAcquireResult::Acquired;

private:
    std::vector<int> *order_;
};

class WorkerPreparingVideoSink final
    : public VideoFramePreparingEncoderSink {
public:
    VideoFramePreparation Prepare(
        const ScheduledVideoFrame &frame) noexcept override
    {
        order.push_back(2);
        auto prepared = frame;
        prepared.surface = prepared_surface;
        return {VRREC_STATUS_OK, std::move(prepared)};
    }

    VideoEncoderWrite WritePrepared(
        const ScheduledVideoFrame &) noexcept override
    {
        ++write_calls;
        order.push_back(4);
        return {VRREC_STATUS_OK, 1, 100};
    }

    VideoEncoderWrite Finish() noexcept override
    {
        return {VRREC_STATUS_OK, 0, 0};
    }

    void Abort() noexcept override
    {
        if (abort_calls != 0) {
            return;
        }
        ++abort_calls;
        order.push_back(5);
    }

    std::shared_ptr<VideoSurface> prepared_surface =
        std::make_shared<WorkerSurface>();
    std::vector<int> order;
    std::size_t write_calls = 0;
    std::size_t abort_calls = 0;
};

class BlockingFinishVideoSink final : public VideoEncoderSink {
public:
    VideoEncoderWrite Write(
        const ScheduledVideoFrame &) noexcept override
    {
        const std::lock_guard lock(mutex);
        ++write_calls;
        return {VRREC_STATUS_OK, 1, 100};
    }

    VideoEncoderWrite Finish() noexcept override
    {
        std::unique_lock lock(mutex);
        ++finish_calls;
        finish_entered = true;
        changed.notify_all();
        changed.wait(lock, [&] { return release_finish; });
        return finish;
    }

    void Abort() noexcept override
    {
        {
            const std::lock_guard lock(mutex);
            ++abort_calls;
        }
        changed.notify_all();
    }

    void WaitForFinish()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return finish_entered; });
    }

    void WaitForAbort()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return abort_calls != 0; });
    }

    void ReleaseFinish()
    {
        {
            const std::lock_guard lock(mutex);
            release_finish = true;
        }
        changed.notify_all();
    }

    std::mutex mutex;
    std::condition_variable changed;
    VideoEncoderWrite finish {VRREC_STATUS_OK, 7, 900};
    std::size_t write_calls = 0;
    std::size_t finish_calls = 0;
    std::size_t abort_calls = 0;
    bool finish_entered = false;
    bool release_finish = false;
};

class BlockingWriteVideoSink final : public VideoEncoderSink {
public:
    VideoEncoderWrite Write(
        const ScheduledVideoFrame &) noexcept override
    {
        std::unique_lock lock(mutex);
        ++write_calls;
        if (write_calls == block_write_call) {
            write_entered = true;
            changed.notify_all();
            changed.wait(lock, [this] { return release_write; });
            return blocked_write_result;
        }
        return immediate_write_result;
    }

    VideoEncoderWrite Finish() noexcept override
    {
        const std::lock_guard lock(mutex);
        ++finish_calls;
        return {VRREC_STATUS_OK, 0, 0};
    }

    void Abort() noexcept override
    {
        {
            const std::lock_guard lock(mutex);
            ++abort_calls;
        }
        changed.notify_all();
    }

    void WaitForWrite()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [this] { return write_entered; });
    }

    void WaitForAbort()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [this] { return abort_calls > 0; });
    }

    void ReleaseWrite()
    {
        {
            const std::lock_guard lock(mutex);
            release_write = true;
        }
        changed.notify_all();
    }

    std::mutex mutex;
    std::condition_variable changed;
    VideoEncoderWrite immediate_write_result {VRREC_STATUS_OK, 1, 100};
    VideoEncoderWrite blocked_write_result {
        VRREC_STATUS_INVALID_STATE,
        0,
        900,
        VideoEncoderFailureStage::Encoding,
    };
    std::size_t block_write_call = 1;
    std::size_t write_calls = 0;
    std::size_t finish_calls = 0;
    std::size_t abort_calls = 0;
    bool write_entered = false;
    bool release_write = false;
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

    void VideoEncoderFailed(
        vrrec_status_t status,
        const char *message_utf8) noexcept override
    {
        ++video_encoder_failure_calls;
        video_encoder_failure_status = status;
        video_encoder_failure_message = message_utf8;
    }

    void AudioEndpointAvailabilityChanged(
        AudioEndpointRole,
        bool,
        std::uint64_t) noexcept override
    {
    }

    std::size_t first_packet_calls = 0;
    std::size_t fault_calls = 0;
    std::size_t video_encoder_failure_calls = 0;
    vrrec_status_t fault_status = VRREC_STATUS_OK;
    vrrec_status_t video_encoder_failure_status = VRREC_STATUS_OK;
    const char *fault_message = nullptr;
    const char *video_encoder_failure_message = nullptr;
};

class ScriptedThreadFactory final : public NativeThreadFactoryPort {
public:
    ScriptedThreadFactory(
        vrrec_status_t status,
        bool create_thread_on_success = true) noexcept
        : status_(status),
          create_thread_on_success_(create_thread_on_success)
    {
    }

    vrrec_status_t Start(
        std::thread &thread,
        NativeThreadEntry entry,
        void *context) noexcept override
    {
        ++start_calls;
        if (status_ == VRREC_STATUS_OK && create_thread_on_success_) {
            thread = std::thread(entry, context);
        }
        return status_;
    }

    std::size_t start_calls = 0;

private:
    vrrec_status_t status_;
    bool create_thread_on_success_;
};

class BlockingThreadFactory final : public NativeThreadFactoryPort {
public:
    BlockingThreadFactory(
        vrrec_status_t status,
        bool create_thread_on_success) noexcept
        : status_(status),
          create_thread_on_success_(create_thread_on_success)
    {
    }

    vrrec_status_t Start(
        std::thread &thread,
        NativeThreadEntry entry,
        void *context) noexcept override
    {
        {
            std::unique_lock lock(mutex_);
            ++start_calls_;
            start_entered_ = true;
            changed_.notify_all();
            changed_.wait(lock, [this] { return release_start_; });
        }
        if (status_ == VRREC_STATUS_OK && create_thread_on_success_) {
            thread = std::thread(entry, context);
        }
        return status_;
    }

    void WaitForStart()
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [this] { return start_entered_; });
    }

    void ReleaseStart()
    {
        {
            const std::lock_guard lock(mutex_);
            release_start_ = true;
        }
        changed_.notify_all();
    }

    std::size_t StartCalls() const
    {
        const std::lock_guard lock(mutex_);
        return start_calls_;
    }

private:
    vrrec_status_t status_;
    bool create_thread_on_success_;
    mutable std::mutex mutex_;
    std::condition_variable changed_;
    std::size_t start_calls_ = 0;
    bool start_entered_ = false;
    bool release_start_ = false;
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

void RuntimeEncoderFailureAfterFirstPacketSealsPartWithoutAbortingSink()
{
    VideoCfrScheduler scheduler;
    CHECK(scheduler.Push({20, 2'000'000}) == VRREC_STATUS_OK);
    ScriptedCfrClock clock;
    clock.ready_tick_count = 2;
    ScriptedVideoSink sink;
    sink.writes.push_back({VRREC_STATUS_OK, 1, 100});
    sink.writes.push_back({
        VRREC_STATUS_INTERNAL_ERROR,
        0,
        999,
        VideoEncoderFailureStage::Encoding,
        true,
    });
    RecordingMediaEvents events;
    VideoEncodingWorker worker(scheduler, clock, sink, events);

    CHECK(worker.Start() == VRREC_STATUS_OK);
    CHECK(worker.Join() ==
          VideoEncodingWorkerResult::EncoderFailedPartSealed);
    CHECK(worker.RequestStop() == VRREC_STATUS_OK);
    CHECK(events.first_packet_calls == 1);
    CHECK(events.fault_calls == 0);
    CHECK(events.video_encoder_failure_calls == 1);
    CHECK(events.video_encoder_failure_status ==
          VRREC_STATUS_INTERNAL_ERROR);
    CHECK(events.video_encoder_failure_message != nullptr);
    CHECK(sink.finish_calls == 0);
    CHECK(sink.abort_calls == 0);
    CHECK(clock.abort_calls == 1);
    CHECK(worker.Statistics().muxed_packet_count == 1);
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

void AbortDominatesAConcurrentGracefulFinish()
{
    VideoCfrScheduler scheduler;
    ScriptedCfrClock clock;
    clock.ready_tick_count = 0;
    BlockingFinishVideoSink sink;
    RecordingMediaEvents events;
    VideoEncodingWorker worker(scheduler, clock, sink, events);

    CHECK(worker.Start() == VRREC_STATUS_OK);
    CHECK(worker.RequestStop() == VRREC_STATUS_OK);
    sink.WaitForFinish();
    std::thread aborting([&] { worker.Abort(); });
    sink.WaitForAbort();
    sink.ReleaseFinish();
    aborting.join();

    CHECK(worker.Join() == VideoEncodingWorkerResult::Aborted);
    const auto statistics = worker.Statistics();
    CHECK(statistics.muxed_packet_count == 0);
    CHECK(statistics.latest_encode_latency_microseconds == 0);
    CHECK(statistics.maximum_encode_latency_microseconds == 0);
    CHECK(events.first_packet_calls == 0);
    CHECK(events.fault_calls == 0);
    {
        const std::lock_guard lock(sink.mutex);
        CHECK(sink.write_calls == 0);
        CHECK(sink.finish_calls == 1);
        CHECK(sink.abort_calls == 1);
    }
}

void AbortDominatesAnInFlightVideoWriteFailure()
{
    VideoCfrScheduler scheduler;
    CHECK(scheduler.Push({40, 4'000'000}) == VRREC_STATUS_OK);
    ScriptedCfrClock clock;
    clock.ready_tick_count = 1;
    BlockingWriteVideoSink sink;
    RecordingMediaEvents events;
    VideoEncodingWorker worker(scheduler, clock, sink, events);

    CHECK(worker.Start() == VRREC_STATUS_OK);
    sink.WaitForWrite();
    std::thread aborting([&] { worker.Abort(); });
    sink.WaitForAbort();
    sink.ReleaseWrite();
    aborting.join();

    CHECK(worker.Join() == VideoEncodingWorkerResult::Aborted);
    const auto statistics = worker.Statistics();
    CHECK(statistics.muxed_packet_count == 0);
    CHECK(statistics.latest_encode_latency_microseconds == 0);
    CHECK(statistics.maximum_encode_latency_microseconds == 0);
    CHECK(events.first_packet_calls == 0);
    CHECK(events.fault_calls == 0);
    CHECK(clock.abort_calls == 1);
    {
        const std::lock_guard lock(sink.mutex);
        CHECK(sink.write_calls == 1);
        CHECK(sink.finish_calls == 0);
        CHECK(sink.abort_calls == 1);
    }
}

void AbortDoesNotCommitASuccessfulInFlightVideoWrite()
{
    VideoCfrScheduler scheduler;
    CHECK(scheduler.Push({40, 4'000'000}) == VRREC_STATUS_OK);
    ScriptedCfrClock clock;
    clock.ready_tick_count = 2;
    BlockingWriteVideoSink sink;
    sink.block_write_call = 2;
    sink.immediate_write_result = {VRREC_STATUS_OK, 0, 100};
    sink.blocked_write_result = {VRREC_STATUS_OK, 1, 900};
    RecordingMediaEvents events;
    VideoEncodingWorker worker(scheduler, clock, sink, events);

    CHECK(worker.Start() == VRREC_STATUS_OK);
    sink.WaitForWrite();
    std::thread aborting([&] { worker.Abort(); });
    sink.WaitForAbort();
    sink.ReleaseWrite();
    aborting.join();

    CHECK(worker.Join() == VideoEncodingWorkerResult::Aborted);
    const auto statistics = worker.Statistics();
    CHECK(statistics.muxed_packet_count == 0);
    CHECK(statistics.latest_encode_latency_microseconds == 100);
    CHECK(statistics.maximum_encode_latency_microseconds == 100);
    CHECK(events.first_packet_calls == 0);
    CHECK(events.fault_calls == 0);
    CHECK(clock.abort_calls == 1);
    {
        const std::lock_guard lock(sink.mutex);
        CHECK(sink.write_calls == 2);
        CHECK(sink.finish_calls == 0);
        CHECK(sink.abort_calls == 1);
    }
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

void ClockFailureReleasesTheEncoderSinkAndRaisesFault()
{
    VideoCfrScheduler scheduler;
    ScriptedCfrClock clock;
    clock.fail_clock = true;
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

void EmptyAndTimedOutTicksRemainNonTerminalUntilStopped()
{
    for (const auto acquire_result : {
             VideoSurfaceAcquireResult::Acquired,
             VideoSurfaceAcquireResult::Timeout,
         }) {
        VideoCfrScheduler scheduler;
        if (acquire_result == VideoSurfaceAcquireResult::Timeout) {
            const auto surface = std::make_shared<WorkerSurface>();
            surface->acquire_result = acquire_result;
            CHECK(scheduler.Push({31, 3'100'000, surface}) ==
                  VRREC_STATUS_OK);
        }
        ScriptedCfrClock clock;
        clock.ready_tick_count = 1;
        ScriptedVideoSink sink;
        RecordingMediaEvents events;
        VideoEncodingWorker worker(scheduler, clock, sink, events);

        CHECK(worker.Start() == VRREC_STATUS_OK);
        clock.WaitUntilWaitingForAbort();
        CHECK(worker.RequestStop() == VRREC_STATUS_OK);
        CHECK(worker.Join() == VideoEncodingWorkerResult::Stopped);
        CHECK(sink.write_calls == 0);
        CHECK(sink.finish_calls == 1);
        CHECK(sink.abort_calls == 0);
        CHECK(events.fault_calls == 0);
    }
}

void ProcessingAndMuxWriteFailuresKeepTheirStageIdentity()
{
    for (const auto &[stage, message] : {
             std::pair {
                 VideoEncoderFailureStage::Processing,
                 "video frame processing failed while recording"},
             std::pair {
                 VideoEncoderFailureStage::Muxing,
                 "video packet muxing failed while recording"},
         }) {
        VideoCfrScheduler scheduler;
        CHECK(scheduler.Push({32, 3'200'000}) == VRREC_STATUS_OK);
        ScriptedCfrClock clock;
        clock.ready_tick_count = 1;
        ScriptedVideoSink sink;
        sink.writes.push_back({
            VRREC_STATUS_INTERNAL_ERROR,
            0,
            0,
            stage,
        });
        RecordingMediaEvents events;
        VideoEncodingWorker worker(scheduler, clock, sink, events);

        CHECK(worker.Start() == VRREC_STATUS_OK);
        CHECK(worker.Join() == VideoEncodingWorkerResult::Failed);
        CHECK(clock.abort_calls == 1);
        CHECK(sink.abort_calls == 1);
        CHECK(sink.finish_calls == 0);
        CHECK(events.fault_calls == 1);
        CHECK(std::string(events.fault_message) == message);
    }
}

void FinishFailurePreservesWhetherTheCurrentPartWasSealed()
{
    for (const auto part_sealed : {false, true}) {
        VideoCfrScheduler scheduler;
        ScriptedCfrClock clock;
        clock.ready_tick_count = 0;
        ScriptedVideoSink sink;
        sink.finish = {
            VRREC_STATUS_INTERNAL_ERROR,
            0,
            0,
            VideoEncoderFailureStage::Encoding,
            part_sealed,
        };
        RecordingMediaEvents events;
        VideoEncodingWorker worker(scheduler, clock, sink, events);

        CHECK(worker.Start() == VRREC_STATUS_OK);
        CHECK(worker.RequestStop() == VRREC_STATUS_OK);
        const auto expected = part_sealed
            ? VideoEncodingWorkerResult::EncoderFailedPartSealed
            : VideoEncodingWorkerResult::EncoderFailed;
        CHECK(worker.Join() == expected);
        CHECK(sink.finish_calls == 1);
        CHECK(sink.abort_calls == (part_sealed ? 0U : 1U));
        CHECK(events.fault_calls == (part_sealed ? 0U : 1U));
        CHECK(events.video_encoder_failure_calls ==
              (part_sealed ? 1U : 0U));
        CHECK(clock.abort_calls == 1);
    }
}

void WorkerReleaseFailureRejectsPreparedFrameBeforeEncoding()
{
    VideoCfrScheduler scheduler;
    ScriptedCfrClock clock;
    clock.ready_tick_count = 1;
    WorkerPreparingVideoSink sink;
    const auto source = std::make_shared<WorkerSurface>(&sink.order);
    source->release_status = VRREC_STATUS_INTERNAL_ERROR;
    CHECK(scheduler.Push({25, 2'500'000, source}) == VRREC_STATUS_OK);
    RecordingMediaEvents events;
    VideoEncodingWorker worker(scheduler, clock, sink, events);

    CHECK(worker.Start() == VRREC_STATUS_OK);
    CHECK(worker.Join() == VideoEncodingWorkerResult::Failed);

    CHECK(sink.order == std::vector<int>({1, 2, 3, 5}));
    CHECK(sink.write_calls == 0);
    CHECK(sink.abort_calls == 1);
    CHECK(events.first_packet_calls == 0);
    CHECK(events.fault_calls == 1);
    CHECK(worker.Statistics().muxed_packet_count == 0);
}

void WorkerPreservesTerminalSurfaceAcquisitionIdentity()
{
    for (const auto &[acquire, expected, message] : {
             std::tuple {
                 VideoSurfaceAcquireResult::Abandoned,
                 VideoEncodingWorkerResult::SurfaceAbandoned,
                 "video surface synchronization was abandoned"},
             std::tuple {
                 VideoSurfaceAcquireResult::DeviceRemoved,
                 VideoEncodingWorkerResult::SurfaceDeviceRemoved,
                 "video device was removed"},
             std::tuple {
                 VideoSurfaceAcquireResult::DeviceReset,
                 VideoEncodingWorkerResult::SurfaceDeviceReset,
                 "video device was reset"},
             std::tuple {
                 VideoSurfaceAcquireResult::Failed,
                 VideoEncodingWorkerResult::Failed,
                 "video surface synchronization failed"},
         }) {
        VideoCfrScheduler scheduler;
        const auto surface = std::make_shared<WorkerSurface>();
        surface->acquire_result = acquire;
        CHECK(scheduler.Push({26, 2'600'000, surface}) ==
              VRREC_STATUS_OK);
        ScriptedCfrClock clock;
        clock.ready_tick_count = 1;
        ScriptedVideoSink sink;
        RecordingMediaEvents events;
        VideoEncodingWorker worker(scheduler, clock, sink, events);

        CHECK(worker.Start() == VRREC_STATUS_OK);
        CHECK(worker.Join() == expected);
        CHECK(sink.write_calls == 0);
        CHECK(sink.abort_calls == 1);
        CHECK(events.fault_calls == 1);
        CHECK(events.fault_status == VRREC_STATUS_BACKEND_UNAVAILABLE);
        CHECK(std::string(events.fault_message) == message);
    }
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

void ThreadCreationFailureIsTerminal(
    vrrec_status_t factory_status,
    vrrec_status_t expected_status,
    bool create_thread_on_success = true)
{
    VideoCfrScheduler scheduler;
    ScriptedCfrClock clock;
    ScriptedVideoSink sink;
    RecordingMediaEvents events;
    ScriptedThreadFactory thread_factory(
        factory_status,
        create_thread_on_success);
    VideoEncodingWorker worker(
        scheduler,
        clock,
        sink,
        events,
        thread_factory);

    CHECK(worker.Start() == expected_status);
    CHECK(thread_factory.start_calls == 1);
    CHECK(worker.Join() == VideoEncodingWorkerResult::Failed);
    CHECK(worker.Join() == VideoEncodingWorkerResult::Failed);
    CHECK(worker.RequestStop() == VRREC_STATUS_INVALID_STATE);
    CHECK(clock.next_tick == 0);
    CHECK(clock.abort_calls == 0);
    CHECK(sink.write_calls == 0);
    CHECK(sink.finish_calls == 0);
    CHECK(sink.abort_calls == 0);
    CHECK(events.first_packet_calls == 0);
    CHECK(events.fault_calls == 0);
    const auto statistics = worker.Statistics();
    CHECK(statistics.scheduler.source_frame_count == 0);
    CHECK(statistics.muxed_packet_count == 0);
    CHECK(statistics.latest_encode_latency_microseconds == 0);
    CHECK(statistics.maximum_encode_latency_microseconds == 0);
    CHECK(worker.Start() == VRREC_STATUS_INVALID_STATE);
    CHECK(thread_factory.start_calls == 1);

    worker.Abort();
    CHECK(worker.Join() == VideoEncodingWorkerResult::Failed);
    CHECK(clock.abort_calls == 0);
    CHECK(sink.abort_calls == 0);
}

void OutOfMemoryThreadCreationIsTerminalFailure()
{
    ThreadCreationFailureIsTerminal(
        VRREC_STATUS_OUT_OF_MEMORY,
        VRREC_STATUS_OUT_OF_MEMORY);
}

void InternalThreadCreationFailureIsTerminalFailure()
{
    ThreadCreationFailureIsTerminal(
        VRREC_STATUS_INTERNAL_ERROR,
        VRREC_STATUS_INTERNAL_ERROR);
}

void EmptySuccessfulThreadCreationFailsClosed()
{
    ThreadCreationFailureIsTerminal(
        VRREC_STATUS_OK,
        VRREC_STATUS_INTERNAL_ERROR,
        false);
}

void AbortWinsDuringThreadCreation(
    vrrec_status_t factory_status,
    bool create_thread_on_success)
{
    VideoCfrScheduler scheduler;
    ScriptedCfrClock clock;
    clock.ready_tick_count = 0;
    ScriptedVideoSink sink;
    RecordingMediaEvents events;
    BlockingThreadFactory thread_factory(
        factory_status,
        create_thread_on_success);
    VideoEncodingWorker worker(
        scheduler,
        clock,
        sink,
        events,
        thread_factory);

    auto starting = std::async(std::launch::async, [&] {
        return worker.Start();
    });
    thread_factory.WaitForStart();
    worker.RequestAbort();

    std::promise<void> cleanup_invoking;
    auto cleanup_invoked = cleanup_invoking.get_future();
    auto cleanup = std::async(std::launch::async, [&] {
        cleanup_invoking.set_value();
        worker.JoinAfterAbort();
    });
    cleanup_invoked.wait();
    sink.WaitForAbort();
    const auto returned_early =
        cleanup.wait_for(std::chrono::milliseconds(50)) ==
        std::future_status::ready;

    thread_factory.ReleaseStart();
    CHECK(starting.get() == VRREC_STATUS_INVALID_STATE);
    cleanup.get();

    CHECK(!returned_early);
    CHECK(thread_factory.StartCalls() == 1);
    CHECK(worker.Join() == VideoEncodingWorkerResult::Aborted);
    CHECK(worker.RequestStop() == VRREC_STATUS_INVALID_STATE);
    CHECK(worker.Start() == VRREC_STATUS_INVALID_STATE);
    CHECK(clock.abort_calls == 1);
    CHECK(sink.write_calls == 0);
    CHECK(sink.finish_calls == 0);
    CHECK(sink.abort_calls == 1);
    CHECK(events.first_packet_calls == 0);
    CHECK(events.fault_calls == 0);
    const auto statistics = worker.Statistics();
    CHECK(statistics.scheduler.source_frame_count == 0);
    CHECK(statistics.muxed_packet_count == 0);
    CHECK(statistics.latest_encode_latency_microseconds == 0);
    CHECK(statistics.maximum_encode_latency_microseconds == 0);
}

void AbortWinsDuringSuccessfulThreadCreation()
{
    AbortWinsDuringThreadCreation(VRREC_STATUS_OK, true);
}

void AbortWinsDuringFailedThreadCreation()
{
    AbortWinsDuringThreadCreation(VRREC_STATUS_OUT_OF_MEMORY, false);
}

void AbortBeforeStartPreventsThreadCreation()
{
    VideoCfrScheduler scheduler;
    ScriptedCfrClock clock;
    ScriptedVideoSink sink;
    RecordingMediaEvents events;
    ScriptedThreadFactory thread_factory(VRREC_STATUS_OK);
    VideoEncodingWorker worker(
        scheduler,
        clock,
        sink,
        events,
        thread_factory);

    worker.Abort();

    CHECK(worker.Start() == VRREC_STATUS_INVALID_STATE);
    CHECK(thread_factory.start_calls == 0);
    CHECK(worker.Join() == VideoEncodingWorkerResult::Aborted);
    CHECK(worker.RequestStop() == VRREC_STATUS_INVALID_STATE);
    CHECK(clock.abort_calls == 1);
    CHECK(sink.write_calls == 0);
    CHECK(sink.finish_calls == 0);
    CHECK(sink.abort_calls == 1);
    CHECK(events.first_packet_calls == 0);
    CHECK(events.fault_calls == 0);
    const auto statistics = worker.Statistics();
    CHECK(statistics.scheduler.source_frame_count == 0);
    CHECK(statistics.muxed_packet_count == 0);
}

}

int main()
{
    {
        VideoCfrScheduler scheduler;
        ScriptedCfrClock clock;
        ScriptedVideoSink sink;
        RecordingMediaEvents events;
        VideoEncodingWorker worker(scheduler, clock, sink, events);
        CHECK(worker.RequestStop() == VRREC_STATUS_INVALID_STATE);
    }
    GracefulStopFlushesAndReportsTheFirstPacketOnce();
    RuntimeEncoderFailureRaisesFaultAndDoesNotFlush();
    RuntimeEncoderFailureAfterFirstPacketSealsPartWithoutAbortingSink();
    ForcedAbortDoesNotFlushOrRaiseFault();
    AbortDominatesAConcurrentGracefulFinish();
    AbortDominatesAnInFlightVideoWriteFailure();
    AbortDoesNotCommitASuccessfulInFlightVideoWrite();
    UnexpectedClockAbortReleasesTheEncoderSinkAndRaisesFault();
    ClockFailureReleasesTheEncoderSinkAndRaisesFault();
    EmptyAndTimedOutTicksRemainNonTerminalUntilStopped();
    ProcessingAndMuxWriteFailuresKeepTheirStageIdentity();
    FinishFailurePreservesWhetherTheCurrentPartWasSealed();
    WorkerReleaseFailureRejectsPreparedFrameBeforeEncoding();
    WorkerPreservesTerminalSurfaceAcquisitionIdentity();
    AbortPreventsATickReturnedByAnInFlightClockWaitFromEncoding();
    OutOfMemoryThreadCreationIsTerminalFailure();
    InternalThreadCreationFailureIsTerminalFailure();
    EmptySuccessfulThreadCreationFailsClosed();
    AbortWinsDuringSuccessfulThreadCreation();
    AbortWinsDuringFailedThreadCreation();
    AbortBeforeStartPreventsThreadCreation();
    return 0;
}
