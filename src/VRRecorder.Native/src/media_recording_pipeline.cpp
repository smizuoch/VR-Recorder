#include "media_recording_pipeline.hpp"

#include <utility>

namespace vrrecorder::native {

MediaRecordingPipeline::MediaRecordingPipeline(
    VideoPipelineSessionPort &video,
    std::chrono::milliseconds video_poll_timeout,
    StereoAudioPipelineSessionPort &audio,
    StereoAudioCaptureSessionConfig audio_config,
    std::size_t audio_encoding_frame_count_48k,
    MediaMuxSessionPort &mux,
    FragmentedMp4StreamConfiguration mux_configuration,
    MediaEventSink &events)
    : video_session_(video),
      audio_session_(audio),
      mux_session_(mux),
      video_(video_session_, video_poll_timeout),
      audio_(
          audio_session_,
          std::move(audio_config),
          audio_encoding_frame_count_48k),
      recording_(
          video_,
          audio_,
          mux,
          std::move(mux_configuration),
          events)
{
}

vrrec_status_t MediaRecordingPipeline::Start() noexcept
{
    return recording_.Start();
}

vrrec_status_t MediaRecordingPipeline::UpdateAudioRouting(
    vrrec_audio_routing_t routing) noexcept
{
    return audio_session_.SetRouting(routing);
}

vrrec_status_t MediaRecordingPipeline::RequestStop() noexcept
{
    return recording_.RequestStop();
}

void MediaRecordingPipeline::RequestAbort() noexcept
{
    recording_.RequestAbort();
}

void MediaRecordingPipeline::JoinAfterAbort() noexcept
{
    recording_.JoinAfterAbort();
}

vrrec_status_t MediaRecordingPipeline::Join() noexcept
{
    return recording_.Join();
}

MediaRecordingPipelineStatistics MediaRecordingPipeline::Statistics()
    const noexcept
{
    return {
        video_session_.Statistics(),
        audio_session_.Statistics(),
        mux_session_.AudioVideoOffsetMicroseconds(),
    };
}

}
