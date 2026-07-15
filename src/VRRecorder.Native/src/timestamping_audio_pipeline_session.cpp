#include "timestamping_audio_pipeline_session.hpp"

#include <new>

namespace vrrecorder::native {

TimestampingStereoAudioPipelineSession::
TimestampingStereoAudioPipelineSession(
    StereoAudioPipelineSessionPort &session,
    AudioSessionStartClock &clock) noexcept
    : session_(session), clock_(clock)
{
}

vrrec_status_t TimestampingStereoAudioPipelineSession::Start(
    const StereoAudioCaptureSessionConfig &config,
    std::size_t encoding_frame_count_48k) noexcept
{
    std::int64_t epoch = -1;
    const auto clock_status = clock_.NowQpc100ns(epoch);
    if (clock_status != VRREC_STATUS_OK) {
        return clock_status;
    }
    if (epoch < 0) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }

    try {
        auto timestamped = config;
        timestamped.session_start_qpc_100ns = epoch;
        return session_.Start(timestamped, encoding_frame_count_48k);
    } catch (const std::bad_alloc &) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

vrrec_status_t TimestampingStereoAudioPipelineSession::SetRouting(
    vrrec_audio_routing_t routing) noexcept
{
    return session_.SetRouting(routing);
}

vrrec_status_t TimestampingStereoAudioPipelineSession::RequestStop() noexcept
{
    return session_.RequestStop();
}

void TimestampingStereoAudioPipelineSession::RequestAbort() noexcept
{
    session_.RequestAbort();
}

void TimestampingStereoAudioPipelineSession::JoinAfterAbort() noexcept
{
    session_.JoinAfterAbort();
}

void TimestampingStereoAudioPipelineSession::Abort() noexcept
{
    session_.Abort();
}

StereoAudioEncodingWorkerResult
TimestampingStereoAudioPipelineSession::Join() noexcept
{
    return session_.Join();
}

StereoAudioPipelineStatistics
TimestampingStereoAudioPipelineSession::Statistics() const noexcept
{
    return session_.Statistics();
}

}
