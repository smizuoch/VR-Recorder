#ifndef VRRECORDER_NATIVE_ENCODER_PROBE_EVIDENCE_BUILDER_HPP
#define VRRECORDER_NATIVE_ENCODER_PROBE_EVIDENCE_BUILDER_HPP

#include <cstdint>
#include <span>
#include <string>

#include "encoder_probe_backend.hpp"
#include "fragmented_mp4_mux_coordinator.hpp"
#include "video_encoder_config.hpp"

namespace vrrecorder::native {

struct EncoderProbeOpenedIdentity final {
    vrrec_encoder_kind_t actual_encoder_kind = 0;
    std::string codec_name;
    bool hardware_accelerated = false;
    std::uint64_t source_adapter_luid = 0;
    std::uint64_t processor_adapter_luid = 0;
    std::uint64_t encoder_adapter_luid = 0;
    vrrec_encoder_input_format_t opened_input_format = 0;
    std::uint32_t width = 0;
    std::uint32_t height = 0;
    std::uint32_t fps_numerator = 0;
    std::uint32_t fps_denominator = 0;
    H264Profile profile = H264Profile::Main;
    std::uint32_t maximum_b_frame_count = 0;
    std::string driver_identity;
    std::string ffmpeg_build_identity;
    std::string device_identity;
};

struct EncoderProbeDecodeResult final {
    vrrec_status_t status = VRREC_STATUS_INTERNAL_ERROR;
    std::uint32_t width = 0;
    std::uint32_t height = 0;
    std::uint32_t decoded_frame_count = 0;
    std::int64_t presentation_start_microseconds = 0;
};

class EncoderProbeDecodePort {
public:
    virtual ~EncoderProbeDecodePort() = default;

    virtual EncoderProbeDecodeResult Decode(
        const H264StreamDescriptor &descriptor,
        std::span<const EncodedMediaPacket> packets) noexcept = 0;
};

vrrec_status_t BuildVerifiedEncoderProbeEvidence(
    const vrrec_encoder_probe_config_v1 &config,
    const EncoderProbeOpenedIdentity &opened,
    std::span<const EncodedMediaPacket> annex_b_packets,
    EncoderProbeDecodePort &decoder,
    EncoderProbeEvidence &evidence) noexcept;

}

#endif
