#include "audio_mix_coordinator.hpp"

#include <chrono>
#include <cstddef>
#include <cstdlib>
#include <future>
#include <iostream>
#include <span>
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

class RecordingAudioHealthSink final : public AudioCaptureAvailabilitySink {
public:
    void AvailabilityChanged(
        AudioCaptureRole,
        bool,
        std::uint64_t) noexcept override
    {
    }

    void BufferHealthChanged(
        AudioCaptureRole role,
        AudioBufferHealth health,
        std::uint64_t frame_position) noexcept override
    {
        event_role = role;
        event_health = health;
        event_frame = frame_position;
        ++event_count;
    }

    AudioCaptureRole event_role = AudioCaptureRole::DesktopLoopback;
    AudioBufferHealth event_health = AudioBufferHealth::Overrun;
    std::uint64_t event_frame = 0;
    std::size_t event_count = 0;
};

std::vector<float> ConstantStereo(
    std::size_t frame_count,
    float sample)
{
    return std::vector<float>(frame_count * 2U, sample);
}

void Push(
    StereoCaptureTimeline &timeline,
    std::uint64_t start_frame,
    std::uint64_t device_position,
    std::int64_t qpc_100ns,
    std::span<const float> samples)
{
    CHECK(timeline.Push({
        start_frame,
        {device_position, qpc_100ns, 10'000'000},
        samples,
        false,
    }) == AudioTimelineResult::Ready);
}

void MixesAlignedDesktopAndMicrophoneWindows()
{
    StereoCaptureTimeline desktop(16);
    StereoCaptureTimeline microphone(16);
    StereoAudioMixer mixer(VRREC_AUDIO_ROUTING_MIXED, 0.0, 0.0);
    StereoAudioMixCoordinator coordinator(desktop, microphone, mixer);
    const auto desktop_samples = ConstantStereo(2, 0.25F);
    const auto microphone_samples = ConstantStereo(2, 0.5F);
    Push(desktop, 0, 100, 1'000'000, desktop_samples);
    Push(microphone, 0, 200, 1'000'000, microphone_samples);

    std::vector<float> output(4, -1.0F);
    StereoAudioMixRead read {};
    CHECK(coordinator.MixNext(2, output, read) ==
          StereoAudioMixResult::Mixed);
    CHECK(read.start_frame_48k == 0);
    CHECK(read.frame_count_48k == 2);
    CHECK(read.desktop_available);
    CHECK(read.microphone_available);
    CHECK(!read.desktop_underrun);
    CHECK(!read.microphone_underrun);
    for (const auto sample : output) {
        CHECK(sample == 0.75F);
    }
}

void SilencesOnlyTheUnavailableInputAtTheScheduledWindow()
{
    StereoCaptureTimeline desktop(16);
    StereoCaptureTimeline microphone(16);
    StereoAudioMixer mixer(VRREC_AUDIO_ROUTING_MIXED, 0.0, 0.0);
    RecordingAudioHealthSink events;
    StereoAudioMixCoordinator coordinator(
        desktop,
        microphone,
        mixer,
        &events);
    const auto desktop_samples = ConstantStereo(4, 0.25F);
    const auto microphone_samples = ConstantStereo(2, 0.5F);
    Push(desktop, 0, 100, 1'000'000, desktop_samples);
    Push(microphone, 0, 200, 1'000'000, microphone_samples);

    std::vector<float> output(4, -1.0F);
    StereoAudioMixRead read {};
    CHECK(coordinator.MixNext(2, output, read) ==
          StereoAudioMixResult::Mixed);
    CHECK(microphone.SetAvailable(false, 2) == AudioTimelineResult::Ready);
    CHECK(coordinator.MixNext(2, output, read) ==
          StereoAudioMixResult::Mixed);

    CHECK(read.start_frame_48k == 2);
    CHECK(read.frame_count_48k == 2);
    CHECK(read.desktop_available);
    CHECK(!read.microphone_available);
    CHECK(!read.desktop_underrun);
    CHECK(read.microphone_underrun);
    CHECK(events.event_count == 1);
    CHECK(events.event_role == AudioCaptureRole::Microphone);
    CHECK(events.event_health == AudioBufferHealth::Underrun);
    CHECK(events.event_frame == 2);
    for (const auto sample : output) {
        CHECK(sample == 0.25F);
    }
    CHECK(mixer.FramePosition() == 4);
}

void RejectsSkewWithoutConsumingAnotherTimeline()
{
    StereoCaptureTimeline desktop(8);
    StereoCaptureTimeline microphone(8);
    StereoAudioMixer mixer(VRREC_AUDIO_ROUTING_MIXED, 0.0, 0.0);
    StereoAudioMixCoordinator coordinator(desktop, microphone, mixer);
    const auto samples = ConstantStereo(1, 0.25F);
    Push(desktop, 0, 100, 1'000'000, samples);
    std::vector<float> consumed(2);
    AudioTimelineRead timeline_read {};
    CHECK(desktop.WaitRead(1, consumed, timeline_read) ==
          AudioTimelineResult::Ready);

    std::vector<float> output(2, -1.0F);
    StereoAudioMixRead read {};
    CHECK(coordinator.MixNext(1, output, read) ==
          StereoAudioMixResult::InvalidState);
    CHECK(desktop.FramePosition() == 1);
    CHECK(microphone.FramePosition() == 0);
    CHECK(mixer.FramePosition() == 0);
    CHECK(output[0] == -1.0F);
    CHECK(output[1] == -1.0F);
}

void AbortReleasesABlockedMixAndIsIdempotent()
{
    using namespace std::chrono_literals;

    StereoCaptureTimeline desktop(8);
    StereoCaptureTimeline microphone(8);
    StereoAudioMixer mixer(VRREC_AUDIO_ROUTING_MIXED, 0.0, 0.0);
    StereoAudioMixCoordinator coordinator(desktop, microphone, mixer);
    std::vector<float> output(4, -1.0F);
    StereoAudioMixRead read {99, 7, true, true, true, true};
    auto mixing = std::async(std::launch::async, [&] {
        return coordinator.MixNext(2, output, read);
    });
    CHECK(mixing.wait_for(20ms) == std::future_status::timeout);

    coordinator.Abort();
    coordinator.Abort();

    CHECK(mixing.wait_for(1s) == std::future_status::ready);
    CHECK(mixing.get() == StereoAudioMixResult::Aborted);
    CHECK(read.start_frame_48k == 0);
    CHECK(read.frame_count_48k == 0);
    CHECK(!read.desktop_available);
    CHECK(!read.microphone_available);
    CHECK(!read.desktop_underrun);
    CHECK(!read.microphone_underrun);
    CHECK(coordinator.MixNext(2, output, read) ==
          StereoAudioMixResult::Aborted);
}

}

int main()
{
    MixesAlignedDesktopAndMicrophoneWindows();
    SilencesOnlyTheUnavailableInputAtTheScheduledWindow();
    RejectsSkewWithoutConsumingAnotherTimeline();
    AbortReleasesABlockedMixAndIsIdempotent();
    return 0;
}
