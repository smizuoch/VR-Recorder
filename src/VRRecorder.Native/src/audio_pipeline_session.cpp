#include "audio_pipeline_session.hpp"

namespace vrrecorder::native {

StereoAudioPipelineSession::StereoAudioPipelineSession(
    StereoAudioCaptureSessionPort &capture,
    StereoAudioEncoderSink &encoder) noexcept
    : capture_(capture),
      encoding_(capture, encoder)
{
}

StereoAudioPipelineSession::~StereoAudioPipelineSession()
{
    Abort();
}

vrrec_status_t StereoAudioPipelineSession::Start(
    const StereoAudioCaptureSessionConfig &config,
    std::size_t encoding_frame_count_48k) noexcept
{
    if (encoding_frame_count_48k == 0) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    if (start_attempted_.exchange(true) || aborted_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }

    const auto capture_status = capture_.Start(config);
    if (capture_status != VRREC_STATUS_OK) {
        return capture_status;
    }

    capture_started_.store(true);
    if (aborted_.load()) {
        if (capture_started_.exchange(false)) {
            capture_.Abort();
        }
        return VRREC_STATUS_INVALID_STATE;
    }

    const auto encoding_status = encoding_.Start(
        encoding_frame_count_48k);
    if (encoding_status != VRREC_STATUS_OK) {
        if (capture_started_.exchange(false)) {
            capture_.Abort();
        }
        return encoding_status;
    }

    encoding_started_.store(true);
    active_.store(true);
    if (aborted_.load()) {
        active_.store(false);
        if (encoding_started_.exchange(false)) {
            encoding_.Abort();
        }
        capture_started_.store(false);
        return VRREC_STATUS_INVALID_STATE;
    }
    return VRREC_STATUS_OK;
}

vrrec_status_t StereoAudioPipelineSession::SetRouting(
    vrrec_audio_routing_t routing) noexcept
{
    if (!active_.load() || aborted_.load() || encoding_.IsFinished()) {
        active_.store(false);
        return VRREC_STATUS_INVALID_STATE;
    }

    return capture_.SetRouting(routing);
}

vrrec_status_t StereoAudioPipelineSession::RequestStop() noexcept
{
    if (!active_.load() || aborted_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }

    const auto status = encoding_.RequestStop();
    if (status != VRREC_STATUS_OK) {
        Abort();
    }

    return status;
}

void StereoAudioPipelineSession::Abort() noexcept
{
    RequestAbort();
    JoinAfterAbort();
}

void StereoAudioPipelineSession::RequestAbort() noexcept
{
    if (aborted_.exchange(true)) {
        return;
    }

    active_.store(false);
    if (encoding_started_.load()) {
        encoding_.RequestAbort();
    }
}

void StereoAudioPipelineSession::JoinAfterAbort() noexcept
{
    const std::lock_guard cleanup_lock(abort_join_mutex_);
    if (!aborted_.load()) {
        return;
    }

    if (encoding_started_.exchange(false)) {
        encoding_.RequestAbort();
        encoding_.JoinAfterAbort();
        capture_started_.store(false);
    } else if (capture_started_.exchange(false)) {
        capture_.Abort();
    }
}

StereoAudioEncodingWorkerResult StereoAudioPipelineSession::Join() noexcept
{
    if (!encoding_started_.load()) {
        return StereoAudioEncodingWorkerResult::InvalidState;
    }

    const auto result = encoding_.Join();
    active_.store(false);
    return result;
}

StereoAudioPipelineStatistics StereoAudioPipelineSession::Statistics()
    const noexcept
{
    return {
        encoding_.SubmittedFrameCount(),
        encoding_.MuxedPacketCount(),
    };
}

}
