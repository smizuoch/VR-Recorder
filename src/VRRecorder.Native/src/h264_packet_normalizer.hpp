#ifndef VRRECORDER_NATIVE_H264_PACKET_NORMALIZER_HPP
#define VRRECORDER_NATIVE_H264_PACKET_NORMALIZER_HPP

#include <optional>
#include <span>

#include "fragmented_mp4_mux_coordinator.hpp"
#include "video_encoder_config.hpp"

namespace vrrecorder::native {

struct H264PacketNormalizationResult final {
    vrrec_status_t status = VRREC_STATUS_INTERNAL_ERROR;
    bool descriptor_became_ready = false;
    EncodedMediaPacket packet {
        MediaStreamKind::Video,
        0,
        0,
        0,
        false,
        {},
        {},
    };
};

class H264PacketNormalizer final {
public:
    explicit H264PacketNormalizer(
        H264VideoEncoderConfig config) noexcept;

    H264PacketNormalizationResult Normalize(
        const EncodedMediaPacket &packet) noexcept;
    vrrec_status_t InitializeFromAnnexBExtradata(
        std::span<const std::byte> extradata) noexcept;
    const H264StreamDescriptor *Descriptor() const noexcept;
    void Abort() noexcept;

private:
    enum class State {
        Active,
        Failed,
        Aborted,
    };

    H264PacketNormalizationResult Fail(vrrec_status_t status) noexcept;

    H264VideoEncoderConfig config_;
    std::optional<H264StreamDescriptor> descriptor_;
    State state_ = State::Active;
};

}

#endif
