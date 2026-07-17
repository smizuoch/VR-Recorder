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

void RetainsThePacketAfterADevicePositionDiscontinuity()
{
    constexpr std::int64_t session_start_qpc_100ns = 8'000'000;
    const vrrecorder::native::CapturePcmFormat format {
        48'000,
        1,
        vrrecorder::native::CaptureSampleEncoding::IeeeFloat,
        32,
        32,
        4,
        0x0000'0004,
    };
    const std::vector<float> first_samples(2, 0.25F);
    const std::vector<float> recovered_samples(2, 0.75F);
    vrrecorder::native::StereoCaptureNormalizer48k normalizer(
        session_start_qpc_100ns);
    vrrecorder::native::CapturedStereoPacket48k first {};
    CHECK(normalizer.Normalize(
              format,
              {
                  100,
                  session_start_qpc_100ns,
                  first_samples.size(),
                  std::as_bytes(std::span<const float>(first_samples)),
                  false,
                  false,
                  false,
              },
              first) ==
          vrrecorder::native::CaptureNormalizationResult::Ready);
    vrrecorder::native::CapturedStereoPacket48k recovered {};

    CHECK(normalizer.Normalize(
              format,
              {
                  0,
                  session_start_qpc_100ns + 10'000,
                  recovered_samples.size(),
                  std::as_bytes(std::span<const float>(recovered_samples)),
                  false,
                  true,
                  false,
              },
              recovered) ==
          vrrecorder::native::CaptureNormalizationResult::Ready);
    CHECK(first.start_frame_48k == 0);
    CHECK(recovered.start_frame_48k == 48);
    CHECK(recovered.device_position == 0);
    CHECK(recovered.frame_count_48k == 2);
    CHECK(recovered.discontinuity);
    CHECK(recovered.interleaved_samples.size() == 4);
    for (const auto sample : recovered.interleaved_samples) {
        CHECK(NearlyEqual(sample, 0.75F));
    }
}

void LeavesTheEpochUninitializedForAValidPreSessionPacket()
{
    constexpr std::int64_t session_start_qpc_100ns = 8'500'000;
    const vrrecorder::native::CapturePcmFormat format {
        48'000,
        1,
        vrrecorder::native::CaptureSampleEncoding::IeeeFloat,
        32,
        32,
        4,
        0x0000'0004,
    };
    const std::vector<float> samples(2, 0.25F);
    vrrecorder::native::StereoCaptureNormalizer48k normalizer(
        session_start_qpc_100ns);
    vrrecorder::native::CapturedStereoPacket48k normalized {};

    CHECK(normalizer.Normalize(
              format,
              {
                  100,
                  session_start_qpc_100ns - 1'000,
                  samples.size(),
                  std::as_bytes(std::span<const float>(samples)),
                  false,
                  true,
                  false,
              },
              normalized) ==
          vrrecorder::native::CaptureNormalizationResult::
              BeforeSessionEpoch);
    CHECK(normalized.frame_count_48k == 0);
    CHECK(normalized.interleaved_samples.empty());

    CHECK(normalizer.Normalize(
              format,
              {
                  102,
                  session_start_qpc_100ns + 10'000,
                  samples.size(),
                  std::as_bytes(std::span<const float>(samples)),
                  false,
                  false,
                  false,
              },
              normalized) ==
          vrrecorder::native::CaptureNormalizationResult::Ready);
    CHECK(normalized.start_frame_48k == 48);
    CHECK(normalized.device_position == 102);
    CHECK(normalized.frame_count_48k == 2);
}

void PreservesAForwardGapAfterAShortDiscontinuityPacket()
{
    constexpr std::int64_t session_start_qpc_100ns = 8'750'000;
    const vrrecorder::native::CapturePcmFormat format {
        48'000,
        1,
        vrrecorder::native::CaptureSampleEncoding::IeeeFloat,
        32,
        32,
        4,
        0x0000'0004,
    };
    const std::vector<float> short_samples(96, 0.25F);
    const std::vector<float> next_samples(480, 0.5F);
    vrrecorder::native::StereoCaptureNormalizer48k normalizer(
        session_start_qpc_100ns);
    vrrecorder::native::CapturedStereoPacket48k short_packet {};
    CHECK(normalizer.Normalize(
              format,
              {
                  100,
                  session_start_qpc_100ns,
                  short_samples.size(),
                  std::as_bytes(
                      std::span<const float>(short_samples)),
                  false,
                  true,
                  false,
              },
              short_packet) ==
          vrrecorder::native::CaptureNormalizationResult::Ready);
    vrrecorder::native::CapturedStereoPacket48k after_gap {};

    CHECK(normalizer.Normalize(
              format,
              {
                  580,
                  session_start_qpc_100ns + 20'000,
                  next_samples.size(),
                  std::as_bytes(std::span<const float>(next_samples)),
                  false,
                  false,
                  false,
              },
              after_gap) ==
          vrrecorder::native::CaptureNormalizationResult::Ready);
    CHECK(short_packet.start_frame_48k == 0);
    CHECK(short_packet.frame_count_48k == 96);
    CHECK(after_gap.start_frame_48k == 480);
    CHECK(after_gap.device_position == 580);
    CHECK(after_gap.frame_count_48k == 480);
    CHECK(after_gap.discontinuity);
}

void RejectsPositionGapsOutsideTheDiscontinuityFollowup()
{
    constexpr std::int64_t session_start_qpc_100ns = 8'900'000;
    const vrrecorder::native::CapturePcmFormat format {
        48'000,
        1,
        vrrecorder::native::CaptureSampleEncoding::IeeeFloat,
        32,
        32,
        4,
        0x0000'0004,
    };
    const std::vector<float> samples(96, 0.25F);

    {
        vrrecorder::native::StereoCaptureNormalizer48k normalizer(
            session_start_qpc_100ns);
        vrrecorder::native::CapturedStereoPacket48k normalized {};
        CHECK(normalizer.Normalize(
                  format,
                  {
                      100,
                      session_start_qpc_100ns,
                      samples.size(),
                      std::as_bytes(std::span<const float>(samples)),
                      false,
                      false,
                      false,
                  },
                  normalized) ==
              vrrecorder::native::CaptureNormalizationResult::Ready);

        CHECK(normalizer.Normalize(
                  format,
                  {
                      580,
                      session_start_qpc_100ns + 20'000,
                      samples.size(),
                      std::as_bytes(std::span<const float>(samples)),
                      false,
                      false,
                      false,
                  },
                  normalized) ==
              vrrecorder::native::CaptureNormalizationResult::
                  InvalidPacket);
    }

    {
        vrrecorder::native::StereoCaptureNormalizer48k normalizer(
            session_start_qpc_100ns);
        vrrecorder::native::CapturedStereoPacket48k normalized {};
        CHECK(normalizer.Normalize(
                  format,
                  {
                      580,
                      session_start_qpc_100ns,
                      samples.size(),
                      std::as_bytes(std::span<const float>(samples)),
                      false,
                      true,
                      false,
                  },
                  normalized) ==
              vrrecorder::native::CaptureNormalizationResult::Ready);

        CHECK(normalizer.Normalize(
                  format,
                  {
                      100,
                      session_start_qpc_100ns + 20'000,
                      samples.size(),
                      std::as_bytes(std::span<const float>(samples)),
                      false,
                      false,
                      false,
                  },
                  normalized) ==
              vrrecorder::native::CaptureNormalizationResult::
                  InvalidPacket);
    }
}

void RejectsNonstandardStereoSpeakerLayoutsInsteadOfSwappingSemantics()
{
    const std::vector<float> center_and_lfe {0.25F, 0.5F};
    const vrrecorder::native::CapturePcmFormat format {
        48'000,
        2,
        vrrecorder::native::CaptureSampleEncoding::IeeeFloat,
        32,
        32,
        8,
        0x0000'000c,
    };
    vrrecorder::native::StereoCaptureNormalizer48k normalizer(9'000'000);
    vrrecorder::native::CapturedStereoPacket48k normalized {};

    CHECK(normalizer.Normalize(
              format,
              {
                  0,
                  9'000'000,
                  1,
                  std::as_bytes(std::span<const float>(center_and_lfe)),
                  false,
                  false,
                  false,
              },
              normalized) ==
          vrrecorder::native::CaptureNormalizationResult::InvalidFormat);
    CHECK(normalized.frame_count_48k == 0);
    CHECK(normalized.interleaved_samples.empty());
}

void RejectsNonzeroPaddingBitsInPcm24StoredIn32Bits()
{
    const std::vector<std::int32_t> malformed {1};
    const vrrecorder::native::CapturePcmFormat format {
        48'000,
        1,
        vrrecorder::native::CaptureSampleEncoding::PcmSignedInteger,
        32,
        24,
        4,
        0x0000'0004,
    };
    vrrecorder::native::StereoCaptureNormalizer48k normalizer(10'000'000);
    vrrecorder::native::CapturedStereoPacket48k normalized {};

    CHECK(normalizer.Normalize(
              format,
              {
                  0,
                  10'000'000,
                  1,
                  std::as_bytes(std::span<const std::int32_t>(malformed)),
                  false,
                  false,
                  false,
              },
              normalized) ==
          vrrecorder::native::CaptureNormalizationResult::InvalidPacket);
    CHECK(normalized.frame_count_48k == 0);
    CHECK(normalized.interleaved_samples.empty());
}

void RejectsEveryUnsupportedCaptureFormat()
{
    using namespace vrrecorder::native;
    const auto rejects = [](const auto mutate) {
        auto format = CapturePcmFormat {
            48'000,
            2,
            CaptureSampleEncoding::IeeeFloat,
            32,
            32,
            8,
            0x0000'0003,
        };
        mutate(format);
        const std::vector<float> samples {0.25F, -0.25F};
        StereoCaptureNormalizer48k normalizer(1'000'000);
        CapturedStereoPacket48k normalized {};
        CHECK(normalizer.Normalize(
                  format,
                  {
                      0,
                      1'000'000,
                      1,
                      std::as_bytes(std::span<const float>(samples)),
                      false,
                      false,
                      false,
                  },
                  normalized) == CaptureNormalizationResult::InvalidFormat);
    };

    rejects([](auto &value) { value.sample_rate_hz = 0; });
    rejects([](auto &value) { value.channel_count = 0; });
    rejects([](auto &value) { value.channel_count = 9; });
    rejects([](auto &value) {
        value.channel_count = 1;
        value.block_align = 4;
        value.speaker_mask = 0x0000'0001;
    });
    rejects([](auto &value) { value.speaker_mask = 0x0000'000c; });
    rejects([](auto &value) {
        value.channel_count = 3;
        value.block_align = 12;
        value.speaker_mask = 0x0000'0003;
    });
    rejects([](auto &value) { value.container_bits = 16; });
    rejects([](auto &value) { value.valid_bits = 16; });
    rejects([](auto &value) {
        value.encoding = CaptureSampleEncoding::PcmSignedInteger;
        value.container_bits = 16;
        value.valid_bits = 15;
        value.block_align = 4;
    });
    rejects([](auto &value) {
        value.encoding = CaptureSampleEncoding::PcmSignedInteger;
        value.container_bits = 24;
        value.valid_bits = 23;
        value.block_align = 6;
    });
    rejects([](auto &value) {
        value.encoding = CaptureSampleEncoding::PcmSignedInteger;
        value.valid_bits = 16;
    });
    rejects([](auto &value) { value.block_align = 9; });
}

void AcceptsAlternateLayoutsAndPcm32()
{
    using namespace vrrecorder::native;
    const auto accepts = [](const CapturePcmFormat &format,
                            std::span<const std::byte> bytes) {
        StereoCaptureNormalizer48k normalizer(2'000'000);
        CapturedStereoPacket48k normalized {};
        CHECK(normalizer.Normalize(
                  format,
                  {0, 2'000'000, 1, bytes, false, false, false},
                  normalized) == CaptureNormalizationResult::Ready);
        CHECK(normalized.frame_count_48k == 1);
    };

    const std::vector<float> mono {0.25F};
    accepts(
        {48'000, 1, CaptureSampleEncoding::IeeeFloat, 32, 32, 4, 0},
        std::as_bytes(std::span<const float>(mono)));
    const std::vector<float> stereo {0.25F, -0.25F};
    accepts(
        {48'000, 2, CaptureSampleEncoding::IeeeFloat, 32, 32, 8, 0},
        std::as_bytes(std::span<const float>(stereo)));
    const std::vector<std::int32_t> pcm32 {
        std::numeric_limits<std::int32_t>::min(),
        std::numeric_limits<std::int32_t>::max(),
    };
    accepts(
        {48'000, 2, CaptureSampleEncoding::PcmSignedInteger,
         32, 32, 8, 0x0000'0003},
        std::as_bytes(std::span<const std::int32_t>(pcm32)));
}

void RejectsMalformedPacketMetadataAndPayloads()
{
    using namespace vrrecorder::native;
    const CapturePcmFormat format {
        48'000,
        2,
        CaptureSampleEncoding::IeeeFloat,
        32,
        32,
        8,
        0x0000'0003,
    };
    const std::vector<float> stereo {0.25F, -0.25F};
    const auto bytes = std::as_bytes(std::span<const float>(stereo));
    const auto rejects = [&](const RawCapturePacket &packet) {
        StereoCaptureNormalizer48k normalizer(3'000'000);
        CapturedStereoPacket48k normalized {};
        CHECK(normalizer.Normalize(format, packet, normalized) ==
              CaptureNormalizationResult::InvalidPacket);
    };

    rejects({0, 3'000'000, 0, {}, false, false, false});
    rejects({0, -1, 1, bytes, false, false, false});
    rejects({0, 0, 1, bytes, false, false, true});
    rejects({0, 3'000'000, 1, bytes.first(4), false, false, false});
    rejects({0, 3'000'000, 1, bytes.first(4), true, false, false});

    StereoCaptureNormalizer48k invalid_epoch(-1);
    CapturedStereoPacket48k normalized {};
    CHECK(invalid_epoch.Normalize(
              format,
              {0, 0, 1, bytes, false, false, false},
              normalized) == CaptureNormalizationResult::InvalidPacket);

    const std::vector<float> not_finite {
        std::numeric_limits<float>::quiet_NaN(),
        0.0F,
    };
    StereoCaptureNormalizer48k non_finite(3'000'000);
    CHECK(non_finite.Normalize(
              format,
              {
                  0,
                  3'000'000,
                  1,
                  std::as_bytes(std::span<const float>(not_finite)),
                  false,
                  false,
                  false,
              },
              normalized) == CaptureNormalizationResult::InvalidPacket);

    StereoCaptureNormalizer48k stateful(3'000'000);
    CHECK(stateful.Normalize(
              format,
              {100, 3'000'000, 1, bytes, false, false, false},
              normalized) == CaptureNormalizationResult::Ready);
    CHECK(stateful.Normalize(
              format,
              {101, 2'999'999, 1, bytes, false, false, false},
              normalized) == CaptureNormalizationResult::InvalidPacket);
    auto changed_format = format;
    changed_format.sample_rate_hz = 44'100;
    CHECK(stateful.Normalize(
              changed_format,
              {101, 3'000'100, 1, bytes, false, false, false},
              normalized) == CaptureNormalizationResult::InvalidPacket);
}

void AcceptsSilentPacketsWithEitherPayloadRepresentation()
{
    using namespace vrrecorder::native;
    const CapturePcmFormat format {
        48'000,
        2,
        CaptureSampleEncoding::IeeeFloat,
        32,
        32,
        8,
        0x0000'0003,
    };
    const std::vector<float> ignored {0.25F, -0.25F};
    const auto accepts = [&](std::span<const std::byte> bytes) {
        StereoCaptureNormalizer48k normalizer(4'000'000);
        CapturedStereoPacket48k normalized {};
        CHECK(normalizer.Normalize(
                  format,
                  {0, 4'000'000, 1, bytes, true, false, false},
                  normalized) == CaptureNormalizationResult::Ready);
        CHECK(normalized.silent);
        CHECK(normalized.interleaved_samples.empty());
    };
    accepts({});
    accepts(std::as_bytes(std::span<const float>(ignored)));
}

void ClearsThePreviousNormalizedPacketWhenAContractCheckFails()
{
    const std::vector<float> stereo {0.25F, -0.25F};
    auto format = vrrecorder::native::CapturePcmFormat {
        48'000,
        2,
        vrrecorder::native::CaptureSampleEncoding::IeeeFloat,
        32,
        32,
        8,
        0x0000'0003,
    };
    vrrecorder::native::StereoCaptureNormalizer48k normalizer(11'000'000);
    vrrecorder::native::CapturedStereoPacket48k normalized {};
    CHECK(normalizer.Normalize(
              format,
              {
                  100,
                  11'000'000,
                  1,
                  std::as_bytes(std::span<const float>(stereo)),
                  false,
                  false,
                  false,
              },
              normalized) ==
          vrrecorder::native::CaptureNormalizationResult::Ready);
    CHECK(normalized.frame_count_48k == 1);
    CHECK(!normalized.interleaved_samples.empty());

    format.sample_rate_hz = 0;
    CHECK(normalizer.Normalize(
              format,
              {
                  101,
                  11'000'100,
                  1,
                  std::as_bytes(std::span<const float>(stereo)),
                  false,
                  false,
                  false,
              },
              normalized) ==
          vrrecorder::native::CaptureNormalizationResult::InvalidFormat);
    CHECK(normalized.start_frame_48k == 0);
    CHECK(normalized.device_position == 0);
    CHECK(normalized.qpc_100ns == 0);
    CHECK(normalized.frame_count_48k == 0);
    CHECK(normalized.interleaved_samples.empty());
    CHECK(!normalized.silent);
    CHECK(!normalized.discontinuity);
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
    RetainsThePacketAfterADevicePositionDiscontinuity();
    LeavesTheEpochUninitializedForAValidPreSessionPacket();
    PreservesAForwardGapAfterAShortDiscontinuityPacket();
    RejectsPositionGapsOutsideTheDiscontinuityFollowup();
    RejectsNonstandardStereoSpeakerLayoutsInsteadOfSwappingSemantics();
    RejectsNonzeroPaddingBitsInPcm24StoredIn32Bits();
    RejectsEveryUnsupportedCaptureFormat();
    AcceptsAlternateLayoutsAndPcm32();
    RejectsMalformedPacketMetadataAndPayloads();
    AcceptsSilentPacketsWithEitherPayloadRepresentation();
    ClearsThePreviousNormalizedPacketWhenAContractCheckFails();
    return 0;
}
