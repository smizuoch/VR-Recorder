#include "h264_bitstream_converter.hpp"

#include <array>
#include <cstdlib>
#include <iostream>
#include <span>
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

std::vector<std::byte> Bytes(std::span<const std::uint8_t> source)
{
    std::vector<std::byte> result(source.size());
    for (std::size_t index = 0; index < source.size(); ++index) {
        result[index] = static_cast<std::byte>(source[index]);
    }
    return result;
}

std::vector<std::byte> AnnexB(std::initializer_list<std::span<const std::uint8_t>> nals)
{
    std::vector<std::byte> result;
    for (auto nal : nals) {
        result.push_back(std::byte {0});
        result.push_back(std::byte {0});
        result.push_back(std::byte {0});
        result.push_back(std::byte {1});
        auto bytes = Bytes(nal);
        result.insert(result.end(), bytes.begin(), bytes.end());
    }
    return result;
}

std::vector<std::byte> AvccAu(std::initializer_list<std::span<const std::uint8_t>> nals)
{
    std::vector<std::byte> result;
    for (auto nal : nals) {
        const auto size = static_cast<std::uint32_t>(nal.size());
        result.push_back(static_cast<std::byte>((size >> 24U) & 0xffU));
        result.push_back(static_cast<std::byte>((size >> 16U) & 0xffU));
        result.push_back(static_cast<std::byte>((size >> 8U) & 0xffU));
        result.push_back(static_cast<std::byte>(size & 0xffU));
        auto bytes = Bytes(nal);
        result.insert(result.end(), bytes.begin(), bytes.end());
    }
    return result;
}

constexpr std::array<std::uint8_t, 23> Sps16x16 {
    0x67, 0x64, 0x00, 0x0a, 0xac, 0xb2, 0x3d, 0x80,
    0x88, 0x00, 0x00, 0x03, 0x00, 0x08, 0x00, 0x00,
    0x03, 0x01, 0xe0, 0x78, 0x91, 0x32, 0x40,
};
constexpr std::array<std::uint8_t, 6> Pps {
    0x68, 0xeb, 0xc3, 0xcb, 0x22, 0xc0,
};
constexpr std::array<std::uint8_t, 5> Idr {
    0x65, 0x88, 0x84, 0x00, 0x2a,
};
constexpr std::array<std::uint8_t, 4> NonIdr {
    0x41, 0x9a, 0x22, 0x11,
};

void ConvertsSpsPpsAndIdrIntoAvccAndLengthPrefixedAccessUnit()
{
    const auto annex_b = AnnexB({Sps16x16, Pps, Idr});
    H264AnnexBConversionResult result {};
    CHECK(ConvertH264AnnexBToAvcc(
              annex_b,
              16,
              16,
              H264Profile::High,
              result) == VRREC_STATUS_OK);
    CHECK(result.profile == H264Profile::High);
    CHECK(result.width == 16);
    CHECK(result.height == 16);
    CHECK(result.key_frame);
    CHECK(result.access_unit == AvccAu({Idr}));

    const std::array<std::uint8_t, 44> expected_avcc {
        0x01, 0x64, 0x00, 0x0a, 0xff, 0xe1, 0x00, 0x17,
        0x67, 0x64, 0x00, 0x0a, 0xac, 0xb2, 0x3d, 0x80,
        0x88, 0x00, 0x00, 0x03, 0x00, 0x08, 0x00, 0x00,
        0x03, 0x01, 0xe0, 0x78, 0x91, 0x32, 0x40, 0x01,
        0x00, 0x06, 0x68, 0xeb, 0xc3, 0xcb, 0x22, 0xc0,
        0xfd, 0xf8, 0xf8, 0x00,
    };
    CHECK(result.avcc == Bytes(expected_avcc));
}

void ReusesKnownParameterSetsForFollowingNonIdrAccessUnits()
{
    const auto first = AnnexB({Sps16x16, Pps, Idr});
    H264AnnexBConversionResult result {};
    CHECK(ConvertH264AnnexBToAvcc(
              first,
              16,
              16,
              H264Profile::High,
              result) == VRREC_STATUS_OK);

    const auto second = AnnexB({NonIdr});
    CHECK(ConvertH264AnnexBToAvcc(
              second,
              16,
              16,
              H264Profile::High,
              result) == VRREC_STATUS_OK);
    CHECK(!result.key_frame);
    CHECK(result.avcc.empty());
    CHECK(result.access_unit == AvccAu({NonIdr}));
}

void DedupesRepeatedIdenticalParameterSets()
{
    const auto annex_b = AnnexB({Sps16x16, Pps, Sps16x16, Pps, Idr});
    H264AnnexBConversionResult result {};
    CHECK(ConvertH264AnnexBToAvcc(
              annex_b,
              16,
              16,
              H264Profile::High,
              result) == VRREC_STATUS_OK);
    CHECK(result.key_frame);
    CHECK(result.access_unit == AvccAu({Idr}));
}

void RejectsMalformedOrIncompleteAnnexB()
{
    H264AnnexBConversionResult result {};
    CHECK(ConvertH264AnnexBToAvcc(
              Bytes(std::array<std::uint8_t, 3> {0, 0, 1}),
              16,
              16,
              H264Profile::High,
              result) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(ConvertH264AnnexBToAvcc(
              AnnexB({Sps16x16, Idr}),
              16,
              16,
              H264Profile::High,
              result) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(ConvertH264AnnexBToAvcc(
              AnnexB({Sps16x16, Pps, Idr}),
              32,
              16,
              H264Profile::High,
              result) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(ConvertH264AnnexBToAvcc(
              AnnexB({Sps16x16, Pps, Idr}),
              16,
              16,
              H264Profile::Main,
              result) == VRREC_STATUS_INVALID_ARGUMENT);

    auto conflicting_sps = Sps16x16;
    conflicting_sps[10] = 0x01;
    CHECK(ConvertH264AnnexBToAvcc(
              AnnexB({Sps16x16, Pps, conflicting_sps, Idr}),
              16,
              16,
              H264Profile::High,
              result) == VRREC_STATUS_INVALID_ARGUMENT);

    constexpr std::array<std::uint8_t, 2> UnsupportedNal {0x80, 0x00};
    CHECK(ConvertH264AnnexBToAvcc(
              AnnexB({UnsupportedNal}),
              16,
              16,
              H264Profile::High,
              result) == VRREC_STATUS_INVALID_ARGUMENT);
}

}

int main()
{
    ConvertsSpsPpsAndIdrIntoAvccAndLengthPrefixedAccessUnit();
    ReusesKnownParameterSetsForFollowingNonIdrAccessUnits();
    DedupesRepeatedIdenticalParameterSets();
    RejectsMalformedOrIncompleteAnnexB();
    return 0;
}
