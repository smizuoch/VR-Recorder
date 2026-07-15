#ifndef VRRECORDER_NATIVE_ENCODER_PROBE_PIPELINE_HPP
#define VRRECORDER_NATIVE_ENCODER_PROBE_PIPELINE_HPP

#include <cstdint>
#include <memory>

#include "encoder_probe_evidence_builder.hpp"
#include "ffmpeg_encoder_state_machine.hpp"

namespace vrrecorder::native {

struct EncoderProbeSyntheticFrame final {
    std::uint32_t frame_index = 0;
    std::uint32_t width = 0;
    std::uint32_t height = 0;
    std::int64_t pts_microseconds = 0;
    std::int64_t duration_microseconds = 0;
};

class EncoderProbeEncodeSession {
public:
    virtual ~EncoderProbeEncodeSession() = default;

    virtual const EncoderProbeOpenedIdentity &OpenedIdentity()
        const noexcept = 0;
    virtual FfmpegEncodeBatch EncodeSyntheticFrame(
        const EncoderProbeSyntheticFrame &frame) noexcept = 0;
    virtual FfmpegEncodeBatch Finish() noexcept = 0;
    virtual void Abort() noexcept = 0;
};

struct EncoderProbeEncodeSessionCreateResult final {
    vrrec_status_t status = VRREC_STATUS_INTERNAL_ERROR;
    std::unique_ptr<EncoderProbeEncodeSession> session;
};

class EncoderProbeEncodeSessionFactoryPort {
public:
    virtual ~EncoderProbeEncodeSessionFactoryPort() = default;

    virtual EncoderProbeEncodeSessionCreateResult Create(
        const vrrec_encoder_probe_config_v1 &config) noexcept = 0;
};

vrrec_status_t RunVerifiedEncoderProbe(
    const vrrec_encoder_probe_config_v1 &config,
    EncoderProbeEncodeSessionFactoryPort &factory,
    EncoderProbeDecodePort &decoder,
    EncoderProbeEvidence &evidence) noexcept;

}

#endif
