#include "encoder_probe_evidence_builder.hpp"

#include <cstdint>
#include <new>
#include <string_view>
#include <utility>
#include <vector>

#include "encoder_probe_identity.hpp"
#include "h264_packet_normalizer.hpp"

namespace vrrecorder::native {
namespace {

constexpr std::uint32_t RequiredSyntheticFrameCount = 16;

bool HasRequiredOpenedIdentity(
    const vrrec_encoder_probe_config_v1 &config,
    const EncoderProbeOpenedIdentity &opened,
    const ExpectedEncoderProbeIdentity &expected) noexcept
{
    const auto adapters_match = expected.hardware_accelerated
        ? opened.source_adapter_luid == config.adapter_luid &&
            opened.processor_adapter_luid == config.adapter_luid &&
            opened.encoder_adapter_luid == config.adapter_luid
        : opened.source_adapter_luid == config.adapter_luid &&
            opened.processor_adapter_luid == config.adapter_luid &&
            opened.encoder_adapter_luid == 0;
    return opened.actual_encoder_kind == config.encoder_kind &&
        opened.codec_name == expected.codec_name &&
        opened.hardware_accelerated == expected.hardware_accelerated &&
        adapters_match &&
        opened.opened_input_format == expected.input_format &&
        opened.width == config.width && opened.height == config.height &&
        opened.fps_numerator == config.fps_numerator &&
        opened.fps_denominator == config.fps_denominator &&
        opened.maximum_b_frame_count == 0 &&
        !opened.driver_identity.empty() &&
        !opened.ffmpeg_build_identity.empty() &&
        !opened.device_identity.empty();
}

std::string_view ProfileName(H264Profile profile) noexcept
{
    switch (profile) {
    case H264Profile::Main:
        return "main";
    case H264Profile::High:
        return "high";
    }
    return {};
}

}

vrrec_status_t BuildVerifiedEncoderProbeEvidence(
    const vrrec_encoder_probe_config_v1 &config,
    const EncoderProbeOpenedIdentity &opened,
    std::span<const EncodedMediaPacket> annex_b_packets,
    EncoderProbeDecodePort &decoder,
    EncoderProbeEvidence &evidence) noexcept
{
    try {
        const auto expected =
            FindExpectedEncoderProbeIdentity(config.encoder_kind);
        const auto profile_name = ProfileName(opened.profile);
        H264VideoEncoderConfig encoder_config;
        if (config.struct_size < sizeof(vrrec_encoder_probe_config_v1) ||
            config.abi_version != VRREC_ABI_V1 ||
            config.synthetic_frame_count != RequiredSyntheticFrameCount ||
            config.adapter_luid == 0 || config.fps_denominator != 1 ||
            config.gpu_identity_utf8 == nullptr ||
            config.gpu_identity_utf8[0] == '\0' || config.reserved != 0 ||
            !expected || profile_name.empty() ||
            CreateH264VideoEncoderConfig(
                config.width,
                config.height,
                config.fps_numerator,
                opened.profile == H264Profile::High,
                encoder_config) != VRREC_STATUS_OK ||
            !HasRequiredOpenedIdentity(config, opened, *expected) ||
            annex_b_packets.empty()) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }

        H264PacketNormalizer normalizer(encoder_config);
        std::vector<EncodedMediaPacket> normalized_packets;
        normalized_packets.reserve(annex_b_packets.size());
        for (const auto &packet : annex_b_packets) {
            auto normalized = normalizer.Normalize(packet);
            if (normalized.status != VRREC_STATUS_OK) {
                return VRREC_STATUS_INTERNAL_ERROR;
            }
            normalized_packets.push_back(std::move(normalized.packet));
        }

        const auto *descriptor = normalizer.Descriptor();
        if (descriptor == nullptr) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        const auto decoded = decoder.Decode(*descriptor, normalized_packets);
        if (decoded.status != VRREC_STATUS_OK) {
            return decoded.status;
        }
        if (decoded.width != config.width ||
            decoded.height != config.height ||
            decoded.decoded_frame_count != config.synthetic_frame_count ||
            decoded.presentation_start_microseconds != 0) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }

        const auto validation_flags =
            VRREC_ENCODER_PROBE_VALIDATION_NONEMPTY_PACKET |
            VRREC_ENCODER_PROBE_VALIDATION_PARSEABLE_ACCESS_UNIT |
            VRREC_ENCODER_PROBE_VALIDATION_SPS |
            VRREC_ENCODER_PROBE_VALIDATION_PPS |
            VRREC_ENCODER_PROBE_VALIDATION_IDR |
            VRREC_ENCODER_PROBE_VALIDATION_DISPLAY_DIMENSIONS |
            VRREC_ENCODER_PROBE_VALIDATION_PROFILE |
            VRREC_ENCODER_PROBE_VALIDATION_FRAME_RATE |
            VRREC_ENCODER_PROBE_VALIDATION_ZERO_B_FRAMES |
            VRREC_ENCODER_PROBE_VALIDATION_DECODED |
            (opened.hardware_accelerated
                 ? VRREC_ENCODER_PROBE_VALIDATION_SAME_ADAPTER
                 : 0U);
        EncoderProbeEvidence verified {
            opened.actual_encoder_kind,
            opened.hardware_accelerated,
            config.adapter_luid,
            opened.opened_input_format,
            config.width,
            config.height,
            config.fps_numerator,
            config.fps_denominator,
            validation_flags,
            opened.codec_name,
            opened.driver_identity,
            opened.ffmpeg_build_identity,
            std::string(profile_name),
            opened.device_identity,
        };
        evidence = std::move(verified);
        return VRREC_STATUS_OK;
    } catch (const std::bad_alloc &) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

}
