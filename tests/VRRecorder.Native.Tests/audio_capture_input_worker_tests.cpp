#include "audio_capture_input_worker.hpp"

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdlib>
#include <future>
#include <iostream>
#include <memory>
#include <mutex>
#include <thread>
#include <utility>
#include <vector>

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
        ++create_calls;
        if (source_ == nullptr) {
            return VRREC_STATUS_BACKEND_UNAVAILABLE;
        }

        source = std::move(source_);
        return VRREC_STATUS_OK;
    }

    std::atomic_int create_calls = 0;

private:
    std::unique_ptr<AudioCaptureSource> source_;
};

class ScriptedThreadFactory final : public NativeThreadFactoryPort {
public:
    explicit ScriptedThreadFactory(
        std::vector<vrrec_status_t> statuses,
        bool create_thread_on_success = true)
        : statuses_(std::move(statuses)),
          create_thread_on_success_(create_thread_on_success)
    {
    }

    vrrec_status_t Start(
        std::thread &thread,
        NativeThreadEntry entry,
        void *context) noexcept override
    {
        ++start_calls;
        const auto status = next_status_ < statuses_.size()
            ? statuses_[next_status_++]
            : VRREC_STATUS_INTERNAL_ERROR;
        if (status == VRREC_STATUS_OK && create_thread_on_success_) {
            thread = std::thread(entry, context);
        }
        return status;
    }

    std::size_t start_calls = 0;

private:
    std::vector<vrrec_status_t> statuses_;
    std::size_t next_status_ = 0;
    bool create_thread_on_success_;
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

    worker.Abort();
    CHECK(waiter.abort_calls == 1);
    std::vector<float> output(2, 1.0F);
    AudioTimelineRead read {};
    auto timeline_read = std::async(std::launch::async, [&] {
        return timeline.WaitRead(1, output, read);
    });
    CHECK(timeline_read.wait_for(std::chrono::seconds(1)) ==
          std::future_status::ready);
    CHECK(timeline_read.get() == AudioTimelineResult::Aborted);
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

void ThreadCreationFailureIsTerminal(
    vrrec_status_t factory_status,
    vrrec_status_t expected_status,
    bool create_thread_on_success = true)
{
    auto state = std::make_shared<SourceState>();
    SingleSourceProvider provider(
        std::make_unique<BlockingSource>(state, VRREC_STATUS_OK));
    RecordingWaiter waiter;
    StereoCaptureTimeline timeline(16);
    ScriptedThreadFactory thread_factory(
        {factory_status},
        create_thread_on_success);
    AudioCaptureInputWorker worker(
        provider,
        waiter,
        timeline,
        thread_factory);

    CHECK(worker.Start(Config()) == expected_status);
    CHECK(thread_factory.start_calls == 1);
    CHECK(provider.create_calls == 0);
    CHECK(state->start_calls == 0);
    CHECK(state->read_calls == 0);
    CHECK(state->abort_calls == 0);
    CHECK(worker.Join() == AudioCaptureInputResult::Failed);
    CHECK(worker.Join() == AudioCaptureInputResult::Failed);
    CHECK(worker.Start(Config()) == VRREC_STATUS_INVALID_STATE);
    CHECK(thread_factory.start_calls == 1);

    worker.Abort();
    CHECK(worker.Join() == AudioCaptureInputResult::Failed);
    CHECK(waiter.abort_calls == 0);
    CHECK(state->abort_calls == 0);
}

void OutOfMemoryThreadCreationIsTerminalFailure()
{
    ThreadCreationFailureIsTerminal(
        VRREC_STATUS_OUT_OF_MEMORY,
        VRREC_STATUS_OUT_OF_MEMORY);
}

void InternalThreadCreationFailureIsTerminalFailure()
{
    ThreadCreationFailureIsTerminal(
        VRREC_STATUS_INTERNAL_ERROR,
        VRREC_STATUS_INTERNAL_ERROR);
}

void EmptySuccessfulThreadCreationFailsClosed()
{
    ThreadCreationFailureIsTerminal(
        VRREC_STATUS_OK,
        VRREC_STATUS_INTERNAL_ERROR,
        false);
}

}

int main()
{
    StartsReadsAndAbortsOnAJoinedCaptureThread();
    ReturnsInitialFailureAfterJoiningTheCaptureThread();
    DestructorAbortsAndJoinsAnActiveCaptureThread();
    OutOfMemoryThreadCreationIsTerminalFailure();
    InternalThreadCreationFailureIsTerminalFailure();
    EmptySuccessfulThreadCreationFailsClosed();
    return 0;
}
