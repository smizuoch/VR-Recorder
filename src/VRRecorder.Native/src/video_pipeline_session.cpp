#include "video_pipeline_session.hpp"

#include <thread>

namespace vrrecorder::native {

VideoPipelineSession::VideoPipelineSession(
    SpoutCaptureWorkerPort &capture,
    VideoEncodingWorkerPort &encoding,
    MediaEventSink &events) noexcept
    : VideoPipelineSession(
          capture,
          encoding,
          events,
          DefaultNativeThreadFactory())
{
}

VideoPipelineSession::VideoPipelineSession(
    SpoutCaptureWorkerPort &capture,
    VideoEncodingWorkerPort &encoding,
    MediaEventSink &events,
    NativeThreadFactoryPort &join_thread_factory) noexcept
    : capture_(capture),
      encoding_(encoding),
      events_(events),
      join_thread_factory_(join_thread_factory)
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

void VideoPipelineSession::CaptureJoinEntry(void *context) noexcept
{
    auto &join = *static_cast<CaptureJoinContext *>(context);
    join.result = join.session.capture_.Join();
    if (join.result == SpoutCaptureWorkerResult::SenderLost ||
        join.result == SpoutCaptureWorkerResult::AdapterChanged ||
        join.result == SpoutCaptureWorkerResult::Failed) {
        join.session.encoding_.Abort();
    }
}

void VideoPipelineSession::CleanupStartedWorkers() noexcept
{
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

    {
        const std::lock_guard state_lock(start_abort_mutex_);
        if (terminal_outcome_.load() != TerminalOutcome::Open) {
            return VRREC_STATUS_INVALID_STATE;
        }
    }

    const auto capture_status = capture_.Start(poll_timeout);
    if (capture_status == VRREC_STATUS_OK) {
        capture_started_.store(true);
    }

    auto abort_won_during_capture_start = false;
    {
        const std::lock_guard state_lock(start_abort_mutex_);
        abort_won_during_capture_start =
            terminal_outcome_.load() != TerminalOutcome::Open;
        if (!abort_won_during_capture_start &&
            capture_status == VRREC_STATUS_OK) {
            encoding_starting_ = true;
        }
    }
    if (abort_won_during_capture_start) {
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

    auto abort_won_during_encoding_start = false;
    auto cleanup_encoding = false;
    auto cleanup_capture = false;
    {
        const std::lock_guard state_lock(start_abort_mutex_);
        encoding_starting_ = false;
        if (encoding_status == VRREC_STATUS_OK) {
            encoding_started_.store(true);
        }

        abort_won_during_encoding_start =
            terminal_outcome_.load() != TerminalOutcome::Open;
        if (abort_won_during_encoding_start) {
            active_.store(false);
            if (encoding_started_.exchange(false)) {
                encoding_abort_requested_ = true;
                encoding_.RequestAbort();
                cleanup_encoding = true;
            } else if (encoding_abort_requested_) {
                cleanup_encoding = true;
            }
            cleanup_capture = capture_started_.exchange(false);
        } else if (encoding_status == VRREC_STATUS_OK) {
            active_.store(true);
        } else {
            cleanup_capture = capture_started_.exchange(false);
        }
    }

    if (cleanup_encoding) {
        encoding_.JoinAfterAbort();
    }
    if (cleanup_capture) {
        AbortCaptureOnce();
        capture_.Join();
    }
    if (abort_won_during_encoding_start) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (encoding_status != VRREC_STATUS_OK) {
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
    const std::lock_guard state_lock(start_abort_mutex_);
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
    if (encoding_starting_ || encoding_started_.load()) {
        encoding_abort_requested_ = true;
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
            CleanupStartedWorkers();
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
    CaptureJoinContext capture_join_context {*this, capture_result};
    const auto launch_status = join_thread_factory_.Start(
        capture_join_thread,
        &VideoPipelineSession::CaptureJoinEntry,
        &capture_join_context);
    const auto effective_launch_status =
        launch_status == VRREC_STATUS_OK && !capture_join_thread.joinable()
        ? VRREC_STATUS_INTERNAL_ERROR
        : launch_status;
    if (effective_launch_status != VRREC_STATUS_OK) {
        {
            const std::lock_guard cleanup_lock(abort_join_mutex_);
            CleanupStartedWorkers();
        }
        if (capture_join_thread.joinable()) {
            capture_join_thread.join();
        }
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
        encoding_result ==
            VideoEncodingWorkerResult::EncoderFailedPartSealed ||
        encoding_result == VideoEncodingWorkerResult::ClockFailed ||
        encoding_result == VideoEncodingWorkerResult::SurfaceAbandoned ||
        encoding_result == VideoEncodingWorkerResult::SurfaceDeviceRemoved ||
        encoding_result == VideoEncodingWorkerResult::SurfaceDeviceReset ||
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

    if (capture_result == SpoutCaptureWorkerResult::AdapterChanged) {
        events_.Faulted(
            VRREC_STATUS_BACKEND_UNAVAILABLE,
            "Spout capture adapter changed while recording");
        return VideoPipelineResult::AdapterChanged;
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
    if (encoding_result ==
        VideoEncodingWorkerResult::EncoderFailedPartSealed) {
        return VideoPipelineResult::EncoderFailedPartSealed;
    }
    if (encoding_result == VideoEncodingWorkerResult::SurfaceAbandoned) {
        return VideoPipelineResult::SurfaceAbandoned;
    }
    if (encoding_result ==
        VideoEncodingWorkerResult::SurfaceDeviceRemoved) {
        return VideoPipelineResult::SurfaceDeviceRemoved;
    }
    if (encoding_result == VideoEncodingWorkerResult::SurfaceDeviceReset) {
        return VideoPipelineResult::SurfaceDeviceReset;
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
