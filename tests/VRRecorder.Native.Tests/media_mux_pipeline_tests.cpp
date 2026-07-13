#include "media_mux_pipeline.hpp"
#include "fragmented_mp4_test_support.hpp"

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <condition_variable>
#include <cstdlib>
#include <iostream>
#include <mutex>
#include <thread>
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
using namespace vrrecorder::native::test;

class RecordingMuxer final : public FragmentedMp4Muxer {
public:
    vrrec_status_t WriteHeader(
        const FragmentedMp4StreamConfiguration &) noexcept override
    {
        order.push_back(0);
        return VRREC_STATUS_OK;
    }

    vrrec_status_t WritePacket(
        const EncodedMediaPacket &packet) noexcept override
    {
        packets.push_back(packet);
        order.push_back(1);
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

class RecordingMediaEvents : public MediaEventSink {
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

class AbortOnDriftMediaEvents final : public RecordingMediaEvents {
public:
    void AvSyncDriftExceeded(
        std::uint64_t,
        std::uint64_t,
        std::uint64_t) noexcept override
    {
        ++drift_calls;
        CHECK(pipeline != nullptr);
        const auto snapshot = pipeline->AvSyncStatistics();
        snapshot_read = snapshot.threshold_event_count == 1;
        pipeline->Abort();
        abort_returned = true;
    }

    MediaMuxPipeline *pipeline = nullptr;
    bool abort_returned = false;
    bool snapshot_read = false;
};

EncodedMediaPacket Packet(MediaStreamKind stream, std::int64_t pts)
{
    return {
        stream,
        pts,
        pts,
        1,
        stream == MediaStreamKind::Video,
        std::vector<std::byte>(1, std::byte{0x01}),
    };
}

void ConnectsMuxedPacketsToDriftEventsAndStatistics()
{
    RecordingMuxer muxer;
    RecordingMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    CHECK(pipeline.Start(TestMp4Streams()) == VRREC_STATUS_OK);

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
    CHECK(pipeline.AudioVideoOffsetMicroseconds() == 100'000);
}

void MuxesNegativeAacPrimingButStartsDriftObservationAtPresentationZero()
{
    RecordingMuxer muxer;
    RecordingMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    CHECK(pipeline.Start(TestMp4Streams()) == VRREC_STATUS_OK);

    CHECK(pipeline.Submit(Packet(MediaStreamKind::Audio, -21'333)) ==
        Mp4MuxResult::Written);
    CHECK(pipeline.Submit(Packet(MediaStreamKind::Video, 0)) ==
        Mp4MuxResult::Written);
    CHECK(muxer.packets.size() == 2);
    CHECK(muxer.packets[0].pts_microseconds == -21'333);
    CHECK(muxer.abort_calls == 0);
    CHECK(!pipeline.AvSyncStatistics().has_both_streams);

    CHECK(pipeline.Submit(Packet(MediaStreamKind::Audio, 0)) ==
        Mp4MuxResult::Written);
    const auto snapshot = pipeline.AvSyncStatistics();
    CHECK(snapshot.has_both_streams);
    CHECK(snapshot.latest_absolute_drift_microseconds == 0);
    CHECK(snapshot.latest_audio_video_offset_microseconds == 0);
    CHECK(events.drift_calls == 0);
    CHECK(muxer.abort_calls == 0);
}

void DriftCallbackCanAbortTheMuxPipelineWithoutDeadlocking()
{
    RecordingMuxer muxer;
    AbortOnDriftMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    events.pipeline = &pipeline;
    CHECK(pipeline.Start(TestMp4Streams()) == VRREC_STATUS_OK);
    CHECK(pipeline.Submit(Packet(MediaStreamKind::Video, 0)) ==
        Mp4MuxResult::Written);

    std::mutex watchdog_mutex;
    std::condition_variable watchdog_changed;
    bool submit_completed = false;
    std::thread watchdog([&] {
        std::unique_lock lock(watchdog_mutex);
        if (!watchdog_changed.wait_for(
                lock,
                std::chrono::seconds(2),
                [&] { return submit_completed; })) {
            std::cerr << __func__
                      << " timed out waiting for reentrant Abort\n";
            std::abort();
        }
    });

    const auto result =
        pipeline.Submit(Packet(MediaStreamKind::Audio, 100'000));
    {
        const std::lock_guard lock(watchdog_mutex);
        submit_completed = true;
    }
    watchdog_changed.notify_all();
    watchdog.join();

    CHECK(result == Mp4MuxResult::Written);
    CHECK(events.drift_calls == 1);
    CHECK(events.abort_returned);
    CHECK(events.snapshot_read);
    CHECK(muxer.abort_calls == 1);
    CHECK(pipeline.Submit(Packet(MediaStreamKind::Video, 1)) ==
        Mp4MuxResult::InvalidState);
}

void FinalizesOnlyAfterBothStreamsFinish()
{
    RecordingMuxer muxer;
    RecordingMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    CHECK(pipeline.Start(TestMp4Streams()) == VRREC_STATUS_OK);
    CHECK(pipeline.Submit(Packet(MediaStreamKind::Video, 0)) ==
          Mp4MuxResult::Written);

    CHECK(pipeline.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(muxer.order == std::vector<int>({0, 1}));
    CHECK(pipeline.EncoderFinished(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    CHECK(muxer.order == std::vector<int>({0, 1, 3, 4}));
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
    MuxesNegativeAacPrimingButStartsDriftObservationAtPresentationZero();
    DriftCallbackCanAbortTheMuxPipelineWithoutDeadlocking();
    FinalizesOnlyAfterBothStreamsFinish();
    AbortStopsTheWholeMuxGraphWithoutATrailer();
    return 0;
}
