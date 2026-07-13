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
    const std::lock_guard lock(operation_mutex_);
    if (terminal_failure_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (started_) {
        terminal_failure_.store(true);
        finalization_.Abort();
        return VRREC_STATUS_INVALID_STATE;
    }
    const auto status = coordinator_.Begin(configuration);
    if (status != VRREC_STATUS_OK) {
        terminal_failure_.store(true);
        finalization_.Abort();
        return status;
    }
    if (terminal_failure_.load()) {
        finalization_.Abort();
        return VRREC_STATUS_INVALID_STATE;
    }
    started_ = true;
    return VRREC_STATUS_OK;
}

Mp4MuxResult MediaMuxPipeline::Submit(
    const EncodedMediaPacket &packet) noexcept
{
    return SubmitBatch(
        packet.stream,
        std::span<const EncodedMediaPacket>(&packet, 1));
}

Mp4MuxResult MediaMuxPipeline::SubmitBatch(
    MediaStreamKind producer,
    std::span<const EncodedMediaPacket> packets) noexcept
{
    const std::lock_guard lock(operation_mutex_);
    if (terminal_failure_.load()) {
        return Mp4MuxResult::InvalidState;
    }
    if (!started_) {
        terminal_failure_.store(true);
        finalization_.Abort();
        return Mp4MuxResult::InvalidState;
    }

    const auto result = finalization_.SubmitBatch(producer, packets);
    if (result != Mp4MuxResult::Written) {
        terminal_failure_.store(true);
        finalization_.Abort();
        return result;
    }

    for (const auto &packet : packets) {
        if (terminal_failure_.load()) {
            return Mp4MuxResult::MuxFailed;
        }
        if (monitor_.Observe(packet) != VRREC_STATUS_OK) {
            terminal_failure_.store(true);
            finalization_.Abort();
            return Mp4MuxResult::MuxFailed;
        }
        if (terminal_failure_.load()) {
            return Mp4MuxResult::MuxFailed;
        }
    }
    if (terminal_failure_.load()) {
        return Mp4MuxResult::MuxFailed;
    }
    return Mp4MuxResult::Written;
}

vrrec_status_t MediaMuxPipeline::EncoderFinished(
    MediaStreamKind stream) noexcept
{
    const std::lock_guard lock(operation_mutex_);
    if (terminal_failure_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (!started_) {
        terminal_failure_.store(true);
        finalization_.Abort();
        return VRREC_STATUS_INVALID_STATE;
    }
    const auto status = finalization_.EncoderFinished(stream);
    if (status != VRREC_STATUS_OK) {
        terminal_failure_.store(true);
        finalization_.Abort();
        return status;
    }
    if (terminal_failure_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }
    return VRREC_STATUS_OK;
}

void MediaMuxPipeline::EncoderFailed(MediaStreamKind stream) noexcept
{
    terminal_failure_.store(true);
    finalization_.EncoderFailed(stream);
}

void MediaMuxPipeline::Abort() noexcept
{
    terminal_failure_.store(true);
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

#if defined(VRRECORDER_NATIVE_TESTING)
bool MediaMuxPipeline::IsMuxAbortRequestedForTesting() const noexcept
{
    return coordinator_.IsAbortRequestedForTesting();
}
#endif

}
