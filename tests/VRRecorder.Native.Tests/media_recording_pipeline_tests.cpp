#include "media_recording_pipeline.hpp"
#include "fragmented_mp4_test_support.hpp"

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <vector>

namespace {

#define CHECK(condition) do { if (!(condition)) { std::cerr << "check failed at " << __FILE__ << ':' << __LINE__ << ": " #condition << '\n'; std::abort(); } } while (false)

using namespace vrrecorder::native;
using namespace vrrecorder::native::test;

class FakeVideoSession final : public VideoPipelineSessionPort {
public:
    explicit FakeVideoSession(std::vector<int> &order) : order_(order) {}
    vrrec_status_t Start(std::chrono::milliseconds timeout) noexcept override { order_.push_back(1); observed_timeout = timeout; return VRREC_STATUS_OK; }
    vrrec_status_t RequestStop() noexcept override { order_.push_back(3); return VRREC_STATUS_OK; }
    void Abort() noexcept override { order_.push_back(5); }
    VideoPipelineResult Join() noexcept override { order_.push_back(7); return VideoPipelineResult::Stopped; }
    VideoEncodingStatistics Statistics() const noexcept override { return {{30, 29, 1, 2}, 101, 1'000, 2'000}; }
    std::vector<int> &order_;
    std::chrono::milliseconds observed_timeout {0};
};

class FakeAudioSession final : public StereoAudioPipelineSessionPort {
public:
    explicit FakeAudioSession(std::vector<int> &order) : order_(order) {}
    vrrec_status_t Start(const StereoAudioCaptureSessionConfig &config, std::size_t frames) noexcept override { order_.push_back(2); observed_config = config; observed_frames = frames; return VRREC_STATUS_OK; }
    vrrec_status_t SetRouting(vrrec_audio_routing_t routing) noexcept override { order_.push_back(6); observed_routing = routing; return VRREC_STATUS_OK; }
    vrrec_status_t RequestStop() noexcept override { order_.push_back(4); return VRREC_STATUS_OK; }
    void Abort() noexcept override { order_.push_back(9); }
    StereoAudioEncodingWorkerResult Join() noexcept override { order_.push_back(8); return StereoAudioEncodingWorkerResult::Stopped; }
    StereoAudioPipelineStatistics Statistics() const noexcept override { return {48'000, 202}; }
    std::vector<int> &order_;
    StereoAudioCaptureSessionConfig observed_config {};
    std::size_t observed_frames = 0;
    vrrec_audio_routing_t observed_routing = VRREC_AUDIO_ROUTING_MIXED;
};

class FakeMuxSession final : public MediaMuxSessionPort {
public:
    explicit FakeMuxSession(std::vector<int> &order) noexcept
        : order_(order)
    {
    }

    vrrec_status_t Start(
        const FragmentedMp4StreamConfiguration &) noexcept override
    {
        order_.push_back(0);
        return VRREC_STATUS_OK;
    }
    void RequestAbort() noexcept override { ++request_abort_calls; }
    void Abort() noexcept override { ++abort_calls; }
    std::int64_t AudioVideoOffsetMicroseconds() const noexcept override { return -12'000; }
    std::vector<int> &order_;
    std::size_t request_abort_calls = 0;
    std::size_t abort_calls = 0;
};

class RecordingEvents final : public MediaEventSink {
public:
    void FirstVideoPacketMuxed() noexcept override {}
    void Stopped(std::uint64_t video, std::uint64_t audio) noexcept override { ++stopped_calls; video_packets = video; audio_packets = audio; }
    void Faulted(vrrec_status_t, const char *) noexcept override {}
    void AudioEndpointAvailabilityChanged(AudioEndpointRole, bool, std::uint64_t) noexcept override {}
    std::size_t stopped_calls = 0;
    std::uint64_t video_packets = 0;
    std::uint64_t audio_packets = 0;
};

void ComposesConfiguredPipelinesIntoOneRecordingLifecycle()
{
    std::vector<int> order;
    FakeVideoSession video(order);
    FakeAudioSession audio(order);
    FakeMuxSession mux(order);
    RecordingEvents events;
    MediaRecordingPipeline pipeline(
        video,
        std::chrono::milliseconds(80),
        audio,
        {"desktop-id", "mic-id", 987'654},
        1'024,
        mux,
        TestMp4Streams(),
        events);

    CHECK(pipeline.Start() == VRREC_STATUS_OK);
    CHECK(video.observed_timeout == std::chrono::milliseconds(80));
    CHECK(audio.observed_config.desktop_endpoint_id_utf8 == "desktop-id");
    CHECK(audio.observed_config.microphone_endpoint_id_utf8 == "mic-id");
    CHECK(audio.observed_config.session_start_qpc_100ns == 987'654);
    CHECK(audio.observed_frames == 1'024);
    CHECK(pipeline.UpdateAudioRouting(VRREC_AUDIO_ROUTING_MUTED) == VRREC_STATUS_OK);
    CHECK(audio.observed_routing == VRREC_AUDIO_ROUTING_MUTED);
    CHECK(pipeline.RequestStop() == VRREC_STATUS_OK);
    CHECK(pipeline.Join() == VRREC_STATUS_OK);
    CHECK(order == std::vector<int>({0, 1, 2, 6, 3, 4, 7, 8}));

    const auto statistics = pipeline.Statistics();
    CHECK(statistics.video.muxed_packet_count == 101);
    CHECK(statistics.video.scheduler.dropped_source_frame_count == 1);
    CHECK(statistics.audio.submitted_frame_count == 48'000);
    CHECK(statistics.audio.muxed_packet_count == 202);
    CHECK(statistics.audio_video_offset_microseconds == -12'000);
    CHECK(events.stopped_calls == 1);
    CHECK(events.video_packets == 101);
    CHECK(events.audio_packets == 202);
    CHECK(mux.abort_calls == 0);
}

}

int main()
{
    ComposesConfiguredPipelinesIntoOneRecordingLifecycle();
    return 0;
}
