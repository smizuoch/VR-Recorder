#include "pipeline_media_backend.hpp"

#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdlib>
#include <future>
#include <iostream>
#include <mutex>
#include <string>
#include <thread>

namespace {

#define CHECK(condition) do { if (!(condition)) { std::cerr << "check failed at " << __FILE__ << ':' << __LINE__ << ": " #condition << '\n'; std::abort(); } } while (false)

using namespace vrrecorder::native;

class FakeRecordingPipeline final : public MediaRecordingPipelinePort {
public:
    vrrec_status_t Start() noexcept override { ++start_calls; return start_status; }
    vrrec_status_t UpdateAudioRouting(vrrec_audio_routing_t routing) noexcept override { observed_routing = routing; ++routing_calls; return routing_status; }
    vrrec_status_t RequestStop() noexcept override { ++stop_calls; return stop_status; }
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
    void WaitForJoinAbort() { std::unique_lock lock(mutex); changed.wait(lock, [this] { return join_abort_completed; }); }
    void WaitForAbort() { std::unique_lock lock(mutex); changed.wait(lock, [this] { return abort_entered; }); }
    void ReleaseJoin() { { const std::lock_guard lock(mutex); allow_join = true; } changed.notify_all(); }
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
    bool allow_join = false;
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
        if (backend.RequestStop() != VRREC_STATUS_OK) {
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
    if (argc == 2 &&
        std::string(argv[1]) == "--abort-from-stop-worker") {
        return AbortFromTheStopWorkerChild();
    }

    AdaptsControlsAndStatistics();
    StopsAndJoinsAsynchronously();
    AbortReturnsBeforeBlockingPipelineCleanupCompletes();
    AbortDoesNotJoinAStopWorkerWaitingForTheAbortCaller();
    ExternalAbortJoinWaitsForTheSharedCleanupCompletion();
    AbortFromTheStopWorkerDoesNotJoinItself(argv[0]);
    return 0;
}
