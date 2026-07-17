#include "audio_mixer.hpp"

#include <cmath>
#include <cstddef>
#include <cstdlib>
#include <iostream>
#include <limits>
#include <span>
#include <vector>

namespace {

constexpr float kTolerance = 0.00001F;

#define CHECK(condition)                                                        \
    do {                                                                        \
        if (!(condition)) {                                                     \
            std::cerr << "check failed at " << __FILE__ << ':' << __LINE__      \
                      << ": " #condition << '\n';                              \
            std::abort();                                                       \
        }                                                                       \
    } while (false)

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

        CHECK(status == VRREC_STATUS_OK);
        for (const auto sample : output) {
            CHECK(NearlyEqual(sample, test_case.expected));
        }
        CHECK(mixer.FramePosition() == 2);
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

    CHECK(mixer.SetRouting(VRREC_AUDIO_ROUTING_DESKTOP_ONLY) ==
          VRREC_STATUS_OK);
    CHECK(mixer.Mix(
              desktop,
              microphone,
              scheduled_frames,
              output) == VRREC_STATUS_OK);

    CHECK(NearlyEqual(output[0], 0.75F));
    CHECK(NearlyEqual(output[240U * 2U], 0.5F));
    CHECK(NearlyEqual(output[480U * 2U], 0.25F));
    CHECK(NearlyEqual(output[480U * 2U + 1U], 0.25F));
    CHECK(mixer.FramePosition() == scheduled_frames);
}

void RequiresRampReversalFromCurrentGain()
{
    const auto desktop = ConstantStereo(721, 0.25F);
    const auto microphone = ConstantStereo(721, 0.5F);
    std::vector<float> first_output(240U * 2U);
    std::vector<float> reversed_output(481U * 2U);
    vrrecorder::native::StereoAudioMixer mixer(
        VRREC_AUDIO_ROUTING_MIXED,
        0.0,
        0.0);

    CHECK(mixer.SetRouting(VRREC_AUDIO_ROUTING_DESKTOP_ONLY) ==
          VRREC_STATUS_OK);
    CHECK(mixer.Mix(
              std::span<const float>(desktop).first(first_output.size()),
              std::span<const float>(microphone).first(first_output.size()),
              240,
              first_output) == VRREC_STATUS_OK);
    CHECK(mixer.SetRouting(VRREC_AUDIO_ROUTING_MIXED) ==
          VRREC_STATUS_OK);
    CHECK(mixer.Mix(
              std::span<const float>(desktop).subspan(first_output.size()),
              std::span<const float>(microphone).subspan(first_output.size()),
              481,
              reversed_output) == VRREC_STATUS_OK);

    CHECK(NearlyEqual(reversed_output[0], 0.5F));
    CHECK(NearlyEqual(reversed_output[480U * 2U], 0.75F));
}

void CancelsAnUnstartedRampAndChangesEitherGainIndependently()
{
    vrrecorder::native::StereoAudioMixer mixer(
        VRREC_AUDIO_ROUTING_MIXED,
        0.0,
        0.0);

    CHECK(mixer.SetRouting(VRREC_AUDIO_ROUTING_DESKTOP_ONLY) ==
          VRREC_STATUS_OK);
    CHECK(mixer.SetRouting(VRREC_AUDIO_ROUTING_MIXED) ==
          VRREC_STATUS_OK);
    CHECK(mixer.SetRouting(VRREC_AUDIO_ROUTING_MIC_ONLY) ==
          VRREC_STATUS_OK);
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

    CHECK(mixer.Mix(desktop, microphone, 4, output) ==
          VRREC_STATUS_OK);

    CHECK(NearlyEqual(output[0], 0.25F));
    CHECK(NearlyEqual(output[3], 0.25F));
    for (std::size_t index = 4; index < output.size(); ++index) {
        CHECK(NearlyEqual(output[index], 0.0F));
    }
    CHECK(mixer.FramePosition() == 4);
}

void RequiresConfiguredInputGainAndStrictBuffers()
{
    const auto desktop = ConstantStereo(1, 0.25F);
    const auto microphone = ConstantStereo(1, 0.25F);
    std::vector<float> output(2);
    vrrecorder::native::StereoAudioMixer mixer(
        VRREC_AUDIO_ROUTING_MIXED,
        -6.0,
        -6.0);

    CHECK(mixer.Mix(desktop, microphone, 1, output) ==
          VRREC_STATUS_OK);
    const auto expected = static_cast<float>(
        0.5 * std::pow(10.0, -6.0 / 20.0));
    CHECK(NearlyEqual(output[0], expected));

    const auto frame_position = mixer.FramePosition();
    std::vector<float> short_output(1);
    CHECK(mixer.Mix(desktop, microphone, 1, short_output) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    const std::vector<float> odd_input {0.25F};
    CHECK(mixer.Mix(odd_input, microphone, 1, output) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    const std::vector<float> non_finite {
        std::numeric_limits<float>::infinity(),
        0.0F,
    };
    CHECK(mixer.Mix(non_finite, microphone, 1, output) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(mixer.SetRouting(999U) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(mixer.FramePosition() == frame_position);
}

void RequiresFinitePeakLimitedOutput()
{
    const auto desktop = ConstantStereo(2, 1.0F);
    const auto microphone = ConstantStereo(2, 1.0F);
    std::vector<float> output(4);
    vrrecorder::native::StereoAudioMixer mixer(
        VRREC_AUDIO_ROUTING_MIXED,
        24.0,
        24.0);

    CHECK(mixer.Mix(desktop, microphone, 2, output) ==
          VRREC_STATUS_OK);
    for (const auto sample : output) {
        CHECK(std::isfinite(sample));
        CHECK(std::fabs(sample) <= 1.0F);
    }
}

void RejectsEveryPublicConfigurationAndBufferBoundary()
{
    const auto stereo = ConstantStereo(1, 0.25F);
    const auto too_many_samples = ConstantStereo(2, 0.25F);
    const std::vector<float> odd_samples {0.25F};
    const std::vector<float> invalid_samples {
        std::numeric_limits<float>::quiet_NaN(),
        0.0F,
    };
    std::vector<float> output(2);

    const struct InvalidConfiguration final {
        vrrec_audio_routing_t routing;
        double desktop_gain_db;
        double microphone_gain_db;
    } configurations[] {
        {999U, 0.0, 0.0},
        {VRREC_AUDIO_ROUTING_MIXED,
         std::numeric_limits<double>::quiet_NaN(), 0.0},
        {VRREC_AUDIO_ROUTING_MIXED, -96.0001, 0.0},
        {VRREC_AUDIO_ROUTING_MIXED, 0.0, 24.0001},
    };
    for (const auto &configuration : configurations) {
        vrrecorder::native::StereoAudioMixer mixer(
            configuration.routing,
            configuration.desktop_gain_db,
            configuration.microphone_gain_db);
        CHECK(mixer.SetRouting(VRREC_AUDIO_ROUTING_MUTED) ==
              VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(mixer.Mix(stereo, stereo, 1, output) ==
              VRREC_STATUS_INVALID_ARGUMENT);
    }

    vrrecorder::native::StereoAudioMixer mixer(
        VRREC_AUDIO_ROUTING_MIXED,
        0.0,
        0.0);
    CHECK(mixer.SetRouting(VRREC_AUDIO_ROUTING_MIXED) ==
          VRREC_STATUS_OK);
    CHECK(mixer.Mix(stereo, stereo, 0, {}) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(mixer.Mix(
              {},
              {},
              std::numeric_limits<std::size_t>::max(),
              {}) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(mixer.Mix(too_many_samples, stereo, 1, output) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(mixer.Mix(stereo, too_many_samples, 1, output) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(mixer.Mix(stereo, odd_samples, 1, output) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(mixer.Mix(stereo, invalid_samples, 1, output) ==
          VRREC_STATUS_INVALID_ARGUMENT);
}

}

int main()
{
    RequiresAllSteadyRoutingModes();
    RequiresTenMillisecondMicrophoneRamp();
    RequiresRampReversalFromCurrentGain();
    CancelsAnUnstartedRampAndChangesEitherGainIndependently();
    RequiresUnderrunZeroFillWithoutChangingTimeline();
    RequiresConfiguredInputGainAndStrictBuffers();
    RequiresFinitePeakLimitedOutput();
    RejectsEveryPublicConfigurationAndBufferBoundary();
    return 0;
}
