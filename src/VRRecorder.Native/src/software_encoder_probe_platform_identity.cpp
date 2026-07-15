#include "software_encoder_probe_platform_identity.hpp"

#include <new>
#include <string_view>
#include <utility>

namespace vrrecorder::native {
namespace {

bool IsRequestValid(const vrrec_encoder_probe_config_v1 &config) noexcept
{
    return config.encoder_kind ==
            VRREC_ENCODER_MEDIA_FOUNDATION_SOFTWARE &&
        config.synthetic_frame_count == EncoderProbeSyntheticFrameCount &&
        config.adapter_luid != 0 && config.gpu_identity_utf8 != nullptr &&
        config.gpu_identity_utf8[0] != '\0' && config.reserved == 0;
}

bool IsIdentityValid(
    const vrrec_encoder_probe_config_v1 &config,
    const SoftwareEncoderProbeAdapterIdentity &identity) noexcept
{
    return identity.adapter_luid == config.adapter_luid &&
        identity.gpu_identity == std::string_view(config.gpu_identity_utf8) &&
        !identity.driver_identity.empty() && !identity.device_identity.empty();
}

}

SoftwareEncoderProbePlatformIdentityResolver::
    SoftwareEncoderProbePlatformIdentityResolver(
        SoftwareEncoderProbeAdapterIdentityLookupPort &lookup) noexcept
    : lookup_(lookup)
{
}

SoftwareEncoderProbePlatformIdentityResult
SoftwareEncoderProbePlatformIdentityResolver::Resolve(
    const vrrec_encoder_probe_config_v1 &config) noexcept
{
    if (config.struct_size < sizeof(vrrec_encoder_probe_config_v1)) {
        return {VRREC_STATUS_INVALID_ARGUMENT, {}};
    }
    if (config.abi_version != VRREC_ABI_V1) {
        return {VRREC_STATUS_UNSUPPORTED_ABI, {}};
    }
    if (!IsRequestValid(config)) {
        return {VRREC_STATUS_INVALID_ARGUMENT, {}};
    }

    auto found = lookup_.Lookup(config.adapter_luid);
    if (found.status != VRREC_STATUS_OK) {
        return {found.status, {}};
    }
    if (!IsIdentityValid(config, found.identity)) {
        return {VRREC_STATUS_BACKEND_UNAVAILABLE, {}};
    }

    try {
        return {
            VRREC_STATUS_OK,
            {
                found.identity.adapter_luid,
                found.identity.adapter_luid,
                std::move(found.identity.gpu_identity),
                std::move(found.identity.driver_identity),
                std::move(found.identity.device_identity),
            },
        };
    } catch (const std::bad_alloc &) {
        return {VRREC_STATUS_OUT_OF_MEMORY, {}};
    } catch (...) {
        return {VRREC_STATUS_INTERNAL_ERROR, {}};
    }
}

}
