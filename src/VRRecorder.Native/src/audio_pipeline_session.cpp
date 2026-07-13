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

    if (terminal_outcome_.load() != TerminalOutcome::Open) {
        return VRREC_STATUS_INVALID_STATE;
    }

    const auto capture_status = capture_.Start(config);
    if (capture_status == VRREC_STATUS_OK) {
        capture_started_.store(true);
    }
    if (terminal_outcome_.load() != TerminalOutcome::Open) {
        if (capture_started_.exchange(false)) {
            capture_.Abort();
        }
        return VRREC_STATUS_INVALID_STATE;
    }
    if (capture_status != VRREC_STATUS_OK) {
        return capture_status;
    }

    const auto encoding_status = encoding_.Start(
        encoding_frame_count_48k);
    if (encoding_status == VRREC_STATUS_OK) {
        encoding_started_.store(true);
        active_.store(true);
    }
    if (terminal_outcome_.load() != TerminalOutcome::Open) {
        active_.store(false);
        if (encoding_started_.exchange(false)) {
            encoding_.RequestAbort();
            encoding_.JoinAfterAbort();
        }
        capture_started_.store(false);
        return VRREC_STATUS_INVALID_STATE;
    }
    if (encoding_status != VRREC_STATUS_OK) {
        if (capture_started_.exchange(false)) {
            capture_.Abort();
        }
        return encoding_status;
    }
    return VRREC_STATUS_OK;
}

vrrec_status_t StereoAudioPipelineSession::SetRouting(
    vrrec_audio_routing_t routing) noexcept
{
    if (!active_.load() ||
        terminal_outcome_.load() != TerminalOutcome::Open ||
        encoding_.IsFinished()) {
        active_.store(false);
        return VRREC_STATUS_INVALID_STATE;
    }

    const auto status = capture_.SetRouting(routing);
    if (terminal_outcome_.load() != TerminalOutcome::Open ||
        !active_.load() || encoding_.IsFinished()) {
        active_.store(false);
        return VRREC_STATUS_INVALID_STATE;
    }
    return status;
}

vrrec_status_t StereoAudioPipelineSession::RequestStop() noexcept
{
    if (!active_.load() ||
        terminal_outcome_.load() != TerminalOutcome::Open) {
        return VRREC_STATUS_INVALID_STATE;
    }

    const auto status = encoding_.RequestStop();
    if (terminal_outcome_.load() != TerminalOutcome::Open) {
        return VRREC_STATUS_INVALID_STATE;
    }
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
    auto expected_outcome = TerminalOutcome::Open;
    if (!terminal_outcome_.compare_exchange_strong(
            expected_outcome,
            TerminalOutcome::AbortRequested)) {
        return;
    }

    auto expected_start = StartPhase::NotStarted;
    if (start_phase_.compare_exchange_strong(
            expected_start,
            StartPhase::Completed)) {
        start_phase_.notify_all();
    }

    active_.store(false);
    if (encoding_started_.load()) {
        encoding_.RequestAbort();
    }
}

void StereoAudioPipelineSession::JoinAfterAbort() noexcept
{
    auto start_phase = start_phase_.load();
    while (start_phase == StartPhase::Starting) {
        start_phase_.wait(start_phase);
        start_phase = start_phase_.load();
    }

    {
        const std::lock_guard cleanup_lock(abort_join_mutex_);
        const auto outcome = terminal_outcome_.load();
        if (outcome == TerminalOutcome::Open) {
            return;
        }

        if (outcome == TerminalOutcome::AbortRequested) {
            if (encoding_started_.exchange(false)) {
                encoding_.RequestAbort();
                encoding_.JoinAfterAbort();
                capture_started_.store(false);
            } else if (capture_started_.exchange(false)) {
                capture_.Abort();
            }
        }
    }

    auto join_in_progress = join_in_progress_.load();
    while (join_in_progress) {
        join_in_progress_.wait(join_in_progress);
        join_in_progress = join_in_progress_.load();
    }
}

StereoAudioEncodingWorkerResult StereoAudioPipelineSession::Join() noexcept
{
    auto expected_join = false;
    if (!join_in_progress_.compare_exchange_strong(
            expected_join,
            true)) {
        return StereoAudioEncodingWorkerResult::InvalidState;
    }

    struct JoinCompletion final {
        std::atomic_bool &in_progress;

        ~JoinCompletion()
        {
            in_progress.store(false);
            in_progress.notify_all();
        }
    } completion {join_in_progress_};

    if (!encoding_started_.load() ||
        terminal_outcome_.load() != TerminalOutcome::Open) {
        return StereoAudioEncodingWorkerResult::InvalidState;
    }

    const auto result = encoding_.Join();
    active_.store(false);
    auto expected_outcome = TerminalOutcome::Open;
    if (!terminal_outcome_.compare_exchange_strong(
            expected_outcome,
            TerminalOutcome::Completed)) {
        if (expected_outcome == TerminalOutcome::AbortRequested &&
            encoding_started_.exchange(false)) {
            encoding_.RequestAbort();
            encoding_.JoinAfterAbort();
        } else {
            encoding_started_.store(false);
        }
        capture_started_.store(false);
        return expected_outcome == TerminalOutcome::AbortRequested
            ? StereoAudioEncodingWorkerResult::Aborted
            : StereoAudioEncodingWorkerResult::InvalidState;
    }

    encoding_started_.store(false);
    capture_started_.store(false);
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
