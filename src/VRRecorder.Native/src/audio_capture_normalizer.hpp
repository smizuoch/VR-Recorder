#ifndef VRRECORDER_NATIVE_AUDIO_CAPTURE_NORMALIZER_HPP
#define VRRECORDER_NATIVE_AUDIO_CAPTURE_NORMALIZER_HPP

#include <cstddef>
#include <cstdint>
#include <span>
#include <vector>

#include "audio_capture_source.hpp"

namespace vrrecorder::native {

enum class CaptureSampleEncoding {
    PcmSignedInteger,
    IeeeFloat,
};

struct CapturePcmFormat final {
    std::uint32_t sample_rate_hz;
    std::uint16_t channel_count;
    CaptureSampleEncoding encoding;
    std::uint16_t container_bits;
    std::uint16_t valid_bits;
    std::uint16_t block_align;
    std::uint32_t speaker_mask;
};

struct RawCapturePacket final {
    std::uint64_t device_position;
    std::int64_t qpc_100ns;
    std::size_t frame_count;
    std::span<const std::byte> bytes;
    bool silent;
    bool discontinuity;
    bool timestamp_error;
};

enum class CaptureNormalizationResult {
    Ready,
    BeforeSessionEpoch,
    InvalidFormat,
    InvalidPacket,
    Discontinuity,
    OutOfMemory,
};

class StereoCaptureNormalizer48k final {
public:
    static constexpr std::uint32_t OutputSampleRate = 48'000;
    static constexpr std::size_t OutputChannelCount = 2;

    explicit StereoCaptureNormalizer48k(
        std::int64_t session_start_qpc_100ns) noexcept;

    CaptureNormalizationResult Normalize(
        const CapturePcmFormat &format,
        const RawCapturePacket &packet,
        CapturedStereoPacket48k &normalized) noexcept;

private:
    static bool IsFormatSupported(
        const CapturePcmFormat &format) noexcept;
    static bool SameFormat(
        const CapturePcmFormat &left,
        const CapturePcmFormat &right) noexcept;
    static bool TryGetStereoGains(
        std::uint32_t speaker_mask,
        std::size_t channel_index,
        float &left,
        float &right) noexcept;
    static bool TryDecodeSample(
        const CapturePcmFormat &format,
        const std::byte *bytes,
        float &sample) noexcept;
    static bool TryScaleRound(
        std::uint64_t value,
        std::uint64_t numerator,
        std::uint64_t denominator,
        std::uint64_t &scaled) noexcept;
    static bool TryAdd(
        std::uint64_t left,
        std::uint64_t right,
        std::uint64_t &sum) noexcept;

    std::int64_t session_start_qpc_100ns_;
    CapturePcmFormat format_ {};
    std::vector<float> normalized_samples_;
    std::uint64_t epoch_start_frame_48k_ = 0;
    std::uint64_t input_frames_ = 0;
    std::uint64_t output_frames_ = 0;
    std::uint64_t expected_device_position_ = 0;
    bool allow_followup_device_position_gap_ = false;
    bool initialized_ = false;
};

}

#endif
