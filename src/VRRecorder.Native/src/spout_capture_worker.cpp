#include "spout_capture_worker.hpp"

namespace vrrecorder::native {

SpoutCaptureWorker::SpoutCaptureWorker(
    SpoutCaptureSource &source) noexcept
    : SpoutCaptureWorker(source, DefaultNativeThreadFactory())
{
}

SpoutCaptureWorker::SpoutCaptureWorker(
    SpoutCaptureSource &source,
    NativeThreadFactoryPort &thread_factory) noexcept
    : source_(source),
      thread_factory_(thread_factory)
{
}

SpoutCaptureWorker::~SpoutCaptureWorker()
{
    Abort();
}

vrrec_status_t SpoutCaptureWorker::Start(
    std::chrono::milliseconds poll_timeout) noexcept
{
    if (poll_timeout <= std::chrono::milliseconds {0} ||
        poll_timeout > std::chrono::milliseconds(
            VRREC_SPOUT_MAX_POLL_TIMEOUT_MILLISECONDS)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    std::unique_lock launch_lock(join_mutex_);
    if (started_.exchange(true) || aborted_.load() || finished_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }

    poll_timeout_ = poll_timeout;
    const auto launch_status = thread_factory_.Start(
        thread_,
        &SpoutCaptureWorker::RunEntry,
        this);
    const auto effective_launch_status =
        launch_status == VRREC_STATUS_OK && !thread_.joinable()
        ? VRREC_STATUS_INTERNAL_ERROR
        : launch_status;
    if (effective_launch_status != VRREC_STATUS_OK) {
        SetResult(SpoutCaptureWorkerResult::Failed);
        return aborted_.load()
            ? VRREC_STATUS_INVALID_STATE
            : effective_launch_status;
    }

    launch_lock.unlock();
    return aborted_.load()
        ? VRREC_STATUS_INVALID_STATE
        : VRREC_STATUS_OK;
}

void SpoutCaptureWorker::RunEntry(void *context) noexcept
{
    static_cast<SpoutCaptureWorker *>(context)->Run();
}

void SpoutCaptureWorker::Abort() noexcept
{
    auto abort_source = false;
    {
        const std::lock_guard lock(state_mutex_);
        if (!finished_.load()) {
            aborted_.store(true);
            result_ = SpoutCaptureWorkerResult::Aborted;
            finished_.store(true);
            abort_source = true;
        }
    }

    if (abort_source) {
        source_.Abort();
    }
    JoinThread();
}

SpoutCaptureWorkerResult SpoutCaptureWorker::Join() noexcept
{
    JoinThread();
    const std::lock_guard lock(state_mutex_);
    return result_;
}

void SpoutCaptureWorker::Run() noexcept
{
    while (true) {
        if (aborted_.load()) {
            SetResult(SpoutCaptureWorkerResult::Aborted);
            return;
        }

        const auto result = source_.PollOne(poll_timeout_);
        if (aborted_.load()) {
            SetResult(SpoutCaptureWorkerResult::Aborted);
            return;
        }
        if (result == SpoutCaptureResult::FrameAccepted ||
            result == SpoutCaptureResult::Timeout ||
            result == SpoutCaptureResult::StaleFrame) {
            continue;
        }

        if (result == SpoutCaptureResult::SenderLost) {
            if (SetResult(SpoutCaptureWorkerResult::SenderLost)) {
                source_.Abort();
            }
        } else if (result == SpoutCaptureResult::AdapterChanged) {
            if (SetResult(SpoutCaptureWorkerResult::AdapterChanged)) {
                source_.Abort();
            }
        } else {
            if (SetResult(SpoutCaptureWorkerResult::Failed)) {
                source_.Abort();
            }
        }

        return;
    }
}

bool SpoutCaptureWorker::SetResult(
    SpoutCaptureWorkerResult result) noexcept
{
    const std::lock_guard lock(state_mutex_);
    if (finished_.load()) {
        return false;
    }

    result_ = result;
    finished_.store(true);
    return true;
}

void SpoutCaptureWorker::JoinThread() noexcept
{
    const std::lock_guard join_lock(join_mutex_);
    if (thread_.joinable() &&
        thread_.get_id() != std::this_thread::get_id()) {
        thread_.join();
    }
}

}
