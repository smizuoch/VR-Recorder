#include "audio_capture_input_worker.hpp"

namespace vrrecorder::native {

AudioCaptureInputWorker::AudioCaptureInputWorker(
    AudioCaptureSourceProvider &provider,
    AudioCaptureRecoveryWaiter &waiter,
    StereoCaptureTimeline &timeline,
    AudioCaptureAvailabilitySink *availability_sink) noexcept
    : AudioCaptureInputWorker(
          provider,
          waiter,
          timeline,
          DefaultNativeThreadFactory(),
          availability_sink)
{
}

AudioCaptureInputWorker::AudioCaptureInputWorker(
    AudioCaptureSourceProvider &provider,
    AudioCaptureRecoveryWaiter &waiter,
    StereoCaptureTimeline &timeline,
    NativeThreadFactoryPort &thread_factory,
    AudioCaptureAvailabilitySink *availability_sink) noexcept
    : runner_(provider, waiter, timeline, availability_sink),
      thread_factory_(thread_factory)
{
}

AudioCaptureInputWorker::~AudioCaptureInputWorker()
{
    Abort();
}

vrrec_status_t AudioCaptureInputWorker::Start(
    const AudioCaptureSourceConfig &config) noexcept
{
    std::unique_lock launch_lock(join_mutex_);
    {
        const std::lock_guard lock(state_mutex_);
        if (started_ || abort_requested_.load()) {
            return VRREC_STATUS_INVALID_STATE;
        }

        started_ = true;
    }

    auto config_status = VRREC_STATUS_OK;
    try {
        config_ = config;
    } catch (const std::bad_alloc &) {
        config_status = VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        config_status = VRREC_STATUS_INTERNAL_ERROR;
    }
    if (config_status != VRREC_STATUS_OK) {
        const auto abort_won = abort_requested_.load();
        PublishStartFailure(
            abort_won ? VRREC_STATUS_INVALID_STATE : config_status,
            abort_won
                ? AudioCaptureInputResult::Aborted
                : AudioCaptureInputResult::Failed);
        return abort_won ? VRREC_STATUS_INVALID_STATE : config_status;
    }
    if (abort_requested_.load()) {
        PublishStartFailure(
            VRREC_STATUS_INVALID_STATE,
            AudioCaptureInputResult::Aborted);
        return VRREC_STATUS_INVALID_STATE;
    }

    const auto launch_status = thread_factory_.Start(
        thread_,
        &AudioCaptureInputWorker::RunEntry,
        this);
    const auto effective_launch_status =
        launch_status == VRREC_STATUS_OK && !thread_.joinable()
        ? VRREC_STATUS_INTERNAL_ERROR
        : launch_status;
    if (effective_launch_status != VRREC_STATUS_OK) {
        const auto abort_won = abort_requested_.load();
        PublishStartFailure(
            abort_won
                ? VRREC_STATUS_INVALID_STATE
                : effective_launch_status,
            abort_won
                ? AudioCaptureInputResult::Aborted
                : AudioCaptureInputResult::Failed);
        return abort_won
            ? VRREC_STATUS_INVALID_STATE
            : effective_launch_status;
    }

    runner_launched_ = true;
    launch_lock.unlock();
    std::unique_lock lock(state_mutex_);
    state_changed_.wait(lock, [this] {
        return start_reported_ || finished_;
    });
    auto status = start_reported_
        ? start_status_
        : VRREC_STATUS_INTERNAL_ERROR;
    if (abort_requested_.load()) {
        status = VRREC_STATUS_INVALID_STATE;
    }
    lock.unlock();
    if (status != VRREC_STATUS_OK) {
        JoinThread();
    }

    return status;
}

void AudioCaptureInputWorker::RunEntry(void *context) noexcept
{
    static_cast<AudioCaptureInputWorker *>(context)->Run();
}

void AudioCaptureInputWorker::Abort() noexcept
{
    abort_requested_.store(true);
    const std::lock_guard join_lock(join_mutex_);
    if (!runner_launched_) {
        return;
    }
    runner_.Abort();
    if (thread_.joinable() &&
        thread_.get_id() != std::this_thread::get_id()) {
        thread_.join();
    }
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

void AudioCaptureInputWorker::PublishStartFailure(
    vrrec_status_t status,
    AudioCaptureInputResult result) noexcept
{
    {
        const std::lock_guard lock(state_mutex_);
        start_status_ = status;
        start_reported_ = true;
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
