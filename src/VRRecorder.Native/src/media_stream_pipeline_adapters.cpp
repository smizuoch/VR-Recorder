#include "media_stream_pipeline_adapters.hpp"

#include <utility>

namespace vrrecorder::native {
namespace {

vrrec_status_t MapVideoResult(VideoPipelineResult result) noexcept
{
    switch (result) {
    case VideoPipelineResult::Stopped:
        return VRREC_STATUS_OK;
    case VideoPipelineResult::SenderLost:
    case VideoPipelineResult::AdapterChanged:
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    case VideoPipelineResult::Aborted:
    case VideoPipelineResult::InvalidState:
        return VRREC_STATUS_INVALID_STATE;
    case VideoPipelineResult::CaptureFailed:
    case VideoPipelineResult::EncoderFailed:
    case VideoPipelineResult::Failed:
        return VRREC_STATUS_INTERNAL_ERROR;
    }
    return VRREC_STATUS_INTERNAL_ERROR;
}

vrrec_status_t MapAudioResult(
    StereoAudioEncodingWorkerResult result) noexcept
{
    switch (result) {
    case StereoAudioEncodingWorkerResult::Stopped:
        return VRREC_STATUS_OK;
    case StereoAudioEncodingWorkerResult::Aborted:
    case StereoAudioEncodingWorkerResult::InvalidState:
        return VRREC_STATUS_INVALID_STATE;
    case StereoAudioEncodingWorkerResult::CaptureFailed:
    case StereoAudioEncodingWorkerResult::EncoderFailed:
    case StereoAudioEncodingWorkerResult::MuxFailed:
    case StereoAudioEncodingWorkerResult::Failed:
        return VRREC_STATUS_INTERNAL_ERROR;
    }
    return VRREC_STATUS_INTERNAL_ERROR;
}

}

VideoMediaStreamPipelineAdapter::VideoMediaStreamPipelineAdapter(
    VideoPipelineSessionPort &session,
    std::chrono::milliseconds poll_timeout) noexcept
    : session_(session), poll_timeout_(poll_timeout)
{
}

vrrec_status_t VideoMediaStreamPipelineAdapter::Start() noexcept
{
    return session_.Start(poll_timeout_);
}

vrrec_status_t VideoMediaStreamPipelineAdapter::RequestStop() noexcept
{
    return session_.RequestStop();
}

void VideoMediaStreamPipelineAdapter::Abort() noexcept
{
    session_.Abort();
}

void VideoMediaStreamPipelineAdapter::RequestAbort() noexcept
{
    session_.RequestAbort();
}

void VideoMediaStreamPipelineAdapter::JoinAfterAbort() noexcept
{
    session_.JoinAfterAbort();
}

vrrec_status_t VideoMediaStreamPipelineAdapter::Join() noexcept
{
    return MapVideoResult(session_.Join());
}

std::uint64_t VideoMediaStreamPipelineAdapter::MuxedPacketCount()
    const noexcept
{
    return session_.Statistics().muxed_packet_count;
}

AudioMediaStreamPipelineAdapter::AudioMediaStreamPipelineAdapter(
    StereoAudioPipelineSessionPort &session,
    StereoAudioCaptureSessionConfig config,
    std::size_t encoding_frame_count_48k)
    : session_(session),
      config_(std::move(config)),
      encoding_frame_count_48k_(encoding_frame_count_48k)
{
}

vrrec_status_t AudioMediaStreamPipelineAdapter::Start() noexcept
{
    return session_.Start(config_, encoding_frame_count_48k_);
}

vrrec_status_t AudioMediaStreamPipelineAdapter::RequestStop() noexcept
{
    return session_.RequestStop();
}

void AudioMediaStreamPipelineAdapter::Abort() noexcept
{
    session_.Abort();
}

void AudioMediaStreamPipelineAdapter::RequestAbort() noexcept
{
    session_.RequestAbort();
}

void AudioMediaStreamPipelineAdapter::JoinAfterAbort() noexcept
{
    session_.JoinAfterAbort();
}

vrrec_status_t AudioMediaStreamPipelineAdapter::Join() noexcept
{
    return MapAudioResult(session_.Join());
}

std::uint64_t AudioMediaStreamPipelineAdapter::MuxedPacketCount()
    const noexcept
{
    return session_.Statistics().muxed_packet_count;
}

}
