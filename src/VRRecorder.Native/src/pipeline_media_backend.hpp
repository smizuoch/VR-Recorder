#ifndef VRRECORDER_NATIVE_PIPELINE_MEDIA_BACKEND_HPP
#define VRRECORDER_NATIVE_PIPELINE_MEDIA_BACKEND_HPP

#include <mutex>
#include <thread>

#include "media_backend.hpp"
#include "media_recording_pipeline.hpp"

namespace vrrecorder::native {

class VideoLayoutUpdatePort {
public:
    virtual ~VideoLayoutUpdatePort() = default;
    virtual vrrec_status_t UpdateVideoLayout(
        const vrrec_video_layout_v1 &layout) noexcept = 0;
};

class PipelineMediaBackend final : public MediaBackend {
public:
    PipelineMediaBackend(
        MediaRecordingPipelinePort &pipeline,
        VideoLayoutUpdatePort &layout) noexcept;
    ~PipelineMediaBackend() override;

    PipelineMediaBackend(const PipelineMediaBackend &) = delete;
    PipelineMediaBackend &operator=(const PipelineMediaBackend &) = delete;

    vrrec_status_t Start() noexcept override;
    vrrec_status_t UpdateVideoLayout(
        const vrrec_video_layout_v1 &layout) noexcept override;
    vrrec_status_t UpdateAudioRouting(
        vrrec_audio_routing_t routing) noexcept override;
    vrrec_status_t GetStatistics(
        vrrec_session_statistics_v1 &statistics) noexcept override;
    vrrec_status_t RequestStop() noexcept override;
    void Abort() noexcept override;

private:
    void JoinStopWorker() noexcept;

    MediaRecordingPipelinePort &pipeline_;
    VideoLayoutUpdatePort &layout_;
    std::mutex mutex_;
    std::thread stop_worker_;
    bool stop_requested_ = false;
};

}

#endif
