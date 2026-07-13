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
    if (terminal_.load()) {
        if (video_status == VRREC_STATUS_OK) {
            video_.Abort();
            video_.Join();
        }
        return VRREC_STATUS_INVALID_STATE;
    }
    if (video_status != VRREC_STATUS_OK) {
        mux_.Abort();
        terminal_.store(true);
        return video_status;
    }
    video_started_.store(true);
    if (terminal_.load()) {
        video_.Abort();
        video_.Join();
        video_started_.store(false);
        return VRREC_STATUS_INVALID_STATE;
    }
    const auto audio_status = audio_.Start();
    if (terminal_.load()) {
        if (audio_status == VRREC_STATUS_OK) {
            audio_.Abort();
            audio_.Join();
        }
        video_.Abort();
        video_.Join();
        video_started_.store(false);
        return VRREC_STATUS_INVALID_STATE;
    }
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

    {
        std::unique_lock lock(stop_mutex_);
        if (stop_in_progress_) {
            stop_changed_.wait(lock, [this] {
                return !stop_in_progress_;
            });
            return stop_status_;
        }
        if (stop_completed_) {
            return stop_status_;
        }
        stop_in_progress_ = true;
    }

    const auto complete = [this](vrrec_status_t status) noexcept {
        {
            const std::lock_guard lock(stop_mutex_);
            stop_status_ = status;
            stop_completed_ = true;
            stop_in_progress_ = false;
        }
        stop_changed_.notify_all();
        return status;
    };

    const auto video_status = video_.RequestStop();
    if (terminal_.load()) {
        return complete(VRREC_STATUS_INVALID_STATE);
    }
    if (video_status != VRREC_STATUS_OK) {
        Abort();
        return complete(video_status);
    }
    const auto audio_status = audio_.RequestStop();
    if (terminal_.load()) {
        return complete(VRREC_STATUS_INVALID_STATE);
    }
    if (audio_status != VRREC_STATUS_OK) {
        Abort();
        return complete(audio_status);
    }
    stop_requested_.store(true);
    return complete(VRREC_STATUS_OK);
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
        terminal_.load() || !stop_requested_.load() ||
        join_attempted_.exchange(true)) {
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
