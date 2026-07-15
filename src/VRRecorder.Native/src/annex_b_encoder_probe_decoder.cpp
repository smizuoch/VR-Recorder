#include "annex_b_encoder_probe_decoder.hpp"

#include <cstddef>
#include <cstdint>
#include <limits>
#include <new>
#include <span>
#include <vector>

#include "h264_avcc_to_annex_b.hpp"

namespace vrrecorder::native {
namespace {

constexpr std::uint32_t MaximumDimension = 16'384;

EncoderProbeDecodeResult Failure(vrrec_status_t status) noexcept
{
    return {status, 0, 0, 0, 0};
}

bool IsDescriptorValid(const H264StreamDescriptor &descriptor) noexcept
{
    const auto profile_valid = descriptor.profile == H264Profile::Main ||
        descriptor.profile == H264Profile::High;
    return descriptor.packet_time_base == MicrosecondPacketTimeBase &&
        descriptor.width != 0 && descriptor.width <= MaximumDimension &&
        (descriptor.width & 1U) == 0 && descriptor.height != 0 &&
        descriptor.height <= MaximumDimension &&
        (descriptor.height & 1U) == 0 && profile_valid &&
        descriptor.packet_format == H264PacketFormat::AvccLengthPrefixed &&
        !descriptor.codec_extradata.empty();
}

bool IsPacketContractValid(const EncodedMediaPacket &packet) noexcept
{
    return packet.stream == MediaStreamKind::Video &&
        packet.pts_microseconds != UnknownMediaTimestamp &&
        packet.dts_microseconds != UnknownMediaTimestamp &&
        packet.pts_microseconds >= packet.dts_microseconds &&
        packet.duration_microseconds > 0 && !packet.payload.empty() &&
        packet.side_data.empty();
}

}

AnnexBEncoderProbeDecoder::AnnexBEncoderProbeDecoder(
    AnnexBH264DecodePort &port) noexcept
    : port_(port)
{
}

EncoderProbeDecodeResult AnnexBEncoderProbeDecoder::Decode(
    const H264StreamDescriptor &descriptor,
    std::span<const EncodedMediaPacket> packets) noexcept
{
    if (!IsDescriptorValid(descriptor) || packets.empty()) {
        return Failure(VRREC_STATUS_INVALID_ARGUMENT);
    }
    for (const auto &packet : packets) {
        if (!IsPacketContractValid(packet)) {
            return Failure(VRREC_STATUS_INVALID_ARGUMENT);
        }
    }

    std::vector<std::byte> parameter_sets;
    const auto descriptor_status = ConvertH264AvccDescriptorToAnnexB(
        descriptor.codec_extradata,
        parameter_sets);
    if (descriptor_status != VRREC_STATUS_OK) {
        return Failure(descriptor_status);
    }

    auto status = port_.Begin(descriptor.width, descriptor.height);
    if (status != VRREC_STATUS_OK) {
        port_.Abort();
        return Failure(status);
    }

    try {
        bool first = true;
        for (const auto &packet : packets) {
            std::vector<std::byte> access_unit;
            status = ConvertH264AvccAccessUnitToAnnexB(
                packet.payload,
                access_unit);
            if (status != VRREC_STATUS_OK) {
                port_.Abort();
                return Failure(status);
            }
            if (first) {
                if (parameter_sets.size() >
                    std::numeric_limits<std::size_t>::max() -
                        access_unit.size()) {
                    port_.Abort();
                    return Failure(VRREC_STATUS_OUT_OF_MEMORY);
                }
                std::vector<std::byte> prefixed;
                prefixed.reserve(parameter_sets.size() + access_unit.size());
                prefixed.insert(
                    prefixed.end(),
                    parameter_sets.begin(),
                    parameter_sets.end());
                prefixed.insert(
                    prefixed.end(),
                    access_unit.begin(),
                    access_unit.end());
                access_unit.swap(prefixed);
                first = false;
            }
            status = port_.Submit(
                access_unit,
                packet.pts_microseconds,
                packet.duration_microseconds);
            if (status != VRREC_STATUS_OK) {
                port_.Abort();
                return Failure(status);
            }
        }

        auto result = port_.Finish();
        if (result.status != VRREC_STATUS_OK) {
            port_.Abort();
        }
        return result;
    } catch (const std::bad_alloc &) {
        port_.Abort();
        return Failure(VRREC_STATUS_OUT_OF_MEMORY);
    } catch (...) {
        port_.Abort();
        return Failure(VRREC_STATUS_INTERNAL_ERROR);
    }
}

}
