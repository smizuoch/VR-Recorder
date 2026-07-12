#include "media_recording_session.hpp"

namespace vrrecorder::native {

MediaRecordingSession::MediaRecordingSession(
    MediaStreamPipelinePort &video,
    MediaStreamPipelinePort &audio,
    MediaMuxSessionPort &mux,
    MediaEventSink &events) noexcept
    : video_(video), audio_(audio), mux_(mux), events_(events)
{
}

MediaRecordingSession::~MediaRecordingSession()
{
    Abort();
}

vrrec_status_t MediaRecordingSession::Start() noexcept
{
    if (start_attempted_ || terminal_) {
        return VRREC_STATUS_INVALID_STATE;
    }
    start_attempted_ = true;
    const auto video_status = video_.Start();
    if (video_status != VRREC_STATUS_OK) {
        mux_.Abort();
        terminal_ = true;
        return video_status;
    }
    video_started_ = true;
    const auto audio_status = audio_.Start();
    if (audio_status != VRREC_STATUS_OK) {
        video_.Abort();
        video_.Join();
        mux_.Abort();
        video_started_ = false;
        terminal_ = true;
        return audio_status;
    }
    audio_started_ = true;
    return VRREC_STATUS_OK;
}

vrrec_status_t MediaRecordingSession::RequestStop() noexcept
{
    if (!video_started_ || !audio_started_ || terminal_) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (stop_requested_) {
        return VRREC_STATUS_OK;
    }
    const auto video_status = video_.RequestStop();
    if (video_status != VRREC_STATUS_OK) {
        Abort();
        return video_status;
    }
    const auto audio_status = audio_.RequestStop();
    if (audio_status != VRREC_STATUS_OK) {
        Abort();
        return audio_status;
    }
    stop_requested_ = true;
    return VRREC_STATUS_OK;
}

void MediaRecordingSession::Abort() noexcept
{
    if (terminal_) {
        return;
    }
    terminal_ = true;
    if (video_started_) {
        video_.Abort();
    }
    if (audio_started_) {
        audio_.Abort();
    }
    mux_.Abort();
}

vrrec_status_t MediaRecordingSession::Join() noexcept
{
    if (!video_started_ || !audio_started_ || terminal_ || !stop_requested_) {
        return VRREC_STATUS_INVALID_STATE;
    }
    const auto video_status = video_.Join();
    if (video_status != VRREC_STATUS_OK) {
        audio_.Abort();
        audio_.Join();
        mux_.Abort();
        terminal_ = true;
        events_.Faulted(video_status, "Video pipeline failed while stopping");
        return video_status;
    }
    const auto audio_status = audio_.Join();
    if (audio_status != VRREC_STATUS_OK) {
        mux_.Abort();
        terminal_ = true;
        events_.Faulted(audio_status, "Audio pipeline failed while stopping");
        return audio_status;
    }
    terminal_ = true;
    events_.Stopped(video_.MuxedPacketCount(), audio_.MuxedPacketCount());
    return VRREC_STATUS_OK;
}

}
