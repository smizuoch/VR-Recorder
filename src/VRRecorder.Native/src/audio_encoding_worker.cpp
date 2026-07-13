#include "audio_encoding_worker.hpp"

#include <limits>
#include <new>
#include <system_error>

namespace vrrecorder::native {

StereoAudioEncodingWorker::StereoAudioEncodingWorker(
    StereoAudioMixSource &source,
    StereoAudioEncoderSink &sink) noexcept
    : source_(source),
      sink_(sink),
      pump_(source, sink)
{
}

StereoAudioEncodingWorker::~StereoAudioEncodingWorker()
{
    Abort();
}

vrrec_status_t StereoAudioEncodingWorker::Start(
    std::size_t frame_count_48k) noexcept
{
    if (frame_count_48k == 0 || started_.exchange(true)) {
        return frame_count_48k == 0
            ? VRREC_STATUS_INVALID_ARGUMENT
            : VRREC_STATUS_INVALID_STATE;
    }

    frame_count_48k_ = frame_count_48k;
    try {
        thread_ = std::thread(&StereoAudioEncodingWorker::Run, this);
    } catch (const std::bad_alloc &) {
        SetResult(StereoAudioEncodingWorkerResult::Failed);
        finished_.store(true);
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (const std::system_error &) {
        SetResult(StereoAudioEncodingWorkerResult::Failed);
        finished_.store(true);
        return VRREC_STATUS_INTERNAL_ERROR;
    } catch (...) {
        SetResult(StereoAudioEncodingWorkerResult::Failed);
        finished_.store(true);
        return VRREC_STATUS_INTERNAL_ERROR;
    }

    return VRREC_STATUS_OK;
}

vrrec_status_t StereoAudioEncodingWorker::RequestStop() noexcept
{
    if (!started_.load() || abort_requested_.load() ||
        (finished_.load() && !stop_requested_.load())) {
        return VRREC_STATUS_INVALID_STATE;
    }

    if (!stop_requested_.exchange(true) && !finished_.load()) {
        source_.Abort();
    }

    return VRREC_STATUS_OK;
}

void StereoAudioEncodingWorker::Abort() noexcept
{
    if (finished_.load()) {
        JoinThread();
        return;
    }

    if (!abort_requested_.exchange(true)) {
        source_.Abort();
        sink_.Abort();
    }

    JoinThread();
}

StereoAudioEncodingWorkerResult StereoAudioEncodingWorker::Join() noexcept
{
    JoinThread();
    const std::lock_guard lock(state_mutex_);
    return result_;
}

std::uint64_t StereoAudioEncodingWorker::SubmittedFrameCount() const noexcept
{
    return pump_.SubmittedFrameCount();
}

std::uint64_t StereoAudioEncodingWorker::MuxedPacketCount() const noexcept
{
    const auto pumped = pump_.MuxedPacketCount();
    const auto flushed = flushed_packet_count_.load();
    return pumped > std::numeric_limits<std::uint64_t>::max() - flushed
        ? std::numeric_limits<std::uint64_t>::max()
        : pumped + flushed;
}

void StereoAudioEncodingWorker::Run() noexcept
{
    while (true) {
        StereoAudioEncodingRead read {};
        const auto result = pump_.PumpNext(frame_count_48k_, read);
        if (result == StereoAudioEncodingResult::Submitted) {
            continue;
        }

        if (result == StereoAudioEncodingResult::Aborted) {
            if (abort_requested_.load()) {
                SetResult(StereoAudioEncodingWorkerResult::Aborted);
            } else if (stop_requested_.load()) {
                const auto finish = sink_.Finish();
                if (finish.status == VRREC_STATUS_OK) {
                    flushed_packet_count_.store(
                        finish.muxed_packet_count);
                    SetResult(StereoAudioEncodingWorkerResult::Stopped);
                } else {
                    sink_.Abort();
                    SetResult(
                        finish.failure_stage ==
                                AudioEncoderFailureStage::Muxing
                            ? StereoAudioEncodingWorkerResult::MuxFailed
                            : StereoAudioEncodingWorkerResult::EncoderFailed);
                }
            } else {
                source_.Abort();
                sink_.Abort();
                SetResult(StereoAudioEncodingWorkerResult::CaptureFailed);
            }
        } else if (result == StereoAudioEncodingResult::EncoderFailed) {
            source_.Abort();
            sink_.Abort();
            SetResult(StereoAudioEncodingWorkerResult::EncoderFailed);
        } else if (result == StereoAudioEncodingResult::MuxFailed) {
            source_.Abort();
            sink_.Abort();
            SetResult(StereoAudioEncodingWorkerResult::MuxFailed);
        } else if (result == StereoAudioEncodingResult::CaptureFailed ||
                   result == StereoAudioEncodingResult::InvalidState) {
            source_.Abort();
            sink_.Abort();
            SetResult(StereoAudioEncodingWorkerResult::CaptureFailed);
        } else {
            source_.Abort();
            sink_.Abort();
            SetResult(StereoAudioEncodingWorkerResult::Failed);
        }

        finished_.store(true);
        return;
    }
}

void StereoAudioEncodingWorker::JoinThread() noexcept
{
    const std::lock_guard join_lock(join_mutex_);
    if (thread_.joinable() &&
        thread_.get_id() != std::this_thread::get_id()) {
        thread_.join();
    }
}

void StereoAudioEncodingWorker::SetResult(
    StereoAudioEncodingWorkerResult result) noexcept
{
    const std::lock_guard lock(state_mutex_);
    result_ = result;
}

}
