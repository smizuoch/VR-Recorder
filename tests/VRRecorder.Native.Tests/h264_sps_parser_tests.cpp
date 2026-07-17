#include "h264_sps_parser.hpp"
#include "h264_test_vectors.hpp"

#include "allocation_failure_test_support.hpp"

#include <array>
#include <cstddef>
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
using test::MakeSps;
using test::MakePps;
using test::PpsSettings;
using test::SpsSettings;

void ParsesCroppedHighProfileDisplayGeometry()
{
    SpsSettings settings;
    settings.sequence_parameter_set_id = 3;
    settings.pic_width_in_mbs_minus1 = 119;
    settings.pic_height_in_map_units_minus1 = 67;
    settings.crop = true;
    settings.crop_bottom = 4;

    H264SpsInfo result {};
    CHECK(ParseH264Sps(MakeSps(settings), result) == VRREC_STATUS_OK);
    CHECK(result.profile_idc == 100);
    CHECK(result.sequence_parameter_set_id == 3);
    CHECK(result.profile_compatibility == 0);
    CHECK(result.level_idc == 40);
    CHECK(result.chroma_format_idc == 1);
    CHECK(result.bit_depth_luma == 8);
    CHECK(result.bit_depth_chroma == 8);
    CHECK(result.width == 1'920);
    CHECK(result.height == 1'080);
    CHECK(result.frame_mbs_only);
}

void ParsesPictureParameterSetIdentifiers()
{
    PpsSettings settings;
    settings.picture_parameter_set_id = 7;
    settings.sequence_parameter_set_id = 3;

    H264PpsInfo result {};
    CHECK(ParseH264Pps(MakePps(settings), result) == VRREC_STATUS_OK);
    CHECK(result.picture_parameter_set_id == 7);
    CHECK(result.sequence_parameter_set_id == 3);
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

void ParsesEveryExtendedProfileVariant()
{
    constexpr std::array<std::uint8_t, 14> profiles {
        44, 83, 86, 100, 110, 118, 122,
        128, 134, 135, 138, 139, 144, 244,
    };
    for (const auto profile : profiles) {
        SpsSettings settings;
        settings.profile_idc = profile;
        H264SpsInfo result {};
        CHECK(ParseH264Sps(MakeSps(settings), result) == VRREC_STATUS_OK);
        CHECK(result.profile_idc == profile);
    }
}

void ParsesChromaPocScalingAndInterlacedVariants()
{
    for (const auto chroma_format : {0U, 1U, 2U, 3U}) {
        SpsSettings settings;
        settings.chroma_format_idc = chroma_format;
        settings.separate_colour_plane = chroma_format == 3U;
        H264SpsInfo result {};
        CHECK(ParseH264Sps(MakeSps(settings), result) == VRREC_STATUS_OK);
        CHECK(result.chroma_format_idc == chroma_format);
    }

    SpsSettings interlaced;
    interlaced.frame_mbs_only = false;
    H264SpsInfo result {};
    CHECK(ParseH264Sps(MakeSps(interlaced), result) == VRREC_STATUS_OK);
    CHECK(!result.frame_mbs_only);
    CHECK(result.height == 32);

    SpsSettings poc_type_two;
    poc_type_two.pic_order_count_type = 2;
    CHECK(ParseH264Sps(MakeSps(poc_type_two), result) == VRREC_STATUS_OK);

    SpsSettings poc_type_one;
    poc_type_one.pic_order_count_type = 1;
    poc_type_one.pic_order_cycle_length = 2;
    CHECK(ParseH264Sps(MakeSps(poc_type_one), result) == VRREC_STATUS_OK);

    SpsSettings scaling_matrix;
    scaling_matrix.scaling_matrix_present = true;
    CHECK(ParseH264Sps(MakeSps(scaling_matrix), result) ==
          VRREC_STATUS_OK);

    scaling_matrix.first_scaling_list_delta = -8;
    CHECK(ParseH264Sps(MakeSps(scaling_matrix), result) ==
          VRREC_STATUS_OK);
    scaling_matrix.first_scaling_list_delta = -265;
    CHECK(ParseH264Sps(MakeSps(scaling_matrix), result) ==
          VRREC_STATUS_OK);

    poc_type_one.offset_for_non_ref_pic = -1;
    poc_type_one.offset_for_top_to_bottom_field = 1;
    poc_type_one.offset_for_ref_frame = -1;
    CHECK(ParseH264Sps(MakeSps(poc_type_one), result) ==
          VRREC_STATUS_OK);

    for (const auto chroma_format : {0U, 1U, 2U, 3U}) {
        SpsSettings cropped;
        cropped.chroma_format_idc = chroma_format;
        cropped.separate_colour_plane = chroma_format == 3U;
        cropped.pic_width_in_mbs_minus1 = 1;
        cropped.pic_height_in_map_units_minus1 = 1;
        cropped.crop = true;
        cropped.crop_left = 1;
        cropped.crop_top = 1;
        CHECK(ParseH264Sps(MakeSps(cropped), result) ==
              VRREC_STATUS_OK);
    }
}

void RejectsEveryTruncatedSpsAndPpsPrefix()
{
    H264SpsInfo sps_result {};
    const auto sps = MakeSps(SpsSettings {});
    for (std::size_t size = 0; size < sps.size(); ++size) {
        CHECK(ParseH264Sps(
                  std::span<const std::byte>(sps).first(size),
                  sps_result) == VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(sps_result.width == 0);
    }

    H264PpsInfo pps_result {};
    const auto pps = MakePps(PpsSettings {});
    for (std::size_t size = 0; size < 2U && size < pps.size(); ++size) {
        CHECK(ParseH264Pps(
                  std::span<const std::byte>(pps).first(size),
                  pps_result) == VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(pps_result.picture_parameter_set_id == 0);
    }
}

void RejectsEmulationPreventionAndTrailingBitCorruption()
{
    H264SpsInfo result {};
    const std::vector<std::byte> invalid_emulation_followup {
        std::byte {0x67},
        std::byte {0x00},
        std::byte {0x00},
        std::byte {0x03},
        std::byte {0x04},
    };
    CHECK(ParseH264Sps(invalid_emulation_followup, result) ==
          VRREC_STATUS_INVALID_ARGUMENT);

    auto missing_stop_bit = MakeSps(SpsSettings {});
    missing_stop_bit.back() = std::byte {0x00};
    CHECK(ParseH264Sps(missing_stop_bit, result) ==
          VRREC_STATUS_INVALID_ARGUMENT);

    auto nonzero_trailing_bit = MakeSps(SpsSettings {});
    nonzero_trailing_bit.back() |= std::byte {0x01};
    CHECK(ParseH264Sps(nonzero_trailing_bit, result) ==
          VRREC_STATUS_INVALID_ARGUMENT);

    for (const auto compatibility : {2U, 3U}) {
        SpsSettings reserved;
        reserved.compatibility = static_cast<std::uint8_t>(compatibility);
        CHECK(ParseH264Sps(MakeSps(reserved), result) ==
              VRREC_STATUS_INVALID_ARGUMENT);
    }
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
    CHECK(ParseH264Sps(
              std::vector<std::byte> {std::byte {0xe7}, std::byte {0x80}},
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
    const std::vector<std::byte> unescaped_start_code {
        std::byte {0x67}, std::byte {100}, std::byte {0}, std::byte {0},
        std::byte {0}, std::byte {1},
    };
    CHECK(ParseH264Sps(unescaped_start_code, result) ==
          VRREC_STATUS_INVALID_ARGUMENT);

    SpsSettings overflowing_width;
    overflowing_width.pic_width_in_mbs_minus1 =
        std::numeric_limits<std::uint32_t>::max();
    CHECK(ParseH264Sps(MakeSps(overflowing_width), result) ==
          VRREC_STATUS_INVALID_ARGUMENT);
}

void RejectsEverySpsSyntaxLimit()
{
    H264SpsInfo result {};
    const auto rejects = [&](const auto mutate) {
        SpsSettings settings;
        mutate(settings);
        CHECK(ParseH264Sps(MakeSps(settings), result) ==
              VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(result.width == 0);
        CHECK(result.height == 0);
    };

    rejects([](auto &value) { value.sequence_parameter_set_id = 32; });
    rejects([](auto &value) { value.chroma_format_idc = 4; });
    rejects([](auto &value) { value.bit_depth_luma_minus8 = 7; });
    rejects([](auto &value) { value.bit_depth_chroma_minus8 = 7; });
    rejects([](auto &value) { value.log2_max_frame_num_minus4 = 13; });
    rejects([](auto &value) {
        value.log2_max_pic_order_count_lsb_minus4 = 13;
    });
    rejects([](auto &value) { value.pic_order_count_type = 3; });
    rejects([](auto &value) {
        value.pic_order_count_type = 1;
        value.pic_order_cycle_length = 256;
    });
    rejects([](auto &value) {
        value.crop = true;
        value.crop_top = 8;
    });
    rejects([](auto &value) {
        value.crop = true;
        value.crop_right = 8;
    });
    rejects([](auto &value) {
        value.pic_height_in_map_units_minus1 =
            std::numeric_limits<std::uint32_t>::max();
    });
}

void RejectsMalformedOrOutOfRangePpsIdentifiers()
{
    H264PpsInfo result {};
    CHECK(ParseH264Pps({}, result) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(ParseH264Pps(
              std::vector<std::byte> {std::byte {0x67}, std::byte {0x80}},
              result) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(ParseH264Pps(
              std::vector<std::byte> {std::byte {0xe8}, std::byte {0x80}},
              result) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(ParseH264Pps(
              std::vector<std::byte> {std::byte {0x68}, std::byte {0x80}},
              result) == VRREC_STATUS_INVALID_ARGUMENT);

    PpsSettings picture_id_too_large;
    picture_id_too_large.picture_parameter_set_id = 256;
    CHECK(ParseH264Pps(MakePps(picture_id_too_large), result) ==
          VRREC_STATUS_INVALID_ARGUMENT);

    PpsSettings sequence_id_too_large;
    sequence_id_too_large.sequence_parameter_set_id = 32;
    CHECK(ParseH264Pps(MakePps(sequence_id_too_large), result) ==
          VRREC_STATUS_INVALID_ARGUMENT);
}

void ClassifiesEverySingleByteSpsAndPpsMutation()
{
    const auto sps = MakeSps(SpsSettings {});
    for (std::size_t index = 0; index < sps.size(); ++index) {
        for (std::uint32_t replacement = 0; replacement <= 0xffU;
             ++replacement) {
            auto mutated = sps;
            mutated[index] = static_cast<std::byte>(replacement);

            H264SpsInfo result {};
            result.width = 1'920;
            result.height = 1'080;
            const auto status = ParseH264Sps(mutated, result);
            CHECK(status == VRREC_STATUS_OK ||
                  status == VRREC_STATUS_INVALID_ARGUMENT);
            if (status == VRREC_STATUS_OK) {
                CHECK(result.width > 0);
                CHECK(result.height > 0);
                CHECK(result.chroma_format_idc <= 3);
                CHECK(result.bit_depth_luma >= 8);
                CHECK(result.bit_depth_luma <= 14);
                CHECK(result.bit_depth_chroma >= 8);
                CHECK(result.bit_depth_chroma <= 14);
            } else {
                CHECK(result.sequence_parameter_set_id == 0);
                CHECK(result.profile_idc == 0);
                CHECK(result.profile_compatibility == 0);
                CHECK(result.level_idc == 0);
                CHECK(result.chroma_format_idc == 1);
                CHECK(result.bit_depth_luma == 8);
                CHECK(result.bit_depth_chroma == 8);
                CHECK(result.width == 0);
                CHECK(result.height == 0);
                CHECK(result.frame_mbs_only);
            }
        }
    }

    const auto pps = MakePps(PpsSettings {});
    for (std::size_t index = 0; index < pps.size(); ++index) {
        for (std::uint32_t replacement = 0; replacement <= 0xffU;
             ++replacement) {
            auto mutated = pps;
            mutated[index] = static_cast<std::byte>(replacement);

            H264PpsInfo result {};
            result.picture_parameter_set_id = 7;
            result.sequence_parameter_set_id = 3;
            const auto status = ParseH264Pps(mutated, result);
            CHECK(status == VRREC_STATUS_OK ||
                  status == VRREC_STATUS_INVALID_ARGUMENT);
            if (status == VRREC_STATUS_OK) {
                CHECK(result.picture_parameter_set_id <= 255);
                CHECK(result.sequence_parameter_set_id <= 31);
            } else {
                CHECK(result.picture_parameter_set_id == 0);
                CHECK(result.sequence_parameter_set_id == 0);
            }
        }
    }
}

void ReportsSpsAndPpsRbspAllocationFailureWithoutPartialResults()
{
    const auto sps = MakeSps(SpsSettings {});
    H264SpsInfo sps_result {};
    sps_result.width = 1'920;
    allocation_failure::fail_on_allocation = 1;
    const auto sps_status = ParseH264Sps(sps, sps_result);
    allocation_failure::fail_on_allocation = 0;
    CHECK(sps_status == VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(sps_result.width == 0);
    CHECK(sps_result.height == 0);

    const auto pps = MakePps(PpsSettings {});
    H264PpsInfo pps_result {};
    pps_result.picture_parameter_set_id = 7;
    allocation_failure::fail_on_allocation = 1;
    const auto pps_status = ParseH264Pps(pps, pps_result);
    allocation_failure::fail_on_allocation = 0;
    CHECK(pps_status == VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(pps_result.picture_parameter_set_id == 0);
    CHECK(pps_result.sequence_parameter_set_id == 0);
}

}

int main()
{
    ParsesCroppedHighProfileDisplayGeometry();
    ParsesPictureParameterSetIdentifiers();
    ParsesMainProfileWithoutExtendedSyntax();
    ParsesEveryExtendedProfileVariant();
    ParsesChromaPocScalingAndInterlacedVariants();
    RejectsEveryTruncatedSpsAndPpsPrefix();
    RejectsEmulationPreventionAndTrailingBitCorruption();
    ParsesEncoderSpsWithEmulationPreventionBytesAndVui();
    RejectsMalformedOrImpossibleSps();
    RejectsEverySpsSyntaxLimit();
    RejectsMalformedOrOutOfRangePpsIdentifiers();
    ClassifiesEverySingleByteSpsAndPpsMutation();
    ReportsSpsAndPpsRbspAllocationFailureWithoutPartialResults();
    return 0;
}
