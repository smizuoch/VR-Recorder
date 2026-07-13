#include "audio_capture_session.hpp"

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdlib>
#include <iostream>
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

struct SourceState final {
    std::mutex mutex;
    std::condition_variable changed;
    AudioCaptureRole role = AudioCaptureRole::DesktopLoopback;
    std::string endpoint;
    std::int64_t session_start_qpc_100ns = 0;
    bool aborted = false;
    std::atomic_int start_calls = 0;
    std::atomic_int abort_calls = 0;
};

class PacketThenBlockingSource final : public AudioCaptureSource {
public:
    PacketThenBlockingSource(
        std::shared_ptr<SourceState> state,
        vrrec_status_t start_status,
        float sample,
        std::size_t frame_count,
        bool lose_after_packet = false)
        : state_(std::move(state)),
          start_status_(start_status),
          samples_(frame_count * 2U, sample),
          frame_count_(frame_count),
          lose_after_packet_(lose_after_packet)
    {
    }

    vrrec_status_t Start(
        const AudioCaptureSourceConfig &config) noexcept override
    {
        ++state_->start_calls;
        const std::lock_guard lock(state_->mutex);
        state_->role = config.role;
        state_->endpoint = config.endpoint_id_utf8;
        state_->session_start_qpc_100ns = config.session_start_qpc_100ns;
        return start_status_;
    }

    AudioCaptureRead Read() noexcept override
    {
        if (!packet_returned_) {
            packet_returned_ = true;
            return {
                AudioCaptureReadResult::Packet,
                {
                    0,
                    100,
                    1'000'000,
                    frame_count_,
                    std::span<const float>(samples_),
                    false,
                    false,
                },
                0,
            };
        }

        if (lose_after_packet_ && !loss_returned_) {
            loss_returned_ = true;
            return {AudioCaptureReadResult::DeviceLost, {}, frame_count_};
        }

        std::unique_lock lock(state_->mutex);
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
    std::vector<float> samples_;
    std::size_t frame_count_;
    bool packet_returned_ = false;
    bool lose_after_packet_ = false;
    bool loss_returned_ = false;
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
    explicit ScriptedThreadFactory(std::vector<vrrec_status_t> statuses)
        : statuses_(std::move(statuses))
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
        if (status == VRREC_STATUS_OK) {
            thread = std::thread(entry, context);
        }
        return status;
    }

    std::size_t start_calls = 0;

private:
    std::vector<vrrec_status_t> statuses_;
    std::size_t next_status_ = 0;
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

class BlockingAvailabilitySink final : public AudioCaptureAvailabilitySink {
public:
    void AvailabilityChanged(
        AudioCaptureRole role,
        bool available,
        std::uint64_t frame_position) noexcept override
    {
        {
            const std::lock_guard lock(mutex);
            event_role = role;
            event_available = available;
            event_frame = frame_position;
            ++event_count;
        }

        changed.notify_all();
    }

    void WaitForEvent()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return event_count > 0; });
    }

    std::mutex mutex;
    std::condition_variable changed;
    AudioCaptureRole event_role = AudioCaptureRole::Microphone;
    bool event_available = true;
    std::uint64_t event_frame = 0;
    std::size_t event_count = 0;
};

StereoAudioCaptureSessionConfig Config()
{
    return {
        "render-endpoint-id",
        "microphone-endpoint-id",
        900'000,
    };
}

void StartsBothInputsAndMixesOneScheduledWindow()
{
    auto desktop_state = std::make_shared<SourceState>();
    auto microphone_state = std::make_shared<SourceState>();
    SingleSourceProvider desktop_provider(
        std::make_unique<PacketThenBlockingSource>(
            desktop_state,
            VRREC_STATUS_OK,
            0.25F,
            2));
    SingleSourceProvider microphone_provider(
        std::make_unique<PacketThenBlockingSource>(
            microphone_state,
            VRREC_STATUS_OK,
            0.5F,
            2));
    RecordingWaiter desktop_waiter;
    RecordingWaiter microphone_waiter;
    StereoAudioCaptureSession session(
        desktop_provider,
        desktop_waiter,
        microphone_provider,
        microphone_waiter,
        16,
        VRREC_AUDIO_ROUTING_MIXED,
        0.0,
        0.0);

    CHECK(session.Start(Config()) == VRREC_STATUS_OK);
    std::vector<float> output(4, -1.0F);
    StereoAudioMixRead read {};
    CHECK(session.MixNext(2, output, read) == StereoAudioMixResult::Mixed);
    CHECK(read.start_frame_48k == 0);
    CHECK(read.frame_count_48k == 2);
    CHECK(read.desktop_available);
    CHECK(read.microphone_available);
    for (const auto sample : output) {
        CHECK(sample == 0.75F);
    }

    {
        const std::lock_guard lock(desktop_state->mutex);
        CHECK(desktop_state->role == AudioCaptureRole::DesktopLoopback);
        CHECK(desktop_state->endpoint == "render-endpoint-id");
        CHECK(desktop_state->session_start_qpc_100ns == 900'000);
    }
    {
        const std::lock_guard lock(microphone_state->mutex);
        CHECK(microphone_state->role == AudioCaptureRole::Microphone);
        CHECK(microphone_state->endpoint == "microphone-endpoint-id");
        CHECK(microphone_state->session_start_qpc_100ns == 900'000);
    }

    session.Abort();
    session.Abort();
    CHECK(desktop_state->abort_calls == 1);
    CHECK(microphone_state->abort_calls == 1);
    CHECK(desktop_waiter.abort_calls == 1);
    CHECK(microphone_waiter.abort_calls == 1);
}

void RollsBackDesktopWhenMicrophoneStartFails()
{
    auto desktop_state = std::make_shared<SourceState>();
    auto microphone_state = std::make_shared<SourceState>();
    SingleSourceProvider desktop_provider(
        std::make_unique<PacketThenBlockingSource>(
            desktop_state,
            VRREC_STATUS_OK,
            0.25F,
            2));
    SingleSourceProvider microphone_provider(
        std::make_unique<PacketThenBlockingSource>(
            microphone_state,
            VRREC_STATUS_BACKEND_UNAVAILABLE,
            0.5F,
            2));
    RecordingWaiter desktop_waiter;
    RecordingWaiter microphone_waiter;
    StereoAudioCaptureSession session(
        desktop_provider,
        desktop_waiter,
        microphone_provider,
        microphone_waiter,
        16,
        VRREC_AUDIO_ROUTING_MIXED,
        0.0,
        0.0);

    CHECK(session.Start(Config()) == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(desktop_state->abort_calls == 1);
    CHECK(microphone_state->abort_calls == 0);
    std::vector<float> output(4, -1.0F);
    StereoAudioMixRead read {99, 7, true, true, true, true};
    CHECK(session.MixNext(2, output, read) ==
          StereoAudioMixResult::InvalidState);
    CHECK(read.start_frame_48k == 0);
    CHECK(read.frame_count_48k == 0);
    CHECK(!read.desktop_available);
    CHECK(!read.microphone_available);
    CHECK(!read.desktop_underrun);
    CHECK(!read.microphone_underrun);
}

void RejectsInvalidRoutingBeforeStartingInputs()
{
    auto desktop_state = std::make_shared<SourceState>();
    auto microphone_state = std::make_shared<SourceState>();
    SingleSourceProvider desktop_provider(
        std::make_unique<PacketThenBlockingSource>(
            desktop_state,
            VRREC_STATUS_OK,
            0.25F,
            2));
    SingleSourceProvider microphone_provider(
        std::make_unique<PacketThenBlockingSource>(
            microphone_state,
            VRREC_STATUS_OK,
            0.5F,
            2));
    RecordingWaiter desktop_waiter;
    RecordingWaiter microphone_waiter;
    StereoAudioCaptureSession session(
        desktop_provider,
        desktop_waiter,
        microphone_provider,
        microphone_waiter,
        16,
        999U,
        0.0,
        0.0);

    CHECK(session.Start(Config()) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(desktop_state->start_calls == 0);
    CHECK(microphone_state->start_calls == 0);
}

void PropagatesDesktopLossWithItsExactFrame()
{
    auto desktop_state = std::make_shared<SourceState>();
    auto microphone_state = std::make_shared<SourceState>();
    SingleSourceProvider desktop_provider(
        std::make_unique<PacketThenBlockingSource>(
            desktop_state,
            VRREC_STATUS_OK,
            0.25F,
            2,
            true));
    SingleSourceProvider microphone_provider(
        std::make_unique<PacketThenBlockingSource>(
            microphone_state,
            VRREC_STATUS_OK,
            0.5F,
            2));
    RecordingWaiter desktop_waiter;
    RecordingWaiter microphone_waiter;
    BlockingAvailabilitySink availability;
    StereoAudioCaptureSession session(
        desktop_provider,
        desktop_waiter,
        microphone_provider,
        microphone_waiter,
        16,
        VRREC_AUDIO_ROUTING_MIXED,
        0.0,
        0.0,
        &availability);

    CHECK(session.Start(Config()) == VRREC_STATUS_OK);
    availability.WaitForEvent();
    {
        const std::lock_guard lock(availability.mutex);
        CHECK(availability.event_count == 1);
        CHECK(availability.event_role ==
              AudioCaptureRole::DesktopLoopback);
        CHECK(!availability.event_available);
        CHECK(availability.event_frame == 2);
    }

    session.Abort();
}

void DesktopThreadCreationFailureDoesNotStartEitherInput()
{
    auto desktop_state = std::make_shared<SourceState>();
    auto microphone_state = std::make_shared<SourceState>();
    SingleSourceProvider desktop_provider(
        std::make_unique<PacketThenBlockingSource>(
            desktop_state,
            VRREC_STATUS_OK,
            0.25F,
            2));
    SingleSourceProvider microphone_provider(
        std::make_unique<PacketThenBlockingSource>(
            microphone_state,
            VRREC_STATUS_OK,
            0.5F,
            2));
    RecordingWaiter desktop_waiter;
    RecordingWaiter microphone_waiter;
    ScriptedThreadFactory thread_factory({VRREC_STATUS_OUT_OF_MEMORY});
    StereoAudioCaptureSession session(
        desktop_provider,
        desktop_waiter,
        microphone_provider,
        microphone_waiter,
        16,
        VRREC_AUDIO_ROUTING_MIXED,
        0.0,
        0.0,
        thread_factory);

    CHECK(session.Start(Config()) == VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(thread_factory.start_calls == 1);
    CHECK(desktop_provider.create_calls == 0);
    CHECK(microphone_provider.create_calls == 0);
    CHECK(desktop_state->start_calls == 0);
    CHECK(microphone_state->start_calls == 0);
    CHECK(session.Start(Config()) == VRREC_STATUS_INVALID_STATE);

    session.Abort();
    CHECK(desktop_waiter.abort_calls == 0);
    CHECK(microphone_waiter.abort_calls == 0);
    CHECK(desktop_state->abort_calls == 0);
    CHECK(microphone_state->abort_calls == 0);
}

void MicrophoneThreadCreationFailureRollsBackDesktop()
{
    auto desktop_state = std::make_shared<SourceState>();
    auto microphone_state = std::make_shared<SourceState>();
    SingleSourceProvider desktop_provider(
        std::make_unique<PacketThenBlockingSource>(
            desktop_state,
            VRREC_STATUS_OK,
            0.25F,
            2));
    SingleSourceProvider microphone_provider(
        std::make_unique<PacketThenBlockingSource>(
            microphone_state,
            VRREC_STATUS_OK,
            0.5F,
            2));
    RecordingWaiter desktop_waiter;
    RecordingWaiter microphone_waiter;
    ScriptedThreadFactory thread_factory(
        {VRREC_STATUS_OK, VRREC_STATUS_INTERNAL_ERROR});
    StereoAudioCaptureSession session(
        desktop_provider,
        desktop_waiter,
        microphone_provider,
        microphone_waiter,
        16,
        VRREC_AUDIO_ROUTING_MIXED,
        0.0,
        0.0,
        thread_factory);

    CHECK(session.Start(Config()) == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(thread_factory.start_calls == 2);
    CHECK(desktop_provider.create_calls == 1);
    CHECK(microphone_provider.create_calls == 0);
    CHECK(desktop_state->start_calls == 1);
    CHECK(microphone_state->start_calls == 0);
    CHECK(desktop_state->abort_calls == 1);
    CHECK(microphone_state->abort_calls == 0);
    CHECK(desktop_waiter.abort_calls == 1);
    CHECK(microphone_waiter.abort_calls == 0);
    CHECK(session.Start(Config()) == VRREC_STATUS_INVALID_STATE);

    session.Abort();
    CHECK(desktop_state->abort_calls == 1);
    CHECK(desktop_waiter.abort_calls == 1);
    CHECK(microphone_state->abort_calls == 0);
    CHECK(microphone_waiter.abort_calls == 0);
}

}

int main()
{
    StartsBothInputsAndMixesOneScheduledWindow();
    RollsBackDesktopWhenMicrophoneStartFails();
    RejectsInvalidRoutingBeforeStartingInputs();
    PropagatesDesktopLossWithItsExactFrame();
    DesktopThreadCreationFailureDoesNotStartEitherInput();
    MicrophoneThreadCreationFailureRollsBackDesktop();
    return 0;
}
