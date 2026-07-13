#include "media_recording_session.hpp"

#include <utility>

namespace vrrecorder::native {

MediaRecordingSession::MediaRecordingSession(
    MediaStreamPipelinePort &video,
    MediaStreamPipelinePort &audio,
    MediaMuxSessionPort &mux,
    FragmentedMp4StreamConfiguration mux_configuration,
    MediaEventSink &events)
    : video_(video),
      audio_(audio),
      mux_(mux),
      mux_configuration_(std::move(mux_configuration)),
      events_(events)
{
}

MediaRecordingSession::~MediaRecordingSession()
{
    Abort();
}

vrrec_status_t MediaRecordingSession::Start() noexcept
{
    auto expected_start = StartPhase::NotStarted;
    if (!start_phase_.compare_exchange_strong(
            expected_start,
            StartPhase::Starting)) {
        return VRREC_STATUS_INVALID_STATE;
    }

    struct StartCompletion final {
        std::atomic<StartPhase> &phase;

        ~StartCompletion()
        {
            phase.store(StartPhase::Completed);
            phase.notify_all();
        }
    } completion {start_phase_};

    if (terminal_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }

    const auto mux_status = mux_.Start(mux_configuration_);
    if (terminal_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (mux_status != VRREC_STATUS_OK) {
        mux_.Abort();
        terminal_.store(true);
        return mux_status;
    }
    const auto video_status = video_.Start();
    if (video_status == VRREC_STATUS_OK) {
        video_started_.store(true);
    }
    if (terminal_.load()) {
        if (video_started_.exchange(false)) {
            video_.RequestAbort();
            video_.JoinAfterAbort();
        }
        return VRREC_STATUS_INVALID_STATE;
    }
    if (video_status != VRREC_STATUS_OK) {
        mux_.Abort();
        terminal_.store(true);
        return video_status;
    }
    const auto audio_status = audio_.Start();
    if (audio_status == VRREC_STATUS_OK) {
        audio_started_.store(true);
    }
    if (terminal_.load()) {
        if (audio_started_.exchange(false)) {
            audio_.RequestAbort();
            audio_.JoinAfterAbort();
        }
        if (video_started_.exchange(false)) {
            video_.RequestAbort();
            video_.JoinAfterAbort();
        }
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
    RequestAbort();
    JoinAfterAbort();
}

void MediaRecordingSession::RequestAbort() noexcept
{
    auto expected_phase = AbortPhase::Idle;
    if (!abort_phase_.compare_exchange_strong(
            expected_phase,
            AbortPhase::Requesting)) {
        return;
    }

    auto expected_terminal = false;
    if (!terminal_.compare_exchange_strong(expected_terminal, true)) {
        abort_phase_.store(AbortPhase::NotNeeded);
        abort_phase_.notify_all();
        return;
    }

    auto expected_start = StartPhase::NotStarted;
    if (start_phase_.compare_exchange_strong(
            expected_start,
            StartPhase::Completed)) {
        start_phase_.notify_all();
    }

    mux_.RequestAbort();
    if (video_started_.load()) {
        video_.RequestAbort();
    }
    if (audio_started_.load()) {
        audio_.RequestAbort();
    }
    abort_phase_.store(AbortPhase::Requested);
    abort_phase_.notify_all();
}

void MediaRecordingSession::JoinAfterAbort() noexcept
{
    auto phase = abort_phase_.load();
    while (phase == AbortPhase::Requesting) {
        abort_phase_.wait(phase);
        phase = abort_phase_.load();
    }
    if (phase != AbortPhase::Requested) {
        return;
    }

    auto start_phase = start_phase_.load();
    while (start_phase == StartPhase::Starting) {
        start_phase_.wait(start_phase);
        start_phase = start_phase_.load();
    }

    {
        std::unique_lock lock(abort_join_mutex_);
        if (abort_join_completed_) {
            return;
        }
        if (abort_join_in_progress_) {
            abort_join_changed_.wait(lock, [this] {
                return abort_join_completed_;
            });
            return;
        }
        abort_join_in_progress_ = true;
    }

    mux_.Abort();
    if (video_started_.exchange(false)) {
        video_.RequestAbort();
        video_.JoinAfterAbort();
    }
    if (audio_started_.exchange(false)) {
        audio_.RequestAbort();
        audio_.JoinAfterAbort();
    }
    {
        const std::lock_guard lock(abort_join_mutex_);
        abort_join_in_progress_ = false;
        abort_join_completed_ = true;
    }
    abort_phase_.store(AbortPhase::CleanupCompleted);
    abort_phase_.notify_all();
    abort_join_changed_.notify_all();
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
