#include "audio_mix_coordinator.hpp"

#include <limits>

namespace vrrecorder::native {

StereoAudioMixCoordinator::StereoAudioMixCoordinator(
    StereoCaptureTimeline &desktop,
    StereoCaptureTimeline &microphone,
    StereoAudioMixer &mixer,
    AudioCaptureAvailabilitySink *health_sink) noexcept
    : desktop_(desktop),
      microphone_(microphone),
      mixer_(mixer),
      health_sink_(health_sink)
{
}

StereoAudioMixResult StereoAudioMixCoordinator::MixNext(
    std::size_t frame_count_48k,
    std::span<float> output_interleaved,
    StereoAudioMixRead &read) noexcept
{
    constexpr auto channel_count = StereoAudioMixer::ChannelCount;
    if (aborted_.load()) {
        return StereoAudioMixResult::Aborted;
    }

    if (frame_count_48k == 0 ||
        frame_count_48k >
            std::numeric_limits<std::size_t>::max() / channel_count ||
        output_interleaved.size() != frame_count_48k * channel_count) {
        return StereoAudioMixResult::InvalidArgument;
    }

    const auto expected_start_frame = mixer_.FramePosition();
    if (desktop_.FramePosition() != expected_start_frame ||
        microphone_.FramePosition() != expected_start_frame) {
        return StereoAudioMixResult::InvalidState;
    }

    const auto sample_count = frame_count_48k * channel_count;
    try {
        desktop_samples_.assign(sample_count, 0.0F);
        microphone_samples_.assign(sample_count, 0.0F);
    } catch (...) {
        return StereoAudioMixResult::Failed;
    }

    AudioTimelineRead desktop_read {};
    const auto desktop_result = desktop_.WaitRead(
        frame_count_48k,
        desktop_samples_,
        desktop_read);
    if (desktop_result != AudioTimelineResult::Ready) {
        return MapTimelineResult(desktop_result);
    }

    AudioTimelineRead microphone_read {};
    const auto microphone_result = microphone_.WaitRead(
        frame_count_48k,
        microphone_samples_,
        microphone_read);
    if (microphone_result != AudioTimelineResult::Ready) {
        return MapTimelineResult(microphone_result);
    }

    if (aborted_.load()) {
        return StereoAudioMixResult::Aborted;
    }

    if (desktop_read.start_frame_48k != expected_start_frame ||
        microphone_read.start_frame_48k != expected_start_frame) {
        return StereoAudioMixResult::InvalidState;
    }

    if (mixer_.Mix(
            desktop_samples_,
            microphone_samples_,
            frame_count_48k,
            output_interleaved) != VRREC_STATUS_OK) {
        return StereoAudioMixResult::Failed;
    }

    if (health_sink_ != nullptr) {
        if (desktop_read.underrun) {
            health_sink_->BufferHealthChanged(
                AudioCaptureRole::DesktopLoopback,
                AudioBufferHealth::Underrun,
                expected_start_frame);
        }

        if (microphone_read.underrun) {
            health_sink_->BufferHealthChanged(
                AudioCaptureRole::Microphone,
                AudioBufferHealth::Underrun,
                expected_start_frame);
        }
    }

    read = StereoAudioMixRead {
        expected_start_frame,
        frame_count_48k,
        desktop_read.input_available,
        microphone_read.input_available,
        desktop_read.underrun,
        microphone_read.underrun,
    };
    return StereoAudioMixResult::Mixed;
}

void StereoAudioMixCoordinator::Abort() noexcept
{
    if (aborted_.exchange(true)) {
        return;
    }

    desktop_.Abort();
    microphone_.Abort();
}

StereoAudioMixResult StereoAudioMixCoordinator::MapTimelineResult(
    AudioTimelineResult result) noexcept
{
    return result == AudioTimelineResult::Aborted
        ? StereoAudioMixResult::Aborted
        : StereoAudioMixResult::Failed;
}

}
