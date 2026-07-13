#ifndef VRRECORDER_NATIVE_PIPELINE_MEDIA_BACKEND_HPP
#define VRRECORDER_NATIVE_PIPELINE_MEDIA_BACKEND_HPP

#include <condition_variable>
#include <mutex>
#include <thread>

#include "media_backend.hpp"
#include "media_recording_pipeline.hpp"
#include "video_layout_update_port.hpp"

namespace vrrecorder::native {

using PipelineMediaThreadEntry = void (*)(void *) noexcept;

class PipelineMediaThreadFactoryPort {
public:
    virtual ~PipelineMediaThreadFactoryPort() = default;
    virtual vrrec_status_t Start(
        std::thread &thread,
        PipelineMediaThreadEntry entry,
        void *context) noexcept = 0;
};

class PipelineMediaBackend final : public MediaBackend {
public:
    PipelineMediaBackend(
        MediaRecordingPipelinePort &pipeline,
        VideoLayoutUpdatePort &layout) noexcept;
    PipelineMediaBackend(
        MediaRecordingPipelinePort &pipeline,
        VideoLayoutUpdatePort &layout,
        PipelineMediaThreadFactoryPort &thread_factory) noexcept;
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
    void RequestAbort() noexcept override;
    void JoinAfterAbort() noexcept override;

private:
    static void RunStopWorkerEntry(void *context) noexcept;
    static void RunCleanupWorkerEntry(void *context) noexcept;
    vrrec_status_t StartCleanupWorker() noexcept;
    void RunCleanupWorker() noexcept;
    void JoinStopWorker() noexcept;
    void ShutdownCleanupWorker() noexcept;

    MediaRecordingPipelinePort &pipeline_;
    VideoLayoutUpdatePort &layout_;
    PipelineMediaThreadFactoryPort &thread_factory_;
    std::mutex mutex_;
    std::mutex stop_join_mutex_;
    std::condition_variable changed_;
    std::thread stop_worker_;
    std::thread cleanup_worker_;
    bool stop_completed_ = false;
    bool stop_start_in_progress_ = false;
    vrrec_status_t stop_status_ = VRREC_STATUS_INVALID_STATE;
    bool abort_requested_ = false;
    bool cleanup_requested_ = false;
    bool cleanup_in_progress_ = false;
    bool cleanup_completed_ = false;
    bool cleanup_shutdown_ = false;
};

}

#endif
