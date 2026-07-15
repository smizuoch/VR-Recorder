#include "software_encoder_probe_session_factory.hpp"

#include <new>
#include <string_view>
#include <utility>

#include "encoder_probe_identity.hpp"
#include "ffmpeg_system_memory_encoder_probe_session.hpp"

extern "C" {
#include <libavutil/frame.h>
}

namespace vrrecorder::native {
namespace {

bool IsRequestPayloadValid(
    const vrrec_encoder_probe_config_v1 &config) noexcept
{
    return config.encoder_kind == VRREC_ENCODER_MEDIA_FOUNDATION_SOFTWARE &&
        config.synthetic_frame_count == EncoderProbeSyntheticFrameCount &&
        config.adapter_luid != 0 && config.fps_denominator == 1 &&
        config.gpu_identity_utf8 != nullptr &&
        config.gpu_identity_utf8[0] != '\0' && config.reserved == 0;
}

bool IsPlatformIdentityValid(
    const vrrec_encoder_probe_config_v1 &config,
    const SoftwareEncoderProbePlatformIdentity &identity) noexcept
{
    return identity.source_adapter_luid == config.adapter_luid &&
        identity.processor_adapter_luid == config.adapter_luid &&
        identity.gpu_identity == std::string_view(config.gpu_identity_utf8) &&
        !identity.driver_identity.empty() &&
        !identity.device_identity.empty();
}

bool IsConfigEqual(
    const H264VideoEncoderConfig &actual,
    const H264VideoEncoderConfig &expected) noexcept
{
    return actual.width == expected.width &&
        actual.height == expected.height &&
        actual.frames_per_second == expected.frames_per_second &&
        actual.gop_frame_count == expected.gop_frame_count &&
        actual.target_bitrate_bits_per_second ==
            expected.target_bitrate_bits_per_second &&
        actual.maximum_bitrate_bits_per_second ==
            expected.maximum_bitrate_bits_per_second &&
        actual.input_pixel_format == expected.input_pixel_format &&
        actual.profile == expected.profile &&
        actual.rate_control == expected.rate_control &&
        actual.maximum_b_frame_count == expected.maximum_b_frame_count;
}

bool IsCodecIdentityValid(
    const FfmpegH264SoftwareOpenedIdentity &identity,
    const H264VideoEncoderConfig &expected) noexcept
{
    return identity.codec_name == "h264_mf" &&
        !identity.hardware_accelerated &&
        identity.opened_input_format ==
            VRREC_ENCODER_INPUT_SYSTEM_MEMORY_NV12 &&
        IsConfigEqual(identity.opened_config, expected) &&
        !identity.ffmpeg_build_identity.empty();
}

EncoderProbeEncodeSessionCreateResult RejectOpenedCodec(
    FfmpegH264SoftwareCodecSessionCreateResult &opened,
    vrrec_status_t status) noexcept
{
    if (opened.session != nullptr) {
        opened.session->Abort();
        opened.session.reset();
    }
    av_frame_free(&opened.owned_frame);
    return {status, nullptr};
}

}

SoftwareEncoderProbeSessionFactory::SoftwareEncoderProbeSessionFactory(
    SoftwareEncoderProbePlatformIdentityPort &platform_identity,
    FfmpegH264SoftwareCodecSessionFactoryPort &codec_factory) noexcept
    : platform_identity_(platform_identity),
      codec_factory_(codec_factory)
{
}

EncoderProbeEncodeSessionCreateResult
SoftwareEncoderProbeSessionFactory::Create(
    const vrrec_encoder_probe_config_v1 &config) noexcept
{
    if (config.struct_size < sizeof(vrrec_encoder_probe_config_v1)) {
        return {VRREC_STATUS_INVALID_ARGUMENT, nullptr};
    }
    if (config.abi_version != VRREC_ABI_V1) {
        return {VRREC_STATUS_UNSUPPORTED_ABI, nullptr};
    }
    if (!IsRequestPayloadValid(config)) {
        return {VRREC_STATUS_INVALID_ARGUMENT, nullptr};
    }

    H264VideoEncoderConfig encoder_config;
    const auto config_status = CreateH264VideoEncoderConfig(
        config.width,
        config.height,
        config.fps_numerator,
        true,
        encoder_config);
    if (config_status != VRREC_STATUS_OK) {
        return {config_status, nullptr};
    }

    auto platform = platform_identity_.Resolve(config);
    if (platform.status != VRREC_STATUS_OK) {
        return {platform.status, nullptr};
    }
    if (!IsPlatformIdentityValid(config, platform.identity)) {
        return {VRREC_STATUS_BACKEND_UNAVAILABLE, nullptr};
    }

    auto opened = codec_factory_.Create(encoder_config);
    if (opened.status != VRREC_STATUS_OK) {
        return RejectOpenedCodec(opened, opened.status);
    }
    if (opened.session == nullptr || opened.owned_frame == nullptr ||
        !IsCodecIdentityValid(opened.opened_identity, encoder_config)) {
        return RejectOpenedCodec(opened, VRREC_STATUS_BACKEND_UNAVAILABLE);
    }

    try {
        EncoderProbeOpenedIdentity identity {
            VRREC_ENCODER_MEDIA_FOUNDATION_SOFTWARE,
            opened.opened_identity.codec_name,
            false,
            platform.identity.source_adapter_luid,
            platform.identity.processor_adapter_luid,
            0,
            opened.opened_identity.opened_input_format,
            opened.opened_identity.opened_config.width,
            opened.opened_identity.opened_config.height,
            opened.opened_identity.opened_config.frames_per_second,
            1,
            opened.opened_identity.opened_config.profile,
            opened.opened_identity.opened_config.maximum_b_frame_count,
            platform.identity.driver_identity,
            opened.opened_identity.ffmpeg_build_identity,
            platform.identity.device_identity,
        };
        return CreateFfmpegSystemMemoryEncoderProbeSession(
            std::move(identity),
            std::move(opened.session),
            std::exchange(opened.owned_frame, nullptr));
    } catch (const std::bad_alloc &) {
        return RejectOpenedCodec(opened, VRREC_STATUS_OUT_OF_MEMORY);
    } catch (...) {
        return RejectOpenedCodec(opened, VRREC_STATUS_INTERNAL_ERROR);
    }
}

}
