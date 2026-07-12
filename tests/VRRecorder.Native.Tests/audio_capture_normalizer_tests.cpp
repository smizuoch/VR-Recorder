#include "audio_capture_normalizer.hpp"

#include <algorithm>
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

void PreservesNative48KhzStereoFloatChannels()
{
    const std::vector<float> stereo {
        0.25F,
        -0.25F,
        0.5F,
        -0.5F,
    };
    const vrrecorder::native::CapturePcmFormat format {
        48'000,
        2,
        vrrecorder::native::CaptureSampleEncoding::IeeeFloat,
        32,
        32,
        8,
        0x0000'0003,
    };
    const vrrecorder::native::RawCapturePacket packet {
        20,
        3'000'000,
        2,
        std::as_bytes(std::span<const float>(stereo)),
        false,
        false,
        false,
    };
    vrrecorder::native::StereoCaptureNormalizer48k normalizer(3'000'000);
    vrrecorder::native::CapturedStereoPacket48k normalized {};

    CHECK(normalizer.Normalize(format, packet, normalized) ==
          vrrecorder::native::CaptureNormalizationResult::Ready);
    CHECK(normalized.frame_count_48k == 2);
    CHECK(normalized.interleaved_samples.size() == stereo.size());
    CHECK(std::equal(
        stereo.begin(),
        stereo.end(),
        normalized.interleaved_samples.begin()));
}

void RetainsRationalPhaseWhenTheSecondPacketTimestampIsInvalid()
{
    constexpr std::int64_t session_start_qpc_100ns = 4'000'000;
    const std::vector<float> first_samples(100, 0.25F);
    const std::vector<float> second_samples(341, 0.25F);
    const vrrecorder::native::CapturePcmFormat format {
        44'100,
        1,
        vrrecorder::native::CaptureSampleEncoding::IeeeFloat,
        32,
        32,
        4,
        0x0000'0004,
    };
    vrrecorder::native::StereoCaptureNormalizer48k normalizer(
        session_start_qpc_100ns);
    vrrecorder::native::CapturedStereoPacket48k first {};
    CHECK(normalizer.Normalize(
              format,
              {
                  1'000,
                  session_start_qpc_100ns,
                  first_samples.size(),
                  std::as_bytes(std::span<const float>(first_samples)),
                  false,
                  false,
                  false,
              },
              first) ==
          vrrecorder::native::CaptureNormalizationResult::Ready);
    const std::vector<float> first_output(
        first.interleaved_samples.begin(),
        first.interleaved_samples.end());
    vrrecorder::native::CapturedStereoPacket48k second {};

    CHECK(normalizer.Normalize(
              format,
              {
                  1'100,
                  0,
                  second_samples.size(),
                  std::as_bytes(std::span<const float>(second_samples)),
                  false,
                  false,
                  true,
              },
              second) ==
          vrrecorder::native::CaptureNormalizationResult::Ready);
    CHECK(first.start_frame_48k == 0);
    CHECK(first.frame_count_48k == 109);
    CHECK(second.start_frame_48k == 109);
    CHECK(second.frame_count_48k == 371);
    CHECK(first.frame_count_48k + second.frame_count_48k == 480);
    CHECK(second.qpc_100ns > first.qpc_100ns);
    CHECK(first_output.size() + second.interleaved_samples.size() == 960);
    for (const auto sample : first_output) {
        CHECK(NearlyEqual(sample, 0.25F));
    }

    for (const auto sample : second.interleaved_samples) {
        CHECK(NearlyEqual(sample, 0.25F));
    }
}

void Downmixes51FloatBySpeakerMaskOrder()
{
    const std::vector<float> surround {
        0.10F,
        0.20F,
        0.10F,
        0.10F,
        0.20F,
        0.25F,
    };
    const vrrecorder::native::CapturePcmFormat format {
        48'000,
        6,
        vrrecorder::native::CaptureSampleEncoding::IeeeFloat,
        32,
        32,
        24,
        0x0000'003f,
    };
    vrrecorder::native::StereoCaptureNormalizer48k normalizer(5'000'000);
    vrrecorder::native::CapturedStereoPacket48k normalized {};

    CHECK(normalizer.Normalize(
              format,
              {
                  0,
                  5'000'000,
                  1,
                  std::as_bytes(std::span<const float>(surround)),
                  false,
                  false,
                  false,
              },
              normalized) ==
          vrrecorder::native::CaptureNormalizationResult::Ready);
    constexpr float minus_3_db = 0.70710678F;
    constexpr float minus_6_db = 0.5F;
    const auto expected_left =
        0.10F + 0.10F * minus_3_db + 0.10F * minus_6_db +
        0.20F * minus_3_db;
    const auto expected_right =
        0.20F + 0.10F * minus_3_db + 0.10F * minus_6_db +
        0.25F * minus_3_db;
    CHECK(normalized.frame_count_48k == 1);
    CHECK(normalized.interleaved_samples.size() == 2);
    CHECK(NearlyEqual(normalized.interleaved_samples[0], expected_left));
    CHECK(NearlyEqual(normalized.interleaved_samples[1], expected_right));
}

void ConvertsPackedPcm24WithSignExtension()
{
    const std::vector<std::byte> mono {
        std::byte {0x00},
        std::byte {0x00},
        std::byte {0x80},
        std::byte {0x00},
        std::byte {0x00},
        std::byte {0x00},
        std::byte {0xff},
        std::byte {0xff},
        std::byte {0x7f},
    };
    const vrrecorder::native::CapturePcmFormat format {
        48'000,
        1,
        vrrecorder::native::CaptureSampleEncoding::PcmSignedInteger,
        24,
        24,
        3,
        0x0000'0004,
    };
    vrrecorder::native::StereoCaptureNormalizer48k normalizer(6'000'000);
    vrrecorder::native::CapturedStereoPacket48k normalized {};

    CHECK(normalizer.Normalize(
              format,
              {
                  0,
                  6'000'000,
                  3,
                  mono,
                  false,
                  false,
                  false,
              },
              normalized) ==
          vrrecorder::native::CaptureNormalizationResult::Ready);
    CHECK(normalized.frame_count_48k == 3);
    CHECK(normalized.interleaved_samples.size() == 6);
    CHECK(NearlyEqual(normalized.interleaved_samples[0], -1.0F));
    CHECK(NearlyEqual(normalized.interleaved_samples[1], -1.0F));
    CHECK(NearlyEqual(normalized.interleaved_samples[2], 0.0F));
    CHECK(NearlyEqual(normalized.interleaved_samples[3], 0.0F));
    const auto positive = 8388607.0F / 8388608.0F;
    CHECK(NearlyEqual(normalized.interleaved_samples[4], positive));
    CHECK(NearlyEqual(normalized.interleaved_samples[5], positive));
}

void ConvertsPcm24StoredInA32BitContainer()
{
    const std::vector<std::int32_t> mono {
        std::numeric_limits<std::int32_t>::min(),
        0,
        static_cast<std::int32_t>(0x7fffff00),
    };
    const vrrecorder::native::CapturePcmFormat format {
        48'000,
        1,
        vrrecorder::native::CaptureSampleEncoding::PcmSignedInteger,
        32,
        24,
        4,
        0x0000'0004,
    };
    vrrecorder::native::StereoCaptureNormalizer48k normalizer(7'000'000);
    vrrecorder::native::CapturedStereoPacket48k normalized {};

    CHECK(normalizer.Normalize(
              format,
              {
                  0,
                  7'000'000,
                  mono.size(),
                  std::as_bytes(std::span<const std::int32_t>(mono)),
                  false,
                  false,
                  false,
              },
              normalized) ==
          vrrecorder::native::CaptureNormalizationResult::Ready);
    CHECK(NearlyEqual(normalized.interleaved_samples[0], -1.0F));
    CHECK(NearlyEqual(normalized.interleaved_samples[1], -1.0F));
    CHECK(NearlyEqual(normalized.interleaved_samples[2], 0.0F));
    CHECK(NearlyEqual(normalized.interleaved_samples[3], 0.0F));
    const auto positive = 8388607.0F / 8388608.0F;
    CHECK(NearlyEqual(normalized.interleaved_samples[4], positive));
    CHECK(NearlyEqual(normalized.interleaved_samples[5], positive));
}

}

int main()
{
    ConvertsOne44100HzMonoPacketToAnExact480FrameStereoWindow();
    ConvertsPcm16EndpointsToFiniteStereoFloat();
    PreservesNative48KhzStereoFloatChannels();
    RetainsRationalPhaseWhenTheSecondPacketTimestampIsInvalid();
    Downmixes51FloatBySpeakerMaskOrder();
    ConvertsPackedPcm24WithSignExtension();
    ConvertsPcm24StoredInA32BitContainer();
    return 0;
}
