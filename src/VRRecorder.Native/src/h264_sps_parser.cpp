#include "h264_sps_parser.hpp"

#include <cstddef>
#include <cstdint>
#include <limits>
#include <new>
#include <span>
#include <vector>

namespace vrrecorder::native {
namespace {

class BitReader final {
public:
    explicit BitReader(std::span<const std::byte> bytes) : bytes_(bytes) {}

    bool ReadBit(std::uint32_t &value)
    {
        if (bit_offset_ >= bytes_.size() * 8U) {
            return false;
        }
        const auto byte = std::to_integer<std::uint8_t>(
            bytes_[bit_offset_ / 8U]);
        const auto shift = 7U - (bit_offset_ % 8U);
        value = (static_cast<std::uint32_t>(byte) >> shift) & 1U;
        ++bit_offset_;
        return true;
    }

    bool ReadBits(std::uint32_t count, std::uint32_t &value)
    {
        if (count > 32U) {
            return false;
        }
        value = 0;
        for (std::uint32_t index = 0; index < count; ++index) {
            std::uint32_t bit = 0;
            if (!ReadBit(bit)) {
                return false;
            }
            value = (value << 1U) | bit;
        }
        return true;
    }

    bool ReadUnsignedExpGolomb(std::uint32_t &value)
    {
        std::uint32_t leading_zero_count = 0;
        std::uint32_t bit = 0;
        do {
            if (!ReadBit(bit)) {
                return false;
            }
            if (bit == 0) {
                ++leading_zero_count;
                if (leading_zero_count > 32U) {
                    return false;
                }
            }
        } while (bit == 0);

        std::uint32_t suffix = 0;
        if (!ReadBits(leading_zero_count, suffix)) {
            return false;
        }
        const auto decoded =
            ((std::uint64_t {1} << leading_zero_count) - 1U) + suffix;
        if (decoded > std::numeric_limits<std::uint32_t>::max()) {
            return false;
        }
        value = static_cast<std::uint32_t>(decoded);
        return true;
    }

    bool ReadSignedExpGolomb(std::int32_t &value)
    {
        std::uint32_t encoded = 0;
        if (!ReadUnsignedExpGolomb(encoded)) {
            return false;
        }
        const auto magnitude =
            (static_cast<std::uint64_t>(encoded) + 1U) / 2U;
        const auto signed_value = encoded % 2U == 0
                                      ? -static_cast<std::int64_t>(magnitude)
                                      : static_cast<std::int64_t>(magnitude);
        if (signed_value < std::numeric_limits<std::int32_t>::min() ||
            signed_value > std::numeric_limits<std::int32_t>::max()) {
            return false;
        }
        value = static_cast<std::int32_t>(signed_value);
        return true;
    }

    bool ConsumeRbspTrailingBits()
    {
        std::uint32_t bit = 0;
        if (!ReadBit(bit) || bit != 1U) {
            return false;
        }
        while (bit_offset_ < bytes_.size() * 8U) {
            if (!ReadBit(bit) || bit != 0U) {
                return false;
            }
        }
        return true;
    }

    [[nodiscard]] bool HasRemainingBits() const
    {
        return bit_offset_ < bytes_.size() * 8U;
    }

private:
    std::span<const std::byte> bytes_;
    std::size_t bit_offset_ = 0;
};

bool HasExtendedProfileSyntax(std::uint32_t profile_idc)
{
    switch (profile_idc) {
    case 44:
    case 83:
    case 86:
    case 100:
    case 110:
    case 118:
    case 122:
    case 128:
    case 134:
    case 135:
    case 138:
    case 139:
    case 144:
    case 244:
        return true;
    default:
        return false;
    }
}

bool RemoveEmulationPreventionBytes(
    std::span<const std::byte> ebsp,
    std::vector<std::byte> &rbsp)
{
    rbsp.clear();
    rbsp.reserve(ebsp.size());
    std::uint32_t zero_count = 0;
    for (std::size_t index = 0; index < ebsp.size(); ++index) {
        const auto value = std::to_integer<std::uint8_t>(ebsp[index]);
        if (zero_count >= 2U && value == 3U) {
            if (index + 1U >= ebsp.size() ||
                std::to_integer<std::uint8_t>(ebsp[index + 1U]) > 3U) {
                return false;
            }
            zero_count = 0;
            continue;
        }
        if (zero_count >= 2U && value <= 2U) {
            return false;
        }
        rbsp.push_back(ebsp[index]);
        zero_count = value == 0 ? zero_count + 1U : 0U;
    }
    return !rbsp.empty();
}

bool SkipScalingList(BitReader &reader, std::uint32_t size)
{
    std::int32_t last_scale = 8;
    std::int32_t next_scale = 8;
    for (std::uint32_t index = 0; index < size; ++index) {
        if (next_scale != 0) {
            std::int32_t delta_scale = 0;
            if (!reader.ReadSignedExpGolomb(delta_scale)) {
                return false;
            }
            const auto sum = static_cast<std::int64_t>(last_scale) +
                             static_cast<std::int64_t>(delta_scale) + 256;
            next_scale = static_cast<std::int32_t>(sum % 256);
            if (next_scale < 0) {
                next_scale += 256;
            }
        }
        last_scale = next_scale == 0 ? last_scale : next_scale;
    }
    return true;
}

bool ParseExtendedProfileSyntax(
    BitReader &reader,
    std::uint32_t &chroma_format_idc,
    bool &separate_colour_plane,
    std::uint32_t &bit_depth_luma,
    std::uint32_t &bit_depth_chroma)
{
    if (!reader.ReadUnsignedExpGolomb(chroma_format_idc) ||
        chroma_format_idc > 3U) {
        return false;
    }
    std::uint32_t bit = 0;
    if (chroma_format_idc == 3U) {
        if (!reader.ReadBit(bit)) {
            return false;
        }
        separate_colour_plane = bit != 0;
    }

    std::uint32_t bit_depth_luma_minus8 = 0;
    std::uint32_t bit_depth_chroma_minus8 = 0;
    if (!reader.ReadUnsignedExpGolomb(bit_depth_luma_minus8) ||
        !reader.ReadUnsignedExpGolomb(bit_depth_chroma_minus8) ||
        bit_depth_luma_minus8 > 6U || bit_depth_chroma_minus8 > 6U) {
        return false;
    }
    bit_depth_luma = bit_depth_luma_minus8 + 8U;
    bit_depth_chroma = bit_depth_chroma_minus8 + 8U;

    if (!reader.ReadBit(bit) || !reader.ReadBit(bit)) {
        return false;
    }
    if (bit == 0) {
        return true;
    }

    const auto scaling_list_count = chroma_format_idc == 3U ? 12U : 8U;
    for (std::uint32_t index = 0; index < scaling_list_count; ++index) {
        if (!reader.ReadBit(bit)) {
            return false;
        }
        if (bit != 0 && !SkipScalingList(reader, index < 6U ? 16U : 64U)) {
            return false;
        }
    }
    return true;
}

bool ParsePictureOrderCount(BitReader &reader)
{
    std::uint32_t pic_order_count_type = 0;
    if (!reader.ReadUnsignedExpGolomb(pic_order_count_type)) {
        return false;
    }
    if (pic_order_count_type == 0U) {
        std::uint32_t log2_max_pic_order_count_lsb_minus4 = 0;
        return reader.ReadUnsignedExpGolomb(
                   log2_max_pic_order_count_lsb_minus4) &&
               log2_max_pic_order_count_lsb_minus4 <= 12U;
    }
    if (pic_order_count_type == 2U) {
        return true;
    }
    if (pic_order_count_type != 1U) {
        return false;
    }

    std::uint32_t bit = 0;
    std::int32_t signed_value = 0;
    std::uint32_t cycle_length = 0;
    if (!reader.ReadBit(bit) ||
        !reader.ReadSignedExpGolomb(signed_value) ||
        !reader.ReadSignedExpGolomb(signed_value) ||
        !reader.ReadUnsignedExpGolomb(cycle_length) || cycle_length > 255U) {
        return false;
    }
    for (std::uint32_t index = 0; index < cycle_length; ++index) {
        if (!reader.ReadSignedExpGolomb(signed_value)) {
            return false;
        }
    }
    return true;
}

bool CalculateDisplayGeometry(
    std::uint32_t pic_width_in_mbs_minus1,
    std::uint32_t pic_height_in_map_units_minus1,
    bool frame_mbs_only,
    std::uint32_t chroma_format_idc,
    bool separate_colour_plane,
    std::uint32_t crop_left,
    std::uint32_t crop_right,
    std::uint32_t crop_top,
    std::uint32_t crop_bottom,
    std::uint32_t &width,
    std::uint32_t &height)
{
    const auto frame_height_factor = frame_mbs_only ? 1U : 2U;
    const auto coded_width =
        (static_cast<std::uint64_t>(pic_width_in_mbs_minus1) + 1U) * 16U;
    const auto coded_height =
        static_cast<std::uint64_t>(frame_height_factor) *
        (static_cast<std::uint64_t>(pic_height_in_map_units_minus1) + 1U) *
        16U;
    if (coded_width > std::numeric_limits<std::uint32_t>::max() ||
        coded_height > std::numeric_limits<std::uint32_t>::max()) {
        return false;
    }

    const auto chroma_array_type =
        separate_colour_plane ? 0U : chroma_format_idc;
    std::uint32_t sub_width = 1;
    std::uint32_t sub_height = 1;
    if (chroma_array_type == 1U) {
        sub_width = 2;
        sub_height = 2;
    } else if (chroma_array_type == 2U) {
        sub_width = 2;
    }
    const auto crop_unit_x = sub_width;
    const auto crop_unit_y = sub_height * frame_height_factor;
    const auto horizontal_crop =
        static_cast<std::uint64_t>(crop_unit_x) *
        (static_cast<std::uint64_t>(crop_left) + crop_right);
    const auto vertical_crop =
        static_cast<std::uint64_t>(crop_unit_y) *
        (static_cast<std::uint64_t>(crop_top) + crop_bottom);
    if (horizontal_crop >= coded_width || vertical_crop >= coded_height) {
        return false;
    }

    width = static_cast<std::uint32_t>(coded_width - horizontal_crop);
    height = static_cast<std::uint32_t>(coded_height - vertical_crop);
    return true;
}

bool ParseSpsRbsp(std::span<const std::byte> rbsp, H264SpsInfo &result)
{
    BitReader reader(rbsp);
    std::uint32_t profile_idc = 0;
    std::uint32_t compatibility = 0;
    std::uint32_t level_idc = 0;
    std::uint32_t sequence_parameter_set_id = 0;
    if (!reader.ReadBits(8, profile_idc) ||
        !reader.ReadBits(8, compatibility) ||
        (compatibility & 3U) != 0 || !reader.ReadBits(8, level_idc) ||
        !reader.ReadUnsignedExpGolomb(sequence_parameter_set_id) ||
        sequence_parameter_set_id > 31U) {
        return false;
    }

    std::uint32_t chroma_format_idc = 1;
    std::uint32_t bit_depth_luma = 8;
    std::uint32_t bit_depth_chroma = 8;
    bool separate_colour_plane = false;
    if (HasExtendedProfileSyntax(profile_idc) &&
        !ParseExtendedProfileSyntax(
            reader,
            chroma_format_idc,
            separate_colour_plane,
            bit_depth_luma,
            bit_depth_chroma)) {
        return false;
    }

    std::uint32_t log2_max_frame_num_minus4 = 0;
    if (!reader.ReadUnsignedExpGolomb(log2_max_frame_num_minus4) ||
        log2_max_frame_num_minus4 > 12U || !ParsePictureOrderCount(reader)) {
        return false;
    }

    std::uint32_t ignored = 0;
    std::uint32_t bit = 0;
    std::uint32_t pic_width_in_mbs_minus1 = 0;
    std::uint32_t pic_height_in_map_units_minus1 = 0;
    if (!reader.ReadUnsignedExpGolomb(ignored) || !reader.ReadBit(bit) ||
        !reader.ReadUnsignedExpGolomb(pic_width_in_mbs_minus1) ||
        !reader.ReadUnsignedExpGolomb(pic_height_in_map_units_minus1) ||
        !reader.ReadBit(bit)) {
        return false;
    }
    const bool frame_mbs_only = bit != 0;
    if (!frame_mbs_only && !reader.ReadBit(bit)) {
        return false;
    }
    if (!reader.ReadBit(bit) || !reader.ReadBit(bit)) {
        return false;
    }

    std::uint32_t crop_left = 0;
    std::uint32_t crop_right = 0;
    std::uint32_t crop_top = 0;
    std::uint32_t crop_bottom = 0;
    if (bit != 0 &&
        (!reader.ReadUnsignedExpGolomb(crop_left) ||
         !reader.ReadUnsignedExpGolomb(crop_right) ||
         !reader.ReadUnsignedExpGolomb(crop_top) ||
         !reader.ReadUnsignedExpGolomb(crop_bottom))) {
        return false;
    }

    std::uint32_t vui_parameters_present = 0;
    if (!reader.ReadBit(vui_parameters_present) ||
        (vui_parameters_present == 0 && !reader.ConsumeRbspTrailingBits()) ||
        (vui_parameters_present != 0 && !reader.HasRemainingBits())) {
        return false;
    }

    H264SpsInfo parsed {};
    if (!CalculateDisplayGeometry(
            pic_width_in_mbs_minus1,
            pic_height_in_map_units_minus1,
            frame_mbs_only,
            chroma_format_idc,
            separate_colour_plane,
            crop_left,
            crop_right,
            crop_top,
            crop_bottom,
            parsed.width,
            parsed.height)) {
        return false;
    }
    parsed.profile_idc = static_cast<std::uint8_t>(profile_idc);
    parsed.profile_compatibility = static_cast<std::uint8_t>(compatibility);
    parsed.level_idc = static_cast<std::uint8_t>(level_idc);
    parsed.chroma_format_idc = chroma_format_idc;
    parsed.bit_depth_luma = bit_depth_luma;
    parsed.bit_depth_chroma = bit_depth_chroma;
    parsed.frame_mbs_only = frame_mbs_only;
    result = parsed;
    return true;
}

}

vrrec_status_t ParseH264Sps(
    std::span<const std::byte> sps_nal,
    H264SpsInfo &result) noexcept
{
    result = {};
    if (sps_nal.size() < 2U) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }
    const auto nal_header = std::to_integer<std::uint8_t>(sps_nal.front());
    if ((nal_header & 0x80U) != 0 || (nal_header & 0x1fU) != 7U) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    try {
        std::vector<std::byte> rbsp;
        if (!RemoveEmulationPreventionBytes(sps_nal.subspan(1), rbsp) ||
            !ParseSpsRbsp(rbsp, result)) {
            result = {};
            return VRREC_STATUS_INVALID_ARGUMENT;
        }
        return VRREC_STATUS_OK;
    } catch (const std::bad_alloc &) {
        result = {};
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        result = {};
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

}
