#include "audio_encoding_worker.hpp"

#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <future>
#include <iostream>
#include <mutex>
#include <span>
#include <thread>
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

class BlockingMixSource final : public StereoAudioMixSource {
public:
    explicit BlockingMixSource(std::size_t ready_windows)
        : ready_windows_(ready_windows)
    {
    }

    StereoAudioMixResult MixNext(
        std::size_t frame_count_48k,
        std::span<float> output_interleaved,
        StereoAudioMixRead &read) noexcept override
    {
        std::unique_lock lock(mutex_);
        if (fail_unexpectedly) {
            return StereoAudioMixResult::Aborted;
        }

        if (fail_capture) {
            return StereoAudioMixResult::Failed;
        }

        if (violate_contract) {
            return StereoAudioMixResult::InvalidArgument;
        }

        if (windows_returned_ < ready_windows_) {
            const auto start = windows_returned_ * frame_count_48k;
            ++windows_returned_;
            for (auto &sample : output_interleaved) {
                sample = 0.25F;
            }

            read = {
                start,
                frame_count_48k,
                true,
                true,
                false,
                false,
            };
            changed_.notify_all();
            return StereoAudioMixResult::Mixed;
        }

        changed_.wait(lock, [&] { return aborted_; });
        return StereoAudioMixResult::Aborted;
    }

    void Abort() noexcept override
    {
        {
            const std::lock_guard lock(mutex_);
            if (aborted_) {
                return;
            }

            aborted_ = true;
            ++abort_calls;
        }

        changed_.notify_all();
    }

    void WaitForWindows(std::size_t count)
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [&] { return windows_returned_ >= count; });
    }

    std::size_t abort_calls = 0;
    bool fail_unexpectedly = false;
    bool fail_capture = false;
    bool violate_contract = false;

private:
    std::mutex mutex_;
    std::condition_variable changed_;
    std::size_t ready_windows_;
    std::size_t windows_returned_ = 0;
    bool aborted_ = false;
};

class RecordingEncoderSink final : public StereoAudioEncoderSink {
public:
    StereoAudioEncoderWrite WritePcm48k(
        std::uint64_t,
        std::span<const float>) noexcept override
    {
        std::unique_lock lock(mutex);
        ++write_calls;
        changed.notify_all();
        if (block_write && write_calls == block_write_call) {
            changed.wait(lock, [this] { return release_write; });
        }
        return write_result;
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
        return finish_result;
    }

    void Abort() noexcept override
    {
        {
            const std::lock_guard lock(mutex);
            ++abort_calls;
        }
        changed.notify_all();
    }

    void WaitForWrites(std::size_t count)
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return write_calls >= count; });
    }

    void WaitForFinish()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return finish_entered; });
    }

    void WaitForAbort()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return abort_calls > 0; });
    }

    void ReleaseFinish()
    {
        {
            const std::lock_guard lock(mutex);
            release_finish = true;
        }
        changed.notify_all();
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
    StereoAudioEncoderWrite write_result {VRREC_STATUS_OK, 1};
    StereoAudioEncoderWrite finish_result {VRREC_STATUS_OK, 1};
    std::size_t write_calls = 0;
    std::size_t finish_calls = 0;
    std::size_t abort_calls = 0;
    bool block_write = false;
    std::size_t block_write_call = 1;
    bool release_write = false;
    bool block_finish = false;
    bool finish_entered = false;
    bool release_finish = false;
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

void GracefulStopFlushesAfterAllSubmittedWindows()
{
    BlockingMixSource source(2);
    RecordingEncoderSink sink;
    StereoAudioEncodingWorker worker(source, sink);

    CHECK(worker.Start(1'024) == VRREC_STATUS_OK);
    sink.WaitForWrites(2);
    CHECK(worker.RequestStop() == VRREC_STATUS_OK);
    CHECK(worker.RequestStop() == VRREC_STATUS_OK);
    CHECK(worker.Join() == StereoAudioEncodingWorkerResult::Stopped);
    CHECK(source.abort_calls == 1);
    CHECK(sink.write_calls == 2);
    CHECK(sink.finish_calls == 1);
    CHECK(sink.abort_calls == 0);
    CHECK(worker.SubmittedFrameCount() == 2'048);
    CHECK(worker.MuxedPacketCount() == 3);
}

void AbortDoesNotFlushTheEncoder()
{
    BlockingMixSource source(0);
    RecordingEncoderSink sink;
    StereoAudioEncodingWorker worker(source, sink);

    CHECK(worker.Start(1'024) == VRREC_STATUS_OK);
    worker.Abort();
    worker.Abort();
    CHECK(worker.Join() == StereoAudioEncodingWorkerResult::Aborted);
    CHECK(source.abort_calls == 1);
    CHECK(sink.finish_calls == 0);
    CHECK(sink.abort_calls == 1);
}

void EncoderFailureAbortsWithoutCountingTheWindow()
{
    BlockingMixSource source(1);
    RecordingEncoderSink sink;
    sink.write_result = {VRREC_STATUS_INTERNAL_ERROR, 0};
    StereoAudioEncodingWorker worker(source, sink);

    CHECK(worker.Start(1'024) == VRREC_STATUS_OK);
    CHECK(worker.Join() == StereoAudioEncodingWorkerResult::EncoderFailed);
    CHECK(sink.write_calls == 1);
    CHECK(sink.finish_calls == 0);
    CHECK(sink.abort_calls == 1);
    CHECK(source.abort_calls == 1);
    CHECK(worker.SubmittedFrameCount() == 0);
    CHECK(worker.MuxedPacketCount() == 0);
}

void UnexpectedSourceAbortReleasesBothPipelineEnds()
{
    BlockingMixSource source(0);
    source.fail_unexpectedly = true;
    RecordingEncoderSink sink;
    StereoAudioEncodingWorker worker(source, sink);

    CHECK(worker.Start(1'024) == VRREC_STATUS_OK);
    CHECK(worker.Join() == StereoAudioEncodingWorkerResult::CaptureFailed);
    CHECK(worker.RequestStop() == VRREC_STATUS_INVALID_STATE);
    CHECK(source.abort_calls == 1);
    CHECK(sink.abort_calls == 1);
    CHECK(sink.finish_calls == 0);
}

void CaptureFailureReleasesBothPipelineEnds()
{
    BlockingMixSource source(0);
    source.fail_capture = true;
    RecordingEncoderSink sink;
    StereoAudioEncodingWorker worker(source, sink);

    CHECK(worker.Start(1'024) == VRREC_STATUS_OK);
    CHECK(worker.Join() == StereoAudioEncodingWorkerResult::CaptureFailed);
    CHECK(source.abort_calls == 1);
    CHECK(sink.abort_calls == 1);
    CHECK(sink.finish_calls == 0);
}

void SourceContractFailureReleasesBothPipelineEnds()
{
    BlockingMixSource source(0);
    source.violate_contract = true;
    RecordingEncoderSink sink;
    StereoAudioEncodingWorker worker(source, sink);

    CHECK(worker.Start(1'024) == VRREC_STATUS_OK);
    CHECK(worker.Join() == StereoAudioEncodingWorkerResult::Failed);
    CHECK(source.abort_calls == 1);
    CHECK(sink.abort_calls == 1);
    CHECK(sink.finish_calls == 0);
}

void AbortDominatesAConcurrentGracefulFinish()
{
    BlockingMixSource source(0);
    RecordingEncoderSink sink;
    sink.block_finish = true;
    sink.finish_result = {VRREC_STATUS_OK, 7};
    StereoAudioEncodingWorker worker(source, sink);

    CHECK(worker.Start(1'024) == VRREC_STATUS_OK);
    CHECK(worker.RequestStop() == VRREC_STATUS_OK);
    sink.WaitForFinish();
    auto aborting = std::async(std::launch::async, [&] {
        worker.Abort();
    });
    sink.WaitForAbort();
    sink.ReleaseFinish();
    aborting.get();

    CHECK(worker.Join() == StereoAudioEncodingWorkerResult::Aborted);
    CHECK(worker.MuxedPacketCount() == 0);
    CHECK(sink.finish_calls == 1);
    CHECK(sink.abort_calls == 1);
}

void AbortDominatesAnInFlightAudioWriteFailure()
{
    BlockingMixSource source(1);
    RecordingEncoderSink sink;
    sink.block_write = true;
    sink.write_result = {
        VRREC_STATUS_INVALID_STATE,
        0,
        AudioEncoderFailureStage::Encoding,
    };
    StereoAudioEncodingWorker worker(source, sink);

    CHECK(worker.Start(1'024) == VRREC_STATUS_OK);
    sink.WaitForWrites(1);
    auto aborting = std::async(std::launch::async, [&] {
        worker.Abort();
    });
    sink.WaitForAbort();
    sink.ReleaseWrite();
    aborting.get();

    CHECK(worker.Join() == StereoAudioEncodingWorkerResult::Aborted);
    CHECK(worker.SubmittedFrameCount() == 0);
    CHECK(worker.MuxedPacketCount() == 0);
    CHECK(source.abort_calls == 1);
    CHECK(sink.write_calls == 1);
    CHECK(sink.finish_calls == 0);
    CHECK(sink.abort_calls == 1);
}

void AbortDoesNotCommitASuccessfulInFlightAudioWrite()
{
    BlockingMixSource source(2);
    RecordingEncoderSink sink;
    sink.block_write = true;
    sink.block_write_call = 2;
    StereoAudioEncodingWorker worker(source, sink);

    CHECK(worker.Start(1'024) == VRREC_STATUS_OK);
    sink.WaitForWrites(2);
    auto aborting = std::async(std::launch::async, [&] {
        worker.Abort();
    });
    sink.WaitForAbort();
    sink.ReleaseWrite();
    aborting.get();

    CHECK(worker.Join() == StereoAudioEncodingWorkerResult::Aborted);
    CHECK(worker.SubmittedFrameCount() == 1'024);
    CHECK(worker.MuxedPacketCount() == 1);
    CHECK(source.abort_calls == 1);
    CHECK(sink.write_calls == 2);
    CHECK(sink.finish_calls == 0);
    CHECK(sink.abort_calls == 1);
}

void ThreadCreationFailureIsTerminal(
    vrrec_status_t factory_status,
    vrrec_status_t expected_status,
    bool create_thread_on_success = true)
{
    BlockingMixSource source(0);
    RecordingEncoderSink sink;
    ScriptedThreadFactory thread_factory(
        factory_status,
        create_thread_on_success);
    StereoAudioEncodingWorker worker(source, sink, thread_factory);

    CHECK(worker.Start(1'024) == expected_status);
    CHECK(thread_factory.start_calls == 1);
    CHECK(worker.Join() == StereoAudioEncodingWorkerResult::Failed);
    CHECK(worker.Join() == StereoAudioEncodingWorkerResult::Failed);
    CHECK(worker.RequestStop() == VRREC_STATUS_INVALID_STATE);
    CHECK(worker.IsFinished());
    CHECK(worker.SubmittedFrameCount() == 0);
    CHECK(worker.MuxedPacketCount() == 0);
    CHECK(source.abort_calls == 0);
    CHECK(sink.write_calls == 0);
    CHECK(sink.finish_calls == 0);
    CHECK(sink.abort_calls == 0);
    CHECK(worker.Start(1'024) == VRREC_STATUS_INVALID_STATE);
    CHECK(thread_factory.start_calls == 1);

    CHECK(!worker.RequestAbort());
    worker.JoinAfterAbort();
    CHECK(worker.Join() == StereoAudioEncodingWorkerResult::Failed);
    CHECK(source.abort_calls == 0);
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
    BlockingMixSource source(0);
    RecordingEncoderSink sink;
    BlockingThreadFactory thread_factory(
        factory_status,
        create_thread_on_success);
    StereoAudioEncodingWorker worker(source, sink, thread_factory);

    auto starting = std::async(std::launch::async, [&] {
        return worker.Start(1'024);
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
    CHECK(worker.Join() == StereoAudioEncodingWorkerResult::Aborted);
    CHECK(worker.RequestStop() == VRREC_STATUS_INVALID_STATE);
    CHECK(worker.Start(1'024) == VRREC_STATUS_INVALID_STATE);
    CHECK(source.abort_calls == 1);
    CHECK(sink.write_calls == 0);
    CHECK(sink.finish_calls == 0);
    CHECK(sink.abort_calls == 1);
    CHECK(worker.SubmittedFrameCount() == 0);
    CHECK(worker.MuxedPacketCount() == 0);
}

void AbortWinsDuringSuccessfulThreadCreation()
{
    AbortWinsDuringThreadCreation(VRREC_STATUS_OK, true);
}

void AbortBeforeDelayedAudioRunPreventsFirstWindow()
{
    BlockingMixSource source(1);
    RecordingEncoderSink sink;
    BlockingThreadFactory thread_factory(VRREC_STATUS_OK, true);
    StereoAudioEncodingWorker worker(source, sink, thread_factory);

    auto starting = std::async(std::launch::async, [&] {
        return worker.Start(1'024);
    });
    thread_factory.WaitForStart();
    CHECK(worker.RequestAbort());
    CHECK(worker.RequestAbort());
    thread_factory.ReleaseStart();

    CHECK(starting.get() == VRREC_STATUS_INVALID_STATE);
    worker.JoinAfterAbort();

    CHECK(worker.Join() == StereoAudioEncodingWorkerResult::Aborted);
    CHECK(source.abort_calls == 1);
    CHECK(sink.write_calls == 0);
    CHECK(sink.finish_calls == 0);
    CHECK(sink.abort_calls == 1);
    CHECK(worker.SubmittedFrameCount() == 0);
    CHECK(worker.MuxedPacketCount() == 0);
}

void AbortWinsDuringFailedThreadCreation()
{
    AbortWinsDuringThreadCreation(VRREC_STATUS_OUT_OF_MEMORY, false);
}

void AbortBeforeStartPreventsThreadCreation()
{
    BlockingMixSource source(0);
    RecordingEncoderSink sink;
    ScriptedThreadFactory thread_factory(VRREC_STATUS_OK);
    StereoAudioEncodingWorker worker(source, sink, thread_factory);

    worker.Abort();

    CHECK(worker.Start(1'024) == VRREC_STATUS_INVALID_STATE);
    CHECK(thread_factory.start_calls == 0);
    CHECK(worker.Join() == StereoAudioEncodingWorkerResult::Aborted);
    CHECK(worker.RequestStop() == VRREC_STATUS_INVALID_STATE);
    CHECK(source.abort_calls == 1);
    CHECK(sink.write_calls == 0);
    CHECK(sink.finish_calls == 0);
    CHECK(sink.abort_calls == 1);
    CHECK(worker.SubmittedFrameCount() == 0);
    CHECK(worker.MuxedPacketCount() == 0);
}

void DestroyingANeverStartedWorkerDoesNotAbortAdjacentPorts()
{
    BlockingMixSource source(0);
    RecordingEncoderSink sink;

    {
        StereoAudioEncodingWorker worker(source, sink);
    }

    CHECK(source.abort_calls == 0);
    CHECK(sink.write_calls == 0);
    CHECK(sink.finish_calls == 0);
    CHECK(sink.abort_calls == 0);
}

}

int main()
{
    GracefulStopFlushesAfterAllSubmittedWindows();
    AbortDoesNotFlushTheEncoder();
    EncoderFailureAbortsWithoutCountingTheWindow();
    UnexpectedSourceAbortReleasesBothPipelineEnds();
    CaptureFailureReleasesBothPipelineEnds();
    SourceContractFailureReleasesBothPipelineEnds();
    AbortDominatesAConcurrentGracefulFinish();
    AbortDominatesAnInFlightAudioWriteFailure();
    AbortDoesNotCommitASuccessfulInFlightAudioWrite();
    OutOfMemoryThreadCreationIsTerminalFailure();
    InternalThreadCreationFailureIsTerminalFailure();
    EmptySuccessfulThreadCreationFailsClosed();
    AbortWinsDuringSuccessfulThreadCreation();
    AbortBeforeDelayedAudioRunPreventsFirstWindow();
    AbortWinsDuringFailedThreadCreation();
    AbortBeforeStartPreventsThreadCreation();
    DestroyingANeverStartedWorkerDoesNotAbortAdjacentPorts();
    return 0;
}
