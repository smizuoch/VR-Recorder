#include "audio_capture_normalizer.hpp"

#include <algorithm>
#include <bit>
#include <cmath>
#include <cstring>
#include <limits>

namespace vrrecorder::native {

StereoCaptureNormalizer48k::StereoCaptureNormalizer48k(
    std::int64_t session_start_qpc_100ns) noexcept
    : session_start_qpc_100ns_(session_start_qpc_100ns)
{
}

CaptureNormalizationResult StereoCaptureNormalizer48k::Normalize(
    const CapturePcmFormat &format,
    const RawCapturePacket &packet,
    CapturedStereoPacket48k &normalized) noexcept
{
    normalized = {};

    if (!IsFormatSupported(format)) {
        return CaptureNormalizationResult::InvalidFormat;
    }

    const auto followup_device_position_gap =
        initialized_ && !packet.discontinuity &&
        allow_followup_device_position_gap_ &&
        SameFormat(format_, format) &&
        packet.device_position > expected_device_position_;
    const auto reset_epoch = !initialized_ || packet.discontinuity ||
        followup_device_position_gap;
    if (packet.frame_count == 0 || session_start_qpc_100ns_ < 0 ||
        (!packet.timestamp_error && packet.qpc_100ns < 0) ||
        (reset_epoch && packet.timestamp_error) ||
        packet.frame_count >
            std::numeric_limits<std::uint64_t>::max()) {
        return CaptureNormalizationResult::InvalidPacket;
    }

    if (packet.frame_count >
        std::numeric_limits<std::size_t>::max() / format.block_align) {
        return CaptureNormalizationResult::InvalidPacket;
    }

    const auto expected_byte_count =
        packet.frame_count * format.block_align;
    if ((!packet.silent && packet.bytes.size() != expected_byte_count) ||
        (packet.silent && !packet.bytes.empty() &&
         packet.bytes.size() != expected_byte_count)) {
        return CaptureNormalizationResult::InvalidPacket;
    }

    if (!initialized_ && !packet.timestamp_error &&
        packet.qpc_100ns < session_start_qpc_100ns_) {
        return CaptureNormalizationResult::BeforeSessionEpoch;
    }

    if (!packet.timestamp_error &&
        packet.qpc_100ns < session_start_qpc_100ns_) {
        return CaptureNormalizationResult::InvalidPacket;
    }

    if (initialized_ && !packet.discontinuity &&
        !followup_device_position_gap &&
        (!SameFormat(format_, format) ||
         packet.device_position != expected_device_position_)) {
        return CaptureNormalizationResult::InvalidPacket;
    }

    const auto base_input_frames = reset_epoch ? 0U : input_frames_;
    const auto base_output_frames = reset_epoch ? 0U : output_frames_;
    const auto frame_count = static_cast<std::uint64_t>(
        packet.frame_count);
    std::uint64_t new_input_frames = 0;
    std::uint64_t new_expected_device_position = 0;
    if (!TryAdd(base_input_frames, frame_count, new_input_frames) ||
        !TryAdd(
            packet.device_position,
            frame_count,
            new_expected_device_position)) {
        return CaptureNormalizationResult::InvalidPacket;
    }

    std::uint64_t new_output_frames = 0;
    if (!TryScaleRound(
            new_input_frames,
            OutputSampleRate,
            format.sample_rate_hz,
            new_output_frames) ||
        new_output_frames < base_output_frames) {
        return CaptureNormalizationResult::InvalidPacket;
    }

    const auto output_frame_count_u64 =
        new_output_frames - base_output_frames;
    if (output_frame_count_u64 == 0 ||
        output_frame_count_u64 >
            std::numeric_limits<std::size_t>::max() ||
        output_frame_count_u64 >
            std::numeric_limits<std::size_t>::max() /
                OutputChannelCount) {
        return CaptureNormalizationResult::InvalidPacket;
    }

    std::uint64_t start_frame_48k = 0;
    if (!reset_epoch) {
        if (!TryAdd(
                epoch_start_frame_48k_,
                base_output_frames,
                start_frame_48k)) {
            return CaptureNormalizationResult::InvalidPacket;
        }
    } else {
        const auto qpc_delta = static_cast<std::uint64_t>(
            packet.qpc_100ns - session_start_qpc_100ns_);
        if (!TryScaleRound(
                qpc_delta,
                OutputSampleRate,
                10'000'000,
                start_frame_48k)) {
            return CaptureNormalizationResult::InvalidPacket;
        }

        if (initialized_) {
            std::uint64_t previous_end_frame_48k = 0;
            if (!TryAdd(
                    epoch_start_frame_48k_,
                    output_frames_,
                    previous_end_frame_48k)) {
                return CaptureNormalizationResult::InvalidPacket;
            }

            if (followup_device_position_gap) {
                const auto missing_input_frames =
                    packet.device_position - expected_device_position_;
                std::uint64_t missing_output_frames = 0;
                std::uint64_t first_frame_after_gap = 0;
                if (!TryScaleRound(
                        missing_input_frames,
                        OutputSampleRate,
                        format.sample_rate_hz,
                        missing_output_frames) ||
                    !TryAdd(
                        previous_end_frame_48k,
                        missing_output_frames,
                        first_frame_after_gap)) {
                    return CaptureNormalizationResult::InvalidPacket;
                }

                previous_end_frame_48k = first_frame_after_gap;
            }

            start_frame_48k = std::max(
                start_frame_48k,
                previous_end_frame_48k);
        }
    }

    auto normalized_qpc_100ns = packet.qpc_100ns;
    if (packet.timestamp_error) {
        std::uint64_t qpc_delta = 0;
        if (!TryScaleRound(
                start_frame_48k,
                10'000'000,
                OutputSampleRate,
                qpc_delta) ||
            qpc_delta > static_cast<std::uint64_t>(
                std::numeric_limits<std::int64_t>::max() -
                session_start_qpc_100ns_)) {
            return CaptureNormalizationResult::InvalidPacket;
        }

        normalized_qpc_100ns = session_start_qpc_100ns_ +
            static_cast<std::int64_t>(qpc_delta);
    }

    const auto output_frame_count = static_cast<std::size_t>(
        output_frame_count_u64);
    try {
        normalized_samples_.assign(
            output_frame_count * OutputChannelCount,
            0.0F);
        if (!packet.silent) {
            std::vector<float> source_stereo(
                packet.frame_count * OutputChannelCount);
            const auto bytes_per_sample = format.container_bits / 8U;
            for (std::size_t frame = 0;
                 frame < packet.frame_count;
                 ++frame) {
                float left = 0.0F;
                float right = 0.0F;
                for (std::size_t source_channel = 0;
                     source_channel < format.channel_count;
                     ++source_channel) {
                    const auto *sample_bytes = packet.bytes.data() +
                        frame * format.block_align +
                        source_channel * bytes_per_sample;
                    float sample = 0.0F;
                    if (!TryDecodeSample(
                            format,
                            sample_bytes,
                            sample)) {
                        return CaptureNormalizationResult::InvalidPacket;
                    }

                    if (format.channel_count == 1) {
                        left = sample;
                        right = sample;
                    } else if (format.channel_count == 2) {
                        if (source_channel == 0) {
                            left = sample;
                        } else {
                            right = sample;
                        }
                    } else {
                        float left_gain = 0.0F;
                        float right_gain = 0.0F;
                        if (!TryGetStereoGains(
                                format.speaker_mask,
                                source_channel,
                                left_gain,
                                right_gain)) {
                            return CaptureNormalizationResult::InvalidFormat;
                        }

                        left += sample * left_gain;
                        right += sample * right_gain;
                    }
                }

                source_stereo[frame * OutputChannelCount] = left;
                source_stereo[frame * OutputChannelCount + 1U] = right;
            }

            for (std::size_t output_frame = 0;
                 output_frame < output_frame_count;
                 ++output_frame) {
                const auto global_output_frame =
                    base_output_frames + output_frame;
                const auto global_source_position =
                    static_cast<long double>(global_output_frame) *
                    static_cast<long double>(format.sample_rate_hz) /
                    static_cast<long double>(OutputSampleRate);
                const auto local_source_position =
                    std::max<long double>(
                        0.0L,
                        global_source_position -
                            static_cast<long double>(base_input_frames));
                const auto first_source_frame =
                    std::min<std::size_t>(
                        static_cast<std::size_t>(local_source_position),
                        packet.frame_count - 1U);
                const auto second_source_frame =
                    std::min(
                        first_source_frame + 1U,
                        packet.frame_count - 1U);
                const auto fraction = static_cast<float>(
                    local_source_position -
                    static_cast<long double>(first_source_frame));
                for (std::size_t channel = 0;
                     channel < OutputChannelCount;
                     ++channel) {
                    const auto first = source_stereo[
                        first_source_frame * OutputChannelCount + channel];
                    const auto second = source_stereo[
                        second_source_frame * OutputChannelCount + channel];
                    normalized_samples_[
                        output_frame * OutputChannelCount + channel] =
                        first + (second - first) * fraction;
                }
            }
        }
    } catch (...) {
        return CaptureNormalizationResult::OutOfMemory;
    }

    if (reset_epoch) {
        format_ = format;
        epoch_start_frame_48k_ = start_frame_48k;
        initialized_ = true;
    }

    input_frames_ = new_input_frames;
    output_frames_ = new_output_frames;
    expected_device_position_ = new_expected_device_position;
    allow_followup_device_position_gap_ = packet.discontinuity;
    normalized = CapturedStereoPacket48k {
        start_frame_48k,
        packet.device_position,
        normalized_qpc_100ns,
        output_frame_count,
        packet.silent
            ? std::span<const float> {}
            : std::span<const float>(normalized_samples_),
        packet.silent,
        packet.discontinuity || followup_device_position_gap,
    };
    return CaptureNormalizationResult::Ready;
}

bool StereoCaptureNormalizer48k::IsFormatSupported(
    const CapturePcmFormat &format) noexcept
{
    constexpr std::uint16_t maximum_channel_count = 8;
    const auto channel_layout_supported =
        (format.channel_count == 1 &&
         (format.speaker_mask == 0 ||
          format.speaker_mask == 0x0000'0004)) ||
        (format.channel_count == OutputChannelCount &&
         (format.speaker_mask == 0 ||
          format.speaker_mask == 0x0000'0003)) ||
        (format.channel_count > OutputChannelCount &&
         std::popcount(format.speaker_mask) == format.channel_count);
    if (format.sample_rate_hz == 0 || format.channel_count == 0 ||
        format.channel_count > maximum_channel_count ||
        !channel_layout_supported) {
        return false;
    }

    const auto bytes_per_sample = format.container_bits / 8U;
    const auto encoding_supported =
        (format.encoding == CaptureSampleEncoding::IeeeFloat &&
         format.container_bits == 32 && format.valid_bits == 32) ||
        (format.encoding == CaptureSampleEncoding::PcmSignedInteger &&
         ((format.container_bits == 16 && format.valid_bits == 16) ||
          (format.container_bits == 24 && format.valid_bits == 24) ||
          (format.container_bits == 32 &&
           (format.valid_bits == 24 || format.valid_bits == 32))));
    return encoding_supported && format.container_bits % 8U == 0 &&
           format.block_align == bytes_per_sample * format.channel_count;
}

bool StereoCaptureNormalizer48k::SameFormat(
    const CapturePcmFormat &left,
    const CapturePcmFormat &right) noexcept
{
    return left.sample_rate_hz == right.sample_rate_hz &&
           left.channel_count == right.channel_count &&
           left.encoding == right.encoding &&
           left.container_bits == right.container_bits &&
           left.valid_bits == right.valid_bits &&
           left.block_align == right.block_align &&
           left.speaker_mask == right.speaker_mask;
}

bool StereoCaptureNormalizer48k::TryGetStereoGains(
    std::uint32_t speaker_mask,
    std::size_t channel_index,
    float &left,
    float &right) noexcept
{
    constexpr float minus_3_db = 0.70710678F;
    constexpr float minus_6_db = 0.5F;
    std::uint32_t remaining = speaker_mask;
    for (std::size_t index = 0; index < channel_index; ++index) {
        if (remaining == 0) {
            return false;
        }

        remaining &= remaining - 1U;
    }

    if (remaining == 0) {
        return false;
    }

    const auto speaker = std::uint32_t {1} << std::countr_zero(remaining);
    switch (speaker) {
    case 0x0000'0001:
        left = 1.0F;
        right = 0.0F;
        return true;
    case 0x0000'0002:
        left = 0.0F;
        right = 1.0F;
        return true;
    case 0x0000'0004:
        left = minus_3_db;
        right = minus_3_db;
        return true;
    case 0x0000'0008:
        left = minus_6_db;
        right = minus_6_db;
        return true;
    case 0x0000'0010:
    case 0x0000'0040:
    case 0x0000'0200:
        left = minus_3_db;
        right = 0.0F;
        return true;
    case 0x0000'0020:
    case 0x0000'0080:
    case 0x0000'0400:
        left = 0.0F;
        right = minus_3_db;
        return true;
    case 0x0000'0100:
        left = minus_6_db;
        right = minus_6_db;
        return true;
    default:
        return false;
    }
}

bool StereoCaptureNormalizer48k::TryDecodeSample(
    const CapturePcmFormat &format,
    const std::byte *bytes,
    float &sample) noexcept
{
    if (format.encoding == CaptureSampleEncoding::IeeeFloat) {
        std::memcpy(&sample, bytes, sizeof(float));
        return std::isfinite(sample);
    }

    if (format.container_bits == 16 && format.valid_bits == 16) {
        std::int16_t pcm = 0;
        std::memcpy(&pcm, bytes, sizeof(pcm));
        sample = static_cast<float>(pcm) / 32768.0F;
        return true;
    }

    if (format.container_bits == 24 && format.valid_bits == 24) {
        auto pcm = static_cast<std::int32_t>(
            std::to_integer<std::uint8_t>(bytes[0]) |
            (static_cast<std::uint32_t>(
                 std::to_integer<std::uint8_t>(bytes[1])) << 8U) |
            (static_cast<std::uint32_t>(
                 std::to_integer<std::uint8_t>(bytes[2])) << 16U));
        if ((pcm & 0x0080'0000) != 0) {
            pcm |= static_cast<std::int32_t>(0xff00'0000U);
        }

        sample = static_cast<float>(pcm) / 8388608.0F;
        return true;
    }

    if (format.container_bits == 32 &&
        (format.valid_bits == 24 || format.valid_bits == 32)) {
        std::int32_t pcm = 0;
        std::memcpy(&pcm, bytes, sizeof(pcm));
        const auto padding_bits =
            static_cast<int>(format.container_bits - format.valid_bits);
        const auto padding_mask =
            (std::uint32_t {1} << padding_bits) - 1U;
        if ((std::bit_cast<std::uint32_t>(pcm) & padding_mask) != 0) {
            return false;
        }
        const auto padding_scale = std::int32_t {1} << padding_bits;
        const auto valid_sample = pcm / padding_scale;
        const auto full_scale = std::ldexp(
            1.0F,
            static_cast<int>(format.valid_bits) - 1);
        sample = static_cast<float>(valid_sample) / full_scale;
        return true;
    }

    return false;
}

bool StereoCaptureNormalizer48k::TryScaleRound(
    std::uint64_t value,
    std::uint64_t numerator,
    std::uint64_t denominator,
    std::uint64_t &scaled) noexcept
{
    if (numerator == 0 || denominator == 0) {
        return false;
    }

    const auto quotient = value / denominator;
    const auto remainder = value % denominator;
    if (quotient >
        std::numeric_limits<std::uint64_t>::max() / numerator ||
        remainder >
            (std::numeric_limits<std::uint64_t>::max() -
             denominator / 2U) /
                numerator) {
        return false;
    }

    const auto whole = quotient * numerator;
    const auto fraction =
        (remainder * numerator + denominator / 2U) / denominator;
    return TryAdd(whole, fraction, scaled);
}

bool StereoCaptureNormalizer48k::TryAdd(
    std::uint64_t left,
    std::uint64_t right,
    std::uint64_t &sum) noexcept
{
    if (left > std::numeric_limits<std::uint64_t>::max() - right) {
        return false;
    }

    sum = left + right;
    return true;
}

}
