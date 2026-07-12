#ifndef VRRECORDER_NATIVE_MEDIA_MUX_PIPELINE_HPP
#define VRRECORDER_NATIVE_MEDIA_MUX_PIPELINE_HPP

#include "av_sync_media_event_adapter.hpp"
#include "fragmented_mp4_mux_coordinator.hpp"
#include "shared_mux_finalization_session.hpp"

namespace vrrecorder::native {

class MediaMuxPipeline final {
public:
    MediaMuxPipeline(
        FragmentedMp4Muxer &muxer,
        MediaEventSink &events) noexcept;

    Mp4MuxResult Submit(const EncodedMediaPacket &packet) noexcept;
    vrrec_status_t EncoderFinished(MediaStreamKind stream) noexcept;
    void EncoderFailed(MediaStreamKind stream) noexcept;
    void Abort() noexcept;
    AvSyncSnapshot AvSyncStatistics() const noexcept;
    SharedMuxFinalizationSession &MuxSession() noexcept;

private:
    AvSyncMediaEventAdapter event_adapter_;
    AvSyncMonitor monitor_;
    FragmentedMp4MuxCoordinator coordinator_;
    SharedMuxFinalizationSession finalization_;
};

}

#endif
