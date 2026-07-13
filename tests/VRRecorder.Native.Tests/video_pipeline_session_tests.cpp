#include "video_pipeline_session.hpp"

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <condition_variable>
#include <cstdlib>
#include <future>
#include <iostream>
#include <mutex>
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

class FakeCaptureWorker final : public SpoutCaptureWorkerPort {
public:
    explicit FakeCaptureWorker(std::vector<int> &order)
        : order_(order)
    {
    }

    vrrec_status_t Start(
        std::chrono::milliseconds timeout) noexcept override
    {
        std::unique_lock lock(mutex);
        order_.push_back(1);
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
        order_.push_back(3);
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

    std::vector<int> &order_;
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
    explicit FakeEncodingWorker(std::vector<int> &order)
        : order_(order)
    {
    }

    vrrec_status_t Start() noexcept override
    {
        std::unique_lock lock(mutex);
        order_.push_back(2);
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
        order_.push_back(4);
        ++stop_calls;
        return stop_status;
    }

    void Abort() noexcept override
    {
        order_.push_back(5);
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

    std::vector<int> &order_;
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
    std::size_t abort_calls = 0;
    std::size_t join_calls = 0;
    std::size_t start_calls = 0;
    std::mutex mutex;
    std::condition_variable changed;
    bool block_start = false;
    bool start_entered = false;
    bool release_start = false;
};

class RecordingEvents final : public MediaEventSink {
public:
    void FirstVideoPacketMuxed() noexcept override
    {
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

    std::size_t fault_calls = 0;
    vrrec_status_t fault_status = VRREC_STATUS_OK;
    const char *fault_message = nullptr;
};

void StartsCaptureBeforeEncodingAndStopsInSafeOrder()
{
    std::vector<int> order;
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
    std::vector<int> order;
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
    std::vector<int> order;
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

void EncoderFailureAbortsCapture()
{
    std::vector<int> order;
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

void AbortDoesNotReturnUntilBothWorkersAreJoined()
{
    std::vector<int> order;
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
    std::vector<int> order;
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
    std::vector<int> order;
    FakeCaptureWorker capture(order);
    capture.block_start = true;
    FakeEncodingWorker encoding(order);
    RecordingEvents events;
    VideoPipelineSession session(capture, encoding, events);

    auto starting = std::async(std::launch::async, [&] {
        return session.Start(std::chrono::milliseconds(100));
    });
    capture.WaitForStart();
    session.Abort();
    capture.ReleaseStart();

    CHECK(starting.get() == VRREC_STATUS_INVALID_STATE);
    CHECK(capture.abort_calls == 1);
    CHECK(capture.join_calls == 1);
    CHECK(encoding.start_calls == 0);
    CHECK(encoding.abort_calls == 0);
}

void AbortDuringEncodingStartRollsBackBothWorkers()
{
    std::vector<int> order;
    FakeCaptureWorker capture(order);
    FakeEncodingWorker encoding(order);
    encoding.block_start = true;
    RecordingEvents events;
    VideoPipelineSession session(capture, encoding, events);

    auto starting = std::async(std::launch::async, [&] {
        return session.Start(std::chrono::milliseconds(100));
    });
    encoding.WaitForStart();
    session.Abort();
    encoding.ReleaseStart();

    CHECK(starting.get() == VRREC_STATUS_INVALID_STATE);
    CHECK(capture.abort_calls == 1);
    CHECK(capture.join_calls == 1);
    CHECK(encoding.abort_calls == 1);
    CHECK(encoding.join_calls == 1);
}

}

int main()
{
    StartsCaptureBeforeEncodingAndStopsInSafeOrder();
    RollsBackCaptureWhenEncodingCannotStart();
    SenderLossAbortsEncodingAndRaisesMediaFault();
    EncoderFailureAbortsCapture();
    AbortDoesNotReturnUntilBothWorkersAreJoined();
    StopFailureAbortsAndJoinsBothWorkersWithoutBeingMasked();
    AbortDuringCaptureStartRollsBackWithoutStartingEncoding();
    AbortDuringEncodingStartRollsBackBothWorkers();
    return 0;
}
