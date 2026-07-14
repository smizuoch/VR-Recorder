#ifndef VRRECORDER_NATIVE_MEDIA_RECORDING_PIPELINE_HPP
#define VRRECORDER_NATIVE_MEDIA_RECORDING_PIPELINE_HPP

#include <chrono>

#include "media_stream_pipeline_adapters.hpp"

namespace vrrecorder::native {

struct MediaRecordingPipelineStatistics final {
    VideoEncodingStatistics video;
    StereoAudioPipelineStatistics audio;
    std::int64_t audio_video_offset_microseconds;
};

class MediaRecordingPipelinePort {
public:
    virtual ~MediaRecordingPipelinePort() = default;
    virtual vrrec_status_t Start() noexcept = 0;
    virtual vrrec_status_t UpdateAudioRouting(
        vrrec_audio_routing_t routing) noexcept = 0;
    virtual vrrec_status_t RequestStop() noexcept = 0;
    // Logical termination only; must not join stream workers.
    virtual void RequestAbort() noexcept = 0;
    // Blocking cleanup for an owner or dedicated cleanup worker.
    virtual void JoinAfterAbort() noexcept = 0;
    virtual vrrec_status_t Join() noexcept = 0;
    virtual MediaRecordingPipelineStatistics Statistics() const noexcept = 0;
};

class MediaRecordingPipeline final : public MediaRecordingPipelinePort {
public:
    MediaRecordingPipeline(
        VideoPipelineSessionPort &video,
        std::chrono::milliseconds video_poll_timeout,
        StereoAudioPipelineSessionPort &audio,
        StereoAudioCaptureSessionConfig audio_config,
        MediaMuxSessionPort &mux,
        FragmentedMp4StreamConfiguration mux_configuration,
        MediaEventSink &events);

    vrrec_status_t Start() noexcept override;
    vrrec_status_t UpdateAudioRouting(
        vrrec_audio_routing_t routing) noexcept override;
    vrrec_status_t RequestStop() noexcept override;
    void RequestAbort() noexcept override;
    void JoinAfterAbort() noexcept override;
    vrrec_status_t Join() noexcept override;
    MediaRecordingPipelineStatistics Statistics() const noexcept override;

private:
    VideoPipelineSessionPort &video_session_;
    StereoAudioPipelineSessionPort &audio_session_;
    MediaMuxSessionPort &mux_session_;
    VideoMediaStreamPipelineAdapter video_;
    AudioMediaStreamPipelineAdapter audio_;
    MediaRecordingSession recording_;
};

}

#endif
