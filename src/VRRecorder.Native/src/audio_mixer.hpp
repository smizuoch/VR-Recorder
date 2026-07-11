#ifndef VRRECORDER_NATIVE_AUDIO_MIXER_HPP
#define VRRECORDER_NATIVE_AUDIO_MIXER_HPP

#include <atomic>
#include <cstddef>
#include <cstdint>
#include <mutex>
#include <span>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

class StereoAudioMixer final {
public:
    static constexpr std::uint32_t SampleRate = 48'000;
    static constexpr std::size_t ChannelCount = 2;
    static constexpr std::size_t RampFrameCount = 480;

    StereoAudioMixer(
        vrrec_audio_routing_t initial_routing,
        double desktop_gain_db,
        double microphone_gain_db) noexcept;

    vrrec_status_t SetRouting(
        vrrec_audio_routing_t routing) noexcept;

    vrrec_status_t Mix(
        std::span<const float> desktop_interleaved,
        std::span<const float> microphone_interleaved,
        std::size_t scheduled_frame_count,
        std::span<float> output_interleaved) noexcept;

    std::uint64_t FramePosition() const noexcept;

private:
    struct RoutingGains final {
        double desktop;
        double microphone;
    };

    static bool IsRoutingDefined(
        vrrec_audio_routing_t routing) noexcept;
    static bool IsGainDefined(double gain_db) noexcept;
    static double DecibelsToLinear(double gain_db) noexcept;
    static RoutingGains RoutingTargets(
        vrrec_audio_routing_t routing,
        double desktop_gain,
        double microphone_gain) noexcept;
    static bool SamplesAreValid(std::span<const float> samples) noexcept;
    static float Limit(double sample) noexcept;

    void AdvanceRamp() noexcept;

    mutable std::mutex mutex_;
    std::atomic<std::uint64_t> frame_position_ {0};
    double desktop_base_gain_ = 0.0;
    double microphone_base_gain_ = 0.0;
    double desktop_current_gain_ = 0.0;
    double microphone_current_gain_ = 0.0;
    double desktop_target_gain_ = 0.0;
    double microphone_target_gain_ = 0.0;
    double desktop_step_ = 0.0;
    double microphone_step_ = 0.0;
    std::size_t ramp_frames_remaining_ = 0;
    vrrec_audio_routing_t routing_ = VRREC_AUDIO_ROUTING_MUTED;
    bool valid_ = false;
};

}

#endif
