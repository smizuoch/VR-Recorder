#include "media_mux_pipeline.hpp"

namespace vrrecorder::native {

MediaMuxPipeline::MediaMuxPipeline(
    FragmentedMp4Muxer &muxer,
    MediaEventSink &events) noexcept
    : event_adapter_(events),
      monitor_(event_adapter_),
      coordinator_(muxer, &monitor_),
      finalization_(coordinator_)
{
}

Mp4MuxResult MediaMuxPipeline::Submit(
    const EncodedMediaPacket &packet) noexcept
{
    return finalization_.Submit(packet);
}

vrrec_status_t MediaMuxPipeline::EncoderFinished(
    MediaStreamKind stream) noexcept
{
    return finalization_.EncoderFinished(stream);
}

void MediaMuxPipeline::EncoderFailed(MediaStreamKind stream) noexcept
{
    finalization_.EncoderFailed(stream);
}

void MediaMuxPipeline::Abort() noexcept
{
    finalization_.Abort();
}

AvSyncSnapshot MediaMuxPipeline::AvSyncStatistics() const noexcept
{
    return monitor_.Snapshot();
}

SharedMuxFinalizationSession &MediaMuxPipeline::MuxSession() noexcept
{
    return finalization_;
}

}
