#ifndef VRRECORDER_NATIVE_MEDIA_MUX_PIPELINE_HPP
#define VRRECORDER_NATIVE_MEDIA_MUX_PIPELINE_HPP

#include <atomic>
#include <mutex>
#include <span>

#include "av_sync_media_event_adapter.hpp"
#include "encoded_media_packet_submission_port.hpp"
#include "fragmented_mp4_mux_coordinator.hpp"
#include "media_recording_session.hpp"
#include "shared_mux_finalization_session.hpp"

namespace vrrecorder::native {

class MediaMuxPipeline final
    : public MediaMuxSessionPort,
      public EncodedMediaPacketSubmissionPort {
public:
    MediaMuxPipeline(
        FragmentedMp4Muxer &muxer,
        MediaEventSink &events) noexcept;

    vrrec_status_t Start(
        const FragmentedMp4StreamConfiguration &configuration)
        noexcept override;
    Mp4MuxResult Submit(const EncodedMediaPacket &packet) noexcept;
    Mp4MuxResult SubmitBatch(
        MediaStreamKind producer,
        std::span<const EncodedMediaPacket> packets) noexcept override;
    vrrec_status_t EncoderFinished(
        MediaStreamKind stream) noexcept override;
    void EncoderFailed(MediaStreamKind stream) noexcept override;
    void Abort() noexcept override;
    AvSyncSnapshot AvSyncStatistics() const noexcept;
    std::int64_t AudioVideoOffsetMicroseconds() const noexcept override;
#if defined(VRRECORDER_NATIVE_TESTING)
    bool IsMuxAbortRequestedForTesting() const noexcept;
#endif
private:
    std::recursive_mutex operation_mutex_;
    std::atomic_bool terminal_failure_ = false;
    bool started_ = false;
    AvSyncMediaEventAdapter event_adapter_;
    AvSyncMonitor monitor_;
    FragmentedMp4MuxCoordinator coordinator_;
    SharedMuxFinalizationSession finalization_;
};

}

#endif
