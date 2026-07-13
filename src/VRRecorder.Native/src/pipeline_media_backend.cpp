#include "pipeline_media_backend.hpp"

#include <utility>

namespace vrrecorder::native {

PipelineMediaBackend::PipelineMediaBackend(
    MediaRecordingPipelinePort &pipeline,
    VideoLayoutUpdatePort &layout) noexcept
    : PipelineMediaBackend(
          pipeline,
          layout,
          DefaultNativeThreadFactory())
{
}

PipelineMediaBackend::PipelineMediaBackend(
    MediaRecordingPipelinePort &pipeline,
    VideoLayoutUpdatePort &layout,
    PipelineMediaThreadFactoryPort &thread_factory) noexcept
    : pipeline_(pipeline),
      layout_(layout),
      thread_factory_(thread_factory)
{
}

PipelineMediaBackend::~PipelineMediaBackend()
{
    RequestAbort();
    JoinAfterAbort();
    ShutdownCleanupWorker();
    JoinStopWorker();
}

vrrec_status_t PipelineMediaBackend::Start() noexcept
{
    const auto cleanup_status = StartCleanupWorker();
    if (cleanup_status != VRREC_STATUS_OK) {
        return cleanup_status;
    }
    {
        const std::lock_guard lock(mutex_);
        if (abort_requested_) {
            return VRREC_STATUS_INVALID_STATE;
        }
    }
    const auto status = pipeline_.Start();
    {
        const std::lock_guard lock(mutex_);
        if (abort_requested_) {
            return VRREC_STATUS_INVALID_STATE;
        }
    }
    return status;
}

vrrec_status_t PipelineMediaBackend::UpdateVideoLayout(
    const vrrec_video_layout_v1 &layout) noexcept
{
    return layout_.UpdateVideoLayout(layout);
}

vrrec_status_t PipelineMediaBackend::UpdateAudioRouting(
    vrrec_audio_routing_t routing) noexcept
{
    return pipeline_.UpdateAudioRouting(routing);
}

vrrec_status_t PipelineMediaBackend::GetStatistics(
    vrrec_session_statistics_v1 &statistics) noexcept
{
    const auto current = pipeline_.Statistics();
    statistics = {
        sizeof(vrrec_session_statistics_v1),
        VRREC_ABI_V1,
        current.video.scheduler.source_frame_count,
        current.video.muxed_packet_count,
        current.audio.muxed_packet_count,
        current.video.scheduler.dropped_source_frame_count,
        current.video.scheduler.duplicated_output_frame_count,
        current.video.latest_encode_latency_microseconds,
        current.video.maximum_encode_latency_microseconds,
        current.audio_video_offset_microseconds,
    };
    return VRREC_STATUS_OK;
}

vrrec_status_t PipelineMediaBackend::RequestStop() noexcept
{
    {
        std::unique_lock lock(mutex_);
        if (stop_completed_) {
            return stop_status_;
        }
        if (stop_start_in_progress_) {
            changed_.wait(lock, [this] {
                return !stop_start_in_progress_;
            });
            return stop_status_;
        }
        if (abort_requested_) {
            return VRREC_STATUS_INVALID_STATE;
        }
        stop_start_in_progress_ = true;
    }

    auto status = pipeline_.RequestStop();
    std::thread worker;
    if (status == VRREC_STATUS_OK) {
        status = thread_factory_.Start(
            worker,
            &PipelineMediaBackend::RunStopWorkerEntry,
            this);
        if (status == VRREC_STATUS_OK && !worker.joinable()) {
            status = VRREC_STATUS_INTERNAL_ERROR;
        }
    }

    auto abort_after_failure = false;
    {
        const std::lock_guard lock(mutex_);
        if (worker.joinable()) {
            stop_worker_ = std::move(worker);
        }
        const auto abort_won = abort_requested_;
        if (abort_won) {
            status = VRREC_STATUS_INVALID_STATE;
        }
        stop_status_ = status;
        stop_completed_ = true;
        stop_start_in_progress_ = false;
        abort_after_failure = !abort_won && status != VRREC_STATUS_OK;
    }
    changed_.notify_all();

    if (abort_after_failure) {
        RequestAbort();
    }
    return status;
}

void PipelineMediaBackend::RequestAbort() noexcept
{
    pipeline_.RequestAbort();

    auto cleanup_synchronously = false;
    {
        const std::lock_guard lock(mutex_);
        abort_requested_ = true;
        if (cleanup_completed_ || cleanup_in_progress_ ||
            cleanup_requested_) {
            return;
        }
        if (cleanup_worker_.joinable()) {
            cleanup_requested_ = true;
        } else {
            cleanup_in_progress_ = true;
            cleanup_synchronously = true;
        }
    }
    changed_.notify_all();

    if (!cleanup_synchronously) {
        return;
    }

    pipeline_.JoinAfterAbort();
    JoinStopWorker();
    {
        const std::lock_guard lock(mutex_);
        cleanup_in_progress_ = false;
        cleanup_completed_ = true;
    }
    changed_.notify_all();
}

void PipelineMediaBackend::JoinAfterAbort() noexcept
{
    RequestAbort();
    std::unique_lock lock(mutex_);
    if (cleanup_worker_.joinable() &&
        cleanup_worker_.get_id() == std::this_thread::get_id()) {
        return;
    }
    changed_.wait(lock, [this] { return cleanup_completed_; });
}

vrrec_status_t PipelineMediaBackend::StartCleanupWorker() noexcept
{
    const std::lock_guard lock(mutex_);
    if (cleanup_worker_.joinable()) {
        return VRREC_STATUS_OK;
    }
    if (cleanup_shutdown_ || abort_requested_) {
        return VRREC_STATUS_INVALID_STATE;
    }

    const auto status = thread_factory_.Start(
        cleanup_worker_,
        &PipelineMediaBackend::RunCleanupWorkerEntry,
        this);
    if (status != VRREC_STATUS_OK) {
        return status;
    }
    if (!cleanup_worker_.joinable()) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
    return VRREC_STATUS_OK;
}

void PipelineMediaBackend::RunStopWorkerEntry(void *context) noexcept
{
    auto &backend = *static_cast<PipelineMediaBackend *>(context);
    static_cast<void>(backend.pipeline_.Join());
}

void PipelineMediaBackend::RunCleanupWorkerEntry(void *context) noexcept
{
    static_cast<PipelineMediaBackend *>(context)->RunCleanupWorker();
}

void PipelineMediaBackend::RunCleanupWorker() noexcept
{
    std::unique_lock lock(mutex_);
    for (;;) {
        changed_.wait(lock, [this] {
            return cleanup_requested_ || cleanup_shutdown_;
        });
        if (cleanup_requested_ && !cleanup_completed_) {
            cleanup_requested_ = false;
            cleanup_in_progress_ = true;
            lock.unlock();
            pipeline_.JoinAfterAbort();
            JoinStopWorker();
            lock.lock();
            cleanup_in_progress_ = false;
            cleanup_completed_ = true;
            changed_.notify_all();
            continue;
        }
        if (cleanup_shutdown_) {
            return;
        }
    }
}

void PipelineMediaBackend::JoinStopWorker() noexcept
{
    const std::lock_guard join_lock(stop_join_mutex_);
    std::thread worker;
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [this] {
            return !stop_start_in_progress_;
        });
        if (!stop_worker_.joinable()) {
            return;
        }
        if (stop_worker_.get_id() == std::this_thread::get_id()) {
            return;
        }
        worker = std::move(stop_worker_);
    }
    worker.join();
}

void PipelineMediaBackend::ShutdownCleanupWorker() noexcept
{
    std::thread worker;
    {
        const std::lock_guard lock(mutex_);
        if (!cleanup_worker_.joinable()) {
            return;
        }
        cleanup_shutdown_ = true;
        worker = std::move(cleanup_worker_);
    }
    changed_.notify_all();
    worker.join();
}

}
