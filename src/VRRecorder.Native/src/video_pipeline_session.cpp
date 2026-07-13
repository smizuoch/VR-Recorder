#include "video_pipeline_session.hpp"

#include <thread>

namespace vrrecorder::native {

VideoPipelineSession::VideoPipelineSession(
    SpoutCaptureWorkerPort &capture,
    VideoEncodingWorkerPort &encoding,
    MediaEventSink &events) noexcept
    : capture_(capture),
      encoding_(encoding),
      events_(events)
{
}

VideoPipelineSession::~VideoPipelineSession()
{
    Abort();
}

void VideoPipelineSession::AbortCaptureOnce() noexcept
{
    if (!capture_abort_requested_.exchange(true)) {
        capture_.Abort();
    }
}

vrrec_status_t VideoPipelineSession::Start(
    std::chrono::milliseconds poll_timeout) noexcept
{
    if (poll_timeout <= std::chrono::milliseconds {0} ||
        poll_timeout > std::chrono::milliseconds(
            VRREC_SPOUT_MAX_POLL_TIMEOUT_MILLISECONDS)) {
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

    const auto capture_status = capture_.Start(poll_timeout);
    if (capture_status == VRREC_STATUS_OK) {
        capture_started_.store(true);
    }
    if (terminal_outcome_.load() != TerminalOutcome::Open) {
        if (capture_started_.exchange(false)) {
            AbortCaptureOnce();
            capture_.Join();
        }
        return VRREC_STATUS_INVALID_STATE;
    }
    if (capture_status != VRREC_STATUS_OK) {
        return capture_status;
    }

    const auto encoding_status = encoding_.Start();
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
        if (capture_started_.exchange(false)) {
            AbortCaptureOnce();
            capture_.Join();
        }
        return VRREC_STATUS_INVALID_STATE;
    }
    if (encoding_status != VRREC_STATUS_OK) {
        if (capture_started_.exchange(false)) {
            AbortCaptureOnce();
            capture_.Join();
        }
        return encoding_status;
    }

    return VRREC_STATUS_OK;
}

vrrec_status_t VideoPipelineSession::RequestStop() noexcept
{
    if (!active_.load() ||
        terminal_outcome_.load() != TerminalOutcome::Open ||
        finished_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }

    if (stop_requested_.exchange(true)) {
        return VRREC_STATUS_OK;
    }

    AbortCaptureOnce();
    if (terminal_outcome_.load() != TerminalOutcome::Open) {
        return VRREC_STATUS_INVALID_STATE;
    }

    const auto encoding_status = encoding_.RequestStop();
    if (terminal_outcome_.load() != TerminalOutcome::Open) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (encoding_status != VRREC_STATUS_OK) {
        active_.store(false);
        RequestAbort();
        JoinAfterAbort();
    }

    return encoding_status;
}

void VideoPipelineSession::Abort() noexcept
{
    RequestAbort();
    JoinAfterAbort();
}

void VideoPipelineSession::RequestAbort() noexcept
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

void VideoPipelineSession::JoinAfterAbort() noexcept
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
            const auto capture_started = capture_started_.exchange(false);
            const auto encoding_started = encoding_started_.exchange(false);
            if (encoding_started) {
                encoding_.RequestAbort();
            }
            if (capture_started) {
                AbortCaptureOnce();
            }
            if (encoding_started) {
                encoding_.JoinAfterAbort();
            }
            if (capture_started) {
                capture_.Join();
            }
            active_.store(false);
            finished_.store(true);
        }
    }

    auto join_in_progress = join_in_progress_.load();
    while (join_in_progress) {
        join_in_progress_.wait(join_in_progress);
        join_in_progress = join_in_progress_.load();
    }
}

VideoPipelineResult VideoPipelineSession::Join() noexcept
{
    auto expected_join = false;
    if (!join_in_progress_.compare_exchange_strong(
            expected_join,
            true)) {
        return VideoPipelineResult::InvalidState;
    }

    struct JoinCompletion final {
        std::atomic_bool &in_progress;

        ~JoinCompletion()
        {
            in_progress.store(false);
            in_progress.notify_all();
        }
    } completion {join_in_progress_};

    if (!capture_started_.load() || !encoding_started_.load() ||
        finished_.load() ||
        terminal_outcome_.load() != TerminalOutcome::Open) {
        return VideoPipelineResult::InvalidState;
    }

    auto capture_result = SpoutCaptureWorkerResult::InvalidState;
    std::thread capture_join_thread;
    try {
        capture_join_thread = std::thread([this, &capture_result]() noexcept {
            capture_result = capture_.Join();
            if (capture_result == SpoutCaptureWorkerResult::SenderLost ||
                capture_result == SpoutCaptureWorkerResult::Failed) {
                encoding_.Abort();
            }
        });
    } catch (...) {
        AbortCaptureOnce();
        encoding_.Abort();
        capture_.Join();
        encoding_.Join();
        active_.store(false);
        capture_started_.store(false);
        encoding_started_.store(false);
        finished_.store(true);
        auto expected_outcome = TerminalOutcome::Open;
        if (!terminal_outcome_.compare_exchange_strong(
                expected_outcome,
                TerminalOutcome::Completed) &&
            expected_outcome == TerminalOutcome::AbortRequested) {
            return VideoPipelineResult::Aborted;
        }
        return VideoPipelineResult::Failed;
    }

    const auto encoding_result = encoding_.Join();
    if (encoding_result == VideoEncodingWorkerResult::EncoderFailed ||
        encoding_result == VideoEncodingWorkerResult::ClockFailed ||
        encoding_result == VideoEncodingWorkerResult::Failed ||
        encoding_result == VideoEncodingWorkerResult::InvalidState ||
        (encoding_result == VideoEncodingWorkerResult::Stopped &&
         !stop_requested_.load())) {
        AbortCaptureOnce();
    }
    if (terminal_outcome_.load() == TerminalOutcome::AbortRequested) {
        AbortCaptureOnce();
    }
    capture_join_thread.join();
    if (terminal_outcome_.load() == TerminalOutcome::AbortRequested) {
        AbortCaptureOnce();
    }

    active_.store(false);
    finished_.store(true);

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
            ? VideoPipelineResult::Aborted
            : VideoPipelineResult::InvalidState;
    }

    capture_started_.store(false);
    encoding_started_.store(false);

    if (capture_result == SpoutCaptureWorkerResult::SenderLost) {
        events_.Faulted(
            VRREC_STATUS_BACKEND_UNAVAILABLE,
            "Spout sender was lost while recording");
        return VideoPipelineResult::SenderLost;
    }

    if (capture_result == SpoutCaptureWorkerResult::Failed) {
        events_.Faulted(
            VRREC_STATUS_INTERNAL_ERROR,
            "Spout capture failed while recording");
        return VideoPipelineResult::CaptureFailed;
    }

    if (capture_result != SpoutCaptureWorkerResult::Aborted) {
        encoding_.Abort();
        return VideoPipelineResult::Failed;
    }

    if (encoding_result == VideoEncodingWorkerResult::Stopped) {
        return VideoPipelineResult::Stopped;
    }
    if (encoding_result == VideoEncodingWorkerResult::Aborted) {
        return VideoPipelineResult::Aborted;
    }
    if (encoding_result == VideoEncodingWorkerResult::EncoderFailed) {
        return VideoPipelineResult::EncoderFailed;
    }
    if (encoding_result == VideoEncodingWorkerResult::InvalidState) {
        return VideoPipelineResult::InvalidState;
    }
    return VideoPipelineResult::Failed;
}

VideoEncodingStatistics VideoPipelineSession::Statistics() const noexcept
{
    return encoding_.Statistics();
}

}
