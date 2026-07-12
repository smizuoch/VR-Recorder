#ifndef VRRECORDER_NATIVE_MEDIA_RECORDING_PIPELINE_HPP
#define VRRECORDER_NATIVE_MEDIA_RECORDING_PIPELINE_HPP

#include <chrono>
#include <cstddef>

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
    virtual void Abort() noexcept = 0;
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
        std::size_t audio_encoding_frame_count_48k,
        MediaMuxSessionPort &mux,
        MediaEventSink &events);

    vrrec_status_t Start() noexcept override;
    vrrec_status_t UpdateAudioRouting(
        vrrec_audio_routing_t routing) noexcept override;
    vrrec_status_t RequestStop() noexcept override;
    void Abort() noexcept override;
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
