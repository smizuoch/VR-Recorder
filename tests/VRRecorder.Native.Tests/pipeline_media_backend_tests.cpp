#include "pipeline_media_backend.hpp"

#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdlib>
#include <future>
#include <iostream>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace {

#define CHECK(condition) do { if (!(condition)) { std::cerr << "check failed at " << __FILE__ << ':' << __LINE__ << ": " #condition << '\n'; std::abort(); } } while (false)

using namespace vrrecorder::native;

class FakeRecordingPipeline final : public MediaRecordingPipelinePort {
public:
    vrrec_status_t Start() noexcept override {
        std::unique_lock lock(mutex);
        ++start_calls;
        start_entered = true;
        changed.notify_all();
        if (block_start) {
            changed.wait(lock, [this] { return allow_start; });
        }
        return start_status;
    }
    vrrec_status_t UpdateAudioRouting(vrrec_audio_routing_t routing) noexcept override { observed_routing = routing; ++routing_calls; return routing_status; }
    vrrec_status_t RequestStop() noexcept override {
        std::unique_lock lock(mutex);
        ++stop_calls;
        stop_entered = true;
        changed.notify_all();
        if (block_stop) {
            changed.wait(lock, [this] { return allow_stop; });
        }
        return stop_status;
    }
    void RequestAbort() noexcept override {
        {
            const std::lock_guard lock(mutex);
            abort_requested = true;
            ++request_abort_calls;
        }
        changed.notify_all();
    }
    void JoinAfterAbort() noexcept override {
        std::unique_lock lock(mutex);
        aborted = true;
        ++abort_calls;
        abort_entered = true;
        changed.notify_all();
        if (block_abort) {
            changed.wait(lock, [this] { return allow_abort; });
        }
    }
    vrrec_status_t Join() noexcept override {
        if (abort_from_join) {
            {
                const std::lock_guard lock(mutex);
                join_entered = true;
            }
            changed.notify_all();
            backend_to_abort->RequestAbort();
            {
                const std::lock_guard lock(mutex);
                ++join_calls;
                join_abort_completed = true;
            }
            changed.notify_all();
            return join_status;
        }
        std::unique_lock lock(mutex); join_entered = true; changed.notify_all(); changed.wait(lock, [this] { return allow_join || (aborted && !join_waits_for_release); }); ++join_calls; return join_status;
    }
    MediaRecordingPipelineStatistics Statistics() const noexcept override { return statistics; }
    void WaitForJoin() { std::unique_lock lock(mutex); changed.wait(lock, [this] { return join_entered; }); }
    void WaitForStart() { std::unique_lock lock(mutex); changed.wait(lock, [this] { return start_entered; }); }
    void WaitForStop() { std::unique_lock lock(mutex); changed.wait(lock, [this] { return stop_entered; }); }
    void WaitForJoinAbort() { std::unique_lock lock(mutex); changed.wait(lock, [this] { return join_abort_completed; }); }
    void WaitForAbort() { std::unique_lock lock(mutex); changed.wait(lock, [this] { return abort_entered; }); }
    void ReleaseJoin() { { const std::lock_guard lock(mutex); allow_join = true; } changed.notify_all(); }
    void ReleaseStart() { { const std::lock_guard lock(mutex); allow_start = true; } changed.notify_all(); }
    void ReleaseStop() { { const std::lock_guard lock(mutex); allow_stop = true; } changed.notify_all(); }
    void ReleaseAbort() { { const std::lock_guard lock(mutex); allow_abort = true; } changed.notify_all(); }
    mutable std::mutex mutex;
    std::condition_variable changed;
    MediaRecordingPipelineStatistics statistics {{{120, 118, 2, 3}, 91, 1'500, 2'500}, {48'000, 142}, -15'000};
    vrrec_status_t start_status = VRREC_STATUS_OK;
    vrrec_status_t routing_status = VRREC_STATUS_OK;
    vrrec_status_t stop_status = VRREC_STATUS_OK;
    vrrec_status_t join_status = VRREC_STATUS_OK;
    vrrec_audio_routing_t observed_routing = VRREC_AUDIO_ROUTING_MIXED;
    std::size_t start_calls = 0;
    std::size_t routing_calls = 0;
    std::size_t stop_calls = 0;
    std::size_t request_abort_calls = 0;
    std::size_t abort_calls = 0;
    std::size_t join_calls = 0;
    bool join_entered = false;
    bool start_entered = false;
    bool stop_entered = false;
    bool allow_join = false;
    bool allow_start = false;
    bool allow_stop = false;
    bool block_start = false;
    bool block_stop = false;
    bool aborted = false;
    bool abort_requested = false;
    bool abort_entered = false;
    bool block_abort = false;
    bool allow_abort = false;
    bool join_waits_for_release = false;
    bool abort_from_join = false;
    bool join_abort_completed = false;
    PipelineMediaBackend *backend_to_abort = nullptr;
};

class FakeLayoutPort final : public VideoLayoutUpdatePort {
public:
    vrrec_status_t UpdateVideoLayout(const vrrec_video_layout_v1 &layout) noexcept override { observed = layout; ++calls; return status; }
    vrrec_video_layout_v1 observed {};
    vrrec_status_t status = VRREC_STATUS_OK;
    std::size_t calls = 0;
};

class ScriptedThreadFactory final : public PipelineMediaThreadFactoryPort {
public:
    explicit ScriptedThreadFactory(std::vector<vrrec_status_t> statuses)
        : statuses_(std::move(statuses))
    {
    }

    vrrec_status_t Start(
        std::thread &thread,
        PipelineMediaThreadEntry entry,
        void *context) noexcept override
    {
        const auto status = call_count < statuses_.size()
            ? statuses_[call_count]
            : VRREC_STATUS_OK;
        ++call_count;
        if (status != VRREC_STATUS_OK) {
            return status;
        }

        try {
            thread = std::thread(entry, context);
        } catch (const std::bad_alloc &) {
            return VRREC_STATUS_OUT_OF_MEMORY;
        } catch (...) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        return VRREC_STATUS_OK;
    }

    std::size_t call_count = 0;

private:
    std::vector<vrrec_status_t> statuses_;
};

class EmptySuccessThreadFactory final
    : public PipelineMediaThreadFactoryPort {
public:
    vrrec_status_t Start(
        std::thread &,
        PipelineMediaThreadEntry,
        void *) noexcept override
    {
        ++call_count;
        return VRREC_STATUS_OK;
    }

    std::size_t call_count = 0;
};

class BlockingStopFailureThreadFactory final
    : public PipelineMediaThreadFactoryPort {
public:
    vrrec_status_t Start(
        std::thread &thread,
        PipelineMediaThreadEntry entry,
        void *context) noexcept override
    {
        std::unique_lock lock(mutex_);
        const auto call = call_count_++;
        if (call == 1) {
            stop_launch_entered_ = true;
            changed_.notify_all();
            changed_.wait(lock, [this] { return release_stop_launch_; });
            return VRREC_STATUS_OUT_OF_MEMORY;
        }
        lock.unlock();

        try {
            thread = std::thread(entry, context);
        } catch (...) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        return VRREC_STATUS_OK;
    }

    void WaitForStopLaunch()
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [this] { return stop_launch_entered_; });
    }

    void ReleaseStopLaunch()
    {
        {
            const std::lock_guard lock(mutex_);
            release_stop_launch_ = true;
        }
        changed_.notify_all();
    }

    std::size_t CallCount() const
    {
        const std::lock_guard lock(mutex_);
        return call_count_;
    }

private:
    mutable std::mutex mutex_;
    std::condition_variable changed_;
    std::size_t call_count_ = 0;
    bool stop_launch_entered_ = false;
    bool release_stop_launch_ = false;
};

vrrec_video_layout_v1 Layout()
{
    return {sizeof(vrrec_video_layout_v1), VRREC_ABI_V1, 1280, 720, 1920, 1080, 0, 0, 1920, 1080, VRREC_CANVAS_BACKGROUND_BLACK, VRREC_VIDEO_ROTATION_NONE};
}

void AdaptsControlsAndStatistics()
{
    FakeRecordingPipeline pipeline;
    FakeLayoutPort layout;
    PipelineMediaBackend backend(pipeline, layout);
    CHECK(backend.Start() == VRREC_STATUS_OK);
    const auto requested_layout = Layout();
    CHECK(backend.UpdateVideoLayout(requested_layout) == VRREC_STATUS_OK);
    CHECK(layout.calls == 1);
    CHECK(layout.observed.source_width == 1280);
    CHECK(backend.UpdateAudioRouting(VRREC_AUDIO_ROUTING_MUTED) == VRREC_STATUS_OK);
    CHECK(pipeline.observed_routing == VRREC_AUDIO_ROUTING_MUTED);
    vrrec_session_statistics_v1 statistics {};
    CHECK(backend.GetStatistics(statistics) == VRREC_STATUS_OK);
    CHECK(statistics.struct_size == sizeof(vrrec_session_statistics_v1));
    CHECK(statistics.abi_version == VRREC_ABI_V1);
    CHECK(statistics.source_video_frame_count == 120);
    CHECK(statistics.muxed_video_packet_count == 91);
    CHECK(statistics.muxed_audio_packet_count == 142);
    CHECK(statistics.dropped_source_video_frame_count == 2);
    CHECK(statistics.duplicated_output_video_frame_count == 3);
    CHECK(statistics.latest_encode_latency_microseconds == 1'500);
    CHECK(statistics.maximum_encode_latency_microseconds == 2'500);
    CHECK(statistics.audio_video_offset_microseconds == -15'000);
}

void StopsAndJoinsAsynchronously()
{
    FakeRecordingPipeline pipeline;
    FakeLayoutPort layout;
    {
        PipelineMediaBackend backend(pipeline, layout);
        CHECK(backend.Start() == VRREC_STATUS_OK);
        CHECK(backend.RequestStop() == VRREC_STATUS_OK);
        pipeline.WaitForJoin();
        CHECK(pipeline.stop_calls == 1);
        CHECK(pipeline.join_calls == 0);
        CHECK(backend.RequestStop() == VRREC_STATUS_OK);
        CHECK(pipeline.stop_calls == 1);
        pipeline.ReleaseJoin();
    }
    CHECK(pipeline.join_calls == 1);
}

void AbortReturnsBeforeBlockingPipelineCleanupCompletes()
{
    FakeRecordingPipeline pipeline;
    pipeline.block_abort = true;
    FakeLayoutPort layout;
    PipelineMediaBackend backend(pipeline, layout);
    CHECK(backend.Start() == VRREC_STATUS_OK);

    std::promise<void> returned;
    auto completed = returned.get_future();
    std::thread aborter([&] {
        backend.RequestAbort();
        returned.set_value();
    });
    pipeline.WaitForAbort();

    const auto returned_before_cleanup =
        completed.wait_for(std::chrono::milliseconds(50)) ==
        std::future_status::ready;
    pipeline.ReleaseAbort();
    aborter.join();

    CHECK(returned_before_cleanup);
    CHECK(pipeline.abort_calls == 1);
}

void AbortDoesNotJoinAStopWorkerWaitingForTheAbortCaller()
{
    FakeRecordingPipeline pipeline;
    pipeline.join_waits_for_release = true;
    FakeLayoutPort layout;
    PipelineMediaBackend backend(pipeline, layout);
    CHECK(backend.Start() == VRREC_STATUS_OK);
    CHECK(backend.RequestStop() == VRREC_STATUS_OK);
    pipeline.WaitForJoin();

    std::promise<void> returned;
    auto completed = returned.get_future();
    std::thread aborter([&] {
        backend.RequestAbort();
        returned.set_value();
    });
    pipeline.WaitForAbort();

    const auto returned_before_join =
        completed.wait_for(std::chrono::milliseconds(50)) ==
        std::future_status::ready;
    pipeline.ReleaseJoin();
    aborter.join();
    backend.JoinAfterAbort();

    CHECK(returned_before_join);
    CHECK(pipeline.join_calls == 1);
}

void ExternalAbortJoinWaitsForTheSharedCleanupCompletion()
{
    FakeRecordingPipeline pipeline;
    pipeline.block_abort = true;
    FakeLayoutPort layout;
    PipelineMediaBackend backend(pipeline, layout);
    CHECK(backend.Start() == VRREC_STATUS_OK);
    backend.RequestAbort();
    pipeline.WaitForAbort();

    std::promise<void> join_entered;
    std::promise<void> join_returned;
    auto entered = join_entered.get_future();
    auto returned = join_returned.get_future();
    std::thread joiner([&] {
        join_entered.set_value();
        backend.JoinAfterAbort();
        join_returned.set_value();
    });
    entered.wait();
    const auto returned_before_cleanup =
        returned.wait_for(std::chrono::milliseconds(50)) ==
        std::future_status::ready;

    pipeline.ReleaseAbort();
    joiner.join();
    backend.JoinAfterAbort();

    CHECK(!returned_before_cleanup);
    CHECK(pipeline.abort_calls == 1);
}

void CleanupWorkerCreationFailurePreventsPipelineStart()
{
    FakeRecordingPipeline pipeline;
    FakeLayoutPort layout;
    ScriptedThreadFactory threads({VRREC_STATUS_OUT_OF_MEMORY});
    PipelineMediaBackend backend(pipeline, layout, threads);

    CHECK(backend.Start() == VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(threads.call_count == 1);
    CHECK(pipeline.start_calls == 0);
}

void CleanupWorkerFailureFallsBackToExactlyOneSynchronousCleanup()
{
    FakeRecordingPipeline pipeline;
    FakeLayoutPort layout;
    ScriptedThreadFactory threads({VRREC_STATUS_OUT_OF_MEMORY});
    {
        PipelineMediaBackend backend(pipeline, layout, threads);
        CHECK(backend.Start() == VRREC_STATUS_OUT_OF_MEMORY);
        backend.JoinAfterAbort();
    }

    CHECK(threads.call_count == 1);
    CHECK(pipeline.start_calls == 0);
    CHECK(pipeline.abort_calls == 1);
    CHECK(pipeline.join_calls == 0);
}

void AbortUsesOnlyThePrestartedCleanupWorker()
{
    FakeRecordingPipeline pipeline;
    FakeLayoutPort layout;
    ScriptedThreadFactory threads({VRREC_STATUS_OK});
    PipelineMediaBackend backend(pipeline, layout, threads);

    CHECK(backend.Start() == VRREC_STATUS_OK);
    CHECK(threads.call_count == 1);
    backend.RequestAbort();
    backend.JoinAfterAbort();

    CHECK(threads.call_count == 1);
    CHECK(pipeline.abort_calls == 1);
}

void AbortWinsWhilePipelineStartReturnsSuccess()
{
    FakeRecordingPipeline pipeline;
    pipeline.block_start = true;
    FakeLayoutPort layout;
    PipelineMediaBackend backend(pipeline, layout);

    auto starting = std::async(std::launch::async, [&] {
        return backend.Start();
    });
    pipeline.WaitForStart();
    backend.RequestAbort();
    pipeline.ReleaseStart();

    CHECK(starting.get() == VRREC_STATUS_INVALID_STATE);
    backend.JoinAfterAbort();
    CHECK(pipeline.abort_calls == 1);
}

void StopWorkerCreationFailureIsCachedAndCleansUp()
{
    FakeRecordingPipeline pipeline;
    FakeLayoutPort layout;
    ScriptedThreadFactory threads({
        VRREC_STATUS_OK,
        VRREC_STATUS_OUT_OF_MEMORY,
    });
    PipelineMediaBackend backend(pipeline, layout, threads);

    CHECK(backend.Start() == VRREC_STATUS_OK);
    CHECK(backend.RequestStop() == VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(backend.RequestStop() == VRREC_STATUS_OUT_OF_MEMORY);
    backend.JoinAfterAbort();

    CHECK(threads.call_count == 2);
    CHECK(pipeline.stop_calls == 1);
    CHECK(pipeline.join_calls == 0);
    CHECK(pipeline.abort_calls == 1);
}

void PipelineStopFailureIsCachedAfterCleanup()
{
    FakeRecordingPipeline pipeline;
    pipeline.stop_status = VRREC_STATUS_INTERNAL_ERROR;
    FakeLayoutPort layout;
    PipelineMediaBackend backend(pipeline, layout);

    CHECK(backend.Start() == VRREC_STATUS_OK);
    CHECK(backend.RequestStop() == VRREC_STATUS_INTERNAL_ERROR);
    backend.JoinAfterAbort();
    CHECK(backend.RequestStop() == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(pipeline.stop_calls == 1);
    CHECK(pipeline.join_calls == 0);
    CHECK(pipeline.abort_calls == 1);
}

void EveryPipelineStopFailureTriggersCleanup()
{
    FakeRecordingPipeline pipeline;
    pipeline.stop_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    FakeLayoutPort layout;
    PipelineMediaBackend backend(pipeline, layout);

    CHECK(backend.Start() == VRREC_STATUS_OK);
    CHECK(backend.RequestStop() == VRREC_STATUS_BACKEND_UNAVAILABLE);
    backend.JoinAfterAbort();
    CHECK(backend.RequestStop() == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(pipeline.stop_calls == 1);
    CHECK(pipeline.join_calls == 0);
    CHECK(pipeline.abort_calls == 1);
}

void ConcurrentStopCallersShareTheFailureResult()
{
    FakeRecordingPipeline pipeline;
    pipeline.block_stop = true;
    pipeline.stop_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    FakeLayoutPort layout;
    PipelineMediaBackend backend(pipeline, layout);
    CHECK(backend.Start() == VRREC_STATUS_OK);

    auto first = std::async(std::launch::async, [&] {
        return backend.RequestStop();
    });
    pipeline.WaitForStop();

    std::promise<void> second_entered;
    auto entered = second_entered.get_future();
    auto second = std::async(std::launch::async, [&] {
        second_entered.set_value();
        return backend.RequestStop();
    });
    entered.wait();
    const auto second_returned_before_completion =
        second.wait_for(std::chrono::milliseconds(50)) ==
        std::future_status::ready;

    pipeline.ReleaseStop();
    CHECK(first.get() == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(second.get() == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(!second_returned_before_completion);
    CHECK(backend.RequestStop() == VRREC_STATUS_BACKEND_UNAVAILABLE);
    backend.JoinAfterAbort();

    CHECK(pipeline.stop_calls == 1);
    CHECK(pipeline.join_calls == 0);
    CHECK(pipeline.abort_calls == 1);
}

void EmptySuccessfulThreadCreationFailsClosed()
{
    FakeRecordingPipeline pipeline;
    FakeLayoutPort layout;
    EmptySuccessThreadFactory threads;
    PipelineMediaBackend backend(pipeline, layout, threads);

    CHECK(backend.Start() == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(threads.call_count == 1);
    CHECK(pipeline.start_calls == 0);
}

void AbortWinsWhilePipelineStopReturnsFailure()
{
    FakeRecordingPipeline pipeline;
    pipeline.block_stop = true;
    pipeline.stop_status = VRREC_STATUS_INTERNAL_ERROR;
    FakeLayoutPort layout;
    PipelineMediaBackend backend(pipeline, layout);
    CHECK(backend.Start() == VRREC_STATUS_OK);

    auto stopping = std::async(std::launch::async, [&] {
        return backend.RequestStop();
    });
    pipeline.WaitForStop();
    backend.RequestAbort();
    pipeline.ReleaseStop();

    CHECK(stopping.get() == VRREC_STATUS_INVALID_STATE);
    backend.JoinAfterAbort();
    CHECK(pipeline.stop_calls == 1);
    CHECK(pipeline.join_calls == 0);
    CHECK(pipeline.abort_calls == 1);
}

void InternalThreadCreationFailuresAreMappedAndCached()
{
    {
        FakeRecordingPipeline pipeline;
        FakeLayoutPort layout;
        ScriptedThreadFactory threads({VRREC_STATUS_INTERNAL_ERROR});
        PipelineMediaBackend backend(pipeline, layout, threads);

        CHECK(backend.Start() == VRREC_STATUS_INTERNAL_ERROR);
        CHECK(threads.call_count == 1);
        CHECK(pipeline.start_calls == 0);
    }

    FakeRecordingPipeline pipeline;
    FakeLayoutPort layout;
    ScriptedThreadFactory threads({
        VRREC_STATUS_OK,
        VRREC_STATUS_INTERNAL_ERROR,
    });
    PipelineMediaBackend backend(pipeline, layout, threads);

    CHECK(backend.Start() == VRREC_STATUS_OK);
    CHECK(backend.RequestStop() == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(backend.RequestStop() == VRREC_STATUS_INTERNAL_ERROR);
    backend.JoinAfterAbort();
    CHECK(threads.call_count == 2);
    CHECK(pipeline.stop_calls == 1);
    CHECK(pipeline.join_calls == 0);
    CHECK(pipeline.abort_calls == 1);
}

void AbortWinsWhileStopThreadLaunchFails()
{
    FakeRecordingPipeline pipeline;
    FakeLayoutPort layout;
    BlockingStopFailureThreadFactory threads;
    PipelineMediaBackend backend(pipeline, layout, threads);
    CHECK(backend.Start() == VRREC_STATUS_OK);

    auto stopping = std::async(std::launch::async, [&] {
        return backend.RequestStop();
    });
    threads.WaitForStopLaunch();
    backend.RequestAbort();
    threads.ReleaseStopLaunch();

    CHECK(stopping.get() == VRREC_STATUS_INVALID_STATE);
    backend.JoinAfterAbort();
    CHECK(threads.CallCount() == 2);
    CHECK(pipeline.stop_calls == 1);
    CHECK(pipeline.join_calls == 0);
    CHECK(pipeline.abort_calls == 1);
}

void DestructorWaitsForTheStopWorker()
{
    FakeRecordingPipeline pipeline;
    pipeline.join_waits_for_release = true;
    FakeLayoutPort layout;
    auto backend = std::make_unique<PipelineMediaBackend>(pipeline, layout);
    CHECK(backend->Start() == VRREC_STATUS_OK);
    CHECK(backend->RequestStop() == VRREC_STATUS_OK);
    pipeline.WaitForJoin();

    std::promise<void> destroyed;
    auto completed = destroyed.get_future();
    std::thread destroyer([&] {
        backend.reset();
        destroyed.set_value();
    });
    pipeline.WaitForAbort();
    const auto returned_before_join =
        completed.wait_for(std::chrono::milliseconds(50)) ==
        std::future_status::ready;

    pipeline.ReleaseJoin();
    destroyer.join();

    CHECK(!returned_before_join);
    CHECK(pipeline.join_calls == 1);
    CHECK(pipeline.abort_calls == 1);
}

int AbortFromTheStopWorkerChild()
{
    FakeRecordingPipeline pipeline;
    FakeLayoutPort layout;
    {
        PipelineMediaBackend backend(pipeline, layout);
        if (backend.Start() != VRREC_STATUS_OK) {
            return 4;
        }
        pipeline.abort_from_join = true;
        pipeline.backend_to_abort = &backend;
        const auto stop_status = backend.RequestStop();
        if (stop_status != VRREC_STATUS_OK &&
            stop_status != VRREC_STATUS_INVALID_STATE) {
            return 2;
        }
        pipeline.WaitForJoin();
        pipeline.WaitForJoinAbort();
    }
    return pipeline.join_calls == 1 ? 0 : 3;
}

void AbortFromTheStopWorkerDoesNotJoinItself(const char *executable)
{
    const auto command = std::string("\"") + executable +
        "\" --abort-from-stop-worker";
    CHECK(std::system(command.c_str()) == 0);
}

}

int main(int argc, char **argv)
{
    if (argc == 2) {
        const auto requested = std::string(argv[1]);
        if (requested == "--abort-from-stop-worker") {
            return AbortFromTheStopWorkerChild();
        }
        if (requested == "--cleanup-thread-failure") {
            CleanupWorkerCreationFailurePreventsPipelineStart();
            return 0;
        }
        if (requested == "--start-abort") {
            AbortWinsWhilePipelineStartReturnsSuccess();
            return 0;
        }
        if (requested == "--stop-thread-failure") {
            StopWorkerCreationFailureIsCachedAndCleansUp();
            return 0;
        }
        if (requested == "--stop-failure-cache") {
            PipelineStopFailureIsCachedAfterCleanup();
            return 0;
        }
        if (requested == "--empty-thread") {
            EmptySuccessfulThreadCreationFailsClosed();
            return 0;
        }
        if (requested == "--stop-abort") {
            AbortWinsWhilePipelineStopReturnsFailure();
            return 0;
        }
        if (requested == "--internal-thread-failure") {
            InternalThreadCreationFailuresAreMappedAndCached();
            return 0;
        }
        if (requested == "--launch-abort") {
            AbortWinsWhileStopThreadLaunchFails();
            return 0;
        }
    }

    AdaptsControlsAndStatistics();
    StopsAndJoinsAsynchronously();
    AbortReturnsBeforeBlockingPipelineCleanupCompletes();
    AbortDoesNotJoinAStopWorkerWaitingForTheAbortCaller();
    ExternalAbortJoinWaitsForTheSharedCleanupCompletion();
    CleanupWorkerCreationFailurePreventsPipelineStart();
    CleanupWorkerFailureFallsBackToExactlyOneSynchronousCleanup();
    AbortUsesOnlyThePrestartedCleanupWorker();
    AbortWinsWhilePipelineStartReturnsSuccess();
    StopWorkerCreationFailureIsCachedAndCleansUp();
    PipelineStopFailureIsCachedAfterCleanup();
    EveryPipelineStopFailureTriggersCleanup();
    ConcurrentStopCallersShareTheFailureResult();
    EmptySuccessfulThreadCreationFailsClosed();
    AbortWinsWhilePipelineStopReturnsFailure();
    InternalThreadCreationFailuresAreMappedAndCached();
    AbortWinsWhileStopThreadLaunchFails();
    DestructorWaitsForTheStopWorker();
    AbortFromTheStopWorkerDoesNotJoinItself(argv[0]);
    return 0;
}
