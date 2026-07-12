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
    if (start_attempted_.exchange(true) || terminal_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }
    const auto video_status = video_.Start();
    if (video_status != VRREC_STATUS_OK) {
        mux_.Abort();
        terminal_.store(true);
        return video_status;
    }
    video_started_.store(true);
    const auto audio_status = audio_.Start();
    if (audio_status != VRREC_STATUS_OK) {
        video_.Abort();
        video_.Join();
        mux_.Abort();
        video_started_.store(false);
        terminal_.store(true);
        return audio_status;
    }
    audio_started_.store(true);
    return VRREC_STATUS_OK;
}

vrrec_status_t MediaRecordingSession::RequestStop() noexcept
{
    if (!video_started_.load() || !audio_started_.load() ||
        terminal_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (stop_requested_.load()) {
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
    stop_requested_.store(true);
    return VRREC_STATUS_OK;
}

void MediaRecordingSession::Abort() noexcept
{
    if (terminal_.exchange(true)) {
        return;
    }
    if (video_started_.load()) {
        video_.Abort();
    }
    if (audio_started_.load()) {
        audio_.Abort();
    }
    mux_.Abort();
}

vrrec_status_t MediaRecordingSession::Join() noexcept
{
    if (!video_started_.load() || !audio_started_.load() ||
        terminal_.load() || !stop_requested_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }
    const auto video_status = video_.Join();
    if (terminal_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (video_status != VRREC_STATUS_OK) {
        audio_.Abort();
        audio_.Join();
        mux_.Abort();
        if (!terminal_.exchange(true)) {
            events_.Faulted(
                video_status,
                "Video pipeline failed while stopping");
        }
        return video_status;
    }
    const auto audio_status = audio_.Join();
    if (terminal_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (audio_status != VRREC_STATUS_OK) {
        mux_.Abort();
        if (!terminal_.exchange(true)) {
            events_.Faulted(
                audio_status,
                "Audio pipeline failed while stopping");
        }
        return audio_status;
    }
    auto expected_terminal = false;
    if (!terminal_.compare_exchange_strong(expected_terminal, true)) {
        return VRREC_STATUS_INVALID_STATE;
    }
    events_.Stopped(video_.MuxedPacketCount(), audio_.MuxedPacketCount());
    return VRREC_STATUS_OK;
}

}
