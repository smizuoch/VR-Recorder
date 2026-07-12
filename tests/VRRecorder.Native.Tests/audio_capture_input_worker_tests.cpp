#include "audio_capture_input_worker.hpp"

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdlib>
#include <iostream>
#include <memory>
#include <mutex>
#include <thread>

namespace {

#define CHECK(condition)                                                        \
    do {                                                                        \
        if (!(condition)) {                                                     \
            std::cerr << "check failed at " << __FILE__ << ':' << __LINE__      \
                      << ": " #condition << '\n';                              \
            std::abort();                                                       \
        }                                                                       \
    } while (false)

using namespace vrrecorder::native;

struct SourceState final {
    std::mutex mutex;
    std::condition_variable changed;
    std::thread::id start_thread;
    std::thread::id read_thread;
    bool read_entered = false;
    bool aborted = false;
    std::atomic_int start_calls = 0;
    std::atomic_int read_calls = 0;
    std::atomic_int abort_calls = 0;
};

class BlockingSource final : public AudioCaptureSource {
public:
    BlockingSource(
        std::shared_ptr<SourceState> state,
        vrrec_status_t start_status)
        : state_(std::move(state)),
          start_status_(start_status)
    {
    }

    vrrec_status_t Start(
        const AudioCaptureSourceConfig &) noexcept override
    {
        ++state_->start_calls;
        const std::lock_guard lock(state_->mutex);
        state_->start_thread = std::this_thread::get_id();
        return start_status_;
    }

    AudioCaptureRead Read() noexcept override
    {
        ++state_->read_calls;
        std::unique_lock lock(state_->mutex);
        state_->read_thread = std::this_thread::get_id();
        state_->read_entered = true;
        state_->changed.notify_all();
        state_->changed.wait(lock, [&] { return state_->aborted; });
        return {AudioCaptureReadResult::Aborted, {}, 0};
    }

    void Abort() noexcept override
    {
        ++state_->abort_calls;
        {
            const std::lock_guard lock(state_->mutex);
            state_->aborted = true;
        }

        state_->changed.notify_all();
    }

private:
    std::shared_ptr<SourceState> state_;
    vrrec_status_t start_status_;
};

class SingleSourceProvider final : public AudioCaptureSourceProvider {
public:
    explicit SingleSourceProvider(std::unique_ptr<AudioCaptureSource> source)
        : source_(std::move(source))
    {
    }

    vrrec_status_t Create(
        std::unique_ptr<AudioCaptureSource> &source) noexcept override
    {
        if (source_ == nullptr) {
            return VRREC_STATUS_BACKEND_UNAVAILABLE;
        }

        source = std::move(source_);
        return VRREC_STATUS_OK;
    }

private:
    std::unique_ptr<AudioCaptureSource> source_;
};

class RecordingWaiter final : public AudioCaptureRecoveryWaiter {
public:
    bool Wait(std::chrono::milliseconds) noexcept override
    {
        return true;
    }

    void Abort() noexcept override
    {
        ++abort_calls;
    }

    std::atomic_int abort_calls = 0;
};

AudioCaptureSourceConfig Config()
{
    return {
        AudioCaptureRole::DesktopLoopback,
        "default-render",
        1'000'000,
    };
}

void StartsReadsAndAbortsOnAJoinedCaptureThread()
{
    const auto caller_thread = std::this_thread::get_id();
    auto state = std::make_shared<SourceState>();
    SingleSourceProvider provider(
        std::make_unique<BlockingSource>(state, VRREC_STATUS_OK));
    RecordingWaiter waiter;
    StereoCaptureTimeline timeline(16);
    AudioCaptureInputWorker worker(provider, waiter, timeline);

    CHECK(worker.Start(Config()) == VRREC_STATUS_OK);
    {
        std::unique_lock lock(state->mutex);
        state->changed.wait(lock, [&] { return state->read_entered; });
        CHECK(state->start_thread != caller_thread);
        CHECK(state->read_thread == state->start_thread);
    }

    worker.Abort();
    worker.Abort();

    CHECK(worker.Join() == AudioCaptureInputResult::Aborted);
    CHECK(state->start_calls == 1);
    CHECK(state->read_calls == 1);
    CHECK(state->abort_calls == 1);
    CHECK(waiter.abort_calls == 1);
    CHECK(worker.Start(Config()) == VRREC_STATUS_INVALID_STATE);
}

void ReturnsInitialFailureAfterJoiningTheCaptureThread()
{
    auto state = std::make_shared<SourceState>();
    SingleSourceProvider provider(std::make_unique<BlockingSource>(
        state,
        VRREC_STATUS_BACKEND_UNAVAILABLE));
    RecordingWaiter waiter;
    StereoCaptureTimeline timeline(16);
    AudioCaptureInputWorker worker(provider, waiter, timeline);

    CHECK(worker.Start(Config()) == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(worker.Join() == AudioCaptureInputResult::Failed);
    CHECK(state->start_calls == 1);
    CHECK(state->read_calls == 0);
    CHECK(state->abort_calls == 0);
}

void DestructorAbortsAndJoinsAnActiveCaptureThread()
{
    auto state = std::make_shared<SourceState>();
    SingleSourceProvider provider(
        std::make_unique<BlockingSource>(state, VRREC_STATUS_OK));
    RecordingWaiter waiter;
    StereoCaptureTimeline timeline(16);
    {
        AudioCaptureInputWorker worker(provider, waiter, timeline);
        CHECK(worker.Start(Config()) == VRREC_STATUS_OK);
        std::unique_lock lock(state->mutex);
        state->changed.wait(lock, [&] { return state->read_entered; });
    }

    CHECK(state->abort_calls == 1);
    CHECK(waiter.abort_calls == 1);
}

}

int main()
{
    StartsReadsAndAbortsOnAJoinedCaptureThread();
    ReturnsInitialFailureAfterJoiningTheCaptureThread();
    DestructorAbortsAndJoinsAnActiveCaptureThread();
    return 0;
}
