#include "audio_mixer.hpp"

#include <algorithm>
#include <cmath>
#include <limits>

namespace vrrecorder::native {

StereoAudioMixer::StereoAudioMixer(
    vrrec_audio_routing_t initial_routing,
    double desktop_gain_db,
    double microphone_gain_db) noexcept
    : desktop_base_gain_(DecibelsToLinear(desktop_gain_db)),
      microphone_base_gain_(DecibelsToLinear(microphone_gain_db)),
      routing_(initial_routing),
      valid_(IsRoutingDefined(initial_routing) &&
             IsGainDefined(desktop_gain_db) &&
             IsGainDefined(microphone_gain_db))
{
    if (!valid_) {
        return;
    }

    const auto targets = RoutingTargets(
        routing_,
        desktop_base_gain_,
        microphone_base_gain_);
    desktop_current_gain_ = targets.desktop;
    microphone_current_gain_ = targets.microphone;
    desktop_target_gain_ = targets.desktop;
    microphone_target_gain_ = targets.microphone;
}

vrrec_status_t StereoAudioMixer::SetRouting(
    vrrec_audio_routing_t routing) noexcept
{
    if (!valid_ || !IsRoutingDefined(routing)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    const std::lock_guard lock(mutex_);
    if (routing == routing_) {
        return VRREC_STATUS_OK;
    }

    const auto targets = RoutingTargets(
        routing,
        desktop_base_gain_,
        microphone_base_gain_);
    routing_ = routing;
    desktop_target_gain_ = targets.desktop;
    microphone_target_gain_ = targets.microphone;
    if (desktop_current_gain_ == desktop_target_gain_ &&
        microphone_current_gain_ == microphone_target_gain_) {
        desktop_step_ = 0.0;
        microphone_step_ = 0.0;
        ramp_frames_remaining_ = 0;
        return VRREC_STATUS_OK;
    }

    desktop_step_ =
        (desktop_target_gain_ - desktop_current_gain_) /
        static_cast<double>(RampFrameCount);
    microphone_step_ =
        (microphone_target_gain_ - microphone_current_gain_) /
        static_cast<double>(RampFrameCount);
    ramp_frames_remaining_ = RampFrameCount;
    return VRREC_STATUS_OK;
}

vrrec_status_t StereoAudioMixer::Mix(
    std::span<const float> desktop_interleaved,
    std::span<const float> microphone_interleaved,
    std::size_t scheduled_frame_count,
    std::span<float> output_interleaved) noexcept
{
    if (!valid_ || scheduled_frame_count == 0 ||
        scheduled_frame_count >
            std::numeric_limits<std::size_t>::max() / ChannelCount) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    const auto scheduled_sample_count =
        scheduled_frame_count * ChannelCount;
    if (output_interleaved.size() != scheduled_sample_count ||
        desktop_interleaved.size() > scheduled_sample_count ||
        microphone_interleaved.size() > scheduled_sample_count ||
        desktop_interleaved.size() % ChannelCount != 0 ||
        microphone_interleaved.size() % ChannelCount != 0 ||
        !SamplesAreValid(desktop_interleaved) ||
        !SamplesAreValid(microphone_interleaved)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    const std::lock_guard lock(mutex_);
    const auto previous_position = frame_position_.load();
    if (scheduled_frame_count >
        std::numeric_limits<std::uint64_t>::max() - previous_position) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    for (std::size_t frame = 0; frame < scheduled_frame_count; ++frame) {
        for (std::size_t channel = 0; channel < ChannelCount; ++channel) {
            const auto index = frame * ChannelCount + channel;
            const auto desktop = index < desktop_interleaved.size()
                ? desktop_interleaved[index]
                : 0.0F;
            const auto microphone = index < microphone_interleaved.size()
                ? microphone_interleaved[index]
                : 0.0F;
            output_interleaved[index] = Limit(
                static_cast<double>(desktop) * desktop_current_gain_ +
                static_cast<double>(microphone) *
                    microphone_current_gain_);
        }

        AdvanceRamp();
    }

    frame_position_.store(
        previous_position + scheduled_frame_count);
    return VRREC_STATUS_OK;
}

std::uint64_t StereoAudioMixer::FramePosition() const noexcept
{
    return frame_position_.load();
}

bool StereoAudioMixer::IsRoutingDefined(
    vrrec_audio_routing_t routing) noexcept
{
    return routing == VRREC_AUDIO_ROUTING_MIXED ||
           routing == VRREC_AUDIO_ROUTING_DESKTOP_ONLY ||
           routing == VRREC_AUDIO_ROUTING_MIC_ONLY ||
           routing == VRREC_AUDIO_ROUTING_MUTED;
}

bool StereoAudioMixer::IsGainDefined(double gain_db) noexcept
{
    return std::isfinite(gain_db) && gain_db >= -96.0 && gain_db <= 24.0;
}

double StereoAudioMixer::DecibelsToLinear(double gain_db) noexcept
{
    return std::pow(10.0, gain_db / 20.0);
}

StereoAudioMixer::RoutingGains StereoAudioMixer::RoutingTargets(
    vrrec_audio_routing_t routing,
    double desktop_gain,
    double microphone_gain) noexcept
{
    switch (routing) {
    case VRREC_AUDIO_ROUTING_MIXED:
        return {desktop_gain, microphone_gain};
    case VRREC_AUDIO_ROUTING_DESKTOP_ONLY:
        return {desktop_gain, 0.0};
    case VRREC_AUDIO_ROUTING_MIC_ONLY:
        return {0.0, microphone_gain};
    case VRREC_AUDIO_ROUTING_MUTED:
    default:
        return {0.0, 0.0};
    }
}

bool StereoAudioMixer::SamplesAreValid(
    std::span<const float> samples) noexcept
{
    return std::all_of(
        samples.begin(),
        samples.end(),
        [](float sample) {
            return std::isfinite(sample);
        });
}

float StereoAudioMixer::Limit(double sample) noexcept
{
    return static_cast<float>(std::clamp(sample, -1.0, 1.0));
}

void StereoAudioMixer::AdvanceRamp() noexcept
{
    if (ramp_frames_remaining_ == 0) {
        return;
    }

    desktop_current_gain_ += desktop_step_;
    microphone_current_gain_ += microphone_step_;
    --ramp_frames_remaining_;
    if (ramp_frames_remaining_ == 0) {
        desktop_current_gain_ = desktop_target_gain_;
        microphone_current_gain_ = microphone_target_gain_;
    }
}

}
