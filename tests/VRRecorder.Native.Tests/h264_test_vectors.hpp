#ifndef VRRECORDER_NATIVE_TESTS_H264_TEST_VECTORS_HPP
#define VRRECORDER_NATIVE_TESTS_H264_TEST_VECTORS_HPP

#include <cstddef>
#include <cstdint>
#include <vector>

namespace vrrecorder::native::test {

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
    std::uint32_t bit_depth_luma_minus8 = 0;
    std::uint32_t bit_depth_chroma_minus8 = 0;
    std::uint32_t sequence_parameter_set_id = 0;
    std::uint32_t pic_width_in_mbs_minus1 = 0;
    std::uint32_t pic_height_in_map_units_minus1 = 0;
    bool frame_mbs_only = true;
    bool crop = false;
    std::uint32_t crop_left = 0;
    std::uint32_t crop_right = 0;
    std::uint32_t crop_top = 0;
    std::uint32_t crop_bottom = 0;
};

struct PpsSettings final {
    std::uint32_t picture_parameter_set_id = 0;
    std::uint32_t sequence_parameter_set_id = 0;
};

inline bool HasExtendedProfileSyntax(std::uint8_t profile_idc)
{
    return profile_idc == 100 || profile_idc == 110 || profile_idc == 122 ||
           profile_idc == 244;
}

inline std::vector<std::byte> EscapeRbsp(
    std::uint8_t nal_header,
    const std::vector<std::uint8_t> &rbsp)
{
    std::vector<std::byte> result {static_cast<std::byte>(nal_header)};
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

inline std::vector<std::byte> MakeSps(const SpsSettings &settings)
{
    BitWriter writer;
    writer.Bits(settings.profile_idc, 8);
    writer.Bits(settings.compatibility, 8);
    writer.Bits(settings.level_idc, 8);
    writer.UnsignedExpGolomb(settings.sequence_parameter_set_id);

    if (HasExtendedProfileSyntax(settings.profile_idc)) {
        writer.UnsignedExpGolomb(settings.chroma_format_idc);
        if (settings.chroma_format_idc == 3) {
            writer.Bit(0); // separate_colour_plane_flag
        }
        writer.UnsignedExpGolomb(settings.bit_depth_luma_minus8);
        writer.UnsignedExpGolomb(settings.bit_depth_chroma_minus8);
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

    return EscapeRbsp(0x67, writer.FinishRbsp());
}

inline std::vector<std::byte> MakePps(const PpsSettings &settings)
{
    BitWriter writer;
    writer.UnsignedExpGolomb(settings.picture_parameter_set_id);
    writer.UnsignedExpGolomb(settings.sequence_parameter_set_id);
    writer.Bit(0); // entropy_coding_mode_flag
    writer.Bit(0); // bottom_field_pic_order_in_frame_present_flag
    writer.UnsignedExpGolomb(0); // num_slice_groups_minus1
    writer.UnsignedExpGolomb(0); // num_ref_idx_l0_default_active_minus1
    writer.UnsignedExpGolomb(0); // num_ref_idx_l1_default_active_minus1
    writer.Bit(0); // weighted_pred_flag
    writer.Bits(0, 2); // weighted_bipred_idc
    writer.UnsignedExpGolomb(0); // pic_init_qp_minus26
    writer.UnsignedExpGolomb(0); // pic_init_qs_minus26
    writer.UnsignedExpGolomb(0); // chroma_qp_index_offset
    writer.Bit(1); // deblocking_filter_control_present_flag
    writer.Bit(0); // constrained_intra_pred_flag
    writer.Bit(0); // redundant_pic_cnt_present_flag
    return EscapeRbsp(0x68, writer.FinishRbsp());
}

}

#endif
