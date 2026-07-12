#include "audio_capture_input_worker.hpp"

#include <new>
#include <system_error>

namespace vrrecorder::native {

AudioCaptureInputWorker::AudioCaptureInputWorker(
    AudioCaptureSourceProvider &provider,
    AudioCaptureRecoveryWaiter &waiter,
    StereoCaptureTimeline &timeline) noexcept
    : runner_(provider, waiter, timeline)
{
}

AudioCaptureInputWorker::~AudioCaptureInputWorker()
{
    Abort();
}

vrrec_status_t AudioCaptureInputWorker::Start(
    const AudioCaptureSourceConfig &config) noexcept
{
    {
        const std::lock_guard lock(state_mutex_);
        if (started_) {
            return VRREC_STATUS_INVALID_STATE;
        }

        started_ = true;
        try {
            config_ = config;
        } catch (const std::bad_alloc &) {
            return VRREC_STATUS_OUT_OF_MEMORY;
        } catch (...) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
    }

    try {
        thread_ = std::thread(&AudioCaptureInputWorker::Run, this);
    } catch (const std::system_error &) {
        return VRREC_STATUS_INTERNAL_ERROR;
    } catch (const std::bad_alloc &) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }

    std::unique_lock lock(state_mutex_);
    state_changed_.wait(lock, [this] {
        return start_reported_ || finished_;
    });
    const auto status = start_reported_
        ? start_status_
        : VRREC_STATUS_INTERNAL_ERROR;
    lock.unlock();
    if (status != VRREC_STATUS_OK) {
        JoinThread();
    }

    return status;
}

void AudioCaptureInputWorker::Abort() noexcept
{
    runner_.Abort();
    JoinThread();
}

AudioCaptureInputResult AudioCaptureInputWorker::Join() noexcept
{
    JoinThread();
    const std::lock_guard lock(state_mutex_);
    return result_;
}

void AudioCaptureInputWorker::Started(vrrec_status_t status) noexcept
{
    {
        const std::lock_guard lock(state_mutex_);
        if (start_reported_) {
            return;
        }

        start_status_ = status;
        start_reported_ = true;
    }

    state_changed_.notify_all();
}

void AudioCaptureInputWorker::Run() noexcept
{
    const auto result = runner_.Run(config_, *this);
    {
        const std::lock_guard lock(state_mutex_);
        result_ = result;
        finished_ = true;
    }

    state_changed_.notify_all();
}

void AudioCaptureInputWorker::JoinThread() noexcept
{
    const std::lock_guard join_lock(join_mutex_);
    if (thread_.joinable() &&
        thread_.get_id() != std::this_thread::get_id()) {
        thread_.join();
    }
}

}
