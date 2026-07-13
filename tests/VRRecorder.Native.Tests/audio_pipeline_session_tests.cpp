#include "audio_pipeline_session.hpp"

#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <future>
#include <iostream>
#include <mutex>
#include <span>

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

class FakeCaptureSession final : public StereoAudioCaptureSessionPort {
public:
    vrrec_status_t Start(
        const StereoAudioCaptureSessionConfig &config) noexcept override
    {
        std::unique_lock lock(mutex);
        ++start_calls;
        last_config = config;
        start_entered = true;
        changed.notify_all();
        if (block_start) {
            changed.wait(lock, [&] { return release_start; });
        }
        return start_status;
    }

    StereoAudioMixResult MixNext(
        std::size_t frame_count_48k,
        std::span<float> output_interleaved,
        StereoAudioMixRead &read) noexcept override
    {
        std::unique_lock lock(mutex);
        ++mix_calls;
        changed.notify_all();
        if (block_mix) {
            changed.wait(lock, [&] { return release_mix; });
            return StereoAudioMixResult::Failed;
        }
        if (fail_immediately) {
            return StereoAudioMixResult::Failed;
        }

        if (windows_returned < ready_windows) {
            const auto start = windows_returned * frame_count_48k;
            ++windows_returned;
            for (auto &sample : output_interleaved) {
                sample = 0.25F;
            }

            read = {start, frame_count_48k, true, true, false, false};
            changed.notify_all();
            return StereoAudioMixResult::Mixed;
        }

        changed.wait(lock, [&] { return aborted; });
        return StereoAudioMixResult::Aborted;
    }

    vrrec_status_t SetRouting(
        vrrec_audio_routing_t routing) noexcept override
    {
        std::unique_lock lock(mutex);
        last_routing = routing;
        ++routing_calls;
        routing_entered = true;
        changed.notify_all();
        if (block_routing) {
            changed.wait(lock, [&] { return release_routing; });
        }
        return routing_status;
    }

    void Abort() noexcept override
    {
        std::unique_lock lock(mutex);
        if (aborted) {
            return;
        }

        aborted = true;
        ++abort_calls;
        abort_entered = true;
        changed.notify_all();
        if (block_abort) {
            changed.wait(lock, [&] { return release_abort; });
        }
    }

    void WaitForWindows(std::size_t count)
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return windows_returned >= count; });
    }

    void WaitForMix()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return mix_calls > 0; });
    }

    void WaitForStart()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return start_entered; });
    }

    void WaitForRouting()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return routing_entered; });
    }

    void WaitForAbort()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return abort_entered; });
    }

    void ReleaseStart()
    {
        {
            const std::lock_guard lock(mutex);
            release_start = true;
        }
        changed.notify_all();
    }

    void ReleaseRouting()
    {
        {
            const std::lock_guard lock(mutex);
            release_routing = true;
        }
        changed.notify_all();
    }

    void ReleaseMix()
    {
        {
            const std::lock_guard lock(mutex);
            release_mix = true;
        }
        changed.notify_all();
    }

    void ReleaseAbort()
    {
        {
            const std::lock_guard lock(mutex);
            release_abort = true;
        }
        changed.notify_all();
    }

    std::mutex mutex;
    std::condition_variable changed;
    StereoAudioCaptureSessionConfig last_config {};
    vrrec_status_t start_status = VRREC_STATUS_OK;
    vrrec_status_t routing_status = VRREC_STATUS_OK;
    vrrec_audio_routing_t last_routing = VRREC_AUDIO_ROUTING_MIXED;
    std::size_t ready_windows = 2;
    std::size_t windows_returned = 0;
    std::size_t start_calls = 0;
    std::size_t routing_calls = 0;
    std::size_t abort_calls = 0;
    std::size_t mix_calls = 0;
    bool aborted = false;
    bool fail_immediately = false;
    bool block_mix = false;
    bool release_mix = false;
    bool block_start = false;
    bool start_entered = false;
    bool release_start = false;
    bool block_routing = false;
    bool routing_entered = false;
    bool release_routing = false;
    bool block_abort = false;
    bool abort_entered = false;
    bool release_abort = false;
};

class CountingEncoderSink final : public StereoAudioEncoderSink {
public:
    StereoAudioEncoderWrite WritePcm48k(
        std::uint64_t,
        std::span<const float>) noexcept override
    {
        ++write_calls;
        return {VRREC_STATUS_OK, 1};
    }

    StereoAudioEncoderWrite Finish() noexcept override
    {
        std::unique_lock lock(mutex);
        ++finish_calls;
        finish_entered = true;
        changed.notify_all();
        if (block_finish) {
            changed.wait(lock, [&] { return release_finish; });
        }
        return {VRREC_STATUS_OK, 1};
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
        changed.wait(lock, [&] { return abort_calls > 0; });
    }

    void WaitForFinish()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return finish_entered; });
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
    std::size_t write_calls = 0;
    std::size_t finish_calls = 0;
    std::size_t abort_calls = 0;
    bool block_finish = false;
    bool finish_entered = false;
    bool release_finish = false;
};

class BlockingThreadFactory final : public NativeThreadFactoryPort {
public:
    BlockingThreadFactory(
        vrrec_status_t status = VRREC_STATUS_OUT_OF_MEMORY,
        bool create_thread_on_success = false) noexcept
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
            worker_context_ = context;
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

    bool EncodingWorkerIsFinished() const
    {
        StereoAudioEncodingWorker *worker = nullptr;
        {
            const std::lock_guard lock(mutex_);
            worker = static_cast<StereoAudioEncodingWorker *>(
                worker_context_);
        }
        return worker != nullptr && worker->IsFinished();
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
    void *worker_context_ = nullptr;
    bool start_entered_ = false;
    bool release_start_ = false;
};

StereoAudioCaptureSessionConfig Config()
{
    return {"render-id", "mic-id", 900'000};
}

void RunsACompleteGracefulAudioPipeline()
{
    FakeCaptureSession capture;
    CountingEncoderSink encoder;
    StereoAudioPipelineSession session(capture, encoder);

    CHECK(session.Start(Config(), 1'024) == VRREC_STATUS_OK);
    CHECK(capture.start_calls == 1);
    CHECK(capture.last_config.desktop_endpoint_id_utf8 == "render-id");
    CHECK(capture.last_config.microphone_endpoint_id_utf8 == "mic-id");
    capture.WaitForWindows(2);
    CHECK(session.SetRouting(VRREC_AUDIO_ROUTING_DESKTOP_ONLY) ==
          VRREC_STATUS_OK);
    CHECK(capture.routing_calls == 1);
    CHECK(capture.last_routing == VRREC_AUDIO_ROUTING_DESKTOP_ONLY);

    CHECK(session.RequestStop() == VRREC_STATUS_OK);
    CHECK(session.RequestStop() == VRREC_STATUS_OK);
    CHECK(session.Join() == StereoAudioEncodingWorkerResult::Stopped);
    const auto statistics = session.Statistics();
    CHECK(statistics.submitted_frame_count == 2'048);
    CHECK(statistics.muxed_packet_count == 3);
    CHECK(encoder.write_calls == 2);
    CHECK(encoder.finish_calls == 1);
    CHECK(encoder.abort_calls == 0);
}

void DoesNotStartEncodingWhenCaptureInitializationFails()
{
    FakeCaptureSession capture;
    capture.start_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    CountingEncoderSink encoder;
    StereoAudioPipelineSession session(capture, encoder);

    CHECK(session.Start(Config(), 1'024) ==
          VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(capture.start_calls == 1);
    CHECK(encoder.write_calls == 0);
    CHECK(session.RequestStop() == VRREC_STATUS_INVALID_STATE);
    CHECK(session.SetRouting(VRREC_AUDIO_ROUTING_MUTED) ==
          VRREC_STATUS_INVALID_STATE);
}

void RejectsInvalidWindowsBeforeStartingCapture()
{
    FakeCaptureSession capture;
    CountingEncoderSink encoder;
    StereoAudioPipelineSession session(capture, encoder);

    CHECK(session.Start(Config(), 0) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(capture.start_calls == 0);
}

void WorkerFailureTerminalizesThePipelineOnStopRequest()
{
    FakeCaptureSession capture;
    capture.fail_immediately = true;
    CountingEncoderSink encoder;
    StereoAudioPipelineSession session(capture, encoder);

    CHECK(session.Start(Config(), 1'024) == VRREC_STATUS_OK);
    encoder.WaitForAbort();
    CHECK(session.SetRouting(VRREC_AUDIO_ROUTING_MUTED) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(capture.routing_calls == 0);
    CHECK(session.RequestStop() == VRREC_STATUS_INVALID_STATE);
    CHECK(session.SetRouting(VRREC_AUDIO_ROUTING_MUTED) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(capture.routing_calls == 0);
    CHECK(capture.abort_calls == 1);
    CHECK(encoder.abort_calls == 1);
}

void AbortDuringCaptureStartRollsBackWithoutStartingEncoding()
{
    FakeCaptureSession capture;
    capture.block_start = true;
    capture.ready_windows = 0;
    CountingEncoderSink encoder;
    StereoAudioPipelineSession session(capture, encoder);

    auto starting = std::async(std::launch::async, [&] {
        return session.Start(Config(), 1'024);
    });
    capture.WaitForStart();
    session.RequestAbort();
    auto aborting = std::async(std::launch::async, [&] {
        session.Abort();
    });
    capture.ReleaseStart();

    CHECK(starting.get() == VRREC_STATUS_INVALID_STATE);
    aborting.get();
    CHECK(capture.abort_calls == 1);
    CHECK(encoder.write_calls == 0);
    CHECK(encoder.finish_calls == 0);
    CHECK(encoder.abort_calls == 0);
}

void AbortDominatesAFailedBlockingCaptureStart()
{
    FakeCaptureSession capture;
    capture.block_start = true;
    capture.start_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    capture.ready_windows = 0;
    CountingEncoderSink encoder;
    StereoAudioPipelineSession session(capture, encoder);

    auto starting = std::async(std::launch::async, [&] {
        return session.Start(Config(), 1'024);
    });
    capture.WaitForStart();
    session.RequestAbort();
    auto aborting = std::async(std::launch::async, [&] {
        session.Abort();
    });
    capture.ReleaseStart();

    CHECK(starting.get() == VRREC_STATUS_INVALID_STATE);
    aborting.get();
    CHECK(capture.abort_calls == 0);
    CHECK(encoder.write_calls == 0);
    CHECK(encoder.finish_calls == 0);
    CHECK(encoder.abort_calls == 0);
}

void AbortDominatesABlockingRoutingUpdate()
{
    FakeCaptureSession capture;
    capture.ready_windows = 0;
    capture.block_routing = true;
    CountingEncoderSink encoder;
    StereoAudioPipelineSession session(capture, encoder);

    CHECK(session.Start(Config(), 1'024) == VRREC_STATUS_OK);
    capture.WaitForMix();
    auto routing = std::async(std::launch::async, [&] {
        return session.SetRouting(VRREC_AUDIO_ROUTING_MUTED);
    });
    capture.WaitForRouting();
    session.Abort();
    capture.ReleaseRouting();

    CHECK(routing.get() == VRREC_STATUS_INVALID_STATE);
    CHECK(capture.routing_calls == 1);
    CHECK(capture.last_routing == VRREC_AUDIO_ROUTING_MUTED);
    CHECK(capture.abort_calls == 1);
    CHECK(encoder.abort_calls == 1);
    const auto statistics = session.Statistics();
    CHECK(statistics.submitted_frame_count == 0);
    CHECK(statistics.muxed_packet_count == 0);
}

void AbortDominatesABlockingEncodingStopRequest()
{
    FakeCaptureSession capture;
    capture.ready_windows = 0;
    capture.block_abort = true;
    CountingEncoderSink encoder;
    encoder.block_finish = true;
    StereoAudioPipelineSession session(capture, encoder);

    CHECK(session.Start(Config(), 1'024) == VRREC_STATUS_OK);
    capture.WaitForMix();
    auto stopping = std::async(std::launch::async, [&] {
        return session.RequestStop();
    });
    capture.WaitForAbort();
    encoder.WaitForFinish();

    auto aborting = std::async(std::launch::async, [&] {
        session.Abort();
    });
    encoder.WaitForAbort();
    encoder.ReleaseFinish();
    aborting.get();
    capture.ReleaseAbort();

    CHECK(stopping.get() == VRREC_STATUS_INVALID_STATE);
    CHECK(capture.abort_calls == 1);
    CHECK(encoder.finish_calls == 1);
    CHECK(encoder.abort_calls == 1);
    const auto statistics = session.Statistics();
    CHECK(statistics.submitted_frame_count == 0);
    CHECK(statistics.muxed_packet_count == 0);
}

void AbortCleanupUnblocksAnInFlightEncodingJoin()
{
    FakeCaptureSession capture;
    capture.ready_windows = 0;
    CountingEncoderSink encoder;
    StereoAudioPipelineSession session(capture, encoder);

    CHECK(session.Start(Config(), 1'024) == VRREC_STATUS_OK);
    capture.WaitForMix();

    std::promise<void> join_invoking;
    auto join_invoked = join_invoking.get_future();
    auto joining = std::async(std::launch::async, [&] {
        join_invoking.set_value();
        return session.Join();
    });
    join_invoked.wait();
    CHECK(joining.wait_for(std::chrono::milliseconds(50)) !=
          std::future_status::ready);

    auto aborting = std::async(std::launch::async, [&] {
        session.Abort();
    });
    CHECK(aborting.wait_for(std::chrono::seconds(1)) ==
          std::future_status::ready);
    aborting.get();
    CHECK(joining.wait_for(std::chrono::seconds(1)) ==
          std::future_status::ready);
    CHECK(joining.get() == StereoAudioEncodingWorkerResult::Aborted);
    CHECK(capture.abort_calls == 1);
    CHECK(encoder.abort_calls == 1);
}

void JoinOwnerPerformsPhysicalCleanupAfterLogicalAbort()
{
    FakeCaptureSession capture;
    capture.block_mix = true;
    CountingEncoderSink encoder;
    StereoAudioPipelineSession session(capture, encoder);

    CHECK(session.Start(Config(), 1'024) == VRREC_STATUS_OK);
    capture.WaitForMix();
    auto joining = std::async(std::launch::async, [&] {
        return session.Join();
    });
    CHECK(joining.wait_for(std::chrono::milliseconds(50)) !=
          std::future_status::ready);

    session.RequestAbort();
    capture.ReleaseMix();

    CHECK(joining.get() == StereoAudioEncodingWorkerResult::Aborted);
    session.JoinAfterAbort();
    CHECK(capture.abort_calls == 1);
    CHECK(encoder.abort_calls == 1);
}

void AbortDuringEncodingThreadFailureReclaimsCapture()
{
    FakeCaptureSession capture;
    capture.ready_windows = 0;
    CountingEncoderSink encoder;
    BlockingThreadFactory thread_factory;
    StereoAudioPipelineSession session(
        capture,
        encoder,
        thread_factory);

    auto starting = std::async(std::launch::async, [&] {
        return session.Start(Config(), 1'024);
    });
    thread_factory.WaitForStart();
    session.RequestAbort();
    CHECK(thread_factory.EncodingWorkerIsFinished());

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

    CHECK(thread_factory.StartCalls() == 1);
    CHECK(capture.start_calls == 1);
    CHECK(capture.abort_calls == 1);
    CHECK(capture.mix_calls == 0);
    CHECK(encoder.write_calls == 0);
    CHECK(encoder.finish_calls == 0);
    CHECK(encoder.abort_calls == 1);
    const auto statistics = session.Statistics();
    CHECK(statistics.submitted_frame_count == 0);
    CHECK(statistics.muxed_packet_count == 0);
    CHECK(session.RequestStop() == VRREC_STATUS_INVALID_STATE);
    CHECK(session.Join() ==
          StereoAudioEncodingWorkerResult::InvalidState);
    CHECK(session.Start(Config(), 1'024) ==
          VRREC_STATUS_INVALID_STATE);

    session.Abort();
    CHECK(capture.abort_calls == 1);
    CHECK(encoder.abort_calls == 1);
}

void AbortDuringSuccessfulEncodingThreadCreationPreventsFirstWindow()
{
    FakeCaptureSession capture;
    capture.ready_windows = 1;
    CountingEncoderSink encoder;
    BlockingThreadFactory thread_factory(VRREC_STATUS_OK, true);
    StereoAudioPipelineSession session(
        capture,
        encoder,
        thread_factory);

    auto starting = std::async(std::launch::async, [&] {
        return session.Start(Config(), 1'024);
    });
    thread_factory.WaitForStart();
    session.RequestAbort();
    CHECK(thread_factory.EncodingWorkerIsFinished());

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

    CHECK(thread_factory.StartCalls() == 1);
    CHECK(capture.start_calls == 1);
    CHECK(capture.abort_calls == 1);
    CHECK(capture.mix_calls == 0);
    CHECK(encoder.write_calls == 0);
    CHECK(encoder.finish_calls == 0);
    CHECK(encoder.abort_calls == 1);
    const auto statistics = session.Statistics();
    CHECK(statistics.submitted_frame_count == 0);
    CHECK(statistics.muxed_packet_count == 0);
    CHECK(session.RequestStop() == VRREC_STATUS_INVALID_STATE);

    session.Abort();
    CHECK(capture.abort_calls == 1);
    CHECK(encoder.abort_calls == 1);
}

void EncodingThreadCreationFailureRollsBackCapture(
    vrrec_status_t factory_status,
    vrrec_status_t expected_status,
    bool create_thread_on_success = false)
{
    FakeCaptureSession capture;
    capture.ready_windows = 0;
    CountingEncoderSink encoder;
    BlockingThreadFactory thread_factory(
        factory_status,
        create_thread_on_success);
    thread_factory.ReleaseStart();
    StereoAudioPipelineSession session(
        capture,
        encoder,
        thread_factory);

    CHECK(session.Start(Config(), 1'024) == expected_status);
    CHECK(thread_factory.StartCalls() == 1);
    CHECK(capture.start_calls == 1);
    CHECK(capture.abort_calls == 1);
    CHECK(capture.mix_calls == 0);
    CHECK(encoder.write_calls == 0);
    CHECK(encoder.finish_calls == 0);
    CHECK(encoder.abort_calls == 0);
    CHECK(session.RequestStop() == VRREC_STATUS_INVALID_STATE);
    CHECK(session.Start(Config(), 1'024) ==
          VRREC_STATUS_INVALID_STATE);
}

void AudioEncodingThreadCreationFailuresRollBackCapture()
{
    EncodingThreadCreationFailureRollsBackCapture(
        VRREC_STATUS_OUT_OF_MEMORY,
        VRREC_STATUS_OUT_OF_MEMORY);
    EncodingThreadCreationFailureRollsBackCapture(
        VRREC_STATUS_INTERNAL_ERROR,
        VRREC_STATUS_INTERNAL_ERROR);
    EncodingThreadCreationFailureRollsBackCapture(
        VRREC_STATUS_OK,
        VRREC_STATUS_INTERNAL_ERROR);
}

}

int main()
{
    RunsACompleteGracefulAudioPipeline();
    DoesNotStartEncodingWhenCaptureInitializationFails();
    RejectsInvalidWindowsBeforeStartingCapture();
    WorkerFailureTerminalizesThePipelineOnStopRequest();
    AbortDuringCaptureStartRollsBackWithoutStartingEncoding();
    AbortDominatesAFailedBlockingCaptureStart();
    AbortDominatesABlockingRoutingUpdate();
    AbortDominatesABlockingEncodingStopRequest();
    AbortCleanupUnblocksAnInFlightEncodingJoin();
    JoinOwnerPerformsPhysicalCleanupAfterLogicalAbort();
    AbortDuringEncodingThreadFailureReclaimsCapture();
    AbortDuringSuccessfulEncodingThreadCreationPreventsFirstWindow();
    AudioEncodingThreadCreationFailuresRollBackCapture();
    return 0;
}
