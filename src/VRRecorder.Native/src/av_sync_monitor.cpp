#include "av_sync_monitor.hpp"

#include <algorithm>
#include <cstdint>

namespace vrrecorder::native {

AvSyncMonitor::AvSyncMonitor(AvSyncEventSink &events) noexcept
    : events_(events)
{
}

vrrec_status_t AvSyncMonitor::Observe(
    const EncodedMediaPacket &packet) noexcept
{
    if ((packet.stream != MediaStreamKind::Video &&
         packet.stream != MediaStreamKind::Audio) ||
        packet.pts_microseconds < 0) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    const std::lock_guard lock(mutex_);
    if (packet.stream == MediaStreamKind::Video) {
        video_pts_microseconds_ = packet.pts_microseconds;
        has_video_ = true;
    } else {
        audio_pts_microseconds_ = packet.pts_microseconds;
        has_audio_ = true;
    }
    if (!has_video_ || !has_audio_) {
        return VRREC_STATUS_OK;
    }

    const auto absolute_drift = video_pts_microseconds_ >=
            audio_pts_microseconds_
        ? static_cast<std::uint64_t>(
              video_pts_microseconds_ - audio_pts_microseconds_)
        : static_cast<std::uint64_t>(
              audio_pts_microseconds_ - video_pts_microseconds_);
    latest_absolute_drift_microseconds_ = absolute_drift;
    maximum_absolute_drift_microseconds_ = std::max(
        maximum_absolute_drift_microseconds_,
        absolute_drift);

    constexpr std::uint64_t threshold_microseconds = 80'000;
    if (absolute_drift <= threshold_microseconds) {
        threshold_active_ = false;
        return VRREC_STATUS_OK;
    }
    if (!threshold_active_) {
        threshold_active_ = true;
        ++threshold_event_count_;
        events_.DriftThresholdExceeded(
            video_pts_microseconds_,
            audio_pts_microseconds_,
            absolute_drift);
    }
    return VRREC_STATUS_OK;
}

AvSyncSnapshot AvSyncMonitor::Snapshot() const noexcept
{
    const std::lock_guard lock(mutex_);
    return {
        latest_absolute_drift_microseconds_,
        maximum_absolute_drift_microseconds_,
        threshold_event_count_,
        has_video_ && has_audio_,
    };
}

}
