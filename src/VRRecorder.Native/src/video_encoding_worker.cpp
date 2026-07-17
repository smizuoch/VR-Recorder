#include "video_encoding_worker.hpp"

#include <algorithm>
#include <limits>

namespace vrrecorder::native {

VideoEncodingWorker::VideoEncodingWorker(
    VideoCfrScheduler &scheduler,
    VideoCfrClock &clock,
    VideoEncoderSink &sink,
    MediaEventSink &events) noexcept
    : VideoEncodingWorker(
          scheduler,
          clock,
          sink,
          events,
          DefaultNativeThreadFactory())
{
}

VideoEncodingWorker::VideoEncodingWorker(
    VideoCfrScheduler &scheduler,
    VideoCfrClock &clock,
    VideoFramePreparingEncoderSink &sink,
    MediaEventSink &events) noexcept
    : VideoEncodingWorker(
          scheduler,
          clock,
          sink,
          events,
          DefaultNativeThreadFactory())
{
}

VideoEncodingWorker::VideoEncodingWorker(
    VideoCfrScheduler &scheduler,
    VideoCfrClock &clock,
    VideoEncoderSink &sink,
    MediaEventSink &events,
    NativeThreadFactoryPort &thread_factory) noexcept
    : clock_(clock),
      sink_(sink),
      events_(events),
      thread_factory_(thread_factory),
      pump_(scheduler, sink)
{
}

VideoEncodingWorker::VideoEncodingWorker(
    VideoCfrScheduler &scheduler,
    VideoCfrClock &clock,
    VideoFramePreparingEncoderSink &sink,
    MediaEventSink &events,
    NativeThreadFactoryPort &thread_factory) noexcept
    : clock_(clock),
      sink_(sink),
      events_(events),
      thread_factory_(thread_factory),
      pump_(scheduler, sink)
{
}

VideoEncodingWorker::~VideoEncodingWorker()
{
    Abort();
}

vrrec_status_t VideoEncodingWorker::Start() noexcept
{
    std::unique_lock launch_lock(join_mutex_);
    if (started_.exchange(true) || abort_requested_.load() ||
        finished_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }

    const auto launch_status = thread_factory_.Start(
        thread_,
        &VideoEncodingWorker::RunEntry,
        this);
    const auto effective_launch_status =
        launch_status == VRREC_STATUS_OK && !thread_.joinable()
        ? VRREC_STATUS_INTERNAL_ERROR
        : launch_status;
    if (effective_launch_status != VRREC_STATUS_OK) {
        SetResult(VideoEncodingWorkerResult::Failed);
        return abort_requested_.load()
            ? VRREC_STATUS_INVALID_STATE
            : effective_launch_status;
    }

    launch_lock.unlock();
    return abort_requested_.load()
        ? VRREC_STATUS_INVALID_STATE
        : VRREC_STATUS_OK;
}

void VideoEncodingWorker::RunEntry(void *context) noexcept
{
    static_cast<VideoEncodingWorker *>(context)->Run();
}

vrrec_status_t VideoEncodingWorker::RequestStop() noexcept
{
    if (!started_.load() || abort_requested_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }

    if (finished_.load()) {
        const std::lock_guard lock(state_mutex_);
        return result_ ==
                VideoEncodingWorkerResult::EncoderFailedPartSealed
            ? VRREC_STATUS_OK
            : VRREC_STATUS_INVALID_STATE;
    }

    if (!stop_requested_.exchange(true) && !finished_.load()) {
        clock_.Abort();
    }

    return VRREC_STATUS_OK;
}

void VideoEncodingWorker::Abort() noexcept
{
    RequestAbort();
    JoinAfterAbort();
}

void VideoEncodingWorker::RequestAbort() noexcept
{
    auto abort_clock = false;
    {
        const std::lock_guard lock(state_mutex_);
        if (!finished_.load()) {
            abort_requested_.store(true);
            result_ = VideoEncodingWorkerResult::Aborted;
            finished_.store(true);
            abort_clock = true;
        }
    }
    if (abort_clock) {
        clock_.Abort();
    }
}

void VideoEncodingWorker::JoinAfterAbort() noexcept
{
    if (abort_requested_.load() &&
        !abort_cleanup_started_.exchange(true)) {
        sink_.Abort();
    }
    JoinThread();
}

VideoEncodingWorkerResult VideoEncodingWorker::Join() noexcept
{
    JoinThread();
    const std::lock_guard lock(state_mutex_);
    return result_;
}

VideoEncodingStatistics VideoEncodingWorker::Statistics() const noexcept
{
    auto statistics = pump_.Statistics();
    statistics.muxed_packet_count =
        committed_muxed_packet_count_.load();
    statistics.latest_encode_latency_microseconds =
        committed_latest_latency_microseconds_.load();
    statistics.maximum_encode_latency_microseconds =
        committed_maximum_latency_microseconds_.load();
    const auto flushed_packets = flushed_packet_count_.load();
    statistics.muxed_packet_count =
        statistics.muxed_packet_count >
                std::numeric_limits<std::uint64_t>::max() - flushed_packets
        ? std::numeric_limits<std::uint64_t>::max()
        : statistics.muxed_packet_count + flushed_packets;
    if (flushed_latency_microseconds_.load() > 0) {
        statistics.latest_encode_latency_microseconds =
            flushed_latency_microseconds_.load();
        statistics.maximum_encode_latency_microseconds = std::max(
            statistics.maximum_encode_latency_microseconds,
            flushed_latency_microseconds_.load());
    }

    return statistics;
}

void VideoEncodingWorker::Run() noexcept
{
    while (true) {
        std::uint64_t tick = 0;
        const auto clock_result = clock_.WaitNext(tick);
        if (clock_result == VideoCfrClockResult::Aborted) {
            if (abort_requested_.load()) {
                SetResult(VideoEncodingWorkerResult::Aborted);
            } else if (stop_requested_.load()) {
                Finish();
            } else {
                Fail(
                    VideoEncodingWorkerResult::ClockFailed,
                    VRREC_STATUS_INTERNAL_ERROR,
                    "video CFR clock aborted unexpectedly",
                    false);
            }

            return;
        }

        if (clock_result != VideoCfrClockResult::Tick) {
            Fail(
                VideoEncodingWorkerResult::ClockFailed,
                VRREC_STATUS_INTERNAL_ERROR,
                "video CFR clock failed",
                false);
            return;
        }

        if (abort_requested_.load()) {
            SetResult(VideoEncodingWorkerResult::Aborted);
            return;
        }
        if (stop_requested_.load()) {
            Finish();
            return;
        }

        VideoEncodingRead read {};
        const auto encoding_result = pump_.PumpTick(tick, read);
        if (abort_requested_.load()) {
            SetResult(VideoEncodingWorkerResult::Aborted);
            return;
        }
        if (encoding_result == VideoEncodingResult::Submitted) {
            auto report_first_packet = false;
            if (!CommitSubmitted(read, report_first_packet)) {
                return;
            }
            if (report_first_packet) {
                events_.FirstVideoPacketMuxed();
            }

            continue;
        }

        if (encoding_result == VideoEncodingResult::NoFrame) {
            continue;
        }

        if (encoding_result == VideoEncodingResult::SurfaceTimeout) {
            continue;
        }

        if (encoding_result == VideoEncodingResult::EncoderFailed) {
            if (read.part_sealed_after_encoder_failure) {
                ReportSealedEncoderFailure(
                    read.encoder_status,
                    "video encoder failed after the current part was sealed",
                    true);
            } else {
                Fail(
                    VideoEncodingWorkerResult::EncoderFailed,
                    read.encoder_status,
                    "video encoder failed while recording",
                    true);
            }
        } else if (encoding_result == VideoEncodingResult::ProcessorFailed) {
            Fail(
                VideoEncodingWorkerResult::Failed,
                read.encoder_status,
                "video frame processing failed while recording",
                true);
        } else if (encoding_result == VideoEncodingResult::MuxFailed) {
            Fail(
                VideoEncodingWorkerResult::Failed,
                read.encoder_status,
                "video packet muxing failed while recording",
                true);
        } else if (encoding_result ==
                   VideoEncodingResult::SurfaceAbandoned) {
            Fail(
                VideoEncodingWorkerResult::SurfaceAbandoned,
                VRREC_STATUS_BACKEND_UNAVAILABLE,
                "video surface synchronization was abandoned",
                true);
        } else if (encoding_result ==
                   VideoEncodingResult::SurfaceDeviceRemoved) {
            Fail(
                VideoEncodingWorkerResult::SurfaceDeviceRemoved,
                VRREC_STATUS_BACKEND_UNAVAILABLE,
                "video device was removed",
                true);
        } else if (encoding_result ==
                   VideoEncodingResult::SurfaceDeviceReset) {
            Fail(
                VideoEncodingWorkerResult::SurfaceDeviceReset,
                VRREC_STATUS_BACKEND_UNAVAILABLE,
                "video device was reset",
                true);
        } else if (encoding_result == VideoEncodingResult::SurfaceFailed) {
            Fail(
                VideoEncodingWorkerResult::Failed,
                VRREC_STATUS_BACKEND_UNAVAILABLE,
                "video surface synchronization failed",
                true);
        } else {
            Fail(
                VideoEncodingWorkerResult::Failed,
                VRREC_STATUS_INTERNAL_ERROR,
                "video scheduling failed while recording",
                true);
        }

        return;
    }
}

void VideoEncodingWorker::Finish() noexcept
{
    const auto finish = sink_.Finish();
    if (abort_requested_.load()) {
        SetResult(VideoEncodingWorkerResult::Aborted);
        return;
    }
    if (finish.status != VRREC_STATUS_OK) {
        if (finish.part_sealed_after_encoder_failure) {
            ReportSealedEncoderFailure(
                finish.status,
                "video encoder flush failed after the current part was sealed",
                false);
        } else {
            Fail(
                VideoEncodingWorkerResult::EncoderFailed,
                finish.status,
                "video encoder flush failed",
                false);
        }
        return;
    }

    auto report_first_packet = false;
    if (!CommitStopped(finish, report_first_packet)) {
        return;
    }
    if (report_first_packet) {
        events_.FirstVideoPacketMuxed();
    }
}

void VideoEncodingWorker::Fail(
    VideoEncodingWorkerResult result,
    vrrec_status_t status,
    const char *message,
    bool abort_clock) noexcept
{
    if (!SetResult(result)) {
        return;
    }
    if (abort_clock) {
        clock_.Abort();
    }
    sink_.Abort();
    events_.Faulted(status, message);
}

void VideoEncodingWorker::ReportSealedEncoderFailure(
    vrrec_status_t status,
    const char *message,
    bool abort_clock) noexcept
{
    if (!SetResult(
            VideoEncodingWorkerResult::EncoderFailedPartSealed)) {
        return;
    }
    if (abort_clock) {
        clock_.Abort();
    }
    events_.VideoEncoderFailed(status, message);
}

bool VideoEncodingWorker::SetResult(
    VideoEncodingWorkerResult result) noexcept
{
    const std::lock_guard lock(state_mutex_);
    if (finished_.load()) {
        return false;
    }

    result_ = result;
    finished_.store(true);
    return true;
}

bool VideoEncodingWorker::CommitStopped(
    const VideoEncoderWrite &finish,
    bool &report_first_packet) noexcept
{
    const std::lock_guard lock(state_mutex_);
    if (finished_.load()) {
        return false;
    }

    flushed_packet_count_.store(finish.muxed_packet_count);
    flushed_latency_microseconds_.store(
        finish.encode_latency_microseconds);
    report_first_packet = finish.muxed_packet_count > 0 &&
        !first_packet_reported_.exchange(true);
    result_ = VideoEncodingWorkerResult::Stopped;
    finished_.store(true);
    return true;
}

bool VideoEncodingWorker::CommitSubmitted(
    const VideoEncodingRead &read,
    bool &report_first_packet) noexcept
{
    const std::lock_guard lock(state_mutex_);
    if (finished_.load()) {
        return false;
    }

    committed_muxed_packet_count_.store(
        committed_muxed_packet_count_.load() + read.muxed_packet_count);
    committed_latest_latency_microseconds_.store(
        read.encode_latency_microseconds);
    committed_maximum_latency_microseconds_.store(std::max(
        committed_maximum_latency_microseconds_.load(),
        read.encode_latency_microseconds));
    report_first_packet = read.first_packet_muxed &&
        !first_packet_reported_.exchange(true);
    return true;
}

void VideoEncodingWorker::JoinThread() noexcept
{
    const std::lock_guard join_lock(join_mutex_);
    if (thread_.joinable() &&
        thread_.get_id() != std::this_thread::get_id()) {
        thread_.join();
    }
}

}
