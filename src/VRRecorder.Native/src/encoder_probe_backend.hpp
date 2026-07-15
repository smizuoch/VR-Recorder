#ifndef VRRECORDER_NATIVE_ENCODER_PROBE_BACKEND_HPP
#define VRRECORDER_NATIVE_ENCODER_PROBE_BACKEND_HPP

#include <memory>
#include <string>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

struct EncoderProbeEvidence final {
    vrrec_encoder_kind_t actual_encoder_kind = 0;
    bool hardware_accelerated = false;
    std::uint64_t adapter_luid = 0;
    vrrec_encoder_input_format_t opened_input_format = 0;
    std::uint32_t width = 0;
    std::uint32_t height = 0;
    std::uint32_t fps_numerator = 0;
    std::uint32_t fps_denominator = 0;
    std::uint32_t validation_flags = 0;
    std::string codec_name;
    std::string driver_identity;
    std::string ffmpeg_build_identity;
    std::string profile;
    std::string device_identity;
};

class EncoderProbeBackend {
public:
    virtual ~EncoderProbeBackend() = default;

    virtual vrrec_status_t Probe(
        const vrrec_encoder_probe_config_v1 &config,
        bool &packet_produced) noexcept = 0;

    virtual vrrec_status_t ProbeV2(
        const vrrec_encoder_probe_config_v1 &config,
        EncoderProbeEvidence &evidence) = 0;
};

std::unique_ptr<EncoderProbeBackend> CreateEncoderProbeBackend(
    vrrec_status_t &status);

}

#endif
