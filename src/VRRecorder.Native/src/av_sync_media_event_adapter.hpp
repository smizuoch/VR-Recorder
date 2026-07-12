#ifndef VRRECORDER_NATIVE_AV_SYNC_MEDIA_EVENT_ADAPTER_HPP
#define VRRECORDER_NATIVE_AV_SYNC_MEDIA_EVENT_ADAPTER_HPP

#include "av_sync_monitor.hpp"
#include "media_backend.hpp"

namespace vrrecorder::native {

class AvSyncMediaEventAdapter final : public AvSyncEventSink {
public:
    explicit AvSyncMediaEventAdapter(MediaEventSink &events) noexcept;

    void DriftThresholdExceeded(
        std::int64_t video_pts_microseconds,
        std::int64_t audio_pts_microseconds,
        std::uint64_t absolute_drift_microseconds) noexcept override;

private:
    MediaEventSink &events_;
};

}

#endif
