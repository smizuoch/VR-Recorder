#ifndef VRRECORDER_NATIVE_ANNEX_B_ENCODER_PROBE_DECODER_HPP
#define VRRECORDER_NATIVE_ANNEX_B_ENCODER_PROBE_DECODER_HPP

#include <cstddef>
#include <cstdint>
#include <span>

#include "encoder_probe_evidence_builder.hpp"

namespace vrrecorder::native {

class AnnexBH264DecodePort {
public:
    virtual ~AnnexBH264DecodePort() = default;

    virtual vrrec_status_t Begin(
        std::uint32_t width,
        std::uint32_t height) noexcept = 0;
    virtual vrrec_status_t Submit(
        std::span<const std::byte> access_unit,
        std::int64_t pts_microseconds,
        std::int64_t duration_microseconds) noexcept = 0;
    virtual EncoderProbeDecodeResult Finish() noexcept = 0;
    virtual void Abort() noexcept = 0;
};

class AnnexBEncoderProbeDecoder final : public EncoderProbeDecodePort {
public:
    explicit AnnexBEncoderProbeDecoder(AnnexBH264DecodePort &port) noexcept;

    EncoderProbeDecodeResult Decode(
        const H264StreamDescriptor &descriptor,
        std::span<const EncodedMediaPacket> packets) noexcept override;

private:
    AnnexBH264DecodePort &port_;
};

}

#endif
