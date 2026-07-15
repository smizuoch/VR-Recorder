#include "h264_sps_parser.hpp"
#include "h264_test_vectors.hpp"

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
using test::SpsSettings;

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
