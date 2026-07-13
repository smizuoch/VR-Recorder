#include "media_mux_pipeline.hpp"

namespace vrrecorder::native {

MediaMuxPipeline::MediaMuxPipeline(
    FragmentedMp4Muxer &muxer,
    MediaEventSink &events) noexcept
    : event_adapter_(events),
      monitor_(event_adapter_),
      coordinator_(muxer),
      finalization_(coordinator_)
{
}

vrrec_status_t MediaMuxPipeline::Start(
    const FragmentedMp4StreamConfiguration &configuration) noexcept
{
    return coordinator_.Begin(configuration);
}

Mp4MuxResult MediaMuxPipeline::Submit(
    const EncodedMediaPacket &packet) noexcept
{
    const std::lock_guard lock(submit_mutex_);
    const auto result = finalization_.Submit(packet);
    if (result != Mp4MuxResult::Written) {
        return result;
    }

    if (monitor_.Observe(packet) != VRREC_STATUS_OK) {
        finalization_.Abort();
        return Mp4MuxResult::MuxFailed;
    }
    return Mp4MuxResult::Written;
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

std::int64_t MediaMuxPipeline::AudioVideoOffsetMicroseconds() const noexcept
{
    return monitor_.Snapshot().latest_audio_video_offset_microseconds;
}

SharedMuxFinalizationSession &MediaMuxPipeline::MuxSession() noexcept
{
    return finalization_;
}

}
