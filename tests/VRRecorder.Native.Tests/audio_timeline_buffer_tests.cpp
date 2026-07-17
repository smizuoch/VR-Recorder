#include "audio_timeline_buffer.hpp"

#include <cmath>
#include <cstddef>
#include <cstdlib>
#include <iostream>
#include <limits>
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

void ReassemblesPacketBoundariesAndZeroFillsTimelineGaps()
{
    vrrecorder::native::StereoAudioTimelineBuffer buffer(8);
    const std::vector<float> first_packet {
        0.10F,
        0.11F,
        0.20F,
        0.21F,
    };
    const std::vector<float> second_packet {0.40F, 0.41F};

    CHECK(buffer.Write(100, first_packet) == VRREC_STATUS_OK);
    CHECK(buffer.Write(103, second_packet) == VRREC_STATUS_OK);

    std::vector<float> output(8, -1.0F);
    CHECK(buffer.Read(100, 4, output) == VRREC_STATUS_OK);
    const std::vector<float> expected {
        0.10F,
        0.11F,
        0.20F,
        0.21F,
        0.0F,
        0.0F,
        0.40F,
        0.41F,
    };
    CHECK(output.size() == expected.size());
    for (std::size_t index = 0; index < output.size(); ++index) {
        CHECK(NearlyEqual(output[index], expected[index]));
    }
}

void RejectsEveryInvalidWriteAndReadBoundary()
{
    using vrrecorder::native::StereoAudioTimelineBuffer;
    const std::vector<float> one_frame {0.25F, -0.25F};
    StereoAudioTimelineBuffer zero_capacity(0);
    CHECK(zero_capacity.Write(0, one_frame) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    std::vector<float> output(2);
    CHECK(zero_capacity.Read(0, 1, output) ==
          VRREC_STATUS_INVALID_ARGUMENT);

    StereoAudioTimelineBuffer overflow_capacity(
        std::numeric_limits<std::size_t>::max());
    CHECK(overflow_capacity.Write(0, one_frame) ==
          VRREC_STATUS_INVALID_ARGUMENT);

    StereoAudioTimelineBuffer buffer(4);
    CHECK(buffer.Write(0, {}) == VRREC_STATUS_INVALID_ARGUMENT);
    const std::vector<float> odd_samples {0.25F};
    CHECK(buffer.Write(0, odd_samples) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(buffer.Write(
              std::numeric_limits<std::uint64_t>::max(),
              std::vector<float> {0.0F, 0.0F, 0.0F, 0.0F}) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    for (const auto invalid_sample : {
             std::numeric_limits<float>::quiet_NaN(),
             std::numeric_limits<float>::infinity(),
         }) {
        CHECK(buffer.Write(0, std::vector<float> {invalid_sample, 0.0F}) ==
              VRREC_STATUS_INVALID_ARGUMENT);
    }

    CHECK(buffer.Read(0, 0, {}) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(buffer.Read(0, 1, std::span<float> {}) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(buffer.Read(
              0,
              std::numeric_limits<std::size_t>::max(),
              std::span<float> {}) == VRREC_STATUS_INVALID_ARGUMENT);
    std::vector<float> overflow_output(4);
    CHECK(buffer.Read(
              std::numeric_limits<std::uint64_t>::max(),
              2,
              overflow_output) == VRREC_STATUS_INVALID_ARGUMENT);
}

void ReportsAndResetsMissingFrameEvidence()
{
    vrrecorder::native::StereoAudioTimelineBuffer buffer(2);
    const std::vector<float> first {0.1F, 0.2F};
    CHECK(buffer.Write(10, first) == VRREC_STATUS_OK);
    std::vector<float> output(4, -1.0F);
    auto had_missing_frames = false;
    CHECK(buffer.Read(10, 2, output, had_missing_frames) == VRREC_STATUS_OK);
    CHECK(had_missing_frames);
    CHECK(NearlyEqual(output[0], 0.1F));
    CHECK(NearlyEqual(output[1], 0.2F));
    CHECK(NearlyEqual(output[2], 0.0F));
    CHECK(NearlyEqual(output[3], 0.0F));

    const std::vector<float> second {0.3F, 0.4F};
    CHECK(buffer.Write(11, second) == VRREC_STATUS_OK);
    had_missing_frames = true;
    CHECK(buffer.Read(10, 2, output, had_missing_frames) == VRREC_STATUS_OK);
    CHECK(!had_missing_frames);
}

}

int main()
{
    ReassemblesPacketBoundariesAndZeroFillsTimelineGaps();
    RejectsEveryInvalidWriteAndReadBoundary();
    ReportsAndResetsMissingFrameEvidence();
    return 0;
}
