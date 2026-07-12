#include "video_encoding_worker.hpp"

#include <algorithm>
#include <limits>
#include <new>
#include <system_error>

namespace vrrecorder::native {

VideoEncodingWorker::VideoEncodingWorker(
    VideoCfrScheduler &scheduler,
    VideoCfrClock &clock,
    VideoEncoderSink &sink,
    MediaEventSink &events) noexcept
    : clock_(clock),
      sink_(sink),
      events_(events),
      pump_(scheduler, sink)
{
}

VideoEncodingWorker::~VideoEncodingWorker()
{
    Abort();
}

vrrec_status_t VideoEncodingWorker::Start() noexcept
{
    if (started_.exchange(true)) {
        return VRREC_STATUS_INVALID_STATE;
    }

    try {
        thread_ = std::thread(&VideoEncodingWorker::Run, this);
    } catch (const std::bad_alloc &) {
        SetResult(VideoEncodingWorkerResult::Failed);
        finished_.store(true);
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (const std::system_error &) {
        SetResult(VideoEncodingWorkerResult::Failed);
        finished_.store(true);
        return VRREC_STATUS_INTERNAL_ERROR;
    } catch (...) {
        SetResult(VideoEncodingWorkerResult::Failed);
        finished_.store(true);
        return VRREC_STATUS_INTERNAL_ERROR;
    }

    return VRREC_STATUS_OK;
}

vrrec_status_t VideoEncodingWorker::RequestStop() noexcept
{
    if (!started_.load() || abort_requested_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }

    if (!stop_requested_.exchange(true) && !finished_.load()) {
        clock_.Abort();
    }

    return VRREC_STATUS_OK;
}

void VideoEncodingWorker::Abort() noexcept
{
    if (finished_.load()) {
        JoinThread();
        return;
    }

    if (!abort_requested_.exchange(true)) {
        clock_.Abort();
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
                    "video CFR clock aborted unexpectedly");
            }

            finished_.store(true);
            return;
        }

        if (clock_result != VideoCfrClockResult::Tick) {
            sink_.Abort();
            Fail(
                VideoEncodingWorkerResult::ClockFailed,
                VRREC_STATUS_INTERNAL_ERROR,
                "video CFR clock failed");
            finished_.store(true);
            return;
        }

        VideoEncodingRead read {};
        const auto encoding_result = pump_.PumpTick(tick, read);
        if (encoding_result == VideoEncodingResult::Submitted) {
            if (read.first_packet_muxed &&
                !first_packet_reported_.exchange(true)) {
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

        clock_.Abort();
        sink_.Abort();
        if (encoding_result == VideoEncodingResult::EncoderFailed) {
            Fail(
                VideoEncodingWorkerResult::EncoderFailed,
                read.encoder_status,
                "video encoder failed while recording");
        } else if (encoding_result == VideoEncodingResult::ProcessorFailed) {
            Fail(
                VideoEncodingWorkerResult::Failed,
                read.encoder_status,
                "video frame processing failed while recording");
        } else if (encoding_result == VideoEncodingResult::MuxFailed) {
            Fail(
                VideoEncodingWorkerResult::Failed,
                read.encoder_status,
                "video packet muxing failed while recording");
        } else if (encoding_result == VideoEncodingResult::SurfaceFailed) {
            Fail(
                VideoEncodingWorkerResult::Failed,
                VRREC_STATUS_BACKEND_UNAVAILABLE,
                "video surface synchronization failed");
        } else {
            Fail(
                VideoEncodingWorkerResult::Failed,
                VRREC_STATUS_INTERNAL_ERROR,
                "video scheduling failed while recording");
        }

        finished_.store(true);
        return;
    }
}

void VideoEncodingWorker::Finish() noexcept
{
    const auto finish = sink_.Finish();
    if (finish.status != VRREC_STATUS_OK) {
        sink_.Abort();
        Fail(
            VideoEncodingWorkerResult::EncoderFailed,
            finish.status,
            "video encoder flush failed");
        return;
    }

    flushed_packet_count_.store(finish.muxed_packet_count);
    flushed_latency_microseconds_.store(
        finish.encode_latency_microseconds);
    if (finish.muxed_packet_count > 0 &&
        !first_packet_reported_.exchange(true)) {
        events_.FirstVideoPacketMuxed();
    }

    SetResult(VideoEncodingWorkerResult::Stopped);
}

void VideoEncodingWorker::Fail(
    VideoEncodingWorkerResult result,
    vrrec_status_t status,
    const char *message) noexcept
{
    SetResult(result);
    events_.Faulted(status, message);
}

void VideoEncodingWorker::SetResult(
    VideoEncodingWorkerResult result) noexcept
{
    const std::lock_guard lock(state_mutex_);
    result_ = result;
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
