#include "pipeline_media_backend.hpp"

#include <condition_variable>
#include <cstddef>
#include <cstdlib>
#include <iostream>
#include <mutex>

namespace {

#define CHECK(condition) do { if (!(condition)) { std::cerr << "check failed at " << __FILE__ << ':' << __LINE__ << ": " #condition << '\n'; std::abort(); } } while (false)

using namespace vrrecorder::native;

class FakeRecordingPipeline final : public MediaRecordingPipelinePort {
public:
    vrrec_status_t Start() noexcept override { ++start_calls; return start_status; }
    vrrec_status_t UpdateAudioRouting(vrrec_audio_routing_t routing) noexcept override { observed_routing = routing; ++routing_calls; return routing_status; }
    vrrec_status_t RequestStop() noexcept override { ++stop_calls; return stop_status; }
    void Abort() noexcept override { { const std::lock_guard lock(mutex); aborted = true; ++abort_calls; } changed.notify_all(); }
    vrrec_status_t Join() noexcept override { std::unique_lock lock(mutex); join_entered = true; changed.notify_all(); changed.wait(lock, [this] { return allow_join || aborted; }); ++join_calls; return join_status; }
    MediaRecordingPipelineStatistics Statistics() const noexcept override { return statistics; }
    void WaitForJoin() { std::unique_lock lock(mutex); changed.wait(lock, [this] { return join_entered; }); }
    void ReleaseJoin() { { const std::lock_guard lock(mutex); allow_join = true; } changed.notify_all(); }
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
    std::size_t abort_calls = 0;
    std::size_t join_calls = 0;
    bool join_entered = false;
    bool allow_join = false;
    bool aborted = false;
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

}

int main()
{
    AdaptsControlsAndStatistics();
    StopsAndJoinsAsynchronously();
    return 0;
}
