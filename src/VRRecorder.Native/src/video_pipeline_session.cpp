#include "video_pipeline_session.hpp"

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

vrrec_status_t VideoPipelineSession::Start(
    std::chrono::milliseconds poll_timeout) noexcept
{
    if (poll_timeout <= std::chrono::milliseconds {0} ||
        poll_timeout > std::chrono::milliseconds(
            VRREC_SPOUT_MAX_POLL_TIMEOUT_MILLISECONDS)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    if (start_attempted_.exchange(true) || aborted_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }

    const auto capture_status = capture_.Start(poll_timeout);
    if (capture_status != VRREC_STATUS_OK) {
        return capture_status;
    }

    capture_started_.store(true);
    const auto encoding_status = encoding_.Start();
    if (encoding_status != VRREC_STATUS_OK) {
        capture_.Abort();
        capture_started_.store(false);
        return encoding_status;
    }

    encoding_started_.store(true);
    active_.store(true);
    return VRREC_STATUS_OK;
}

vrrec_status_t VideoPipelineSession::RequestStop() noexcept
{
    if (!active_.load() || aborted_.load() || finished_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }

    if (stop_requested_.exchange(true)) {
        return VRREC_STATUS_OK;
    }

    capture_.Abort();
    return encoding_.RequestStop();
}

void VideoPipelineSession::Abort() noexcept
{
    if (finished_.load() || aborted_.exchange(true)) {
        return;
    }

    active_.store(false);
    if (capture_started_.load()) {
        capture_.Abort();
    }
    if (encoding_started_.load()) {
        encoding_.Abort();
    }
}

VideoPipelineResult VideoPipelineSession::Join() noexcept
{
    if (!capture_started_.load() || !encoding_started_.load() ||
        finished_.load()) {
        return VideoPipelineResult::InvalidState;
    }

    const auto capture_result = capture_.Join();
    if (capture_result == SpoutCaptureWorkerResult::SenderLost) {
        encoding_.Abort();
        events_.Faulted(
            VRREC_STATUS_BACKEND_UNAVAILABLE,
            "Spout sender was lost while recording");
        active_.store(false);
        finished_.store(true);
        return VideoPipelineResult::SenderLost;
    }

    if (capture_result == SpoutCaptureWorkerResult::Failed) {
        encoding_.Abort();
        events_.Faulted(
            VRREC_STATUS_INTERNAL_ERROR,
            "Spout capture failed while recording");
        active_.store(false);
        finished_.store(true);
        return VideoPipelineResult::CaptureFailed;
    }

    if (capture_result != SpoutCaptureWorkerResult::Aborted) {
        encoding_.Abort();
        active_.store(false);
        finished_.store(true);
        return VideoPipelineResult::Failed;
    }

    const auto encoding_result = encoding_.Join();
    active_.store(false);
    finished_.store(true);
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
