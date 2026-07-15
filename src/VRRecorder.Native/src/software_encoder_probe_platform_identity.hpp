#ifndef VRRECORDER_NATIVE_SOFTWARE_ENCODER_PROBE_PLATFORM_IDENTITY_HPP
#define VRRECORDER_NATIVE_SOFTWARE_ENCODER_PROBE_PLATFORM_IDENTITY_HPP

#include <cstdint>
#include <string>

#include "encoder_probe_identity.hpp"
#include "software_encoder_probe_session_factory.hpp"

namespace vrrecorder::native {

struct SoftwareEncoderProbeAdapterIdentity final {
    std::uint64_t adapter_luid = 0;
    std::string gpu_identity;
    std::string driver_identity;
    std::string device_identity;
};

struct SoftwareEncoderProbeAdapterIdentityResult final {
    vrrec_status_t status = VRREC_STATUS_INTERNAL_ERROR;
    SoftwareEncoderProbeAdapterIdentity identity;
};

class SoftwareEncoderProbeAdapterIdentityLookupPort {
public:
    virtual ~SoftwareEncoderProbeAdapterIdentityLookupPort() = default;

    virtual SoftwareEncoderProbeAdapterIdentityResult Lookup(
        std::uint64_t adapter_luid) noexcept = 0;
};

class SoftwareEncoderProbePlatformIdentityResolver final
    : public SoftwareEncoderProbePlatformIdentityPort {
public:
    explicit SoftwareEncoderProbePlatformIdentityResolver(
        SoftwareEncoderProbeAdapterIdentityLookupPort &lookup) noexcept;

    SoftwareEncoderProbePlatformIdentityResult Resolve(
        const vrrec_encoder_probe_config_v1 &config) noexcept override;

private:
    SoftwareEncoderProbeAdapterIdentityLookupPort &lookup_;
};

}

#endif
