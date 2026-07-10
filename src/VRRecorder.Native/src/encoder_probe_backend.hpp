#ifndef VRRECORDER_NATIVE_ENCODER_PROBE_BACKEND_HPP
#define VRRECORDER_NATIVE_ENCODER_PROBE_BACKEND_HPP

#include <memory>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

class EncoderProbeBackend {
public:
    virtual ~EncoderProbeBackend() = default;

    virtual vrrec_status_t Probe(
        const vrrec_encoder_probe_config_v1 &config,
        bool &packet_produced) noexcept = 0;
};

std::unique_ptr<EncoderProbeBackend> CreateEncoderProbeBackend(
    vrrec_status_t &status);

}

#endif
