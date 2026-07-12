#include "media_recording_session.hpp"

#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <vector>

namespace {

#define CHECK(condition) do { if (!(condition)) { std::cerr << "check failed at " << __FILE__ << ':' << __LINE__ << ": " #condition << '\n'; std::abort(); } } while (false)

using namespace vrrecorder::native;

class FakeStreamPipeline final : public MediaStreamPipelinePort {
public:
    FakeStreamPipeline(std::vector<int> &order, int base) noexcept : order_(order), base_(base) {}
    vrrec_status_t Start() noexcept override { order_.push_back(base_ + 1); return start_status; }
    vrrec_status_t RequestStop() noexcept override { order_.push_back(base_ + 2); return stop_status; }
    void Abort() noexcept override { order_.push_back(base_ + 3); ++abort_calls; }
    vrrec_status_t Join() noexcept override { order_.push_back(base_ + 4); ++join_calls; return join_status; }
    std::uint64_t MuxedPacketCount() const noexcept override { return muxed_packet_count; }
    std::vector<int> &order_;
    int base_;
    vrrec_status_t start_status = VRREC_STATUS_OK;
    vrrec_status_t stop_status = VRREC_STATUS_OK;
    vrrec_status_t join_status = VRREC_STATUS_OK;
    std::uint64_t muxed_packet_count = 0;
    std::size_t abort_calls = 0;
    std::size_t join_calls = 0;
};

class FakeMuxSession final : public MediaMuxSessionPort {
public:
    explicit FakeMuxSession(std::vector<int> &order) noexcept : order_(order) {}
    void Abort() noexcept override { order_.push_back(23); ++abort_calls; }
    std::vector<int> &order_;
    std::size_t abort_calls = 0;
};

class RecordingEvents final : public MediaEventSink {
public:
    void FirstVideoPacketMuxed() noexcept override {}
    void Stopped(std::uint64_t video, std::uint64_t audio) noexcept override { ++stopped_calls; video_packets = video; audio_packets = audio; }
    void Faulted(vrrec_status_t status, const char *) noexcept override { ++fault_calls; fault_status = status; }
    void AudioEndpointAvailabilityChanged(AudioEndpointRole, bool, std::uint64_t) noexcept override {}
    std::size_t stopped_calls = 0;
    std::size_t fault_calls = 0;
    std::uint64_t video_packets = 0;
    std::uint64_t audio_packets = 0;
    vrrec_status_t fault_status = VRREC_STATUS_OK;
};

void GracefulStopOrdersVideoBeforeAudioAndPublishesFinalCounts()
{
    std::vector<int> order;
    FakeStreamPipeline video(order, 0), audio(order, 10);
    FakeMuxSession mux(order);
    RecordingEvents events;
    video.muxed_packet_count = 91;
    audio.muxed_packet_count = 47;
    MediaRecordingSession session(video, audio, mux, events);
    CHECK(session.Start() == VRREC_STATUS_OK);
    CHECK(order == std::vector<int>({1, 11}));
    CHECK(session.RequestStop() == VRREC_STATUS_OK);
    CHECK(session.RequestStop() == VRREC_STATUS_OK);
    CHECK(order == std::vector<int>({1, 11, 2, 12}));
    CHECK(session.Join() == VRREC_STATUS_OK);
    CHECK(order == std::vector<int>({1, 11, 2, 12, 4, 14}));
    CHECK(events.stopped_calls == 1);
    CHECK(events.video_packets == 91);
    CHECK(events.audio_packets == 47);
    CHECK(events.fault_calls == 0);
    CHECK(mux.abort_calls == 0);
}

void AudioStartFailureRollsBackVideoAndMux()
{
    std::vector<int> order;
    FakeStreamPipeline video(order, 0), audio(order, 10);
    FakeMuxSession mux(order);
    RecordingEvents events;
    audio.start_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    MediaRecordingSession session(video, audio, mux, events);
    CHECK(session.Start() == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(order == std::vector<int>({1, 11, 3, 4, 23}));
    CHECK(video.abort_calls == 1);
    CHECK(video.join_calls == 1);
    CHECK(events.stopped_calls == 0);
}

void StreamFailureAbortsPeerAndMuxWithoutStoppedEvent()
{
    std::vector<int> order;
    FakeStreamPipeline video(order, 0), audio(order, 10);
    FakeMuxSession mux(order);
    RecordingEvents events;
    video.join_status = VRREC_STATUS_INTERNAL_ERROR;
    MediaRecordingSession session(video, audio, mux, events);
    CHECK(session.Start() == VRREC_STATUS_OK);
    CHECK(session.RequestStop() == VRREC_STATUS_OK);
    CHECK(session.Join() == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(audio.abort_calls == 1);
    CHECK(audio.join_calls == 1);
    CHECK(mux.abort_calls == 1);
    CHECK(events.stopped_calls == 0);
    CHECK(events.fault_calls == 1);
    CHECK(events.fault_status == VRREC_STATUS_INTERNAL_ERROR);
}

}

int main()
{
    GracefulStopOrdersVideoBeforeAudioAndPublishesFinalCounts();
    AudioStartFailureRollsBackVideoAndMux();
    StreamFailureAbortsPeerAndMuxWithoutStoppedEvent();
    return 0;
}
