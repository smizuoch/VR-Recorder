#include "h264_packet_normalizer.hpp"

#include <cstdint>
#include <limits>
#include <utility>

#include "h264_bitstream_converter.hpp"

namespace vrrecorder::native {

H264PacketNormalizer::H264PacketNormalizer(
    H264VideoEncoderConfig config) noexcept
    : config_(config)
{
}

H264PacketNormalizationResult H264PacketNormalizer::Normalize(
    const EncodedMediaPacket &packet) noexcept
{
    if (state_ != State::Active) {
        return {VRREC_STATUS_INVALID_STATE, false, {}};
    }
    if (!IsH264VideoEncoderConfigValid(config_) ||
        packet.stream != MediaStreamKind::Video ||
        packet.payload.empty() || !packet.side_data.empty() ||
        packet.pts_microseconds < 0 || packet.dts_microseconds < 0 ||
        packet.pts_microseconds < packet.dts_microseconds ||
        packet.duration_microseconds <= 0 ||
        packet.pts_microseconds >
            std::numeric_limits<std::int64_t>::max() -
                packet.duration_microseconds ||
        packet.dts_microseconds >
            std::numeric_limits<std::int64_t>::max() -
                packet.duration_microseconds) {
        return Fail(VRREC_STATUS_INVALID_ARGUMENT);
    }

    H264AnnexBConversionResult conversion {};
    const auto conversion_status = ConvertH264AnnexBToAvcc(
        packet.payload,
        config_.width,
        config_.height,
        config_.profile,
        descriptor_.has_value(),
        conversion);
    if (conversion_status != VRREC_STATUS_OK) {
        return Fail(conversion_status);
    }
    if (packet.key_frame != conversion.key_frame) {
        return Fail(VRREC_STATUS_INVALID_ARGUMENT);
    }

    bool descriptor_became_ready = false;
    if (!descriptor_.has_value()) {
        if (!conversion.key_frame || conversion.avcc.empty()) {
            return Fail(VRREC_STATUS_INVALID_ARGUMENT);
        }
        descriptor_became_ready = true;
        descriptor_.emplace(H264StreamDescriptor {
            MicrosecondPacketTimeBase,
            config_.width,
            config_.height,
            config_.profile,
            H264PacketFormat::AvccLengthPrefixed,
            std::move(conversion.avcc),
        });
    } else if (!conversion.avcc.empty() &&
        conversion.avcc != descriptor_->codec_extradata) {
        return Fail(VRREC_STATUS_INVALID_ARGUMENT);
    }

    EncodedMediaPacket normalized {
        MediaStreamKind::Video,
        packet.pts_microseconds,
        packet.dts_microseconds,
        packet.duration_microseconds,
        conversion.key_frame,
        std::move(conversion.access_unit),
        {},
    };
    return {
        VRREC_STATUS_OK,
        descriptor_became_ready,
        std::move(normalized),
    };
}

const H264StreamDescriptor *H264PacketNormalizer::Descriptor() const noexcept
{
    return descriptor_.has_value() ? &*descriptor_ : nullptr;
}

void H264PacketNormalizer::Abort() noexcept
{
    state_ = State::Aborted;
}

H264PacketNormalizationResult H264PacketNormalizer::Fail(
    vrrec_status_t status) noexcept
{
    state_ = State::Failed;
    return {status, false, {}};
}

}
