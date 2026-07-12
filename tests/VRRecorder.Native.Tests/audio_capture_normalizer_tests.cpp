#include "audio_capture_normalizer.hpp"

#include <cmath>
#include <cstddef>
#include <cstdlib>
#include <iostream>
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

void ConvertsOne44100HzMonoPacketToAnExact480FrameStereoWindow()
{
    constexpr std::int64_t session_start_qpc_100ns = 1'000'000;
    std::vector<float> mono(441, 0.25F);
    const vrrecorder::native::CapturePcmFormat format {
        44'100,
        1,
        vrrecorder::native::CaptureSampleEncoding::IeeeFloat,
        32,
        32,
        4,
        0x0000'0004,
    };
    const vrrecorder::native::RawCapturePacket packet {
        0,
        session_start_qpc_100ns,
        441,
        std::as_bytes(std::span<const float>(mono)),
        false,
        false,
        false,
    };
    vrrecorder::native::StereoCaptureNormalizer48k normalizer(
        session_start_qpc_100ns);
    vrrecorder::native::CapturedStereoPacket48k normalized {};

    CHECK(normalizer.Normalize(format, packet, normalized) ==
          vrrecorder::native::CaptureNormalizationResult::Ready);
    CHECK(normalized.start_frame_48k == 0);
    CHECK(normalized.device_position == 0);
    CHECK(normalized.qpc_100ns == session_start_qpc_100ns);
    CHECK(normalized.frame_count_48k == 480);
    CHECK(normalized.interleaved_samples.size() == 960);
    CHECK(!normalized.silent);
    CHECK(!normalized.discontinuity);
    for (std::size_t frame = 0; frame < 480; ++frame) {
        CHECK(NearlyEqual(
            normalized.interleaved_samples[frame * 2U],
            0.25F));
        CHECK(NearlyEqual(
            normalized.interleaved_samples[frame * 2U + 1U],
            0.25F));
    }
}

}

int main()
{
    ConvertsOne44100HzMonoPacketToAnExact480FrameStereoWindow();
    return 0;
}
