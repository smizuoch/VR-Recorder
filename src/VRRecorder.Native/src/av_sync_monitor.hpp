#ifndef VRRECORDER_NATIVE_AV_SYNC_MONITOR_HPP
#define VRRECORDER_NATIVE_AV_SYNC_MONITOR_HPP

#include <cstdint>
#include <mutex>

#include "fragmented_mp4_mux_coordinator.hpp"

namespace vrrecorder::native {

class AvSyncEventSink {
public:
    virtual ~AvSyncEventSink() = default;

    virtual void DriftThresholdExceeded(
        std::int64_t video_pts_microseconds,
        std::int64_t audio_pts_microseconds,
        std::uint64_t absolute_drift_microseconds) noexcept = 0;
};

struct AvSyncSnapshot final {
    std::uint64_t latest_absolute_drift_microseconds = 0;
    std::uint64_t maximum_absolute_drift_microseconds = 0;
    std::uint64_t threshold_event_count = 0;
    bool has_both_streams = false;
};

class AvSyncMonitor final : public EncodedMediaPacketObserver {
public:
    explicit AvSyncMonitor(AvSyncEventSink &events) noexcept;

    vrrec_status_t Observe(
        const EncodedMediaPacket &packet) noexcept override;
    AvSyncSnapshot Snapshot() const noexcept;

private:
    AvSyncEventSink &events_;
    mutable std::mutex mutex_;
    std::int64_t video_pts_microseconds_ = 0;
    std::int64_t audio_pts_microseconds_ = 0;
    std::uint64_t latest_absolute_drift_microseconds_ = 0;
    std::uint64_t maximum_absolute_drift_microseconds_ = 0;
    std::uint64_t threshold_event_count_ = 0;
    bool has_video_ = false;
    bool has_audio_ = false;
    bool threshold_active_ = false;
};

}

#endif
