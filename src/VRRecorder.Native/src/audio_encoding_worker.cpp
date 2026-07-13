#include "audio_encoding_worker.hpp"

#include <limits>

namespace vrrecorder::native {

StereoAudioEncodingWorker::StereoAudioEncodingWorker(
    StereoAudioMixSource &source,
    StereoAudioEncoderSink &sink) noexcept
    : StereoAudioEncodingWorker(
          source,
          sink,
          DefaultNativeThreadFactory())
{
}

StereoAudioEncodingWorker::StereoAudioEncodingWorker(
    StereoAudioMixSource &source,
    StereoAudioEncoderSink &sink,
    NativeThreadFactoryPort &thread_factory) noexcept
    : source_(source),
      sink_(sink),
      thread_factory_(thread_factory),
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
    if (frame_count_48k == 0) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    std::unique_lock launch_lock(join_mutex_);
    if (started_.exchange(true) || abort_requested_.load() ||
        terminal_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }

    frame_count_48k_ = frame_count_48k;
    const auto launch_status = thread_factory_.Start(
        thread_,
        &StereoAudioEncodingWorker::RunEntry,
        this);
    const auto effective_launch_status =
        launch_status == VRREC_STATUS_OK && !thread_.joinable()
        ? VRREC_STATUS_INTERNAL_ERROR
        : launch_status;
    if (effective_launch_status != VRREC_STATUS_OK) {
        SetResult(StereoAudioEncodingWorkerResult::Failed);
        return abort_requested_.load()
            ? VRREC_STATUS_INVALID_STATE
            : effective_launch_status;
    }

    launch_lock.unlock();
    return abort_requested_.load()
        ? VRREC_STATUS_INVALID_STATE
        : VRREC_STATUS_OK;
}

void StereoAudioEncodingWorker::RunEntry(void *context) noexcept
{
    static_cast<StereoAudioEncodingWorker *>(context)->Run();
}

vrrec_status_t StereoAudioEncodingWorker::RequestStop() noexcept
{
    if (!started_.load() || abort_requested_.load() ||
        (terminal_.load() && !stop_requested_.load())) {
        return VRREC_STATUS_INVALID_STATE;
    }

    if (!stop_requested_.exchange(true) && !terminal_.load()) {
        source_.Abort();
    }

    return VRREC_STATUS_OK;
}

void StereoAudioEncodingWorker::Abort() noexcept
{
    RequestAbort();
    JoinAfterAbort();
}

bool StereoAudioEncodingWorker::RequestAbort() noexcept
{
    const std::lock_guard lock(state_mutex_);
    if (!terminal_.load()) {
        abort_requested_.store(true);
        result_ = StereoAudioEncodingWorkerResult::Aborted;
        terminal_.store(true);
    }
    return abort_requested_.load();
}

void StereoAudioEncodingWorker::JoinAfterAbort() noexcept
{
    if (abort_requested_.load() &&
        !abort_cleanup_started_.exchange(true)) {
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
    return submitted_frame_count_.load();
}

std::uint64_t StereoAudioEncodingWorker::MuxedPacketCount() const noexcept
{
    const auto submitted = muxed_packet_count_.load();
    const auto flushed = flushed_packet_count_.load();
    return submitted > std::numeric_limits<std::uint64_t>::max() - flushed
        ? std::numeric_limits<std::uint64_t>::max()
        : submitted + flushed;
}

bool StereoAudioEncodingWorker::IsFinished() const noexcept
{
    return terminal_.load();
}

void StereoAudioEncodingWorker::Run() noexcept
{
    while (true) {
        if (abort_requested_.load()) {
            SetResult(StereoAudioEncodingWorkerResult::Aborted);
            return;
        }

        StereoAudioEncodingRead read {};
        const auto result = pump_.PumpNext(frame_count_48k_, read);
        if (abort_requested_.load()) {
            SetResult(StereoAudioEncodingWorkerResult::Aborted);
            return;
        }
        if (result == StereoAudioEncodingResult::Submitted) {
            if (!CommitSubmitted(read)) {
                return;
            }
            continue;
        }

        if (result == StereoAudioEncodingResult::Aborted) {
            if (abort_requested_.load()) {
                SetResult(StereoAudioEncodingWorkerResult::Aborted);
            } else if (stop_requested_.load()) {
                const auto finish = sink_.Finish();
                if (abort_requested_.load()) {
                    SetResult(StereoAudioEncodingWorkerResult::Aborted);
                } else if (finish.status == VRREC_STATUS_OK) {
                    CommitStopped(finish.muxed_packet_count);
                } else {
                    const auto failure_result = finish.failure_stage ==
                            AudioEncoderFailureStage::Muxing
                        ? StereoAudioEncodingWorkerResult::MuxFailed
                        : StereoAudioEncodingWorkerResult::EncoderFailed;
                    if (SetResult(failure_result)) {
                        sink_.Abort();
                    }
                }
            } else {
                if (SetResult(
                        StereoAudioEncodingWorkerResult::CaptureFailed)) {
                    source_.Abort();
                    sink_.Abort();
                }
            }
        } else if (result == StereoAudioEncodingResult::EncoderFailed) {
            if (SetResult(
                    StereoAudioEncodingWorkerResult::EncoderFailed)) {
                source_.Abort();
                sink_.Abort();
            }
        } else if (result == StereoAudioEncodingResult::MuxFailed) {
            if (SetResult(StereoAudioEncodingWorkerResult::MuxFailed)) {
                source_.Abort();
                sink_.Abort();
            }
        } else if (result == StereoAudioEncodingResult::CaptureFailed ||
                   result == StereoAudioEncodingResult::InvalidState) {
            if (SetResult(
                    StereoAudioEncodingWorkerResult::CaptureFailed)) {
                source_.Abort();
                sink_.Abort();
            }
        } else {
            if (SetResult(StereoAudioEncodingWorkerResult::Failed)) {
                source_.Abort();
                sink_.Abort();
            }
        }

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

bool StereoAudioEncodingWorker::SetResult(
    StereoAudioEncodingWorkerResult result) noexcept
{
    const std::lock_guard lock(state_mutex_);
    if (terminal_.load()) {
        return false;
    }

    result_ = result;
    terminal_.store(true);
    return true;
}

bool StereoAudioEncodingWorker::CommitSubmitted(
    const StereoAudioEncodingRead &read) noexcept
{
    const std::lock_guard lock(state_mutex_);
    if (terminal_.load()) {
        return false;
    }

    submitted_frame_count_.store(
        submitted_frame_count_.load() + read.mix.frame_count_48k);
    muxed_packet_count_.store(
        muxed_packet_count_.load() + read.muxed_packet_count);
    return true;
}

bool StereoAudioEncodingWorker::CommitStopped(
    std::uint64_t flushed_packet_count) noexcept
{
    const std::lock_guard lock(state_mutex_);
    if (terminal_.load()) {
        return false;
    }

    flushed_packet_count_.store(flushed_packet_count);
    result_ = StereoAudioEncodingWorkerResult::Stopped;
    terminal_.store(true);
    return true;
}

}
