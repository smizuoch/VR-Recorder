#ifndef VRRECORDER_NATIVE_HARDWARE_ENCODER_PROBE_SESSION_FACTORY_HPP
#define VRRECORDER_NATIVE_HARDWARE_ENCODER_PROBE_SESSION_FACTORY_HPP

#include "encoder_probe_pipeline.hpp"
#include "windows_software_encoder_probe_adapter_identity_lookup.hpp"

namespace vrrecorder::native {

class HardwareEncoderProbeSessionFactory final
    : public EncoderProbeEncodeSessionFactoryPort {
public:
    explicit HardwareEncoderProbeSessionFactory(
        SoftwareEncoderProbeAdapterIdentityLookupPort &identity_lookup)
        noexcept;

    EncoderProbeEncodeSessionCreateResult Create(
        const vrrec_encoder_probe_config_v1 &config) noexcept override;

private:
    SoftwareEncoderProbeAdapterIdentityLookupPort &identity_lookup_;
};

}

#endif
