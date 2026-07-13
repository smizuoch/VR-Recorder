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
        {
            const std::lock_guard lock(mutex);
            ++write_calls;
        }

        changed.notify_all();
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

    std::mutex mutex;
    std::condition_variable changed;
    StereoAudioEncoderWrite write_result {VRREC_STATUS_OK, 1};
    StereoAudioEncoderWrite finish_result {VRREC_STATUS_OK, 1};
    std::size_t write_calls = 0;
    std::size_t finish_calls = 0;
    std::size_t abort_calls = 0;
    bool block_finish = false;
    bool finish_entered = false;
    bool release_finish = false;
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
    return 0;
}
