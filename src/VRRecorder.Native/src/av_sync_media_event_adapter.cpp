#include "av_sync_media_event_adapter.hpp"

#include <cstdint>

namespace vrrecorder::native {

AvSyncMediaEventAdapter::AvSyncMediaEventAdapter(
    MediaEventSink &events) noexcept
    : events_(events)
{
}

void AvSyncMediaEventAdapter::DriftThresholdExceeded(
    std::int64_t video_pts_microseconds,
    std::int64_t audio_pts_microseconds,
    std::uint64_t absolute_drift_microseconds) noexcept
{
    if (video_pts_microseconds < 0 || audio_pts_microseconds < 0) {
        return;
    }
    events_.AvSyncDriftExceeded(
        static_cast<std::uint64_t>(video_pts_microseconds),
        static_cast<std::uint64_t>(audio_pts_microseconds),
        absolute_drift_microseconds);
}

}
