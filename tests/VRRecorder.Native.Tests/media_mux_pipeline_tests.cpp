#include "media_mux_pipeline.hpp"

#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <iostream>
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

class RecordingMuxer final : public FragmentedMp4Muxer {
public:
    vrrec_status_t WritePacket(
        const EncodedMediaPacket &packet) noexcept override
    {
        packets.push_back(packet);
        order.push_back(1);
        return VRREC_STATUS_OK;
    }

    vrrec_status_t EndFragment() noexcept override
    {
        order.push_back(2);
        return VRREC_STATUS_OK;
    }

    vrrec_status_t WriteTrailer() noexcept override
    {
        order.push_back(3);
        return VRREC_STATUS_OK;
    }

    vrrec_status_t FlushFile() noexcept override
    {
        order.push_back(4);
        return VRREC_STATUS_OK;
    }

    void Abort() noexcept override
    {
        order.push_back(5);
        ++abort_calls;
    }

    std::vector<EncodedMediaPacket> packets;
    std::vector<int> order;
    std::size_t abort_calls = 0;
};

class RecordingMediaEvents final : public MediaEventSink {
public:
    void FirstVideoPacketMuxed() noexcept override
    {
    }

    void Stopped(std::uint64_t, std::uint64_t) noexcept override
    {
    }

    void Faulted(vrrec_status_t, const char *) noexcept override
    {
    }

    void AudioEndpointAvailabilityChanged(
        AudioEndpointRole,
        bool,
        std::uint64_t) noexcept override
    {
    }

    void AvSyncDriftExceeded(
        std::uint64_t video_pts_microseconds,
        std::uint64_t audio_pts_microseconds,
        std::uint64_t absolute_drift_microseconds) noexcept override
    {
        ++drift_calls;
        video_pts = video_pts_microseconds;
        audio_pts = audio_pts_microseconds;
        absolute_drift = absolute_drift_microseconds;
    }

    std::size_t drift_calls = 0;
    std::uint64_t video_pts = 0;
    std::uint64_t audio_pts = 0;
    std::uint64_t absolute_drift = 0;
};

EncodedMediaPacket Packet(MediaStreamKind stream, std::int64_t pts)
{
    return {stream, pts, pts, 1, stream == MediaStreamKind::Video, 1};
}

void ConnectsMuxedPacketsToDriftEventsAndStatistics()
{
    RecordingMuxer muxer;
    RecordingMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);

    CHECK(pipeline.Submit(Packet(MediaStreamKind::Video, 0)) ==
          Mp4MuxResult::Written);
    CHECK(pipeline.Submit(Packet(MediaStreamKind::Audio, 100'000)) ==
          Mp4MuxResult::Written);
    CHECK(events.drift_calls == 1);
    CHECK(events.video_pts == 0);
    CHECK(events.audio_pts == 100'000);
    CHECK(events.absolute_drift == 100'000);
    const auto snapshot = pipeline.AvSyncStatistics();
    CHECK(snapshot.latest_absolute_drift_microseconds == 100'000);
    CHECK(snapshot.maximum_absolute_drift_microseconds == 100'000);
    CHECK(snapshot.threshold_event_count == 1);
}

void FinalizesOnlyAfterBothStreamsFinish()
{
    RecordingMuxer muxer;
    RecordingMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    CHECK(pipeline.Submit(Packet(MediaStreamKind::Video, 0)) ==
          Mp4MuxResult::Written);

    CHECK(pipeline.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(muxer.order == std::vector<int>({1}));
    CHECK(pipeline.EncoderFinished(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    CHECK(muxer.order == std::vector<int>({1, 2, 3, 4}));
}

void AbortStopsTheWholeMuxGraphWithoutATrailer()
{
    RecordingMuxer muxer;
    RecordingMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    pipeline.Abort();
    pipeline.Abort();
    CHECK(muxer.order == std::vector<int>({5}));
    CHECK(muxer.abort_calls == 1);
}

}

int main()
{
    ConnectsMuxedPacketsToDriftEventsAndStatistics();
    FinalizesOnlyAfterBothStreamsFinish();
    AbortStopsTheWholeMuxGraphWithoutATrailer();
    return 0;
}
