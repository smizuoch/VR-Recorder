#include "spout_capture_worker.hpp"

#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdlib>
#include <deque>
#include <future>
#include <iostream>
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

class BlockingCaptureSource final : public SpoutCaptureSource {
public:
    SpoutCaptureResult PollOne(
        std::chrono::milliseconds timeout) noexcept override
    {
        std::unique_lock lock(mutex);
        last_timeout = timeout;
        ++poll_calls;
        changed.notify_all();
        if (block_poll) {
            changed.wait(lock, [&] { return release_poll; });
            return blocked_result;
        }
        if (!results.empty()) {
            const auto result = results.front();
            results.pop_front();
            changed.notify_all();
            return result;
        }

        changed.wait(lock, [&] { return aborted; });
        return SpoutCaptureResult::Aborted;
    }

    void Abort() noexcept override
    {
        {
            const std::lock_guard lock(mutex);
            aborted = true;
            ++abort_calls;
        }

        changed.notify_all();
    }

    void WaitForPolls(std::size_t count)
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return poll_calls >= count; });
    }

    void WaitUntilAborted()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return aborted; });
    }

    void ReleasePoll()
    {
        {
            const std::lock_guard lock(mutex);
            release_poll = true;
        }
        changed.notify_all();
    }

    std::mutex mutex;
    std::condition_variable changed;
    std::deque<SpoutCaptureResult> results;
    std::chrono::milliseconds last_timeout {0};
    std::size_t poll_calls = 0;
    std::size_t abort_calls = 0;
    SpoutCaptureResult blocked_result = SpoutCaptureResult::Aborted;
    bool block_poll = false;
    bool release_poll = false;
    bool aborted = false;
};

class ScriptedThreadFactory final : public NativeThreadFactoryPort {
public:
    ScriptedThreadFactory(
        vrrec_status_t status,
        bool create_thread_on_success = true) noexcept
        : status_(status),
          create_thread_on_success_(create_thread_on_success)
    {
    }

    vrrec_status_t Start(
        std::thread &thread,
        NativeThreadEntry entry,
        void *context) noexcept override
    {
        ++start_calls;
        if (status_ == VRREC_STATUS_OK && create_thread_on_success_) {
            thread = std::thread(entry, context);
        }
        return status_;
    }

    std::size_t start_calls = 0;

private:
    vrrec_status_t status_;
    bool create_thread_on_success_;
};

class BlockingThreadFactory final : public NativeThreadFactoryPort {
public:
    BlockingThreadFactory(
        vrrec_status_t status,
        bool create_thread_on_success) noexcept
        : status_(status),
          create_thread_on_success_(create_thread_on_success)
    {
    }

    vrrec_status_t Start(
        std::thread &thread,
        NativeThreadEntry entry,
        void *context) noexcept override
    {
        {
            std::unique_lock lock(mutex_);
            ++start_calls_;
            start_entered_ = true;
            changed_.notify_all();
            changed_.wait(lock, [this] { return release_start_; });
        }
        if (status_ == VRREC_STATUS_OK && create_thread_on_success_) {
            thread = std::thread(entry, context);
        }
        return status_;
    }

    void WaitForStart()
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [this] { return start_entered_; });
    }

    void ReleaseStart()
    {
        {
            const std::lock_guard lock(mutex_);
            release_start_ = true;
        }
        changed_.notify_all();
    }

    std::size_t StartCalls() const
    {
        const std::lock_guard lock(mutex_);
        return start_calls_;
    }

private:
    vrrec_status_t status_;
    bool create_thread_on_success_;
    mutable std::mutex mutex_;
    std::condition_variable changed_;
    std::size_t start_calls_ = 0;
    bool start_entered_ = false;
    bool release_start_ = false;
};

void ContinuesAcrossTimeoutsUntilAborted()
{
    BlockingCaptureSource source;
    source.results.push_back(SpoutCaptureResult::Timeout);
    source.results.push_back(SpoutCaptureResult::FrameAccepted);
    SpoutCaptureWorker worker(source);

    CHECK(worker.Start(std::chrono::milliseconds(100)) == VRREC_STATUS_OK);
    source.WaitForPolls(3);
    worker.Abort();
    worker.Abort();
    CHECK(worker.Join() == SpoutCaptureWorkerResult::Aborted);
    CHECK(source.abort_calls == 1);
    CHECK(source.last_timeout == std::chrono::milliseconds(100));
}

void StopsWhenTheSelectedSenderIsLost()
{
    BlockingCaptureSource source;
    source.results.push_back(SpoutCaptureResult::FrameAccepted);
    source.results.push_back(SpoutCaptureResult::SenderLost);
    SpoutCaptureWorker worker(source);

    CHECK(worker.Start(std::chrono::milliseconds(50)) == VRREC_STATUS_OK);
    CHECK(worker.Join() == SpoutCaptureWorkerResult::SenderLost);
    CHECK(source.poll_calls == 2);
    CHECK(source.abort_calls == 1);
}

void InvalidFramesFailTheWorker()
{
    BlockingCaptureSource source;
    source.results.push_back(SpoutCaptureResult::InvalidFrame);
    SpoutCaptureWorker worker(source);

    CHECK(worker.Start(std::chrono::milliseconds(50)) == VRREC_STATUS_OK);
    CHECK(worker.Join() == SpoutCaptureWorkerResult::Failed);
    CHECK(source.poll_calls == 1);
}

void RejectsInvalidPollIntervalsBeforeStarting()
{
    BlockingCaptureSource source;
    SpoutCaptureWorker worker(source);

    CHECK(worker.Start(std::chrono::milliseconds(0)) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(source.poll_calls == 0);
}

void AbortBeforeStartPreventsWorkerLaunch()
{
    BlockingCaptureSource source;
    ScriptedThreadFactory thread_factory(VRREC_STATUS_OK);
    SpoutCaptureWorker worker(source, thread_factory);

    worker.Abort();

    CHECK(worker.Start(std::chrono::milliseconds(50)) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(worker.Join() == SpoutCaptureWorkerResult::Aborted);
    CHECK(thread_factory.start_calls == 0);
    CHECK(source.poll_calls == 0);
    CHECK(source.abort_calls == 1);
}

void AbortWinsOverSenderLossReturnedByAnInFlightPoll()
{
    BlockingCaptureSource source;
    source.block_poll = true;
    source.blocked_result = SpoutCaptureResult::SenderLost;
    SpoutCaptureWorker worker(source);

    CHECK(worker.Start(std::chrono::milliseconds(50)) == VRREC_STATUS_OK);
    source.WaitForPolls(1);
    auto aborting = std::async(std::launch::async, [&] {
        worker.Abort();
    });
    source.WaitUntilAborted();
    source.ReleasePoll();
    aborting.get();

    CHECK(worker.Join() == SpoutCaptureWorkerResult::Aborted);
    CHECK(source.poll_calls == 1);
    CHECK(source.abort_calls == 1);
}

void ThreadCreationFailureIsTerminal(
    vrrec_status_t factory_status,
    vrrec_status_t expected_status,
    bool create_thread_on_success = true)
{
    BlockingCaptureSource source;
    ScriptedThreadFactory thread_factory(
        factory_status,
        create_thread_on_success);
    SpoutCaptureWorker worker(source, thread_factory);

    CHECK(worker.Start(std::chrono::milliseconds(50)) == expected_status);
    CHECK(thread_factory.start_calls == 1);
    CHECK(worker.Join() == SpoutCaptureWorkerResult::Failed);
    CHECK(worker.Join() == SpoutCaptureWorkerResult::Failed);
    CHECK(source.poll_calls == 0);
    CHECK(source.abort_calls == 0);
    CHECK(worker.Start(std::chrono::milliseconds(50)) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(thread_factory.start_calls == 1);

    worker.Abort();
    CHECK(worker.Join() == SpoutCaptureWorkerResult::Failed);
    CHECK(source.abort_calls == 0);
}

void CaptureThreadCreationFailuresAreTerminal()
{
    ThreadCreationFailureIsTerminal(
        VRREC_STATUS_OUT_OF_MEMORY,
        VRREC_STATUS_OUT_OF_MEMORY);
    ThreadCreationFailureIsTerminal(
        VRREC_STATUS_INTERNAL_ERROR,
        VRREC_STATUS_INTERNAL_ERROR);
    ThreadCreationFailureIsTerminal(
        VRREC_STATUS_OK,
        VRREC_STATUS_INTERNAL_ERROR,
        false);
}

void AbortWinsDuringThreadCreation(
    vrrec_status_t factory_status,
    bool create_thread_on_success)
{
    BlockingCaptureSource source;
    source.results.push_back(SpoutCaptureResult::FrameAccepted);
    BlockingThreadFactory thread_factory(
        factory_status,
        create_thread_on_success);
    SpoutCaptureWorker worker(source, thread_factory);

    auto starting = std::async(std::launch::async, [&] {
        return worker.Start(std::chrono::milliseconds(50));
    });
    thread_factory.WaitForStart();
    auto aborting = std::async(std::launch::async, [&] {
        worker.Abort();
    });
    source.WaitUntilAborted();
    CHECK(aborting.wait_for(std::chrono::milliseconds(50)) !=
          std::future_status::ready);

    thread_factory.ReleaseStart();
    CHECK(starting.get() == VRREC_STATUS_INVALID_STATE);
    aborting.get();

    CHECK(thread_factory.StartCalls() == 1);
    CHECK(worker.Join() == SpoutCaptureWorkerResult::Aborted);
    CHECK(source.poll_calls == 0);
    CHECK(source.abort_calls == 1);
    CHECK(worker.Start(std::chrono::milliseconds(50)) ==
          VRREC_STATUS_INVALID_STATE);
}

void AbortWinsDuringSuccessfulThreadCreation()
{
    AbortWinsDuringThreadCreation(VRREC_STATUS_OK, true);
}

void AbortWinsDuringFailedThreadCreation()
{
    AbortWinsDuringThreadCreation(VRREC_STATUS_OUT_OF_MEMORY, false);
}

void JoinWaitsForSuccessfulThreadPublication()
{
    BlockingCaptureSource source;
    source.results.push_back(SpoutCaptureResult::SenderLost);
    BlockingThreadFactory thread_factory(VRREC_STATUS_OK, true);
    SpoutCaptureWorker worker(source, thread_factory);

    auto starting = std::async(std::launch::async, [&] {
        return worker.Start(std::chrono::milliseconds(50));
    });
    thread_factory.WaitForStart();
    std::promise<void> join_invoking;
    auto join_invoked = join_invoking.get_future();
    auto joining = std::async(std::launch::async, [&] {
        join_invoking.set_value();
        return worker.Join();
    });
    join_invoked.wait();
    CHECK(joining.wait_for(std::chrono::milliseconds(50)) !=
          std::future_status::ready);

    thread_factory.ReleaseStart();
    CHECK(starting.get() == VRREC_STATUS_OK);
    CHECK(joining.get() == SpoutCaptureWorkerResult::SenderLost);
    CHECK(source.poll_calls == 1);
    CHECK(source.abort_calls == 1);
}

}

int main()
{
    ContinuesAcrossTimeoutsUntilAborted();
    StopsWhenTheSelectedSenderIsLost();
    InvalidFramesFailTheWorker();
    RejectsInvalidPollIntervalsBeforeStarting();
    AbortBeforeStartPreventsWorkerLaunch();
    AbortWinsOverSenderLossReturnedByAnInFlightPoll();
    CaptureThreadCreationFailuresAreTerminal();
    AbortWinsDuringSuccessfulThreadCreation();
    AbortWinsDuringFailedThreadCreation();
    JoinWaitsForSuccessfulThreadPublication();
    return 0;
}
