#include "audio_timeline_buffer.hpp"

#include <cmath>
#include <cstddef>
#include <cstdlib>
#include <iostream>
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

}

int main()
{
    ReassemblesPacketBoundariesAndZeroFillsTimelineGaps();
    return 0;
}
