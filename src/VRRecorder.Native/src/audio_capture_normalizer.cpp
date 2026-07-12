#include "audio_capture_normalizer.hpp"

#include <algorithm>
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
    if (!IsFormatSupported(format)) {
        return CaptureNormalizationResult::InvalidFormat;
    }

    if (packet.frame_count == 0 || packet.qpc_100ns < 0 ||
        session_start_qpc_100ns_ < 0 ||
        packet.qpc_100ns < session_start_qpc_100ns_ ||
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

    if (initialized_ &&
        (!SameFormat(format_, format) ||
         packet.device_position != expected_device_position_)) {
        return packet.discontinuity
            ? CaptureNormalizationResult::Discontinuity
            : CaptureNormalizationResult::InvalidPacket;
    }

    const auto frame_count = static_cast<std::uint64_t>(
        packet.frame_count);
    std::uint64_t new_input_frames = 0;
    std::uint64_t new_expected_device_position = 0;
    if (!TryAdd(input_frames_, frame_count, new_input_frames) ||
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
        new_output_frames < output_frames_) {
        return CaptureNormalizationResult::InvalidPacket;
    }

    const auto output_frame_count_u64 =
        new_output_frames - output_frames_;
    if (output_frame_count_u64 == 0 ||
        output_frame_count_u64 >
            std::numeric_limits<std::size_t>::max() ||
        output_frame_count_u64 >
            std::numeric_limits<std::size_t>::max() /
                OutputChannelCount) {
        return CaptureNormalizationResult::InvalidPacket;
    }

    std::uint64_t start_frame_48k = 0;
    if (initialized_) {
        if (!TryAdd(
                epoch_start_frame_48k_,
                output_frames_,
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
            for (std::size_t frame = 0;
                 frame < packet.frame_count;
                 ++frame) {
                float sample = 0.0F;
                std::memcpy(
                    &sample,
                    packet.bytes.data() + frame * format.block_align,
                    sizeof(float));
                if (!std::isfinite(sample)) {
                    return CaptureNormalizationResult::InvalidPacket;
                }

                source_stereo[frame * OutputChannelCount] = sample;
                source_stereo[frame * OutputChannelCount + 1U] = sample;
            }

            for (std::size_t output_frame = 0;
                 output_frame < output_frame_count;
                 ++output_frame) {
                const auto global_output_frame =
                    output_frames_ + output_frame;
                const auto global_source_position =
                    static_cast<long double>(global_output_frame) *
                    static_cast<long double>(format.sample_rate_hz) /
                    static_cast<long double>(OutputSampleRate);
                const auto local_source_position =
                    std::max<long double>(
                        0.0L,
                        global_source_position -
                            static_cast<long double>(input_frames_));
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

    if (!initialized_) {
        format_ = format;
        epoch_start_frame_48k_ = start_frame_48k;
        initialized_ = true;
    }

    input_frames_ = new_input_frames;
    output_frames_ = new_output_frames;
    expected_device_position_ = new_expected_device_position;
    normalized = CapturedStereoPacket48k {
        start_frame_48k,
        packet.device_position,
        packet.qpc_100ns,
        output_frame_count,
        packet.silent
            ? std::span<const float> {}
            : std::span<const float>(normalized_samples_),
        packet.silent,
        packet.discontinuity,
    };
    return CaptureNormalizationResult::Ready;
}

bool StereoCaptureNormalizer48k::IsFormatSupported(
    const CapturePcmFormat &format) noexcept
{
    if (format.sample_rate_hz == 0 || format.channel_count != 1 ||
        format.encoding != CaptureSampleEncoding::IeeeFloat ||
        format.container_bits != 32 || format.valid_bits != 32) {
        return false;
    }

    return format.block_align == sizeof(float) * format.channel_count;
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
