#include "h264_sps_parser.hpp"

#include <array>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <limits>
#include <vector>

namespace {

#define CHECK(condition)                                                        \
    do {                                                                        \
        if (!(condition)) {                                                     \
            std::cerr << "check failed at " << __FILE__ << ':' << __LINE__      \
                      << ": " #condition << '\n';                              \
            std::abort();                                                       \
        }                                                                       \
    } while (false)

using namespace vrrecorder::native;

class BitWriter final {
public:
    void Bits(std::uint32_t value, std::uint32_t count)
    {
        for (auto remaining = count; remaining > 0; --remaining) {
            Bit((value >> (remaining - 1U)) & 1U);
        }
    }

    void Bit(std::uint32_t value)
    {
        if (bit_offset_ == 0) {
            bytes_.push_back(0);
        }
        bytes_.back() |= static_cast<std::uint8_t>(
            (value & 1U) << (7U - bit_offset_));
        bit_offset_ = (bit_offset_ + 1U) % 8U;
    }

    void UnsignedExpGolomb(std::uint32_t value)
    {
        const auto code = static_cast<std::uint64_t>(value) + 1U;
        std::uint32_t bit_count = 0;
        for (auto scan = code; scan != 0; scan >>= 1U) {
            ++bit_count;
        }
        for (std::uint32_t index = 1; index < bit_count; ++index) {
            Bit(0);
        }
        for (auto remaining = bit_count; remaining > 0; --remaining) {
            Bit(static_cast<std::uint32_t>(
                (code >> (remaining - 1U)) & 1U));
        }
    }

    std::vector<std::uint8_t> FinishRbsp()
    {
        Bit(1);
        while (bit_offset_ != 0) {
            Bit(0);
        }
        return bytes_;
    }

private:
    std::vector<std::uint8_t> bytes_;
    std::uint32_t bit_offset_ = 0;
};

struct SpsSettings final {
    std::uint8_t profile_idc = 100;
    std::uint8_t compatibility = 0;
    std::uint8_t level_idc = 40;
    std::uint32_t chroma_format_idc = 1;
    std::uint32_t pic_width_in_mbs_minus1 = 0;
    std::uint32_t pic_height_in_map_units_minus1 = 0;
    bool frame_mbs_only = true;
    bool crop = false;
    std::uint32_t crop_left = 0;
    std::uint32_t crop_right = 0;
    std::uint32_t crop_top = 0;
    std::uint32_t crop_bottom = 0;
};

bool HasExtendedProfileSyntax(std::uint8_t profile_idc)
{
    return profile_idc == 100 || profile_idc == 110 || profile_idc == 122 ||
           profile_idc == 244;
}

std::vector<std::byte> MakeSps(const SpsSettings &settings)
{
    BitWriter writer;
    writer.Bits(settings.profile_idc, 8);
    writer.Bits(settings.compatibility, 8);
    writer.Bits(settings.level_idc, 8);
    writer.UnsignedExpGolomb(0); // seq_parameter_set_id

    if (HasExtendedProfileSyntax(settings.profile_idc)) {
        writer.UnsignedExpGolomb(settings.chroma_format_idc);
        if (settings.chroma_format_idc == 3) {
            writer.Bit(0); // separate_colour_plane_flag
        }
        writer.UnsignedExpGolomb(0); // bit_depth_luma_minus8
        writer.UnsignedExpGolomb(0); // bit_depth_chroma_minus8
        writer.Bit(0); // qpprime_y_zero_transform_bypass_flag
        writer.Bit(0); // seq_scaling_matrix_present_flag
    }

    writer.UnsignedExpGolomb(0); // log2_max_frame_num_minus4
    writer.UnsignedExpGolomb(0); // pic_order_cnt_type
    writer.UnsignedExpGolomb(0); // log2_max_pic_order_cnt_lsb_minus4
    writer.UnsignedExpGolomb(1); // max_num_ref_frames
    writer.Bit(0); // gaps_in_frame_num_value_allowed_flag
    writer.UnsignedExpGolomb(settings.pic_width_in_mbs_minus1);
    writer.UnsignedExpGolomb(settings.pic_height_in_map_units_minus1);
    writer.Bit(settings.frame_mbs_only ? 1U : 0U);
    if (!settings.frame_mbs_only) {
        writer.Bit(0); // mb_adaptive_frame_field_flag
    }
    writer.Bit(1); // direct_8x8_inference_flag
    writer.Bit(settings.crop ? 1U : 0U);
    if (settings.crop) {
        writer.UnsignedExpGolomb(settings.crop_left);
        writer.UnsignedExpGolomb(settings.crop_right);
        writer.UnsignedExpGolomb(settings.crop_top);
        writer.UnsignedExpGolomb(settings.crop_bottom);
    }
    writer.Bit(0); // vui_parameters_present_flag

    const auto rbsp = writer.FinishRbsp();
    std::vector<std::byte> result {std::byte {0x67}};
    std::uint32_t zero_count = 0;
    for (const auto value : rbsp) {
        if (zero_count >= 2 && value <= 3) {
            result.push_back(std::byte {3});
            zero_count = 0;
        }
        result.push_back(static_cast<std::byte>(value));
        zero_count = value == 0 ? zero_count + 1U : 0U;
    }
    return result;
}

void ParsesCroppedHighProfileDisplayGeometry()
{
    SpsSettings settings;
    settings.pic_width_in_mbs_minus1 = 119;
    settings.pic_height_in_map_units_minus1 = 67;
    settings.crop = true;
    settings.crop_bottom = 4;

    H264SpsInfo result {};
    CHECK(ParseH264Sps(MakeSps(settings), result) == VRREC_STATUS_OK);
    CHECK(result.profile_idc == 100);
    CHECK(result.profile_compatibility == 0);
    CHECK(result.level_idc == 40);
    CHECK(result.chroma_format_idc == 1);
    CHECK(result.bit_depth_luma == 8);
    CHECK(result.bit_depth_chroma == 8);
    CHECK(result.width == 1'920);
    CHECK(result.height == 1'080);
    CHECK(result.frame_mbs_only);
}

void ParsesMainProfileWithoutExtendedSyntax()
{
    SpsSettings settings;
    settings.profile_idc = 77;
    settings.level_idc = 31;
    settings.pic_width_in_mbs_minus1 = 79;
    settings.pic_height_in_map_units_minus1 = 44;

    H264SpsInfo result {};
    CHECK(ParseH264Sps(MakeSps(settings), result) == VRREC_STATUS_OK);
    CHECK(result.profile_idc == 77);
    CHECK(result.level_idc == 31);
    CHECK(result.chroma_format_idc == 1);
    CHECK(result.bit_depth_luma == 8);
    CHECK(result.bit_depth_chroma == 8);
    CHECK(result.width == 1'280);
    CHECK(result.height == 720);
    CHECK(result.frame_mbs_only);
}

void ParsesEncoderSpsWithEmulationPreventionBytesAndVui()
{
    constexpr std::array<std::byte, 23> sps {
        std::byte {0x67}, std::byte {0x64}, std::byte {0x00},
        std::byte {0x0a}, std::byte {0xac}, std::byte {0xb2},
        std::byte {0x3d}, std::byte {0x80}, std::byte {0x88},
        std::byte {0x00}, std::byte {0x00}, std::byte {0x03},
        std::byte {0x00}, std::byte {0x08}, std::byte {0x00},
        std::byte {0x00}, std::byte {0x03}, std::byte {0x01},
        std::byte {0xe0}, std::byte {0x78}, std::byte {0x91},
        std::byte {0x32}, std::byte {0x40},
    };

    H264SpsInfo result {};
    CHECK(ParseH264Sps(sps, result) == VRREC_STATUS_OK);
    CHECK(result.profile_idc == 100);
    CHECK(result.width == 16);
    CHECK(result.height == 16);
}

void RejectsMalformedOrImpossibleSps()
{
    H264SpsInfo result {};
    CHECK(ParseH264Sps({}, result) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(ParseH264Sps(
              std::vector<std::byte> {std::byte {0x68}, std::byte {0x80}},
              result) == VRREC_STATUS_INVALID_ARGUMENT);

    auto truncated = MakeSps(SpsSettings {});
    truncated.resize(4);
    CHECK(ParseH264Sps(truncated, result) == VRREC_STATUS_INVALID_ARGUMENT);

    SpsSettings impossible_crop;
    impossible_crop.crop = true;
    impossible_crop.crop_left = 8;
    CHECK(ParseH264Sps(MakeSps(impossible_crop), result) ==
          VRREC_STATUS_INVALID_ARGUMENT);

    SpsSettings reserved_bits;
    reserved_bits.compatibility = 1;
    CHECK(ParseH264Sps(MakeSps(reserved_bits), result) ==
          VRREC_STATUS_INVALID_ARGUMENT);

    const std::vector<std::byte> dangling_emulation_prevention {
        std::byte {0x67}, std::byte {100}, std::byte {0}, std::byte {40},
        std::byte {0}, std::byte {0}, std::byte {3},
    };
    CHECK(ParseH264Sps(dangling_emulation_prevention, result) ==
          VRREC_STATUS_INVALID_ARGUMENT);

    SpsSettings overflowing_width;
    overflowing_width.pic_width_in_mbs_minus1 =
        std::numeric_limits<std::uint32_t>::max();
    CHECK(ParseH264Sps(MakeSps(overflowing_width), result) ==
          VRREC_STATUS_INVALID_ARGUMENT);
}

}

int main()
{
    ParsesCroppedHighProfileDisplayGeometry();
    ParsesMainProfileWithoutExtendedSyntax();
    ParsesEncoderSpsWithEmulationPreventionBytesAndVui();
    RejectsMalformedOrImpossibleSps();
    return 0;
}
