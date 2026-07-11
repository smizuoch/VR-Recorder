#include "audio_mixer.hpp"

#include <cassert>
#include <cmath>
#include <cstddef>
#include <limits>
#include <span>
#include <vector>

namespace {

constexpr float kTolerance = 0.00001F;

bool NearlyEqual(float actual, float expected)
{
    return std::fabs(actual - expected) <= kTolerance;
}

std::vector<float> ConstantStereo(
    std::size_t frame_count,
    float sample)
{
    return std::vector<float>(frame_count * 2U, sample);
}

void RequiresAllSteadyRoutingModes()
{
    const auto desktop = ConstantStereo(2, 0.25F);
    const auto microphone = ConstantStereo(2, 0.5F);
    struct Case final {
        vrrec_audio_routing_t routing;
        float expected;
    };
    const Case cases[] {
        {VRREC_AUDIO_ROUTING_MIXED, 0.75F},
        {VRREC_AUDIO_ROUTING_DESKTOP_ONLY, 0.25F},
        {VRREC_AUDIO_ROUTING_MIC_ONLY, 0.5F},
        {VRREC_AUDIO_ROUTING_MUTED, 0.0F},
    };

    for (const auto &test_case : cases) {
        vrrecorder::native::StereoAudioMixer mixer(
            test_case.routing,
            0.0,
            0.0);
        std::vector<float> output(4);

        const auto status = mixer.Mix(
            desktop,
            microphone,
            2,
            output);

        assert(status == VRREC_STATUS_OK);
        for (const auto sample : output) {
            assert(NearlyEqual(sample, test_case.expected));
        }
        assert(mixer.FramePosition() == 2);
    }
}

void RequiresTenMillisecondMicrophoneRamp()
{
    constexpr std::size_t scheduled_frames = 481;
    const auto desktop = ConstantStereo(scheduled_frames, 0.25F);
    const auto microphone = ConstantStereo(scheduled_frames, 0.5F);
    std::vector<float> output(scheduled_frames * 2U);
    vrrecorder::native::StereoAudioMixer mixer(
        VRREC_AUDIO_ROUTING_MIXED,
        0.0,
        0.0);

    assert(mixer.SetRouting(VRREC_AUDIO_ROUTING_DESKTOP_ONLY) ==
           VRREC_STATUS_OK);
    assert(mixer.Mix(
               desktop,
               microphone,
               scheduled_frames,
               output) == VRREC_STATUS_OK);

    assert(NearlyEqual(output[0], 0.75F));
    assert(NearlyEqual(output[240U * 2U], 0.5F));
    assert(NearlyEqual(output[480U * 2U], 0.25F));
    assert(NearlyEqual(output[480U * 2U + 1U], 0.25F));
    assert(mixer.FramePosition() == scheduled_frames);
}

void RequiresUnderrunZeroFillWithoutChangingTimeline()
{
    const auto desktop = ConstantStereo(2, 0.25F);
    const std::vector<float> microphone;
    std::vector<float> output(8, -1.0F);
    vrrecorder::native::StereoAudioMixer mixer(
        VRREC_AUDIO_ROUTING_MIXED,
        0.0,
        0.0);

    assert(mixer.Mix(desktop, microphone, 4, output) ==
           VRREC_STATUS_OK);

    assert(NearlyEqual(output[0], 0.25F));
    assert(NearlyEqual(output[3], 0.25F));
    for (std::size_t index = 4; index < output.size(); ++index) {
        assert(NearlyEqual(output[index], 0.0F));
    }
    assert(mixer.FramePosition() == 4);
}

void RequiresConfiguredInputGainAndStrictBuffers()
{
    const auto desktop = ConstantStereo(1, 1.0F);
    const auto microphone = ConstantStereo(1, 1.0F);
    std::vector<float> output(2);
    vrrecorder::native::StereoAudioMixer mixer(
        VRREC_AUDIO_ROUTING_MIXED,
        -6.0,
        -6.0);

    assert(mixer.Mix(desktop, microphone, 1, output) ==
           VRREC_STATUS_OK);
    const auto expected = static_cast<float>(
        2.0 * std::pow(10.0, -6.0 / 20.0));
    assert(NearlyEqual(output[0], expected));

    std::vector<float> short_output(1);
    assert(mixer.Mix(desktop, microphone, 1, short_output) ==
           VRREC_STATUS_INVALID_ARGUMENT);
    const std::vector<float> odd_input {0.25F};
    assert(mixer.Mix(odd_input, microphone, 1, output) ==
           VRREC_STATUS_INVALID_ARGUMENT);
    const std::vector<float> non_finite {
        std::numeric_limits<float>::infinity(),
        0.0F,
    };
    assert(mixer.Mix(non_finite, microphone, 1, output) ==
           VRREC_STATUS_INVALID_ARGUMENT);
    assert(mixer.SetRouting(999U) == VRREC_STATUS_INVALID_ARGUMENT);
}

}

int main()
{
    RequiresAllSteadyRoutingModes();
    RequiresTenMillisecondMicrophoneRamp();
    RequiresUnderrunZeroFillWithoutChangingTimeline();
    RequiresConfiguredInputGainAndStrictBuffers();
    return 0;
}
