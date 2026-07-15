#ifndef VRRECORDER_NATIVE_WINDOWS_SOFTWARE_ENCODER_PROBE_ADAPTER_IDENTITY_LOOKUP_HPP
#define VRRECORDER_NATIVE_WINDOWS_SOFTWARE_ENCODER_PROBE_ADAPTER_IDENTITY_LOOKUP_HPP

#include "software_encoder_probe_platform_identity.hpp"

namespace vrrecorder::native {

class WindowsSoftwareEncoderProbeAdapterIdentityLookup final
    : public SoftwareEncoderProbeAdapterIdentityLookupPort {
public:
    SoftwareEncoderProbeAdapterIdentityResult Lookup(
        std::uint64_t adapter_luid) noexcept override;
};

}

#endif
