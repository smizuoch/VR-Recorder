#include "audio_capture_input_runner.hpp"
#include "audio_capture_pump.hpp"

#include "allocation_failure_test_support.hpp"

#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <deque>
#include <iostream>
#include <limits>
#include <future>
#include <memory>
#include <mutex>
#include <span>
#include <string>
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

struct ScriptedRead final {
    AudioCaptureReadResult result = AudioCaptureReadResult::Failed;
    std::uint64_t start_frame_48k = 0;
    std::uint64_t device_position = 0;
    std::int64_t qpc_100ns = 0;
    std::size_t frame_count_48k = 0;
    std::vector<float> samples;
    bool silent = false;
    bool discontinuity = false;
    std::uint64_t effective_frame_48k = 0;
};

class FakeAudioCaptureSource final : public AudioCaptureSource {
public:
    vrrec_status_t Start(
        const AudioCaptureSourceConfig &config) noexcept override
    {
        ++start_calls;
        started_role = config.role;
        endpoint = config.endpoint_id_utf8;
        session_start_qpc_100ns = config.session_start_qpc_100ns;
        if (shared_start_config != nullptr) {
            *shared_start_config = config;
        }
        return start_status;
    }

    AudioCaptureRead Read() noexcept override
    {
        if (next_read >= reads.size()) {
            return {};
        }

        auto &script = reads[next_read++];
        return AudioCaptureRead {
            script.result,
            {
                script.start_frame_48k,
                script.device_position,
                script.qpc_100ns,
                script.frame_count_48k,
                std::span<const float>(script.samples),
                script.silent,
                script.discontinuity,
            },
            script.effective_frame_48k,
        };
    }

    void Abort() noexcept override
    {
        ++abort_calls;
        if (shared_abort_calls != nullptr) {
            ++*shared_abort_calls;
        }
    }

    vrrec_status_t start_status = VRREC_STATUS_OK;
    std::vector<ScriptedRead> reads;
    std::size_t next_read = 0;
    int start_calls = 0;
    int abort_calls = 0;
    std::shared_ptr<int> shared_abort_calls;
    std::shared_ptr<AudioCaptureSourceConfig> shared_start_config;
    AudioCaptureRole started_role = AudioCaptureRole::Microphone;
    std::string endpoint;
    std::int64_t session_start_qpc_100ns = 0;
};

class BlockingStartAudioCaptureSource final : public AudioCaptureSource {
public:
    vrrec_status_t Start(
        const AudioCaptureSourceConfig &) noexcept override
    {
        std::unique_lock lock(mutex_);
        start_entered_ = true;
        changed_.notify_all();
        changed_.wait(lock, [&] { return release_start_; });
        live_ = true;
        return VRREC_STATUS_OK;
    }

    AudioCaptureRead Read() noexcept override
    {
        return {};
    }

    void Abort() noexcept override
    {
        const std::lock_guard lock(mutex_);
        ++abort_calls;
        if (live_) {
            live_ = false;
        }
    }

    void WaitForStart()
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [&] { return start_entered_; });
    }

    void ReleaseStart()
    {
        {
            const std::lock_guard lock(mutex_);
            release_start_ = true;
        }
        changed_.notify_all();
    }

    bool IsLive() const
    {
        const std::lock_guard lock(mutex_);
        return live_;
    }

    std::size_t AbortCalls() const
    {
        const std::lock_guard lock(mutex_);
        return abort_calls;
    }

private:
    mutable std::mutex mutex_;
    std::condition_variable changed_;
    bool start_entered_ = false;
    bool release_start_ = false;
    bool live_ = false;
    std::size_t abort_calls = 0;
};

class FailingRecoverySourceProvider final
    : public AudioCaptureSourceProvider {
public:
    explicit FailingRecoverySourceProvider(
        std::unique_ptr<AudioCaptureSource> initial)
        : initial_(std::move(initial))
    {
    }

    vrrec_status_t Create(
        std::unique_ptr<AudioCaptureSource> &source) noexcept override
    {
        ++create_calls;
        if (initial_ != nullptr) {
            source = std::move(initial_);
            return VRREC_STATUS_OK;
        }

        source.reset();
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    }

    std::size_t create_calls = 0;

private:
    std::unique_ptr<AudioCaptureSource> initial_;
};

class ScriptedSourceProvider final : public AudioCaptureSourceProvider {
public:
    void Add(
        vrrec_status_t status,
        std::unique_ptr<AudioCaptureSource> source = nullptr)
    {
        steps_.push_back({status, std::move(source)});
    }

    vrrec_status_t Create(
        std::unique_ptr<AudioCaptureSource> &source) noexcept override
    {
        ++create_calls;
        if (steps_.empty()) {
            source.reset();
            return VRREC_STATUS_BACKEND_UNAVAILABLE;
        }
        auto step = std::move(steps_.front());
        steps_.pop_front();
        source = std::move(step.source);
        return step.status;
    }

    std::size_t create_calls = 0;

private:
    struct Step final {
        vrrec_status_t status;
        std::unique_ptr<AudioCaptureSource> source;
    };
    std::deque<Step> steps_;
};

class RecordingRecoveryWaiter final : public AudioCaptureRecoveryWaiter {
public:
    bool Wait(std::chrono::milliseconds duration) noexcept override
    {
        ++wait_calls;
        total_wait += duration;
        return true;
    }

    void Abort() noexcept override
    {
    }

    std::size_t wait_calls = 0;
    std::chrono::milliseconds total_wait {0};
};

class BlockingRecoveryWaiter final : public AudioCaptureRecoveryWaiter {
public:
    bool Wait(std::chrono::milliseconds) noexcept override
    {
        std::unique_lock lock(mutex_);
        entered_ = true;
        changed_.notify_all();
        changed_.wait(lock, [&] { return aborted_; });
        return false;
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

    void WaitUntilEntered()
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [&] { return entered_; });
    }

    std::size_t abort_calls = 0;

private:
    std::mutex mutex_;
    std::condition_variable changed_;
    bool entered_ = false;
    bool aborted_ = false;
};

class RecordingInputStartSink final : public AudioCaptureInputStartSink {
public:
    void Started(vrrec_status_t status) noexcept override
    {
        statuses.push_back(status);
    }

    std::vector<vrrec_status_t> statuses;
};

struct AvailabilityEvent final {
    AudioCaptureRole role;
    bool available;
    std::uint64_t frame_position;
};

class RecordingAvailabilitySink final : public AudioCaptureAvailabilitySink {
public:
    void AvailabilityChanged(
        AudioCaptureRole role,
        bool available,
        std::uint64_t frame_position) noexcept override
    {
        CHECK(count < 4);
        events[count++] = {role, available, frame_position};
    }

    void BufferHealthChanged(
        AudioCaptureRole role,
        AudioBufferHealth health,
        std::uint64_t frame_position) noexcept override
    {
        health_role = role;
        health_kind = health;
        health_frame = frame_position;
        ++health_count;
    }

    AvailabilityEvent events[4] {};
    std::size_t count = 0;
    AudioCaptureRole health_role = AudioCaptureRole::Microphone;
    AudioBufferHealth health_kind = AudioBufferHealth::Underrun;
    std::uint64_t health_frame = 0;
    std::size_t health_count = 0;
};

AudioCaptureSourceConfig Config()
{
    return {
        AudioCaptureRole::DesktopLoopback,
        "{render-endpoint}",
        900'000,
    };
}

void StartsAndForwardsAClockedStereoPacket()
{
    FakeAudioCaptureSource source;
    source.reads.push_back({
        AudioCaptureReadResult::Packet,
        0,
        100,
        1'000'000,
        2,
        {0.25F, -0.25F, 0.5F, -0.5F},
        false,
        false,
        0,
    });
    StereoCaptureTimeline timeline(8);
    AudioCapturePump pump(source, timeline);

    CHECK(pump.Start(Config()) == VRREC_STATUS_OK);
    CHECK(source.start_calls == 1);
    CHECK(source.started_role == AudioCaptureRole::DesktopLoopback);
    CHECK(source.endpoint == "{render-endpoint}");
    CHECK(source.session_start_qpc_100ns == 900'000);
    CHECK(pump.PumpOne() == AudioCapturePumpResult::PacketAccepted);

    std::vector<float> output(4, -1.0F);
    AudioTimelineRead read {};
    CHECK(timeline.WaitRead(2, output, read) == AudioTimelineResult::Ready);
    CHECK(!read.underrun);
    CHECK(output == source.reads[0].samples);
}

void ExpandsSilentPacketsUsingTheirExplicitFrameCount()
{
    FakeAudioCaptureSource source;
    source.reads.push_back({
        AudioCaptureReadResult::Packet,
        0,
        200,
        2'000'000,
        4,
        std::vector<float>(8, 0.75F),
        true,
        false,
        0,
    });
    StereoCaptureTimeline timeline(8);
    AudioCapturePump pump(source, timeline);

    CHECK(pump.Start(Config()) == VRREC_STATUS_OK);
    CHECK(pump.PumpOne() == AudioCapturePumpResult::PacketAccepted);

    std::vector<float> output(8, -1.0F);
    AudioTimelineRead read {};
    CHECK(timeline.WaitRead(4, output, read) == AudioTimelineResult::Ready);
    CHECK(!read.underrun);
    for (const auto sample : output) {
        CHECK(sample == 0.0F);
    }
}

void ReportsTimelineOverrunAtTheRejectedPacketFrame()
{
    FakeAudioCaptureSource source;
    source.reads.push_back({
        AudioCaptureReadResult::Packet,
        24'000,
        300,
        3'000'000,
        9,
        std::vector<float>(18, 0.25F),
        false,
        false,
        0,
    });
    StereoCaptureTimeline timeline(8);
    RecordingAvailabilitySink events;
    AudioCapturePump pump(source, timeline, &events);

    CHECK(pump.Start(Config()) == VRREC_STATUS_OK);
    CHECK(pump.PumpOne() == AudioCapturePumpResult::Overrun);
    CHECK(events.health_count == 1);
    CHECK(events.health_role == AudioCaptureRole::DesktopLoopback);
    CHECK(events.health_kind == AudioBufferHealth::Overrun);
    CHECK(events.health_frame == 24'000);
}

void AppliesLossAtTheExactFrameAndAbortsIdempotently()
{
    FakeAudioCaptureSource source;
    source.reads.push_back({
        AudioCaptureReadResult::Packet,
        0,
        300,
        3'000'000,
        2,
        {0.25F, -0.25F, 0.5F, -0.5F},
        false,
        false,
        0,
    });
    source.reads.push_back({
        AudioCaptureReadResult::DeviceLost,
        0,
        0,
        0,
        0,
        {},
        false,
        false,
        2,
    });
    StereoCaptureTimeline timeline(8);
    AudioCapturePump pump(source, timeline);

    CHECK(pump.Start(Config()) == VRREC_STATUS_OK);
    CHECK(pump.PumpOne() == AudioCapturePumpResult::PacketAccepted);
    CHECK(pump.PumpOne() == AudioCapturePumpResult::DeviceLost);

    std::vector<float> output(8, -1.0F);
    AudioTimelineRead read {};
    CHECK(timeline.WaitRead(4, output, read) == AudioTimelineResult::Ready);
    CHECK(!read.input_available);
    CHECK(read.underrun);
    CHECK(output[0] == 0.25F);
    CHECK(output[1] == -0.25F);
    CHECK(output[2] == 0.5F);
    CHECK(output[3] == -0.5F);
    for (std::size_t index = 4; index < output.size(); ++index) {
        CHECK(output[index] == 0.0F);
    }

    pump.Abort();
    pump.Abort();
    CHECK(source.abort_calls == 1);
    CHECK(timeline.WaitRead(4, output, read) == AudioTimelineResult::Aborted);
    CHECK(timeline.FramePosition() == 4);
}

void RetainsAFlaggedPacketAfterItsExplicitGap()
{
    FakeAudioCaptureSource source;
    source.reads.push_back({
        AudioCaptureReadResult::Packet,
        0,
        100,
        1'000'000,
        2,
        {0.25F, 0.25F, 0.25F, 0.25F},
        false,
        false,
        0,
    });
    source.reads.push_back({
        AudioCaptureReadResult::Packet,
        4,
        0,
        1'010'000,
        2,
        {0.75F, 0.75F, 0.75F, 0.75F},
        false,
        true,
        0,
    });
    source.reads.push_back({
        AudioCaptureReadResult::DeviceLost,
        0,
        0,
        0,
        0,
        {},
        false,
        false,
        6,
    });
    StereoCaptureTimeline timeline(12);
    AudioCapturePump pump(source, timeline);

    CHECK(pump.Start(Config()) == VRREC_STATUS_OK);
    CHECK(pump.PumpOne() == AudioCapturePumpResult::PacketAccepted);
    CHECK(pump.PumpOne() == AudioCapturePumpResult::PacketAccepted);
    CHECK(pump.PumpOne() == AudioCapturePumpResult::DeviceLost);

    std::vector<float> output(12, -1.0F);
    AudioTimelineRead read {};
    CHECK(timeline.WaitRead(6, output, read) == AudioTimelineResult::Ready);
    CHECK(read.underrun);
    CHECK(read.input_available);
    for (std::size_t frame = 0; frame < 6; ++frame) {
        const auto expected = frame < 2
            ? 0.25F
            : (frame < 4 ? 0.0F : 0.75F);
        CHECK(output[frame * 2U] == expected);
        CHECK(output[frame * 2U + 1U] == expected);
    }
}

void RecoversAReplacementSourceAtItsExactFirstFrame()
{
    FakeAudioCaptureSource lost_source;
    lost_source.reads.push_back({
        AudioCaptureReadResult::Packet,
        0,
        500,
        2'000'000,
        2,
        {0.25F, 0.25F, 0.25F, 0.25F},
        false,
        false,
        0,
    });
    lost_source.reads.push_back({
        AudioCaptureReadResult::DeviceLost,
        0,
        0,
        0,
        0,
        {},
        false,
        false,
        2,
    });
    StereoCaptureTimeline timeline(12);
    AudioCapturePump initial(lost_source, timeline);
    CHECK(initial.Start(Config()) == VRREC_STATUS_OK);
    CHECK(initial.PumpOne() == AudioCapturePumpResult::PacketAccepted);
    CHECK(initial.PumpOne() == AudioCapturePumpResult::DeviceLost);

    FakeAudioCaptureSource recovered_source;
    recovered_source.reads.push_back({
        AudioCaptureReadResult::Packet,
        4,
        0,
        2'010'000,
        2,
        {0.75F, 0.75F, 0.75F, 0.75F},
        false,
        false,
        0,
    });
    AudioCapturePump recovered(recovered_source, timeline);
    CHECK(recovered.StartRecovery(Config()) == VRREC_STATUS_OK);
    CHECK(recovered.PumpOne() == AudioCapturePumpResult::PacketAccepted);

    std::vector<float> output(12, -1.0F);
    AudioTimelineRead read {};
    CHECK(timeline.WaitRead(6, output, read) == AudioTimelineResult::Ready);
    CHECK(read.input_available);
    CHECK(read.underrun);
    for (std::size_t frame = 0; frame < 6; ++frame) {
        const auto expected = frame < 2
            ? 0.25F
            : (frame < 4 ? 0.0F : 0.75F);
        CHECK(output[frame * 2U] == expected);
        CHECK(output[frame * 2U + 1U] == expected);
    }
}

void FailedReplacementStartDoesNotPoisonLaterRecovery()
{
    FakeAudioCaptureSource lost_source;
    lost_source.reads.push_back({
        AudioCaptureReadResult::Packet,
        0,
        500,
        2'000'000,
        2,
        {0.25F, 0.25F, 0.25F, 0.25F},
        false,
        false,
        0,
    });
    lost_source.reads.push_back({
        AudioCaptureReadResult::DeviceLost,
        0,
        0,
        0,
        0,
        {},
        false,
        false,
        2,
    });
    StereoCaptureTimeline timeline(12);
    AudioCapturePump initial(lost_source, timeline);
    CHECK(initial.Start(Config()) == VRREC_STATUS_OK);
    CHECK(initial.PumpOne() == AudioCapturePumpResult::PacketAccepted);
    CHECK(initial.PumpOne() == AudioCapturePumpResult::DeviceLost);

    FakeAudioCaptureSource failed_replacement;
    failed_replacement.start_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    AudioCapturePump failed(failed_replacement, timeline);
    CHECK(failed.StartRecovery(Config()) ==
          VRREC_STATUS_BACKEND_UNAVAILABLE);

    FakeAudioCaptureSource recovered_source;
    recovered_source.reads.push_back({
        AudioCaptureReadResult::Packet,
        4,
        0,
        2'010'000,
        2,
        {0.75F, 0.75F, 0.75F, 0.75F},
        false,
        false,
        0,
    });
    AudioCapturePump recovered(recovered_source, timeline);
    CHECK(recovered.StartRecovery(Config()) == VRREC_STATUS_OK);
    CHECK(recovered.PumpOne() == AudioCapturePumpResult::PacketAccepted);

    std::vector<float> output(12, -1.0F);
    AudioTimelineRead read {};
    CHECK(timeline.WaitRead(6, output, read) == AudioTimelineResult::Ready);
    CHECK(read.input_available);
    CHECK(read.underrun);
    for (std::size_t frame = 0; frame < 6; ++frame) {
        const auto expected = frame < 2
            ? 0.25F
            : (frame < 4 ? 0.0F : 0.75F);
        CHECK(output[frame * 2U] == expected);
        CHECK(output[frame * 2U + 1U] == expected);
    }
}

void ReportsLossAndRecoveryAtTheirExactFrames()
{
    FakeAudioCaptureSource lost_source;
    lost_source.reads.push_back({
        AudioCaptureReadResult::Packet,
        0,
        500,
        2'000'000,
        2,
        {0.25F, 0.25F, 0.25F, 0.25F},
        false,
        false,
        0,
    });
    lost_source.reads.push_back({
        AudioCaptureReadResult::DeviceLost,
        0,
        0,
        0,
        0,
        {},
        false,
        false,
        2,
    });
    StereoCaptureTimeline timeline(12);
    RecordingAvailabilitySink availability;
    AudioCapturePump initial(lost_source, timeline, &availability);
    CHECK(initial.Start(Config()) == VRREC_STATUS_OK);
    CHECK(initial.PumpOne() == AudioCapturePumpResult::PacketAccepted);
    CHECK(initial.PumpOne() == AudioCapturePumpResult::DeviceLost);
    CHECK(availability.count == 1);
    CHECK(availability.events[0].role ==
          AudioCaptureRole::DesktopLoopback);
    CHECK(!availability.events[0].available);
    CHECK(availability.events[0].frame_position == 2);

    FakeAudioCaptureSource recovered_source;
    recovered_source.reads.push_back({
        AudioCaptureReadResult::Packet,
        4,
        0,
        2'010'000,
        2,
        {0.75F, 0.75F, 0.75F, 0.75F},
        false,
        false,
        0,
    });
    AudioCapturePump recovered(
        recovered_source,
        timeline,
        &availability);
    CHECK(recovered.StartRecovery(Config()) == VRREC_STATUS_OK);
    CHECK(recovered.PumpOne() == AudioCapturePumpResult::PacketAccepted);
    CHECK(availability.count == 2);
    CHECK(availability.events[1].role ==
          AudioCaptureRole::DesktopLoopback);
    CHECK(availability.events[1].available);
    CHECK(availability.events[1].frame_position == 4);
}

void StopsDefaultEndpointRediscoveryAfterFiveSeconds()
{
    auto initial = std::make_unique<FakeAudioCaptureSource>();
    initial->reads.push_back({
        AudioCaptureReadResult::Packet,
        0,
        100,
        3'000'000,
        2,
        {0.25F, 0.25F, 0.25F, 0.25F},
        false,
        false,
        0,
    });
    initial->reads.push_back({
        AudioCaptureReadResult::DeviceLost,
        0,
        0,
        0,
        0,
        {},
        false,
        false,
        2,
    });
    FailingRecoverySourceProvider provider(std::move(initial));
    RecordingRecoveryWaiter waiter;
    StereoCaptureTimeline timeline(12);
    AudioCaptureInputRunner runner(provider, waiter, timeline);

    CHECK(runner.Run(Config()) ==
          AudioCaptureInputResult::RecoveryTimedOut);
    CHECK(provider.create_calls == 51);
    CHECK(waiter.wait_calls == 50);
    CHECK(waiter.total_wait == std::chrono::seconds(5));

    std::vector<float> output(8, -1.0F);
    AudioTimelineRead read {};
    CHECK(timeline.WaitRead(4, output, read) == AudioTimelineResult::Ready);
    CHECK(!read.input_available);
    CHECK(read.underrun);
    for (std::size_t frame = 0; frame < 4; ++frame) {
        const auto expected = frame < 2 ? 0.25F : 0.0F;
        CHECK(output[frame * 2U] == expected);
        CHECK(output[frame * 2U + 1U] == expected);
    }
}

void AbortReleasesARecoveryWaitImmediately()
{
    using namespace std::chrono_literals;

    auto initial = std::make_unique<FakeAudioCaptureSource>();
    initial->reads.push_back({
        AudioCaptureReadResult::Packet,
        0,
        100,
        4'000'000,
        2,
        {0.25F, 0.25F, 0.25F, 0.25F},
        false,
        false,
        0,
    });
    initial->reads.push_back({
        AudioCaptureReadResult::DeviceLost,
        0,
        0,
        0,
        0,
        {},
        false,
        false,
        2,
    });
    FailingRecoverySourceProvider provider(std::move(initial));
    BlockingRecoveryWaiter waiter;
    StereoCaptureTimeline timeline(12);
    AudioCaptureInputRunner runner(provider, waiter, timeline);
    auto running = std::async(std::launch::async, [&] {
        return runner.Run(Config());
    });
    waiter.WaitUntilEntered();

    runner.Abort();

    CHECK(running.wait_for(1s) == std::future_status::ready);
    CHECK(running.get() == AudioCaptureInputResult::Aborted);
    CHECK(waiter.abort_calls == 1);
    std::vector<float> output(8, -1.0F);
    AudioTimelineRead read {};
    CHECK(timeline.WaitRead(4, output, read) == AudioTimelineResult::Aborted);
}

void ReportsInitialCaptureStartExactlyOnce()
{
    auto source = std::make_unique<FakeAudioCaptureSource>();
    source->reads.push_back({
        AudioCaptureReadResult::Failed,
        0,
        0,
        0,
        0,
        {},
        false,
        false,
        0,
    });
    FailingRecoverySourceProvider provider(std::move(source));
    RecordingRecoveryWaiter waiter;
    RecordingInputStartSink starts;
    StereoCaptureTimeline timeline(8);
    AudioCaptureInputRunner runner(provider, waiter, timeline);

    CHECK(runner.Run(Config(), starts) == AudioCaptureInputResult::Failed);
    CHECK(starts.statuses.size() == 1);
    CHECK(starts.statuses[0] == VRREC_STATUS_OK);
}

void ReportsInitialCaptureStartFailureExactlyOnce()
{
    auto source = std::make_unique<FakeAudioCaptureSource>();
    source->start_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    FailingRecoverySourceProvider provider(std::move(source));
    RecordingRecoveryWaiter waiter;
    RecordingInputStartSink starts;
    StereoCaptureTimeline timeline(8);
    AudioCaptureInputRunner runner(provider, waiter, timeline);

    CHECK(runner.Run(Config(), starts) == AudioCaptureInputResult::Failed);
    CHECK(starts.statuses.size() == 1);
    CHECK(starts.statuses[0] == VRREC_STATUS_BACKEND_UNAVAILABLE);
}

void RejectsConcurrentAndPostAbortRunnerStarts()
{
    auto source = std::make_unique<BlockingStartAudioCaptureSource>();
    auto *borrowed = source.get();
    ScriptedSourceProvider provider;
    provider.Add(VRREC_STATUS_OK, std::move(source));
    RecordingRecoveryWaiter waiter;
    StereoCaptureTimeline timeline(8);
    AudioCaptureInputRunner runner(provider, waiter, timeline);
    RecordingInputStartSink first_start;
    auto running = std::async(std::launch::async, [&] {
        return runner.Run(Config(), first_start);
    });
    borrowed->WaitForStart();

    RecordingInputStartSink concurrent_start;
    CHECK(runner.Run(Config(), concurrent_start) ==
          AudioCaptureInputResult::InvalidState);
    CHECK(concurrent_start.statuses ==
          std::vector<vrrec_status_t> {VRREC_STATUS_INVALID_STATE});

    runner.Abort();
    runner.Abort();
    borrowed->ReleaseStart();
    CHECK(running.get() == AudioCaptureInputResult::Aborted);
    CHECK(waiter.wait_calls == 0);

    RecordingInputStartSink post_abort_start;
    CHECK(runner.Run(Config(), post_abort_start) ==
          AudioCaptureInputResult::InvalidState);
    CHECK(post_abort_start.statuses ==
          std::vector<vrrec_status_t> {VRREC_STATUS_INVALID_STATE});
}

void RejectsInitialProviderFailureAndNullSuccess()
{
    for (const auto status : {
             VRREC_STATUS_BACKEND_UNAVAILABLE,
             VRREC_STATUS_OK,
         }) {
        ScriptedSourceProvider provider;
        provider.Add(status);
        RecordingRecoveryWaiter waiter;
        RecordingInputStartSink starts;
        StereoCaptureTimeline timeline(8);
        AudioCaptureInputRunner runner(provider, waiter, timeline);
        CHECK(runner.Run(Config(), starts) ==
              AudioCaptureInputResult::Failed);
        CHECK(starts.statuses.size() == 1);
        CHECK(starts.statuses[0] == (status == VRREC_STATUS_OK
                  ? VRREC_STATUS_INTERNAL_ERROR
                  : status));
    }
}

void RecoversTheDefaultCaptureEndpointAndClearsRecoveryState()
{
    auto initial = std::make_unique<FakeAudioCaptureSource>();
    initial->reads.push_back({
        AudioCaptureReadResult::DeviceLost,
        0,
        0,
        0,
        0,
        {},
        false,
        false,
        0,
    });
    auto recovered = std::make_unique<FakeAudioCaptureSource>();
    auto recovered_start =
        std::make_shared<AudioCaptureSourceConfig>();
    recovered->shared_start_config = recovered_start;
    recovered->reads.push_back({
        AudioCaptureReadResult::Packet,
        0,
        0,
        6'000'000,
        1,
        {0.5F, 0.5F},
        false,
        false,
        0,
    });
    recovered->reads.push_back({
        AudioCaptureReadResult::Failed, 0, 0, 0, 0, {}, false, false, 0});
    ScriptedSourceProvider provider;
    provider.Add(VRREC_STATUS_OK, std::move(initial));
    provider.Add(VRREC_STATUS_OK, std::move(recovered));
    RecordingRecoveryWaiter waiter;
    RecordingInputStartSink starts;
    StereoCaptureTimeline timeline(8);
    AudioCaptureInputRunner runner(provider, waiter, timeline);
    auto config = Config();
    config.role = AudioCaptureRole::Microphone;
    config.endpoint_id_utf8 = "{capture-endpoint}";

    CHECK(runner.Run(config, starts) == AudioCaptureInputResult::Failed);
    CHECK(starts.statuses ==
          std::vector<vrrec_status_t> {VRREC_STATUS_OK});
    CHECK(recovered_start->role == AudioCaptureRole::Microphone);
    CHECK(recovered_start->endpoint_id_utf8 == "default-capture");
    CHECK(waiter.wait_calls == 0);
    CHECK(provider.create_calls == 2);
}

void RetriesAFailedRecoveryStartBeforeUsingTheNextSource()
{
    auto initial = std::make_unique<FakeAudioCaptureSource>();
    initial->reads.push_back({
        AudioCaptureReadResult::DeviceLost,
        0, 0, 0, 0, {}, false, false, 0});
    auto failed = std::make_unique<FakeAudioCaptureSource>();
    failed->start_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    auto final_source = std::make_unique<FakeAudioCaptureSource>();
    final_source->reads.push_back({
        AudioCaptureReadResult::Failed, 0, 0, 0, 0, {}, false, false, 0});
    ScriptedSourceProvider provider;
    provider.Add(VRREC_STATUS_OK, std::move(initial));
    provider.Add(VRREC_STATUS_OK, std::move(failed));
    provider.Add(VRREC_STATUS_OK, std::move(final_source));
    RecordingRecoveryWaiter waiter;
    StereoCaptureTimeline timeline(8);
    AudioCaptureInputRunner runner(provider, waiter, timeline);

    CHECK(runner.Run(Config()) == AudioCaptureInputResult::Failed);
    CHECK(provider.create_calls == 3);
    CHECK(waiter.wait_calls == 1);
    CHECK(waiter.total_wait == AudioCaptureInputRunner::RetryInterval);
}

void MapsAnAbortedPumpWithoutRetrying()
{
    auto source = std::make_unique<FakeAudioCaptureSource>();
    source->reads.push_back({
        AudioCaptureReadResult::Aborted, 0, 0, 0, 0, {}, false, false, 0});
    ScriptedSourceProvider provider;
    provider.Add(VRREC_STATUS_OK, std::move(source));
    RecordingRecoveryWaiter waiter;
    RecordingInputStartSink starts;
    StereoCaptureTimeline timeline(8);
    AudioCaptureInputRunner runner(provider, waiter, timeline);

    CHECK(runner.Run(Config(), starts) == AudioCaptureInputResult::Aborted);
    CHECK(starts.statuses ==
          std::vector<vrrec_status_t> {VRREC_STATUS_OK});
    CHECK(waiter.wait_calls == 0);
}

void FatalPacketFailureAbortsTheSourceAndTimeline()
{
    auto abort_calls = std::make_shared<int>(0);
    auto source = std::make_unique<FakeAudioCaptureSource>();
    source->shared_abort_calls = abort_calls;
    source->reads.push_back({
        AudioCaptureReadResult::Packet,
        0,
        100,
        5'000'000,
        2,
        {0.25F, -0.25F},
        false,
        false,
        0,
    });
    FailingRecoverySourceProvider provider(std::move(source));
    RecordingRecoveryWaiter waiter;
    StereoCaptureTimeline timeline(8);
    AudioCaptureInputRunner runner(provider, waiter, timeline);

    CHECK(runner.Run(Config()) == AudioCaptureInputResult::Failed);
    CHECK(*abort_calls == 1);
    const std::vector<float> samples {0.0F, 0.0F};
    CHECK(timeline.Push({
              0,
              {0, 5'000'000, 10'000'000},
              samples,
              false,
          }) == AudioTimelineResult::Aborted);
}

void AbortDuringSourceStartPerformsPostStartCleanup()
{
    BlockingStartAudioCaptureSource source;
    StereoCaptureTimeline timeline(8);
    AudioCapturePump pump(source, timeline);
    auto starting = std::async(std::launch::async, [&] {
        return pump.Start(Config());
    });
    source.WaitForStart();

    pump.Abort();
    source.ReleaseStart();

    CHECK(starting.get() == VRREC_STATUS_INVALID_STATE);
    CHECK(!source.IsLive());
    CHECK(source.AbortCalls() == 2);
}

void RejectsPumpStateAndPacketBoundaries()
{
    FakeAudioCaptureSource source;
    source.reads.push_back({
        AudioCaptureReadResult::Packet,
        0, 0, 0, 0, {}, false, false, 0});
    source.reads.push_back({
        AudioCaptureReadResult::Packet,
        0, 0, 0,
        std::numeric_limits<std::size_t>::max(),
        {}, true, false, 0});
    source.reads.push_back({
        static_cast<AudioCaptureReadResult>(999U),
        0, 0, 0, 0, {}, false, false, 0});
    StereoCaptureTimeline timeline(8);
    AudioCapturePump pump(source, timeline);

    CHECK(pump.PumpOne() == AudioCapturePumpResult::InvalidState);
    CHECK(pump.Start(Config()) == VRREC_STATUS_OK);
    CHECK(pump.Start(Config()) == VRREC_STATUS_INVALID_STATE);
    CHECK(pump.PumpOne() == AudioCapturePumpResult::InvalidPacket);
    CHECK(pump.PumpOne() == AudioCapturePumpResult::InvalidPacket);
    CHECK(pump.PumpOne() == AudioCapturePumpResult::Failed);

    pump.Abort();
    CHECK(pump.PumpOne() == AudioCapturePumpResult::Aborted);
    CHECK(pump.StartRecovery(Config()) == VRREC_STATUS_INVALID_STATE);
}

void AbortsTheTimelineWhenSilentPacketExpansionFails()
{
    FakeAudioCaptureSource source;
    source.reads.push_back({
        AudioCaptureReadResult::Packet,
        0, 0, 1'000'000, 2, {}, true, false, 0});
    StereoCaptureTimeline timeline(8);
    AudioCapturePump pump(source, timeline);
    CHECK(pump.Start(Config()) == VRREC_STATUS_OK);

    allocation_failure::fail_on_allocation = 1;
    CHECK(pump.PumpOne() == AudioCapturePumpResult::Failed);
    allocation_failure::fail_on_allocation = 0;

    const std::vector<float> samples {0.0F, 0.0F};
    CHECK(timeline.Push({
              0,
              {0, 1'000'000, 10'000'000},
              samples,
              false,
          }) == AudioTimelineResult::Aborted);
}

void RejectsInvalidRecoveryAndAbortsFailedAppliedRecovery()
{
    FakeAudioCaptureSource invalid_source;
    invalid_source.reads.push_back({
        AudioCaptureReadResult::Packet,
        0, 0, 1'000'000, 1, {0.5F, 0.5F}, false, false, 0});
    StereoCaptureTimeline invalid_timeline(8);
    AudioCapturePump invalid_recovery(invalid_source, invalid_timeline);
    CHECK(invalid_recovery.StartRecovery(Config()) == VRREC_STATUS_OK);
    CHECK(invalid_recovery.PumpOne() ==
          AudioCapturePumpResult::InvalidPacket);

    FakeAudioCaptureSource overrun_source;
    overrun_source.reads.push_back({
        AudioCaptureReadResult::Packet,
        0, 0, 1'000'000, 9,
        std::vector<float>(18, 0.5F), false, false, 0});
    StereoCaptureTimeline overrun_timeline(8);
    CHECK(overrun_timeline.SetAvailable(false, 0) ==
          AudioTimelineResult::Ready);
    AudioCapturePump failed_recovery(overrun_source, overrun_timeline);
    CHECK(failed_recovery.StartRecovery(Config()) == VRREC_STATUS_OK);
    CHECK(failed_recovery.PumpOne() == AudioCapturePumpResult::Overrun);

    const std::vector<float> samples {0.0F, 0.0F};
    CHECK(overrun_timeline.Push({
              0,
              {0, 1'000'000, 10'000'000},
              samples,
              false,
          }) == AudioTimelineResult::Aborted);
}

void MapsDeviceLossAgainstAnAbortedTimeline()
{
    FakeAudioCaptureSource source;
    source.reads.push_back({
        AudioCaptureReadResult::DeviceLost,
        0, 0, 0, 0, {}, false, false, 0});
    StereoCaptureTimeline timeline(8);
    AudioCapturePump pump(source, timeline);
    CHECK(pump.Start(Config()) == VRREC_STATUS_OK);
    timeline.Abort();
    CHECK(pump.PumpOne() == AudioCapturePumpResult::Aborted);
}

void RecoveryConfigurationAllocationFailureStopsBeforeSourceCreation()
{
    ScriptedSourceProvider provider;
    RecordingRecoveryWaiter waiter;
    RecordingInputStartSink starts;
    StereoCaptureTimeline timeline(8);
    AudioCaptureInputRunner runner(provider, waiter, timeline);
    auto config = Config();
    config.endpoint_id_utf8.assign(256, 'x');

    allocation_failure::fail_on_allocation = 1;
    const auto result = runner.Run(config, starts);
    allocation_failure::fail_on_allocation = 0;

    CHECK(result == AudioCaptureInputResult::Failed);
    CHECK(starts.statuses ==
          std::vector<vrrec_status_t> {VRREC_STATUS_INTERNAL_ERROR});
    CHECK(provider.create_calls == 0);
    CHECK(waiter.wait_calls == 0);
}

}

int main()
{
    StartsAndForwardsAClockedStereoPacket();
    ExpandsSilentPacketsUsingTheirExplicitFrameCount();
    ReportsTimelineOverrunAtTheRejectedPacketFrame();
    AppliesLossAtTheExactFrameAndAbortsIdempotently();
    RetainsAFlaggedPacketAfterItsExplicitGap();
    RecoversAReplacementSourceAtItsExactFirstFrame();
    FailedReplacementStartDoesNotPoisonLaterRecovery();
    ReportsLossAndRecoveryAtTheirExactFrames();
    StopsDefaultEndpointRediscoveryAfterFiveSeconds();
    AbortReleasesARecoveryWaitImmediately();
    ReportsInitialCaptureStartExactlyOnce();
    ReportsInitialCaptureStartFailureExactlyOnce();
    RejectsConcurrentAndPostAbortRunnerStarts();
    RejectsInitialProviderFailureAndNullSuccess();
    RecoversTheDefaultCaptureEndpointAndClearsRecoveryState();
    RetriesAFailedRecoveryStartBeforeUsingTheNextSource();
    MapsAnAbortedPumpWithoutRetrying();
    FatalPacketFailureAbortsTheSourceAndTimeline();
    AbortDuringSourceStartPerformsPostStartCleanup();
    RejectsPumpStateAndPacketBoundaries();
    AbortsTheTimelineWhenSilentPacketExpansionFails();
    RejectsInvalidRecoveryAndAbortsFailedAppliedRecovery();
    MapsDeviceLossAgainstAnAbortedTimeline();
    RecoveryConfigurationAllocationFailureStopsBeforeSourceCreation();
    return 0;
}
