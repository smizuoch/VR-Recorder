#include "media_recording_session.hpp"
#include "fragmented_mp4_test_support.hpp"

#include <cstddef>
#include <cstdint>
#include <chrono>
#include <condition_variable>
#include <cstdlib>
#include <atomic>
#include <future>
#include <iostream>
#include <mutex>
#include <thread>
#include <vector>

namespace {

#define CHECK(condition) do { if (!(condition)) { std::cerr << "check failed at " << __FILE__ << ':' << __LINE__ << ": " #condition << '\n'; std::abort(); } } while (false)

using namespace vrrecorder::native;
using namespace vrrecorder::native::test;

class FakeStreamPipeline final : public MediaStreamPipelinePort {
public:
    FakeStreamPipeline(std::vector<int> &order, int base) noexcept : order_(order), base_(base) {}
    vrrec_status_t Start() noexcept override { order_.push_back(base_ + 1); return start_status; }
    vrrec_status_t RequestStop() noexcept override { order_.push_back(base_ + 2); return stop_status; }
    void RequestAbort() noexcept override
    {
        order_.push_back(base_ + 5);
        ++request_abort_calls;
    }
    void JoinAfterAbort() noexcept override
    {
        ++join_after_abort_calls;
        Abort();
    }
    void Abort() noexcept override { order_.push_back(base_ + 3); ++abort_calls; }
    vrrec_status_t Join() noexcept override { order_.push_back(base_ + 4); ++join_calls; return join_status; }
    std::uint64_t MuxedPacketCount() const noexcept override { return muxed_packet_count; }
    std::vector<int> &order_;
    int base_;
    vrrec_status_t start_status = VRREC_STATUS_OK;
    vrrec_status_t stop_status = VRREC_STATUS_OK;
    vrrec_status_t join_status = VRREC_STATUS_OK;
    std::uint64_t muxed_packet_count = 0;
    std::size_t abort_calls = 0;
    std::size_t request_abort_calls = 0;
    std::size_t join_after_abort_calls = 0;
    std::size_t join_calls = 0;
};

class FakeMuxSession final : public MediaMuxSessionPort {
public:
    explicit FakeMuxSession(std::vector<int> &order) noexcept : order_(order) {}
    vrrec_status_t Start(
        const FragmentedMp4StreamConfiguration &) noexcept override
    {
        order_.push_back(21);
        ++start_calls;
        return start_status;
    }
    void Abort() noexcept override { order_.push_back(23); ++abort_calls; }
    void RequestAbort() noexcept override
    {
        order_.push_back(22);
        ++request_abort_calls;
    }
    std::vector<int> &order_;
    vrrec_status_t start_status = VRREC_STATUS_OK;
    std::size_t start_calls = 0;
    std::size_t request_abort_calls = 0;
    std::size_t abort_calls = 0;
};

class BlockingRequestMuxSession final : public MediaMuxSessionPort {
public:
    vrrec_status_t Start(
        const FragmentedMp4StreamConfiguration &) noexcept override
    {
        return VRREC_STATUS_OK;
    }

    void RequestAbort() noexcept override
    {
        std::unique_lock lock(mutex_);
        ++request_abort_calls_;
        request_abort_entered_ = true;
        changed_.notify_all();
        changed_.wait(lock, [this] { return release_request_abort_; });
    }

    void Abort() noexcept override
    {
        const std::lock_guard lock(mutex_);
        ++abort_calls_;
    }

    void WaitForRequestAbort()
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [this] { return request_abort_entered_; });
    }

    void ReleaseRequestAbort()
    {
        {
            const std::lock_guard lock(mutex_);
            release_request_abort_ = true;
        }
        changed_.notify_all();
    }

    std::size_t RequestAbortCalls() const noexcept
    {
        const std::lock_guard lock(mutex_);
        return request_abort_calls_;
    }

    std::size_t AbortCalls() const noexcept
    {
        const std::lock_guard lock(mutex_);
        return abort_calls_;
    }

private:
    mutable std::mutex mutex_;
    std::condition_variable changed_;
    std::size_t request_abort_calls_ = 0;
    std::size_t abort_calls_ = 0;
    bool request_abort_entered_ = false;
    bool release_request_abort_ = false;
};

class ConcurrentStreamPipeline final : public MediaStreamPipelinePort {
public:
    explicit ConcurrentStreamPipeline(
        bool block_join,
        bool block_stop = false,
        bool block_start = false,
        bool block_abort = false) noexcept
        : block_join_(block_join),
          block_stop_(block_stop),
          block_start_(block_start),
          block_abort_(block_abort)
    {
    }

    vrrec_status_t Start() noexcept override
    {
        std::unique_lock lock(mutex_);
        ++start_calls;
        start_entered_ = true;
        changed_.notify_all();
        if (block_start_) {
            changed_.wait(lock, [this] { return release_start_; });
        }
        return VRREC_STATUS_OK;
    }
    vrrec_status_t RequestStop() noexcept override
    {
        std::unique_lock lock(mutex_);
        ++stop_calls;
        stop_entered_ = true;
        changed_.notify_all();
        if (block_stop_) {
            changed_.wait(lock, [this] {
                return aborted_ || release_stop_;
            });
        }
        return VRREC_STATUS_OK;
    }

    void Abort() noexcept override
    {
        std::unique_lock lock(mutex_);
        aborted_ = true;
        ++abort_calls;
        abort_entered_ = true;
        changed_.notify_all();
        if (block_abort_) {
            changed_.wait(lock, [this] { return release_abort_; });
        }
    }

    void RequestAbort() noexcept override
    {
        const std::lock_guard lock(mutex_);
        ++request_abort_calls;
    }

    void JoinAfterAbort() noexcept override
    {
        ++join_after_abort_calls;
        Abort();
    }

    vrrec_status_t Join() noexcept override
    {
        std::unique_lock lock(mutex_);
        ++join_entries_;
        join_entered_ = true;
        changed_.notify_all();
        if (block_join_) {
            changed_.wait(lock, [this] {
                return aborted_ || release_join_;
            });
        }
        ++join_calls;
        return VRREC_STATUS_OK;
    }

    std::uint64_t MuxedPacketCount() const noexcept override { return 1; }

    void WaitForJoin()
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [this] { return join_entered_; });
    }

    void WaitForStop()
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [this] { return stop_entered_; });
    }

    void WaitForStopDecision(const std::atomic_bool &second_done)
    {
        std::unique_lock lock(mutex_);
        changed_.wait_for(lock, std::chrono::milliseconds(100), [&] {
            return stop_calls >= 2 || second_done.load();
        });
    }

    void WaitForJoinDecision(const std::atomic_bool &second_done)
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [&] {
            return join_entries_ >= 2 || second_done.load();
        });
    }

    void WaitForStart()
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [this] { return start_entered_; });
    }

    void WaitForAbort()
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [this] { return abort_entered_; });
    }

    void ReleaseStart()
    {
        {
            const std::lock_guard lock(mutex_);
            release_start_ = true;
        }
        changed_.notify_all();
    }

    void ReleaseAbort()
    {
        {
            const std::lock_guard lock(mutex_);
            release_abort_ = true;
        }
        changed_.notify_all();
    }

    void ReleaseStop()
    {
        {
            const std::lock_guard lock(mutex_);
            release_stop_ = true;
        }
        changed_.notify_all();
    }

    void ReleaseJoin()
    {
        {
            const std::lock_guard lock(mutex_);
            release_join_ = true;
        }
        changed_.notify_all();
    }

    void NotifyDecision() noexcept
    {
        changed_.notify_all();
    }

    std::mutex mutex_;
    std::condition_variable changed_;
    bool block_join_;
    bool block_stop_;
    bool block_start_;
    bool block_abort_;
    bool start_entered_ = false;
    bool release_start_ = false;
    bool stop_entered_ = false;
    bool join_entered_ = false;
    bool aborted_ = false;
    bool abort_entered_ = false;
    bool release_abort_ = false;
    bool release_stop_ = false;
    bool release_join_ = false;
    std::size_t abort_calls = 0;
    std::size_t request_abort_calls = 0;
    std::size_t join_after_abort_calls = 0;
    std::size_t join_calls = 0;
    std::size_t stop_calls = 0;
    std::size_t start_calls = 0;
    std::size_t join_entries_ = 0;
};

class ConcurrentMuxSession final : public MediaMuxSessionPort {
public:
    vrrec_status_t Start(
        const FragmentedMp4StreamConfiguration &) noexcept override
    {
        std::unique_lock lock(mutex_);
        start_entered_ = true;
        changed_.notify_all();
        changed_.wait(lock, [this] { return aborted_ || release_start_; });
        return VRREC_STATUS_OK;
    }

    void Abort() noexcept override
    {
        {
            const std::lock_guard lock(mutex_);
            aborted_ = true;
            ++abort_calls;
        }
        changed_.notify_all();
    }

    void RequestAbort() noexcept override
    {
        {
            const std::lock_guard lock(mutex_);
            aborted_ = true;
            ++request_abort_calls;
        }
        changed_.notify_all();
    }

    void WaitForStart()
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [this] { return start_entered_; });
    }

    std::mutex mutex_;
    std::condition_variable changed_;
    bool start_entered_ = false;
    bool release_start_ = false;
    bool aborted_ = false;
    std::size_t request_abort_calls = 0;
    std::size_t abort_calls = 0;
};

class RecordingEvents final : public MediaEventSink {
public:
    void FirstVideoPacketMuxed() noexcept override {}
    void Stopped(std::uint64_t video, std::uint64_t audio) noexcept override { ++stopped_calls; video_packets = video; audio_packets = audio; }
    void Faulted(vrrec_status_t status, const char *) noexcept override { ++fault_calls; fault_status = status; }
    void AudioEndpointAvailabilityChanged(AudioEndpointRole, bool, std::uint64_t) noexcept override {}
    std::size_t stopped_calls = 0;
    std::size_t fault_calls = 0;
    std::uint64_t video_packets = 0;
    std::uint64_t audio_packets = 0;
    vrrec_status_t fault_status = VRREC_STATUS_OK;
};

void GracefulStopOrdersVideoBeforeAudioAndPublishesFinalCounts()
{
    std::vector<int> order;
    FakeStreamPipeline video(order, 0), audio(order, 10);
    FakeMuxSession mux(order);
    RecordingEvents events;
    video.muxed_packet_count = 91;
    audio.muxed_packet_count = 47;
    MediaRecordingSession session(
        video,
        audio,
        mux,
        TestMp4Streams(),
        events);
    CHECK(session.Start() == VRREC_STATUS_OK);
    CHECK(order == std::vector<int>({21, 1, 11}));
    CHECK(session.RequestStop() == VRREC_STATUS_OK);
    CHECK(session.RequestStop() == VRREC_STATUS_OK);
    CHECK(order == std::vector<int>({21, 1, 11, 2, 12}));
    CHECK(session.Join() == VRREC_STATUS_OK);
    CHECK(order == std::vector<int>({21, 1, 11, 2, 12, 4, 14}));
    CHECK(events.stopped_calls == 1);
    CHECK(events.video_packets == 91);
    CHECK(events.audio_packets == 47);
    CHECK(events.fault_calls == 0);
    CHECK(mux.abort_calls == 0);
}

void AudioStartFailureRollsBackVideoAndMux()
{
    std::vector<int> order;
    FakeStreamPipeline video(order, 0), audio(order, 10);
    FakeMuxSession mux(order);
    RecordingEvents events;
    audio.start_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    MediaRecordingSession session(
        video,
        audio,
        mux,
        TestMp4Streams(),
        events);
    CHECK(session.Start() == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(order == std::vector<int>({21, 1, 11, 3, 4, 23}));
    CHECK(video.abort_calls == 1);
    CHECK(video.join_calls == 1);
    CHECK(events.stopped_calls == 0);
}

void MuxHeaderFailureDoesNotStartEitherStream()
{
    std::vector<int> order;
    FakeStreamPipeline video(order, 0), audio(order, 10);
    FakeMuxSession mux(order);
    RecordingEvents events;
    mux.start_status = VRREC_STATUS_INTERNAL_ERROR;
    MediaRecordingSession session(
        video,
        audio,
        mux,
        TestMp4Streams(),
        events);

    CHECK(session.Start() == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(order == std::vector<int>({21, 23}));
    CHECK(mux.start_calls == 1);
    CHECK(mux.request_abort_calls == 0);
    CHECK(mux.abort_calls == 1);
    CHECK(events.stopped_calls == 0);
}

void VideoStartFailureDoesNotStartAudio()
{
    std::vector<int> order;
    FakeStreamPipeline video(order, 0), audio(order, 10);
    FakeMuxSession mux(order);
    RecordingEvents events;
    video.start_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    MediaRecordingSession session(
        video, audio, mux, TestMp4Streams(), events);

    CHECK(session.Start() == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(order == std::vector<int>({21, 1, 23}));
    CHECK(audio.start_status == VRREC_STATUS_OK);
    CHECK(events.stopped_calls == 0);
}

void RejectsOutOfOrderAndRepeatedLifecycleOperations()
{
    std::vector<int> order;
    FakeStreamPipeline video(order, 0), audio(order, 10);
    FakeMuxSession mux(order);
    RecordingEvents events;
    MediaRecordingSession session(
        video, audio, mux, TestMp4Streams(), events);

    CHECK(session.RequestStop() == VRREC_STATUS_INVALID_STATE);
    CHECK(session.Join() == VRREC_STATUS_INVALID_STATE);
    CHECK(session.Start() == VRREC_STATUS_OK);
    CHECK(session.Start() == VRREC_STATUS_INVALID_STATE);
    CHECK(session.Join() == VRREC_STATUS_INVALID_STATE);
    CHECK(session.RequestStop() == VRREC_STATUS_OK);
    CHECK(session.Join() == VRREC_STATUS_OK);
    CHECK(session.Join() == VRREC_STATUS_INVALID_STATE);
    CHECK(session.RequestStop() == VRREC_STATUS_INVALID_STATE);
}

void PropagatesEachGracefulStopAndJoinFailure()
{
    for (const auto video_stop_fails : {true, false}) {
        std::vector<int> order;
        FakeStreamPipeline video(order, 0), audio(order, 10);
        FakeMuxSession mux(order);
        RecordingEvents events;
        if (video_stop_fails) {
            video.stop_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
        } else {
            audio.stop_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
        }
        MediaRecordingSession session(
            video, audio, mux, TestMp4Streams(), events);
        CHECK(session.Start() == VRREC_STATUS_OK);
        CHECK(session.RequestStop() == VRREC_STATUS_BACKEND_UNAVAILABLE);
        CHECK(mux.request_abort_calls == 1);
        CHECK(mux.abort_calls == 1);
        CHECK(events.stopped_calls == 0);
    }

    std::vector<int> order;
    FakeStreamPipeline video(order, 0), audio(order, 10);
    FakeMuxSession mux(order);
    RecordingEvents events;
    audio.join_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    MediaRecordingSession session(
        video, audio, mux, TestMp4Streams(), events);
    CHECK(session.Start() == VRREC_STATUS_OK);
    CHECK(session.RequestStop() == VRREC_STATUS_OK);
    CHECK(session.Join() == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(events.fault_calls == 1);
    CHECK(events.fault_status == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(events.stopped_calls == 0);
    CHECK(mux.abort_calls == 1);
}

void AbortBeforeStartCleansOnlyTheMuxAndIsIdempotent()
{
    std::vector<int> order;
    FakeStreamPipeline video(order, 0), audio(order, 10);
    FakeMuxSession mux(order);
    RecordingEvents events;
    MediaRecordingSession session(
        video, audio, mux, TestMp4Streams(), events);

    session.RequestAbort();
    session.RequestAbort();
    session.JoinAfterAbort();
    session.JoinAfterAbort();
    CHECK(mux.request_abort_calls == 1);
    CHECK(mux.abort_calls == 1);
    CHECK(video.request_abort_calls == 0);
    CHECK(audio.request_abort_calls == 0);
    CHECK(video.join_after_abort_calls == 0);
    CHECK(audio.join_after_abort_calls == 0);
    CHECK(session.Start() == VRREC_STATUS_INVALID_STATE);
}

void StreamFailureAbortsPeerAndMuxWithoutStoppedEvent()
{
    std::vector<int> order;
    FakeStreamPipeline video(order, 0), audio(order, 10);
    FakeMuxSession mux(order);
    RecordingEvents events;
    video.join_status = VRREC_STATUS_INTERNAL_ERROR;
    MediaRecordingSession session(
        video,
        audio,
        mux,
        TestMp4Streams(),
        events);
    CHECK(session.Start() == VRREC_STATUS_OK);
    CHECK(session.RequestStop() == VRREC_STATUS_OK);
    CHECK(session.Join() == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(audio.abort_calls == 1);
    CHECK(audio.join_calls == 1);
    CHECK(mux.request_abort_calls == 0);
    CHECK(mux.abort_calls == 1);
    CHECK(events.stopped_calls == 0);
    CHECK(events.fault_calls == 1);
    CHECK(events.fault_status == VRREC_STATUS_INTERNAL_ERROR);
}

void AbortDuringJoinSuppressesSuccessfulStoppedCompletion()
{
    std::vector<int> order;
    ConcurrentStreamPipeline video(true);
    ConcurrentStreamPipeline audio(false);
    FakeMuxSession mux(order);
    RecordingEvents events;
    MediaRecordingSession session(
        video,
        audio,
        mux,
        TestMp4Streams(),
        events);
    CHECK(session.Start() == VRREC_STATUS_OK);
    CHECK(session.RequestStop() == VRREC_STATUS_OK);

    auto join_result = VRREC_STATUS_OK;
    std::thread joiner([&] { join_result = session.Join(); });
    video.WaitForJoin();
    session.Abort();
    joiner.join();

    CHECK(join_result == VRREC_STATUS_INVALID_STATE);
    CHECK(video.abort_calls == 1);
    CHECK(audio.abort_calls == 1);
    CHECK(mux.request_abort_calls == 1);
    CHECK(mux.abort_calls == 1);
    CHECK(events.stopped_calls == 0);
}

void LogicalAbortTerminalizesMuxAndCleanupReassertsStreamAbortBeforeJoin()
{
    std::vector<int> order;
    FakeStreamPipeline video(order, 0), audio(order, 10);
    FakeMuxSession mux(order);
    RecordingEvents events;
    MediaRecordingSession session(
        video,
        audio,
        mux,
        TestMp4Streams(),
        events);
    CHECK(session.Start() == VRREC_STATUS_OK);

    session.RequestAbort();
    session.RequestAbort();

    CHECK(mux.request_abort_calls == 1);
    CHECK(mux.abort_calls == 0);
    CHECK(video.request_abort_calls == 1);
    CHECK(audio.request_abort_calls == 1);
    CHECK(video.abort_calls == 0);
    CHECK(audio.abort_calls == 0);
    CHECK(events.stopped_calls == 0);
    CHECK(events.fault_calls == 0);

    session.JoinAfterAbort();
    session.JoinAfterAbort();

    CHECK(video.abort_calls == 1);
    CHECK(audio.abort_calls == 1);
    CHECK(video.request_abort_calls == 2);
    CHECK(audio.request_abort_calls == 2);
    CHECK(mux.request_abort_calls == 1);
    CHECK(mux.abort_calls == 1);
    CHECK(events.stopped_calls == 0);
    CHECK(events.fault_calls == 0);
}

void ConcurrentAbortJoinersWaitForTheSameCleanupCompletion()
{
    std::vector<int> order;
    ConcurrentStreamPipeline video(false, false, false, true);
    ConcurrentStreamPipeline audio(false);
    FakeMuxSession mux(order);
    RecordingEvents events;
    MediaRecordingSession session(
        video,
        audio,
        mux,
        TestMp4Streams(),
        events);
    CHECK(session.Start() == VRREC_STATUS_OK);
    session.RequestAbort();

    std::thread first_joiner([&] { session.JoinAfterAbort(); });
    video.WaitForAbort();

    std::promise<void> second_entered;
    std::promise<void> second_returned;
    auto entered = second_entered.get_future();
    auto returned = second_returned.get_future();
    std::thread second_joiner([&] {
        second_entered.set_value();
        session.JoinAfterAbort();
        second_returned.set_value();
    });
    entered.wait();
    const auto returned_before_cleanup =
        returned.wait_for(std::chrono::milliseconds(50)) ==
        std::future_status::ready;

    video.ReleaseAbort();
    first_joiner.join();
    second_joiner.join();

    CHECK(!returned_before_cleanup);
    CHECK(video.abort_calls == 1);
    CHECK(audio.abort_calls == 1);
    CHECK(mux.abort_calls == 1);
}

void CleanupWaitsForLogicalMuxAbortToCommit()
{
    std::vector<int> order;
    FakeStreamPipeline video(order, 0), audio(order, 10);
    BlockingRequestMuxSession mux;
    RecordingEvents events;
    MediaRecordingSession session(
        video,
        audio,
        mux,
        TestMp4Streams(),
        events);
    CHECK(session.Start() == VRREC_STATUS_OK);

    std::thread requester([&] { session.RequestAbort(); });
    mux.WaitForRequestAbort();

    std::promise<void> cleanup_entered;
    std::promise<void> cleanup_returned;
    auto entered = cleanup_entered.get_future();
    auto returned = cleanup_returned.get_future();
    std::thread cleanup([&] {
        cleanup_entered.set_value();
        session.JoinAfterAbort();
        cleanup_returned.set_value();
    });
    entered.wait();
    const auto returned_before_request_commit =
        returned.wait_for(std::chrono::milliseconds(50)) ==
        std::future_status::ready;

    mux.ReleaseRequestAbort();
    requester.join();
    cleanup.join();

    CHECK(!returned_before_request_commit);
    CHECK(mux.RequestAbortCalls() == 1);
    CHECK(mux.AbortCalls() == 1);
    CHECK(video.abort_calls == 1);
    CHECK(audio.abort_calls == 1);
}

void AbortDuringStopRequestSkipsTheRemainingGracefulSequence()
{
    std::vector<int> order;
    ConcurrentStreamPipeline video(false, true);
    ConcurrentStreamPipeline audio(false);
    FakeMuxSession mux(order);
    RecordingEvents events;
    MediaRecordingSession session(
        video,
        audio,
        mux,
        TestMp4Streams(),
        events);
    CHECK(session.Start() == VRREC_STATUS_OK);

    auto stop_result = VRREC_STATUS_OK;
    std::thread stopper([&] { stop_result = session.RequestStop(); });
    video.WaitForStop();
    session.Abort();
    stopper.join();

    CHECK(stop_result == VRREC_STATUS_INVALID_STATE);
    CHECK(video.stop_calls == 1);
    CHECK(audio.stop_calls == 0);
    CHECK(events.stopped_calls == 0);
}

void AbortDuringVideoStartRollsBackWithoutStartingAudio()
{
    std::vector<int> order;
    ConcurrentStreamPipeline video(false, false, true);
    ConcurrentStreamPipeline audio(false);
    FakeMuxSession mux(order);
    RecordingEvents events;
    MediaRecordingSession session(
        video,
        audio,
        mux,
        TestMp4Streams(),
        events);

    auto start_result = VRREC_STATUS_OK;
    std::thread starter([&] { start_result = session.Start(); });
    video.WaitForStart();
    session.RequestAbort();
    auto cleanup = std::async(std::launch::async, [&] {
        session.JoinAfterAbort();
    });
    const auto cleanup_returned_before_start =
        cleanup.wait_for(std::chrono::milliseconds(50)) ==
        std::future_status::ready;
    video.ReleaseStart();
    starter.join();
    cleanup.get();

    CHECK(!cleanup_returned_before_start);
    CHECK(start_result == VRREC_STATUS_INVALID_STATE);
    CHECK(video.abort_calls == 1);
    CHECK(audio.start_calls == 0);
    CHECK(mux.abort_calls == 1);
}

void CleanupWaitsWhenStartOwnsTheStreamAbortJoin()
{
    std::vector<int> order;
    ConcurrentStreamPipeline video(false, false, true, true);
    ConcurrentStreamPipeline audio(false);
    FakeMuxSession mux(order);
    RecordingEvents events;
    MediaRecordingSession session(
        video,
        audio,
        mux,
        TestMp4Streams(),
        events);

    auto start_result = VRREC_STATUS_OK;
    std::thread starter([&] { start_result = session.Start(); });
    video.WaitForStart();
    session.RequestAbort();
    video.ReleaseStart();
    video.WaitForAbort();

    auto cleanup = std::async(std::launch::async, [&] {
        session.JoinAfterAbort();
    });
    const auto cleanup_returned_before_start_cleanup =
        cleanup.wait_for(std::chrono::milliseconds(50)) ==
        std::future_status::ready;

    video.ReleaseAbort();
    starter.join();
    cleanup.get();

    CHECK(!cleanup_returned_before_start_cleanup);
    CHECK(start_result == VRREC_STATUS_INVALID_STATE);
    CHECK(video.abort_calls == 1);
    CHECK(audio.start_calls == 0);
    CHECK(mux.abort_calls == 1);
}

void AbortDuringMuxHeaderStartDoesNotStartEitherStream()
{
    ConcurrentStreamPipeline video(false);
    ConcurrentStreamPipeline audio(false);
    ConcurrentMuxSession mux;
    RecordingEvents events;
    MediaRecordingSession session(
        video,
        audio,
        mux,
        TestMp4Streams(),
        events);

    auto start_result = VRREC_STATUS_OK;
    std::thread starter([&] { start_result = session.Start(); });
    mux.WaitForStart();
    session.Abort();
    starter.join();

    CHECK(start_result == VRREC_STATUS_INVALID_STATE);
    CHECK(mux.abort_calls == 1);
    CHECK(video.start_calls == 0);
    CHECK(audio.start_calls == 0);
}

void ConcurrentStopRequestsExecuteEachStreamStopExactlyOnce()
{
    std::vector<int> order;
    ConcurrentStreamPipeline video(false, true);
    ConcurrentStreamPipeline audio(false);
    FakeMuxSession mux(order);
    RecordingEvents events;
    MediaRecordingSession session(
        video,
        audio,
        mux,
        TestMp4Streams(),
        events);
    CHECK(session.Start() == VRREC_STATUS_OK);

    auto first_result = VRREC_STATUS_INTERNAL_ERROR;
    auto second_result = VRREC_STATUS_INTERNAL_ERROR;
    std::atomic_bool second_done = false;
    std::thread first([&] { first_result = session.RequestStop(); });
    video.WaitForStop();
    std::promise<void> second_started;
    auto second_started_future = second_started.get_future();
    std::thread second([&] {
        second_started.set_value();
        second_result = session.RequestStop();
        second_done.store(true);
        video.NotifyDecision();
    });
    second_started_future.wait();
    video.WaitForStopDecision(second_done);
    video.ReleaseStop();
    first.join();
    second.join();

    CHECK(first_result == VRREC_STATUS_OK);
    CHECK(second_result == VRREC_STATUS_OK);
    CHECK(video.stop_calls == 1);
    CHECK(audio.stop_calls == 1);
}

void ConcurrentJoinExecutesEachStreamJoinExactlyOnce()
{
    std::vector<int> order;
    ConcurrentStreamPipeline video(true);
    ConcurrentStreamPipeline audio(false);
    FakeMuxSession mux(order);
    RecordingEvents events;
    MediaRecordingSession session(
        video,
        audio,
        mux,
        TestMp4Streams(),
        events);
    CHECK(session.Start() == VRREC_STATUS_OK);
    CHECK(session.RequestStop() == VRREC_STATUS_OK);

    auto first_result = VRREC_STATUS_INTERNAL_ERROR;
    auto second_result = VRREC_STATUS_INTERNAL_ERROR;
    std::atomic_bool second_done = false;
    std::thread first([&] { first_result = session.Join(); });
    video.WaitForJoin();
    std::promise<void> second_started;
    auto second_started_future = second_started.get_future();
    std::thread second([&] {
        second_started.set_value();
        second_result = session.Join();
        second_done.store(true);
        video.NotifyDecision();
    });
    second_started_future.wait();
    video.WaitForJoinDecision(second_done);
    video.ReleaseJoin();
    first.join();
    second.join();

    CHECK((first_result == VRREC_STATUS_OK &&
           second_result == VRREC_STATUS_INVALID_STATE) ||
          (second_result == VRREC_STATUS_OK &&
           first_result == VRREC_STATUS_INVALID_STATE));
    CHECK(video.join_entries_ == 1);
    CHECK(video.join_calls == 1);
    CHECK(audio.join_calls == 1);
    CHECK(events.stopped_calls == 1);
}

}

int main()
{
    GracefulStopOrdersVideoBeforeAudioAndPublishesFinalCounts();
    MuxHeaderFailureDoesNotStartEitherStream();
    VideoStartFailureDoesNotStartAudio();
    RejectsOutOfOrderAndRepeatedLifecycleOperations();
    PropagatesEachGracefulStopAndJoinFailure();
    AbortBeforeStartCleansOnlyTheMuxAndIsIdempotent();
    AudioStartFailureRollsBackVideoAndMux();
    StreamFailureAbortsPeerAndMuxWithoutStoppedEvent();
    AbortDuringJoinSuppressesSuccessfulStoppedCompletion();
    LogicalAbortTerminalizesMuxAndCleanupReassertsStreamAbortBeforeJoin();
    ConcurrentAbortJoinersWaitForTheSameCleanupCompletion();
    CleanupWaitsForLogicalMuxAbortToCommit();
    AbortDuringStopRequestSkipsTheRemainingGracefulSequence();
    AbortDuringVideoStartRollsBackWithoutStartingAudio();
    CleanupWaitsWhenStartOwnsTheStreamAbortJoin();
    AbortDuringMuxHeaderStartDoesNotStartEitherStream();
    ConcurrentStopRequestsExecuteEachStreamStopExactlyOnce();
    ConcurrentJoinExecutesEachStreamJoinExactlyOnce();
    return 0;
}
