#include "av_sync_monitor.hpp"

#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <limits>
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

struct DriftEvent final {
    std::int64_t video_pts;
    std::int64_t audio_pts;
    std::uint64_t absolute_drift;
};

class RecordingDriftEvents final : public AvSyncEventSink {
public:
    void DriftThresholdExceeded(
        std::int64_t video_pts_microseconds,
        std::int64_t audio_pts_microseconds,
        std::uint64_t absolute_drift_microseconds) noexcept override
    {
        events.push_back({
            video_pts_microseconds,
            audio_pts_microseconds,
            absolute_drift_microseconds,
        });
    }

    std::vector<DriftEvent> events;
};

EncodedMediaPacket Packet(MediaStreamKind stream, std::int64_t pts)
{
    return {
        stream,
        pts,
        pts,
        1,
        false,
        std::vector<std::byte>(1, std::byte{0x01}),
    };
}

void WaitsForBothStreamsAndTreatsExactlyEightyMillisecondsAsHealthy()
{
    RecordingDriftEvents events;
    AvSyncMonitor monitor(events);

    CHECK(monitor.Observe(Packet(MediaStreamKind::Video, 100'000)) ==
          VRREC_STATUS_OK);
    CHECK(events.events.empty());
    CHECK(monitor.Observe(Packet(MediaStreamKind::Audio, 20'000)) ==
          VRREC_STATUS_OK);
    CHECK(events.events.empty());
    const auto snapshot = monitor.Snapshot();
    CHECK(snapshot.latest_absolute_drift_microseconds == 80'000);
    CHECK(snapshot.maximum_absolute_drift_microseconds == 80'000);
    CHECK(snapshot.latest_audio_video_offset_microseconds == -80'000);
}

void EmitsOncePerExcursionAndRearmsAfterRecovery()
{
    RecordingDriftEvents events;
    AvSyncMonitor monitor(events);
    CHECK(monitor.Observe(Packet(MediaStreamKind::Audio, 0)) ==
          VRREC_STATUS_OK);
    CHECK(monitor.Observe(Packet(MediaStreamKind::Video, 80'001)) ==
          VRREC_STATUS_OK);
    CHECK(events.events.size() == 1);
    CHECK(events.events[0].absolute_drift == 80'001);
    CHECK(monitor.Observe(Packet(MediaStreamKind::Video, 90'000)) ==
          VRREC_STATUS_OK);
    CHECK(events.events.size() == 1);
    CHECK(monitor.Observe(Packet(MediaStreamKind::Audio, 20'000)) ==
          VRREC_STATUS_OK);
    CHECK(events.events.size() == 1);
    CHECK(monitor.Observe(Packet(MediaStreamKind::Video, 110'001)) ==
          VRREC_STATUS_OK);
    CHECK(events.events.size() == 2);

    const auto snapshot = monitor.Snapshot();
    CHECK(snapshot.threshold_event_count == 2);
    CHECK(snapshot.latest_absolute_drift_microseconds == 90'001);
    CHECK(snapshot.maximum_absolute_drift_microseconds == 90'001);
    CHECK(snapshot.latest_audio_video_offset_microseconds == -90'001);
}

void IgnoresNegativeAacPrimingForDiagnosticsWithoutFailingMuxObservation()
{
    RecordingDriftEvents events;
    AvSyncMonitor monitor(events);

    CHECK(monitor.Observe(Packet(MediaStreamKind::Audio, -21'333)) ==
        VRREC_STATUS_OK);
    CHECK(monitor.Observe(Packet(MediaStreamKind::Video, 0)) ==
        VRREC_STATUS_OK);
    auto snapshot = monitor.Snapshot();
    CHECK(!snapshot.has_both_streams);
    CHECK(snapshot.latest_absolute_drift_microseconds == 0);
    CHECK(events.events.empty());

    CHECK(monitor.Observe(Packet(MediaStreamKind::Audio, 0)) ==
        VRREC_STATUS_OK);
    snapshot = monitor.Snapshot();
    CHECK(snapshot.has_both_streams);
    CHECK(snapshot.latest_absolute_drift_microseconds == 0);
    CHECK(snapshot.latest_audio_video_offset_microseconds == 0);
}

void RejectsInvalidStreamMissingTimestampOrNegativeVideoWithoutChangingStatistics()
{
    RecordingDriftEvents events;
    AvSyncMonitor monitor(events);
    CHECK(monitor.Observe(Packet(MediaStreamKind::Video, -1)) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    auto invalid = Packet(MediaStreamKind::Video, 0);
    invalid.stream = static_cast<MediaStreamKind>(99);
    CHECK(monitor.Observe(invalid) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(monitor.Observe(Packet(
        MediaStreamKind::Audio,
        std::numeric_limits<std::int64_t>::min())) ==
        VRREC_STATUS_INVALID_ARGUMENT);
    const auto snapshot = monitor.Snapshot();
    CHECK(!snapshot.has_both_streams);
    CHECK(snapshot.threshold_event_count == 0);
}

}

int main()
{
    WaitsForBothStreamsAndTreatsExactlyEightyMillisecondsAsHealthy();
    EmitsOncePerExcursionAndRearmsAfterRecovery();
    IgnoresNegativeAacPrimingForDiagnosticsWithoutFailingMuxObservation();
    RejectsInvalidStreamMissingTimestampOrNegativeVideoWithoutChangingStatistics();
    return 0;
}
