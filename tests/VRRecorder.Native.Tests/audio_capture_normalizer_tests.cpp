#include "audio_capture_normalizer.hpp"

#include <cmath>
#include <cstddef>
#include <cstdint>
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

void ConvertsPcm16EndpointsToFiniteStereoFloat()
{
    std::vector<std::int16_t> mono {
        std::numeric_limits<std::int16_t>::min(),
        0,
        std::numeric_limits<std::int16_t>::max(),
    };
    const vrrecorder::native::CapturePcmFormat format {
        48'000,
        1,
        vrrecorder::native::CaptureSampleEncoding::PcmSignedInteger,
        16,
        16,
        2,
        0x0000'0004,
    };
    const vrrecorder::native::RawCapturePacket packet {
        10,
        2'000'000,
        mono.size(),
        std::as_bytes(std::span<const std::int16_t>(mono)),
        false,
        false,
        false,
    };
    vrrecorder::native::StereoCaptureNormalizer48k normalizer(2'000'000);
    vrrecorder::native::CapturedStereoPacket48k normalized {};

    CHECK(normalizer.Normalize(format, packet, normalized) ==
          vrrecorder::native::CaptureNormalizationResult::Ready);
    CHECK(normalized.frame_count_48k == 3);
    CHECK(normalized.interleaved_samples.size() == 6);
    CHECK(NearlyEqual(normalized.interleaved_samples[0], -1.0F));
    CHECK(NearlyEqual(normalized.interleaved_samples[1], -1.0F));
    CHECK(NearlyEqual(normalized.interleaved_samples[2], 0.0F));
    CHECK(NearlyEqual(normalized.interleaved_samples[3], 0.0F));
    const auto positive = 32767.0F / 32768.0F;
    CHECK(NearlyEqual(normalized.interleaved_samples[4], positive));
    CHECK(NearlyEqual(normalized.interleaved_samples[5], positive));
}

}

int main()
{
    ConvertsOne44100HzMonoPacketToAnExact480FrameStereoWindow();
    ConvertsPcm16EndpointsToFiniteStereoFloat();
    return 0;
}
