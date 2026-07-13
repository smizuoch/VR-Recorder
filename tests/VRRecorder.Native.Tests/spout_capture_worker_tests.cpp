#include "spout_capture_worker.hpp"

#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdlib>
#include <deque>
#include <iostream>
#include <mutex>

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
            if (aborted) {
                return;
            }

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

    std::mutex mutex;
    std::condition_variable changed;
    std::deque<SpoutCaptureResult> results;
    std::chrono::milliseconds last_timeout {0};
    std::size_t poll_calls = 0;
    std::size_t abort_calls = 0;
    bool aborted = false;
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

}

int main()
{
    ContinuesAcrossTimeoutsUntilAborted();
    StopsWhenTheSelectedSenderIsLost();
    InvalidFramesFailTheWorker();
    RejectsInvalidPollIntervalsBeforeStarting();
    return 0;
}
