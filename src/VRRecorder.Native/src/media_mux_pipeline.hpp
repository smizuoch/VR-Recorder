#ifndef VRRECORDER_NATIVE_MEDIA_MUX_PIPELINE_HPP
#define VRRECORDER_NATIVE_MEDIA_MUX_PIPELINE_HPP

#include <mutex>

#include "av_sync_media_event_adapter.hpp"
#include "fragmented_mp4_mux_coordinator.hpp"
#include "media_recording_session.hpp"
#include "shared_mux_finalization_session.hpp"

namespace vrrecorder::native {

class MediaMuxPipeline final : public MediaMuxSessionPort {
public:
    MediaMuxPipeline(
        FragmentedMp4Muxer &muxer,
        MediaEventSink &events) noexcept;

    vrrec_status_t Start(
        const FragmentedMp4StreamConfiguration &configuration)
        noexcept override;
    Mp4MuxResult Submit(const EncodedMediaPacket &packet) noexcept;
    vrrec_status_t EncoderFinished(MediaStreamKind stream) noexcept;
    void EncoderFailed(MediaStreamKind stream) noexcept;
    void Abort() noexcept override;
    AvSyncSnapshot AvSyncStatistics() const noexcept;
    std::int64_t AudioVideoOffsetMicroseconds() const noexcept override;
    SharedMuxFinalizationSession &MuxSession() noexcept;

private:
    std::mutex submit_mutex_;
    AvSyncMediaEventAdapter event_adapter_;
    AvSyncMonitor monitor_;
    FragmentedMp4MuxCoordinator coordinator_;
    SharedMuxFinalizationSession finalization_;
};

}

#endif
