#ifndef VRRECORDER_NATIVE_MEDIA_RECORDING_PIPELINE_HPP
#define VRRECORDER_NATIVE_MEDIA_RECORDING_PIPELINE_HPP

#include <chrono>
#include <cstddef>

#include "media_stream_pipeline_adapters.hpp"

namespace vrrecorder::native {

struct MediaRecordingPipelineStatistics final {
    VideoEncodingStatistics video;
    StereoAudioPipelineStatistics audio;
};

class MediaRecordingPipeline final {
public:
    MediaRecordingPipeline(
        VideoPipelineSessionPort &video,
        std::chrono::milliseconds video_poll_timeout,
        StereoAudioPipelineSessionPort &audio,
        StereoAudioCaptureSessionConfig audio_config,
        std::size_t audio_encoding_frame_count_48k,
        MediaMuxSessionPort &mux,
        MediaEventSink &events);

    vrrec_status_t Start() noexcept;
    vrrec_status_t UpdateAudioRouting(
        vrrec_audio_routing_t routing) noexcept;
    vrrec_status_t RequestStop() noexcept;
    void Abort() noexcept;
    vrrec_status_t Join() noexcept;
    MediaRecordingPipelineStatistics Statistics() const noexcept;

private:
    VideoPipelineSessionPort &video_session_;
    StereoAudioPipelineSessionPort &audio_session_;
    VideoMediaStreamPipelineAdapter video_;
    AudioMediaStreamPipelineAdapter audio_;
    MediaRecordingSession recording_;
};

}

#endif
