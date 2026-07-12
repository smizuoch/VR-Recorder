#include "pipeline_media_backend.hpp"

namespace vrrecorder::native {

PipelineMediaBackend::PipelineMediaBackend(
    MediaRecordingPipelinePort &pipeline,
    VideoLayoutUpdatePort &layout) noexcept
    : pipeline_(pipeline), layout_(layout)
{
}

PipelineMediaBackend::~PipelineMediaBackend()
{
    Abort();
}

vrrec_status_t PipelineMediaBackend::Start() noexcept
{
    return pipeline_.Start();
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
        0,
    };
    return VRREC_STATUS_OK;
}

vrrec_status_t PipelineMediaBackend::RequestStop() noexcept
{
    std::lock_guard lock(mutex_);
    if (stop_requested_) {
        return VRREC_STATUS_OK;
    }

    const auto status = pipeline_.RequestStop();
    if (status != VRREC_STATUS_OK) {
        return status;
    }

    try {
        stop_worker_ = std::thread([this]() noexcept {
            pipeline_.Join();
        });
    } catch (...) {
        pipeline_.Abort();
        return VRREC_STATUS_OUT_OF_MEMORY;
    }

    stop_requested_ = true;
    return VRREC_STATUS_OK;
}

void PipelineMediaBackend::Abort() noexcept
{
    pipeline_.Abort();
    JoinStopWorker();
}

void PipelineMediaBackend::JoinStopWorker() noexcept
{
    std::thread worker;
    {
        std::lock_guard lock(mutex_);
        if (!stop_worker_.joinable()) {
            return;
        }
        worker = std::move(stop_worker_);
    }
    worker.join();
}

}
