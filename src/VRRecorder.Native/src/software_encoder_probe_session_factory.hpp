#ifndef VRRECORDER_NATIVE_SOFTWARE_ENCODER_PROBE_SESSION_FACTORY_HPP
#define VRRECORDER_NATIVE_SOFTWARE_ENCODER_PROBE_SESSION_FACTORY_HPP

#include <cstdint>
#include <string>

#include "encoder_probe_pipeline.hpp"
#include "ffmpeg_h264_software_codec_session.hpp"

namespace vrrecorder::native {

struct SoftwareEncoderProbePlatformIdentity final {
    std::uint64_t source_adapter_luid = 0;
    std::uint64_t processor_adapter_luid = 0;
    std::string gpu_identity;
    std::string driver_identity;
    std::string device_identity;
};

struct SoftwareEncoderProbePlatformIdentityResult final {
    vrrec_status_t status = VRREC_STATUS_INTERNAL_ERROR;
    SoftwareEncoderProbePlatformIdentity identity;
};

class SoftwareEncoderProbePlatformIdentityPort {
public:
    virtual ~SoftwareEncoderProbePlatformIdentityPort() = default;

    virtual SoftwareEncoderProbePlatformIdentityResult Resolve(
        const vrrec_encoder_probe_config_v1 &config) noexcept = 0;
};

class SoftwareEncoderProbeSessionFactory final
    : public EncoderProbeEncodeSessionFactoryPort {
public:
    SoftwareEncoderProbeSessionFactory(
        SoftwareEncoderProbePlatformIdentityPort &platform_identity,
        FfmpegH264SoftwareCodecSessionFactoryPort &codec_factory) noexcept;

    EncoderProbeEncodeSessionCreateResult Create(
        const vrrec_encoder_probe_config_v1 &config) noexcept override;

private:
    SoftwareEncoderProbePlatformIdentityPort &platform_identity_;
    FfmpegH264SoftwareCodecSessionFactoryPort &codec_factory_;
};

}

#endif
