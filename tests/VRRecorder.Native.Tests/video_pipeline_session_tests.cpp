#include "video_pipeline_session.hpp"

#include <atomic>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <condition_variable>
#include <cstdlib>
#include <future>
#include <iostream>
#include <mutex>
#include <string>
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

class CallOrder final {
public:
    void Push(int call)
    {
        const std::lock_guard lock(mutex_);
        calls_.push_back(call);
    }

    bool operator==(const std::vector<int> &expected) const
    {
        const std::lock_guard lock(mutex_);
        return calls_ == expected;
    }

private:
    mutable std::mutex mutex_;
    std::vector<int> calls_;
};

class FakeCaptureWorker final : public SpoutCaptureWorkerPort {
public:
    explicit FakeCaptureWorker(CallOrder &order)
        : order_(order)
    {
    }

    vrrec_status_t Start(
        std::chrono::milliseconds timeout) noexcept override
    {
        std::unique_lock lock(mutex);
        order_.Push(1);
        last_timeout = timeout;
        ++start_calls;
        start_entered = true;
        changed.notify_all();
        if (block_start) {
            changed.wait(lock, [&] { return release_start; });
        }
        return start_status;
    }

    void Abort() noexcept override
    {
        order_.Push(3);
        ++abort_calls;
    }

    SpoutCaptureWorkerResult Join() noexcept override
    {
        ++join_calls;
        return join_result;
    }

    void WaitForStart()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return start_entered; });
    }

    void ReleaseStart()
    {
        {
            const std::lock_guard lock(mutex);
            release_start = true;
        }
        changed.notify_all();
    }

    CallOrder &order_;
    vrrec_status_t start_status = VRREC_STATUS_OK;
    SpoutCaptureWorkerResult join_result = SpoutCaptureWorkerResult::Aborted;
    std::chrono::milliseconds last_timeout {0};
    std::size_t abort_calls = 0;
    std::size_t join_calls = 0;
    std::size_t start_calls = 0;
    std::mutex mutex;
    std::condition_variable changed;
    bool block_start = false;
    bool start_entered = false;
    bool release_start = false;
};

class FakeEncodingWorker final : public VideoEncodingWorkerPort {
public:
    explicit FakeEncodingWorker(CallOrder &order)
        : order_(order)
    {
    }

    vrrec_status_t Start() noexcept override
    {
        std::unique_lock lock(mutex);
        order_.Push(2);
        ++start_calls;
        start_entered = true;
        changed.notify_all();
        if (block_start) {
            changed.wait(lock, [&] { return release_start; });
        }
        return start_status;
    }

    vrrec_status_t RequestStop() noexcept override
    {
        order_.Push(4);
        ++stop_calls;
        return stop_status;
    }

    void RequestAbort() noexcept override
    {
        request_abort_calls.fetch_add(1);
    }

    void JoinAfterAbort() noexcept override
    {
        ++join_after_abort_calls;
        Abort();
        static_cast<void>(Join());
    }

    void Abort() noexcept override
    {
        order_.Push(5);
        ++abort_calls;
    }

    VideoEncodingWorkerResult Join() noexcept override
    {
        ++join_calls;
        return join_result;
    }

    VideoEncodingStatistics Statistics() const noexcept override
    {
        return statistics;
    }

    void WaitForStart()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return start_entered; });
    }

    void ReleaseStart()
    {
        {
            const std::lock_guard lock(mutex);
            release_start = true;
        }
        changed.notify_all();
    }

    std::size_t RequestAbortCalls() const noexcept
    {
        return request_abort_calls.load();
    }

    CallOrder &order_;
    vrrec_status_t start_status = VRREC_STATUS_OK;
    vrrec_status_t stop_status = VRREC_STATUS_OK;
    VideoEncodingWorkerResult join_result = VideoEncodingWorkerResult::Stopped;
    VideoEncodingStatistics statistics {
        {10, 8, 2, 1},
        7,
        1'500,
        2'500,
    };
    std::size_t stop_calls = 0;
    std::atomic_size_t request_abort_calls {0};
    std::size_t join_after_abort_calls = 0;
    std::size_t abort_calls = 0;
    std::size_t join_calls = 0;
    std::size_t start_calls = 0;
    std::mutex mutex;
    std::condition_variable changed;
    bool block_start = false;
    bool start_entered = false;
    bool release_start = false;
};

class CoordinatedCaptureWorker final : public SpoutCaptureWorkerPort {
public:
    vrrec_status_t Start(
        std::chrono::milliseconds timeout) noexcept override
    {
        const std::lock_guard lock(mutex_);
        last_timeout_ = timeout;
        ++start_calls_;
        return start_status;
    }

    void Abort() noexcept override
    {
        {
            const std::lock_guard lock(mutex_);
            ++abort_calls_;
            if (abort_releases_join) {
                release_join_ = true;
            }
        }
        changed_.notify_all();
    }

    SpoutCaptureWorkerResult Join() noexcept override
    {
        std::unique_lock lock(mutex_);
        ++join_entries_;
        changed_.notify_all();
        if (block_join) {
            changed_.wait(lock, [this] { return release_join_; });
        }
        ++join_calls_;
        return join_result;
    }

    void WaitForJoinEntries(std::size_t count)
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [this, count] {
            return join_entries_ >= count;
        });
    }

    void ReleaseJoin()
    {
        {
            const std::lock_guard lock(mutex_);
            release_join_ = true;
        }
        changed_.notify_all();
    }

    std::size_t AbortCalls() const noexcept
    {
        const std::lock_guard lock(mutex_);
        return abort_calls_;
    }

    std::size_t JoinCalls() const noexcept
    {
        const std::lock_guard lock(mutex_);
        return join_calls_;
    }

    vrrec_status_t start_status = VRREC_STATUS_OK;
    SpoutCaptureWorkerResult join_result =
        SpoutCaptureWorkerResult::Aborted;
    bool block_join = false;
    bool abort_releases_join = false;

private:
    mutable std::mutex mutex_;
    std::condition_variable changed_;
    std::chrono::milliseconds last_timeout_ {0};
    std::size_t start_calls_ = 0;
    std::size_t abort_calls_ = 0;
    std::size_t join_entries_ = 0;
    std::size_t join_calls_ = 0;
    bool release_join_ = false;
};

class CoordinatedEncodingWorker final : public VideoEncodingWorkerPort {
public:
    vrrec_status_t Start() noexcept override
    {
        const std::lock_guard lock(mutex_);
        ++start_calls_;
        return start_status;
    }

    vrrec_status_t RequestStop() noexcept override
    {
        std::unique_lock lock(mutex_);
        ++stop_calls_;
        stop_entered_ = true;
        changed_.notify_all();
        if (block_stop) {
            changed_.wait(lock, [this] { return release_stop_; });
        }
        return stop_status;
    }

    void RequestAbort() noexcept override
    {
        {
            const std::lock_guard lock(mutex_);
            ++request_abort_calls_;
            if (request_abort_releases_join) {
                release_join_ = true;
            }
        }
        changed_.notify_all();
    }

    void JoinAfterAbort() noexcept override
    {
        {
            const std::lock_guard lock(mutex_);
            ++join_after_abort_calls_;
            if (cleanup_releases_join) {
                release_join_ = true;
            }
        }
        changed_.notify_all();
        static_cast<void>(Join());
    }

    void Abort() noexcept override
    {
        RequestAbort();
        JoinAfterAbort();
    }

    VideoEncodingWorkerResult Join() noexcept override
    {
        std::unique_lock lock(mutex_);
        ++join_entries_;
        changed_.notify_all();
        if (block_join) {
            changed_.wait(lock, [this] { return release_join_; });
        }
        ++join_calls_;
        return join_result;
    }

    VideoEncodingStatistics Statistics() const noexcept override
    {
        return {
            {0, 0, 0, 0},
            0,
            0,
            0,
        };
    }

    void WaitForStop()
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [this] { return stop_entered_; });
    }

    void WaitForJoinEntries(std::size_t count)
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [this, count] {
            return join_entries_ >= count;
        });
    }

    void ReleaseStop()
    {
        {
            const std::lock_guard lock(mutex_);
            release_stop_ = true;
        }
        changed_.notify_all();
    }

    void ReleaseJoin()
    {
        {
            const std::lock_guard lock(mutex_);
            release_join_ = true;
        }
        changed_.notify_all();
    }

    std::size_t RequestAbortCalls() const noexcept
    {
        const std::lock_guard lock(mutex_);
        return request_abort_calls_;
    }

    std::size_t JoinAfterAbortCalls() const noexcept
    {
        const std::lock_guard lock(mutex_);
        return join_after_abort_calls_;
    }

    std::size_t JoinCalls() const noexcept
    {
        const std::lock_guard lock(mutex_);
        return join_calls_;
    }

    vrrec_status_t start_status = VRREC_STATUS_OK;
    vrrec_status_t stop_status = VRREC_STATUS_OK;
    VideoEncodingWorkerResult join_result =
        VideoEncodingWorkerResult::Aborted;
    bool block_stop = false;
    bool block_join = false;
    bool cleanup_releases_join = false;
    bool request_abort_releases_join = false;

private:
    mutable std::mutex mutex_;
    std::condition_variable changed_;
    std::size_t start_calls_ = 0;
    std::size_t stop_calls_ = 0;
    std::size_t request_abort_calls_ = 0;
    std::size_t join_after_abort_calls_ = 0;
    std::size_t join_entries_ = 0;
    std::size_t join_calls_ = 0;
    bool stop_entered_ = false;
    bool release_stop_ = false;
    bool release_join_ = false;
};

class BlockingNativeThreadFactory final : public NativeThreadFactoryPort {
public:
    explicit BlockingNativeThreadFactory(
        vrrec_status_t status = VRREC_STATUS_OK,
        bool create_thread = true) noexcept
        : status_(status),
          create_thread_(create_thread)
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
        if (status_ == VRREC_STATUS_OK && create_thread_) {
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

    std::size_t StartCalls() const noexcept
    {
        const std::lock_guard lock(mutex_);
        return start_calls_;
    }

private:
    mutable std::mutex mutex_;
    std::condition_variable changed_;
    vrrec_status_t status_;
    bool create_thread_;
    std::size_t start_calls_ = 0;
    bool start_entered_ = false;
    bool release_start_ = false;
};

class AbortAwareVideoClock final : public VideoCfrClock {
public:
    VideoCfrClockResult WaitNext(std::uint64_t &tick) noexcept override
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [this] { return aborted_; });
        tick = 0;
        return VideoCfrClockResult::Aborted;
    }

    void Abort() noexcept override
    {
        {
            const std::lock_guard lock(mutex_);
            if (aborted_) {
                return;
            }
            aborted_ = true;
            ++abort_calls_;
        }
        changed_.notify_all();
    }

    std::size_t AbortCalls() const noexcept
    {
        const std::lock_guard lock(mutex_);
        return abort_calls_;
    }

private:
    mutable std::mutex mutex_;
    std::condition_variable changed_;
    std::size_t abort_calls_ = 0;
    bool aborted_ = false;
};

class CountingVideoSink final : public VideoEncoderSink {
public:
    VideoEncoderWrite Write(
        const ScheduledVideoFrame &) noexcept override
    {
        const std::lock_guard lock(mutex_);
        ++write_calls_;
        return {VRREC_STATUS_OK, 1, 100};
    }

    VideoEncoderWrite Finish() noexcept override
    {
        const std::lock_guard lock(mutex_);
        ++finish_calls_;
        return {VRREC_STATUS_OK, 1, 100};
    }

    void Abort() noexcept override
    {
        const std::lock_guard lock(mutex_);
        ++abort_calls_;
    }

    std::size_t WriteCalls() const noexcept
    {
        const std::lock_guard lock(mutex_);
        return write_calls_;
    }

    std::size_t FinishCalls() const noexcept
    {
        const std::lock_guard lock(mutex_);
        return finish_calls_;
    }

    std::size_t AbortCalls() const noexcept
    {
        const std::lock_guard lock(mutex_);
        return abort_calls_;
    }

private:
    mutable std::mutex mutex_;
    std::size_t write_calls_ = 0;
    std::size_t finish_calls_ = 0;
    std::size_t abort_calls_ = 0;
};

class RecordingEvents final : public MediaEventSink {
public:
    void FirstVideoPacketMuxed() noexcept override
    {
        ++first_packet_calls;
    }

    void Stopped(std::uint64_t, std::uint64_t) noexcept override
    {
        ++stopped_calls;
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

    std::size_t fault_calls = 0;
    std::size_t first_packet_calls = 0;
    std::size_t stopped_calls = 0;
    vrrec_status_t fault_status = VRREC_STATUS_OK;
    const char *fault_message = nullptr;
};

void StartsCaptureBeforeEncodingAndStopsInSafeOrder()
{
    CallOrder order;
    FakeCaptureWorker capture(order);
    FakeEncodingWorker encoding(order);
    RecordingEvents events;
    VideoPipelineSession session(capture, encoding, events);

    CHECK(session.Start(std::chrono::milliseconds(100)) == VRREC_STATUS_OK);
    CHECK(order == std::vector<int>({1, 2}));
    CHECK(capture.last_timeout == std::chrono::milliseconds(100));
    CHECK(session.RequestStop() == VRREC_STATUS_OK);
    CHECK(session.RequestStop() == VRREC_STATUS_OK);
    CHECK(order == std::vector<int>({1, 2, 3, 4}));
    CHECK(session.Join() == VideoPipelineResult::Stopped);
    CHECK(capture.join_calls == 1);
    CHECK(encoding.join_calls == 1);

    const auto statistics = session.Statistics();
    CHECK(statistics.scheduler.source_frame_count == 10);
    CHECK(statistics.scheduler.output_frame_count == 8);
    CHECK(statistics.scheduler.dropped_source_frame_count == 2);
    CHECK(statistics.scheduler.duplicated_output_frame_count == 1);
    CHECK(statistics.muxed_packet_count == 7);
    CHECK(statistics.latest_encode_latency_microseconds == 1'500);
    CHECK(statistics.maximum_encode_latency_microseconds == 2'500);
}

void RollsBackCaptureWhenEncodingCannotStart()
{
    CallOrder order;
    FakeCaptureWorker capture(order);
    FakeEncodingWorker encoding(order);
    encoding.start_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    RecordingEvents events;
    VideoPipelineSession session(capture, encoding, events);

    CHECK(session.Start(std::chrono::milliseconds(100)) ==
          VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(order == std::vector<int>({1, 2, 3}));
    CHECK(capture.abort_calls == 1);
    CHECK(capture.join_calls == 1);
    CHECK(session.RequestStop() == VRREC_STATUS_INVALID_STATE);
}

void SenderLossAbortsEncodingAndRaisesMediaFault()
{
    CallOrder order;
    FakeCaptureWorker capture(order);
    capture.join_result = SpoutCaptureWorkerResult::SenderLost;
    FakeEncodingWorker encoding(order);
    RecordingEvents events;
    VideoPipelineSession session(capture, encoding, events);

    CHECK(session.Start(std::chrono::milliseconds(100)) == VRREC_STATUS_OK);
    CHECK(session.Join() == VideoPipelineResult::SenderLost);
    CHECK(encoding.abort_calls == 1);
    CHECK(events.fault_calls == 1);
    CHECK(events.fault_status == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(events.fault_message != nullptr);
}

void AdapterChangeAbortsEncodingWithADistinctPipelineResult()
{
    CallOrder order;
    FakeCaptureWorker capture(order);
    capture.join_result = SpoutCaptureWorkerResult::AdapterChanged;
    FakeEncodingWorker encoding(order);
    RecordingEvents events;
    VideoPipelineSession session(capture, encoding, events);

    CHECK(session.Start(std::chrono::milliseconds(100)) == VRREC_STATUS_OK);
    CHECK(session.Join() == VideoPipelineResult::AdapterChanged);
    CHECK(encoding.abort_calls == 1);
    CHECK(events.fault_calls == 1);
    CHECK(events.fault_status == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(events.fault_message != nullptr);
}

void EncoderFailureAbortsCapture()
{
    CallOrder order;
    FakeCaptureWorker capture(order);
    FakeEncodingWorker encoding(order);
    encoding.join_result = VideoEncodingWorkerResult::EncoderFailed;
    RecordingEvents events;
    VideoPipelineSession session(capture, encoding, events);

    CHECK(session.Start(std::chrono::milliseconds(100)) == VRREC_STATUS_OK);
    CHECK(session.Join() == VideoPipelineResult::EncoderFailed);
    CHECK(capture.abort_calls == 1);
    CHECK(capture.join_calls == 1);
    CHECK(encoding.join_calls == 1);
}

void PreservesVideoDeviceRemovedAndResetResults()
{
    for (const auto &[encoding_result, pipeline_result] : {
             std::pair {
                 VideoEncodingWorkerResult::SurfaceDeviceRemoved,
                 VideoPipelineResult::SurfaceDeviceRemoved},
             std::pair {
                 VideoEncodingWorkerResult::SurfaceDeviceReset,
                 VideoPipelineResult::SurfaceDeviceReset},
         }) {
        CallOrder order;
        FakeCaptureWorker capture(order);
        FakeEncodingWorker encoding(order);
        encoding.join_result = encoding_result;
        RecordingEvents events;
        VideoPipelineSession session(capture, encoding, events);

        CHECK(session.Start(std::chrono::milliseconds(100)) ==
              VRREC_STATUS_OK);
        CHECK(session.Join() == pipeline_result);
        CHECK(capture.abort_calls == 1);
        CHECK(capture.join_calls == 1);
    }
}

void AbortDoesNotReturnUntilBothWorkersAreJoined()
{
    CallOrder order;
    FakeCaptureWorker capture(order);
    FakeEncodingWorker encoding(order);
    encoding.join_result = VideoEncodingWorkerResult::Aborted;
    RecordingEvents events;
    VideoPipelineSession session(capture, encoding, events);

    CHECK(session.Start(std::chrono::milliseconds(100)) == VRREC_STATUS_OK);
    session.Abort();
    session.Abort();
    CHECK(capture.abort_calls == 1);
    CHECK(encoding.abort_calls == 1);
    CHECK(capture.join_calls == 1);
    CHECK(encoding.join_calls == 1);
}

void StopFailureAbortsAndJoinsBothWorkersWithoutBeingMasked()
{
    CallOrder order;
    FakeCaptureWorker capture(order);
    FakeEncodingWorker encoding(order);
    encoding.stop_status = VRREC_STATUS_INTERNAL_ERROR;
    encoding.join_result = VideoEncodingWorkerResult::Aborted;
    RecordingEvents events;
    VideoPipelineSession session(capture, encoding, events);

    CHECK(session.Start(std::chrono::milliseconds(100)) == VRREC_STATUS_OK);
    CHECK(session.RequestStop() == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(session.RequestStop() == VRREC_STATUS_INVALID_STATE);
    CHECK(capture.abort_calls == 1);
    CHECK(encoding.abort_calls == 1);
    CHECK(capture.join_calls == 1);
    CHECK(encoding.join_calls == 1);
    CHECK(session.Join() == VideoPipelineResult::InvalidState);
}

void AbortDuringCaptureStartRollsBackWithoutStartingEncoding()
{
    CallOrder order;
    FakeCaptureWorker capture(order);
    capture.block_start = true;
    FakeEncodingWorker encoding(order);
    RecordingEvents events;
    VideoPipelineSession session(capture, encoding, events);

    auto starting = std::async(std::launch::async, [&] {
        return session.Start(std::chrono::milliseconds(100));
    });
    capture.WaitForStart();
    session.RequestAbort();
    auto aborting = std::async(std::launch::async, [&] {
        session.JoinAfterAbort();
    });
    capture.ReleaseStart();

    CHECK(starting.get() == VRREC_STATUS_INVALID_STATE);
    aborting.get();
    CHECK(capture.abort_calls == 1);
    CHECK(capture.join_calls == 1);
    CHECK(encoding.start_calls == 0);
    CHECK(encoding.abort_calls == 0);
}

void AbortDuringEncodingStartRollsBackBothWorkers()
{
    CallOrder order;
    FakeCaptureWorker capture(order);
    FakeEncodingWorker encoding(order);
    encoding.block_start = true;
    RecordingEvents events;
    VideoPipelineSession session(capture, encoding, events);

    auto starting = std::async(std::launch::async, [&] {
        return session.Start(std::chrono::milliseconds(100));
    });
    encoding.WaitForStart();
    session.RequestAbort();
    CHECK(encoding.RequestAbortCalls() == 1);
    auto aborting = std::async(std::launch::async, [&] {
        session.JoinAfterAbort();
    });
    encoding.ReleaseStart();

    CHECK(starting.get() == VRREC_STATUS_INVALID_STATE);
    aborting.get();
    CHECK(capture.abort_calls == 1);
    CHECK(capture.join_calls == 1);
    CHECK(encoding.abort_calls == 1);
    CHECK(encoding.join_calls == 1);
}

void AbortDominatesAFailedBlockingEncodingStart()
{
    CallOrder order;
    FakeCaptureWorker capture(order);
    FakeEncodingWorker encoding(order);
    encoding.block_start = true;
    encoding.start_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    RecordingEvents events;
    VideoPipelineSession session(capture, encoding, events);

    auto starting = std::async(std::launch::async, [&] {
        return session.Start(std::chrono::milliseconds(100));
    });
    encoding.WaitForStart();
    session.RequestAbort();
    CHECK(encoding.RequestAbortCalls() == 1);
    auto cleanup = std::async(std::launch::async, [&] {
        session.JoinAfterAbort();
    });
    encoding.ReleaseStart();

    CHECK(starting.get() == VRREC_STATUS_INVALID_STATE);
    cleanup.get();
    CHECK(capture.abort_calls == 1);
    CHECK(capture.join_calls == 1);
    CHECK(encoding.abort_calls == 1);
    CHECK(encoding.join_calls == 1);
    CHECK(events.fault_calls == 0);
}

void AbortReachesARealEncodingWorkerBeforeThreadPublication()
{
    CallOrder order;
    FakeCaptureWorker capture(order);
    VideoCfrScheduler scheduler;
    CHECK(scheduler.Push({1, 1'000}) == VRREC_STATUS_OK);
    AbortAwareVideoClock clock;
    CountingVideoSink sink;
    RecordingEvents events;
    BlockingNativeThreadFactory thread_factory;
    VideoEncodingWorker encoding(
        scheduler,
        clock,
        sink,
        events,
        thread_factory);
    VideoPipelineSession session(capture, encoding, events);

    auto starting = std::async(std::launch::async, [&] {
        return session.Start(std::chrono::milliseconds(100));
    });
    thread_factory.WaitForStart();
    session.RequestAbort();
    CHECK(clock.AbortCalls() == 1);

    std::promise<void> cleanup_invoking;
    auto cleanup_invoked = cleanup_invoking.get_future();
    auto cleanup = std::async(std::launch::async, [&] {
        cleanup_invoking.set_value();
        session.JoinAfterAbort();
    });
    cleanup_invoked.wait();
    CHECK(cleanup.wait_for(std::chrono::milliseconds(50)) !=
          std::future_status::ready);
    thread_factory.ReleaseStart();

    CHECK(starting.get() == VRREC_STATUS_INVALID_STATE);
    cleanup.get();
    CHECK(encoding.Join() == VideoEncodingWorkerResult::Aborted);
    CHECK(capture.abort_calls == 1);
    CHECK(capture.join_calls == 1);
    CHECK(sink.WriteCalls() == 0);
    CHECK(sink.FinishCalls() == 0);
    CHECK(sink.AbortCalls() == 1);
    CHECK(events.first_packet_calls == 0);
    CHECK(events.stopped_calls == 0);
    CHECK(events.fault_calls == 0);
    const auto statistics = session.Statistics();
    CHECK(statistics.scheduler.output_frame_count == 0);
    CHECK(statistics.muxed_packet_count == 0);
    CHECK(statistics.latest_encode_latency_microseconds == 0);
    CHECK(statistics.maximum_encode_latency_microseconds == 0);

    session.Abort();
    CHECK(clock.AbortCalls() == 1);
    CHECK(capture.abort_calls == 1);
    CHECK(capture.join_calls == 1);
    CHECK(sink.AbortCalls() == 1);
}

void AbortCleanupWaitsForCaptureStartRollback()
{
    CallOrder order;
    FakeCaptureWorker capture(order);
    capture.block_start = true;
    FakeEncodingWorker encoding(order);
    RecordingEvents events;
    VideoPipelineSession session(capture, encoding, events);

    auto starting = std::async(std::launch::async, [&] {
        return session.Start(std::chrono::milliseconds(100));
    });
    capture.WaitForStart();
    session.RequestAbort();

    std::promise<void> cleanup_invoking;
    auto cleanup_invoked = cleanup_invoking.get_future();
    auto cleanup = std::async(std::launch::async, [&] {
        cleanup_invoking.set_value();
        session.JoinAfterAbort();
    });
    cleanup_invoked.wait();
    const auto cleanup_returned_before_start =
        cleanup.wait_for(std::chrono::milliseconds(50)) ==
        std::future_status::ready;

    capture.ReleaseStart();
    CHECK(starting.get() == VRREC_STATUS_INVALID_STATE);
    cleanup.get();

    CHECK(!cleanup_returned_before_start);
    CHECK(capture.abort_calls == 1);
    CHECK(capture.join_calls == 1);
    CHECK(encoding.start_calls == 0);
    CHECK(events.fault_calls == 0);
}

void AbortCleanupWaitsForEncodingStartRollback()
{
    CallOrder order;
    FakeCaptureWorker capture(order);
    FakeEncodingWorker encoding(order);
    encoding.block_start = true;
    RecordingEvents events;
    VideoPipelineSession session(capture, encoding, events);

    auto starting = std::async(std::launch::async, [&] {
        return session.Start(std::chrono::milliseconds(100));
    });
    encoding.WaitForStart();
    session.RequestAbort();

    std::promise<void> cleanup_invoking;
    auto cleanup_invoked = cleanup_invoking.get_future();
    auto cleanup = std::async(std::launch::async, [&] {
        cleanup_invoking.set_value();
        session.JoinAfterAbort();
    });
    cleanup_invoked.wait();
    const auto cleanup_returned_before_start =
        cleanup.wait_for(std::chrono::milliseconds(50)) ==
        std::future_status::ready;

    encoding.ReleaseStart();
    CHECK(starting.get() == VRREC_STATUS_INVALID_STATE);
    cleanup.get();

    CHECK(!cleanup_returned_before_start);
    CHECK(capture.abort_calls == 1);
    CHECK(capture.join_calls == 1);
    CHECK(encoding.abort_calls == 1);
    CHECK(encoding.join_calls == 1);
    CHECK(events.fault_calls == 0);
}

void AbortWinsAgainstAnInFlightEncodingStopRequest()
{
    CoordinatedCaptureWorker capture;
    CoordinatedEncodingWorker encoding;
    encoding.block_stop = true;
    RecordingEvents events;
    VideoPipelineSession session(capture, encoding, events);
    CHECK(session.Start(std::chrono::milliseconds(100)) ==
          VRREC_STATUS_OK);

    auto stopping = std::async(std::launch::async, [&] {
        return session.RequestStop();
    });
    encoding.WaitForStop();
    session.RequestAbort();
    auto cleanup = std::async(std::launch::async, [&] {
        session.JoinAfterAbort();
    });

    encoding.ReleaseStop();
    const auto stop_status = stopping.get();
    cleanup.get();

    CHECK(stop_status == VRREC_STATUS_INVALID_STATE);
    CHECK(encoding.RequestAbortCalls() >= 1);
    CHECK(events.fault_calls == 0);
}

void AbortWinsAgainstAnInFlightEncodingJoin()
{
    CoordinatedCaptureWorker capture;
    CoordinatedEncodingWorker encoding;
    encoding.block_join = true;
    encoding.cleanup_releases_join = true;
    encoding.join_result = VideoEncodingWorkerResult::Stopped;
    RecordingEvents events;
    VideoPipelineSession session(capture, encoding, events);
    CHECK(session.Start(std::chrono::milliseconds(100)) ==
          VRREC_STATUS_OK);
    CHECK(session.RequestStop() == VRREC_STATUS_OK);

    auto joining = std::async(std::launch::async, [&] {
        return session.Join();
    });
    encoding.WaitForJoinEntries(1);
    session.RequestAbort();
    auto cleanup = std::async(std::launch::async, [&] {
        session.JoinAfterAbort();
    });

    const auto result = joining.get();
    cleanup.get();

    CHECK(result == VideoPipelineResult::Aborted);
    CHECK(capture.AbortCalls() == 1);
    CHECK(encoding.JoinAfterAbortCalls() == 1);
    CHECK(events.fault_calls == 0);
}

void JoinOwnerPerformsPhysicalCleanupAfterLogicalAbort()
{
    CoordinatedCaptureWorker capture;
    capture.block_join = true;
    capture.abort_releases_join = true;
    CoordinatedEncodingWorker encoding;
    encoding.block_join = true;
    encoding.request_abort_releases_join = true;
    encoding.join_result = VideoEncodingWorkerResult::Aborted;
    RecordingEvents events;
    VideoPipelineSession session(capture, encoding, events);
    CHECK(session.Start(std::chrono::milliseconds(100)) ==
          VRREC_STATUS_OK);

    auto joining = std::async(std::launch::async, [&] {
        return session.Join();
    });
    capture.WaitForJoinEntries(1);
    encoding.WaitForJoinEntries(1);
    session.RequestAbort();

    CHECK(joining.get() == VideoPipelineResult::Aborted);
    session.JoinAfterAbort();
    CHECK(capture.AbortCalls() == 1);
    CHECK(encoding.JoinAfterAbortCalls() == 1);
    CHECK(events.fault_calls == 0);
}

struct CaptureJoinAbortObservation {
    VideoPipelineResult result;
    std::size_t fault_calls;
    std::size_t capture_abort_calls;
    std::size_t capture_join_calls;
};

CaptureJoinAbortObservation ObserveAbortDuringCaptureJoin(
    SpoutCaptureWorkerResult capture_result)
{
    CoordinatedCaptureWorker capture;
    capture.block_join = true;
    capture.join_result = capture_result;
    CoordinatedEncodingWorker encoding;
    encoding.join_result = VideoEncodingWorkerResult::Aborted;
    RecordingEvents events;
    VideoPipelineSession session(capture, encoding, events);
    CHECK(session.Start(std::chrono::milliseconds(100)) ==
          VRREC_STATUS_OK);

    auto joining = std::async(std::launch::async, [&] {
        return session.Join();
    });
    capture.WaitForJoinEntries(1);
    session.RequestAbort();
    auto cleanup = std::async(std::launch::async, [&] {
        session.JoinAfterAbort();
    });

    capture.ReleaseJoin();
    const auto result = joining.get();
    cleanup.get();

    return {
        result,
        events.fault_calls,
        capture.AbortCalls(),
        capture.JoinCalls(),
    };
}

void AbortWinsAgainstSenderLossFromAnInFlightCaptureJoin()
{
    const auto observed = ObserveAbortDuringCaptureJoin(
        SpoutCaptureWorkerResult::SenderLost);
    CHECK(observed.result == VideoPipelineResult::Aborted);
    CHECK(observed.capture_abort_calls == 1);
    CHECK(observed.capture_join_calls >= 1);
}

void AbortSuppressesSenderLossFaultFromAnInFlightCaptureJoin()
{
    const auto observed = ObserveAbortDuringCaptureJoin(
        SpoutCaptureWorkerResult::SenderLost);
    CHECK(observed.fault_calls == 0);
}

void AbortWinsAgainstFailureFromAnInFlightCaptureJoin()
{
    const auto observed = ObserveAbortDuringCaptureJoin(
        SpoutCaptureWorkerResult::Failed);
    CHECK(observed.result == VideoPipelineResult::Aborted);
    CHECK(observed.capture_abort_calls == 1);
    CHECK(observed.capture_join_calls >= 1);
}

void AbortSuppressesCaptureFailureFaultFromAnInFlightCaptureJoin()
{
    const auto observed = ObserveAbortDuringCaptureJoin(
        SpoutCaptureWorkerResult::Failed);
    CHECK(observed.fault_calls == 0);
}

void CaptureJoinThreadCreationFailureCleansWorkers(
    vrrec_status_t launch_status,
    bool create_thread)
{
    CallOrder order;
    FakeCaptureWorker capture(order);
    FakeEncodingWorker encoding(order);
    RecordingEvents events;
    BlockingNativeThreadFactory thread_factory(
        launch_status,
        create_thread);
    thread_factory.ReleaseStart();
    VideoPipelineSession session(
        capture,
        encoding,
        events,
        thread_factory);

    CHECK(session.Start(std::chrono::milliseconds(100)) ==
          VRREC_STATUS_OK);
    CHECK(session.Join() == VideoPipelineResult::Failed);
    CHECK(thread_factory.StartCalls() == 1);
    CHECK(capture.abort_calls == 1);
    CHECK(capture.join_calls == 1);
    CHECK(encoding.RequestAbortCalls() == 1);
    CHECK(encoding.join_after_abort_calls == 1);
    CHECK(encoding.abort_calls == 1);
    CHECK(encoding.join_calls == 1);
    CHECK(events.first_packet_calls == 0);
    CHECK(events.stopped_calls == 0);
    CHECK(events.fault_calls == 0);
    CHECK(session.Join() == VideoPipelineResult::InvalidState);
    CHECK(session.RequestStop() == VRREC_STATUS_INVALID_STATE);
}

void CaptureJoinThreadOutOfMemoryCleansWorkers()
{
    CaptureJoinThreadCreationFailureCleansWorkers(
        VRREC_STATUS_OUT_OF_MEMORY,
        false);
}

void CaptureJoinThreadInternalFailureCleansWorkers()
{
    CaptureJoinThreadCreationFailureCleansWorkers(
        VRREC_STATUS_INTERNAL_ERROR,
        false);
}

void CaptureJoinThreadSuccessWithoutPublicationCleansWorkers()
{
    CaptureJoinThreadCreationFailureCleansWorkers(
        VRREC_STATUS_OK,
        false);
}

void AbortWinsAgainstBlockedCaptureJoinThreadFailure()
{
    CallOrder order;
    FakeCaptureWorker capture(order);
    FakeEncodingWorker encoding(order);
    RecordingEvents events;
    BlockingNativeThreadFactory thread_factory(
        VRREC_STATUS_OUT_OF_MEMORY,
        false);
    VideoPipelineSession session(
        capture,
        encoding,
        events,
        thread_factory);
    CHECK(session.Start(std::chrono::milliseconds(100)) ==
          VRREC_STATUS_OK);

    auto joining = std::async(std::launch::async, [&] {
        return session.Join();
    });
    thread_factory.WaitForStart();
    session.RequestAbort();
    std::promise<void> cleanup_invoking;
    auto cleanup_invoked = cleanup_invoking.get_future();
    auto cleanup = std::async(std::launch::async, [&] {
        cleanup_invoking.set_value();
        session.JoinAfterAbort();
    });
    cleanup_invoked.wait();
    CHECK(cleanup.wait_for(std::chrono::milliseconds(50)) !=
          std::future_status::ready);

    thread_factory.ReleaseStart();
    CHECK(joining.get() == VideoPipelineResult::Aborted);
    cleanup.get();

    CHECK(capture.abort_calls == 1);
    CHECK(capture.join_calls == 1);
    CHECK(encoding.join_after_abort_calls == 1);
    CHECK(encoding.abort_calls == 1);
    CHECK(encoding.join_calls == 1);
    CHECK(events.first_packet_calls == 0);
    CHECK(events.stopped_calls == 0);
    CHECK(events.fault_calls == 0);
}

void AbortWinsAgainstBlockedCaptureJoinThreadPublication()
{
    CoordinatedCaptureWorker capture;
    CoordinatedEncodingWorker encoding;
    RecordingEvents events;
    BlockingNativeThreadFactory thread_factory;
    VideoPipelineSession session(
        capture,
        encoding,
        events,
        thread_factory);
    CHECK(session.Start(std::chrono::milliseconds(100)) ==
          VRREC_STATUS_OK);

    auto joining = std::async(std::launch::async, [&] {
        return session.Join();
    });
    thread_factory.WaitForStart();
    session.RequestAbort();
    std::promise<void> cleanup_invoking;
    auto cleanup_invoked = cleanup_invoking.get_future();
    auto cleanup = std::async(std::launch::async, [&] {
        cleanup_invoking.set_value();
        session.JoinAfterAbort();
    });
    cleanup_invoked.wait();
    CHECK(cleanup.wait_for(std::chrono::milliseconds(50)) !=
          std::future_status::ready);

    thread_factory.ReleaseStart();
    CHECK(joining.get() == VideoPipelineResult::Aborted);
    cleanup.get();

    CHECK(capture.AbortCalls() == 1);
    CHECK(capture.JoinCalls() >= 1);
    CHECK(encoding.RequestAbortCalls() >= 1);
    CHECK(encoding.JoinAfterAbortCalls() == 1);
    CHECK(encoding.JoinCalls() >= 1);
    CHECK(events.first_packet_calls == 0);
    CHECK(events.stopped_calls == 0);
    CHECK(events.fault_calls == 0);
}

bool RunRequestedTest(const std::string &name)
{
    if (name == "capture-start") {
        AbortCleanupWaitsForCaptureStartRollback();
    } else if (name == "encoding-start") {
        AbortCleanupWaitsForEncodingStartRollback();
    } else if (name == "request-stop") {
        AbortWinsAgainstAnInFlightEncodingStopRequest();
    } else if (name == "encoding-join") {
        AbortWinsAgainstAnInFlightEncodingJoin();
    } else if (name == "join-owned-cleanup") {
        JoinOwnerPerformsPhysicalCleanupAfterLogicalAbort();
    } else if (name == "sender-loss-result") {
        AbortWinsAgainstSenderLossFromAnInFlightCaptureJoin();
    } else if (name == "sender-loss-fault") {
        AbortSuppressesSenderLossFaultFromAnInFlightCaptureJoin();
    } else if (name == "capture-failure-result") {
        AbortWinsAgainstFailureFromAnInFlightCaptureJoin();
    } else if (name == "capture-failure-fault") {
        AbortSuppressesCaptureFailureFaultFromAnInFlightCaptureJoin();
    } else {
        return false;
    }
    return true;
}

}

int main(int argc, char **argv)
{
    if (argc == 2) {
        return RunRequestedTest(argv[1]) ? 0 : 2;
    }

    StartsCaptureBeforeEncodingAndStopsInSafeOrder();
    RollsBackCaptureWhenEncodingCannotStart();
    SenderLossAbortsEncodingAndRaisesMediaFault();
    AdapterChangeAbortsEncodingWithADistinctPipelineResult();
    EncoderFailureAbortsCapture();
    PreservesVideoDeviceRemovedAndResetResults();
    AbortDoesNotReturnUntilBothWorkersAreJoined();
    StopFailureAbortsAndJoinsBothWorkersWithoutBeingMasked();
    AbortDuringCaptureStartRollsBackWithoutStartingEncoding();
    AbortDuringEncodingStartRollsBackBothWorkers();
    AbortDominatesAFailedBlockingEncodingStart();
    AbortReachesARealEncodingWorkerBeforeThreadPublication();
    AbortCleanupWaitsForCaptureStartRollback();
    AbortCleanupWaitsForEncodingStartRollback();
    AbortWinsAgainstAnInFlightEncodingStopRequest();
    AbortWinsAgainstAnInFlightEncodingJoin();
    JoinOwnerPerformsPhysicalCleanupAfterLogicalAbort();
    AbortWinsAgainstSenderLossFromAnInFlightCaptureJoin();
    AbortSuppressesSenderLossFaultFromAnInFlightCaptureJoin();
    AbortWinsAgainstFailureFromAnInFlightCaptureJoin();
    AbortSuppressesCaptureFailureFaultFromAnInFlightCaptureJoin();
    CaptureJoinThreadOutOfMemoryCleansWorkers();
    CaptureJoinThreadInternalFailureCleansWorkers();
    CaptureJoinThreadSuccessWithoutPublicationCleansWorkers();
    AbortWinsAgainstBlockedCaptureJoinThreadFailure();
    AbortWinsAgainstBlockedCaptureJoinThreadPublication();
    return 0;
}
