#include "encoder_probe_backend.hpp"

#if !defined(_WIN32)
#error "The production encoder probe backend requires Windows"
#endif

#include <memory>
#include <new>

#include "annex_b_encoder_probe_decoder.hpp"
#include "encoder_probe_pipeline.hpp"
#include "ffmpeg_h264_software_codec_session.hpp"
#include "software_encoder_probe_platform_identity.hpp"
#include "software_encoder_probe_session_factory.hpp"
#include "windows_media_foundation_h264_decode_port.hpp"
#include "windows_software_encoder_probe_adapter_identity_lookup.hpp"

namespace vrrecorder::native {
namespace {

bool IsKnownHardwareEncoder(vrrec_encoder_kind_t kind) noexcept
{
    return kind == VRREC_ENCODER_NVENC || kind == VRREC_ENCODER_AMF ||
        kind == VRREC_ENCODER_QSV;
}

class ProductionEncoderProbeBackend final : public EncoderProbeBackend {
public:
    ProductionEncoderProbeBackend() noexcept
        : platform_identity_(adapter_identity_lookup_),
          session_factory_(platform_identity_, codec_factory_),
          decoder_(media_foundation_decoder_),
          verified_(session_factory_, decoder_)
    {
    }

    vrrec_status_t Probe(
        const vrrec_encoder_probe_config_v1 &config,
        bool &packet_produced) noexcept override
    {
        packet_produced = false;
        if (IsKnownHardwareEncoder(config.encoder_kind)) {
            return VRREC_STATUS_BACKEND_UNAVAILABLE;
        }
        return verified_.Probe(config, packet_produced);
    }

    vrrec_status_t ProbeV2(
        const vrrec_encoder_probe_config_v1 &config,
        EncoderProbeEvidence &evidence) override
    {
        if (IsKnownHardwareEncoder(config.encoder_kind)) {
            return VRREC_STATUS_BACKEND_UNAVAILABLE;
        }
        return verified_.ProbeV2(config, evidence);
    }

private:
    WindowsSoftwareEncoderProbeAdapterIdentityLookup
        adapter_identity_lookup_;
    SoftwareEncoderProbePlatformIdentityResolver platform_identity_;
    LibavcodecH264SoftwareCodecSessionFactory codec_factory_;
    SoftwareEncoderProbeSessionFactory session_factory_;
    WindowsMediaFoundationH264DecodePort media_foundation_decoder_;
    AnnexBEncoderProbeDecoder decoder_;
    VerifiedEncoderProbeBackend verified_;
};

}

std::unique_ptr<EncoderProbeBackend> CreateEncoderProbeBackend(
    vrrec_status_t &status)
{
    std::unique_ptr<EncoderProbeBackend> backend(
        new (std::nothrow) ProductionEncoderProbeBackend());
    status = backend == nullptr
        ? VRREC_STATUS_OUT_OF_MEMORY
        : VRREC_STATUS_OK;
    return backend;
}

}
