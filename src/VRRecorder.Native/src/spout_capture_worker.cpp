#include "spout_capture_worker.hpp"

#include <new>
#include <system_error>

namespace vrrecorder::native {

SpoutCaptureWorker::SpoutCaptureWorker(
    SpoutCaptureSource &source) noexcept
    : source_(source)
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

    if (started_.exchange(true)) {
        return VRREC_STATUS_INVALID_STATE;
    }

    poll_timeout_ = poll_timeout;
    try {
        thread_ = std::thread(&SpoutCaptureWorker::Run, this);
    } catch (const std::bad_alloc &) {
        SetResult(SpoutCaptureWorkerResult::Failed);
        finished_.store(true);
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (const std::system_error &) {
        SetResult(SpoutCaptureWorkerResult::Failed);
        finished_.store(true);
        return VRREC_STATUS_INTERNAL_ERROR;
    } catch (...) {
        SetResult(SpoutCaptureWorkerResult::Failed);
        finished_.store(true);
        return VRREC_STATUS_INTERNAL_ERROR;
    }

    return VRREC_STATUS_OK;
}

void SpoutCaptureWorker::Abort() noexcept
{
    if (finished_.load()) {
        JoinThread();
        return;
    }

    if (!aborted_.exchange(true)) {
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
        const auto result = source_.PollOne(poll_timeout_);
        if (result == SpoutCaptureResult::FrameAccepted ||
            result == SpoutCaptureResult::Timeout) {
            continue;
        }

        if (result == SpoutCaptureResult::Aborted && aborted_.load()) {
            SetResult(SpoutCaptureWorkerResult::Aborted);
        } else if (result == SpoutCaptureResult::SenderLost) {
            SetResult(SpoutCaptureWorkerResult::SenderLost);
        } else {
            source_.Abort();
            SetResult(SpoutCaptureWorkerResult::Failed);
        }

        finished_.store(true);
        return;
    }
}

void SpoutCaptureWorker::SetResult(
    SpoutCaptureWorkerResult result) noexcept
{
    const std::lock_guard lock(state_mutex_);
    result_ = result;
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
