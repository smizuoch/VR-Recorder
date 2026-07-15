#include "h264_bitstream_converter.hpp"
#include "h264_test_vectors.hpp"

#include <algorithm>
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

std::vector<std::byte> AnnexBBytes(
    std::initializer_list<std::span<const std::byte>> nals)
{
    std::vector<std::byte> result;
    for (auto nal : nals) {
        result.insert(
            result.end(),
            {std::byte {0}, std::byte {0}, std::byte {0}, std::byte {1}});
        result.insert(result.end(), nal.begin(), nal.end());
    }
    return result;
}

void AppendAnnexBNal(
    std::vector<std::byte> &annex_b,
    std::span<const std::byte> nal)
{
    annex_b.insert(
        annex_b.end(),
        {std::byte {0}, std::byte {0}, std::byte {0}, std::byte {1}});
    annex_b.insert(annex_b.end(), nal.begin(), nal.end());
}

std::vector<std::byte> AvccParameterSets(
    std::initializer_list<std::span<const std::byte>> sequence_parameter_sets,
    std::initializer_list<std::span<const std::byte>> picture_parameter_sets)
{
    CHECK(sequence_parameter_sets.size() > 0);
    const auto first_sps = *sequence_parameter_sets.begin();
    std::vector<std::byte> result {
        std::byte {1},
        first_sps[1],
        first_sps[2],
        first_sps[3],
        std::byte {0xff},
        static_cast<std::byte>(0xe0U | sequence_parameter_sets.size()),
    };
    for (const auto sps : sequence_parameter_sets) {
        result.push_back(static_cast<std::byte>(sps.size() >> 8U));
        result.push_back(static_cast<std::byte>(sps.size() & 0xffU));
        result.insert(result.end(), sps.begin(), sps.end());
    }
    result.push_back(static_cast<std::byte>(picture_parameter_sets.size()));
    for (const auto pps : picture_parameter_sets) {
        result.push_back(static_cast<std::byte>(pps.size() >> 8U));
        result.push_back(static_cast<std::byte>(pps.size() & 0xffU));
        result.insert(result.end(), pps.begin(), pps.end());
    }
    result.insert(
        result.end(),
        {std::byte {0xfd},
         std::byte {0xf8},
         std::byte {0xf8},
         std::byte {0}});
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

void ConvertsCroppedHighProfileSpsAtRecordingResolution()
{
    test::SpsSettings settings;
    settings.compatibility = 0x80;
    settings.level_idc = 42;
    settings.pic_width_in_mbs_minus1 = 119;
    settings.pic_height_in_map_units_minus1 = 67;
    settings.crop = true;
    settings.crop_bottom = 4;
    const auto sps = test::MakeSps(settings);
    const auto pps = Bytes(Pps);
    const auto idr = Bytes(Idr);
    const auto annex_b = AnnexBBytes(
        {std::span<const std::byte> {sps},
         std::span<const std::byte> {pps},
         std::span<const std::byte> {idr}});

    H264AnnexBConversionResult result {};
    CHECK(ConvertH264AnnexBToAvcc(
              annex_b,
              1'920,
              1'080,
              H264Profile::High,
              result) == VRREC_STATUS_OK);
    CHECK(result.profile == H264Profile::High);
    CHECK(result.width == 1'920);
    CHECK(result.height == 1'080);
    CHECK(result.key_frame);
    CHECK(result.access_unit == AvccAu({Idr}));
    CHECK(result.avcc.size() == 15U + sps.size() + pps.size());
    CHECK(result.avcc[1] == std::byte {100});
    CHECK(result.avcc[2] == std::byte {0x80});
    CHECK(result.avcc[3] == std::byte {42});
    CHECK(result.avcc[6] == static_cast<std::byte>(sps.size() >> 8U));
    CHECK(result.avcc[7] == static_cast<std::byte>(sps.size() & 0xffU));
    CHECK(std::ranges::equal(
        sps,
        std::span<const std::byte> {result.avcc}.subspan(8U, sps.size())));
}

void ConvertsMainProfileSpsAtRecordingResolution()
{
    test::SpsSettings settings;
    settings.profile_idc = 77;
    settings.level_idc = 31;
    settings.pic_width_in_mbs_minus1 = 79;
    settings.pic_height_in_map_units_minus1 = 44;
    const auto sps = test::MakeSps(settings);
    const auto pps = Bytes(Pps);
    const auto idr = Bytes(Idr);
    const auto annex_b = AnnexBBytes(
        {std::span<const std::byte> {sps},
         std::span<const std::byte> {pps},
         std::span<const std::byte> {idr}});

    H264AnnexBConversionResult result {};
    CHECK(ConvertH264AnnexBToAvcc(
              annex_b,
              1'280,
              720,
              H264Profile::Main,
              result) == VRREC_STATUS_OK);
    CHECK(result.profile == H264Profile::Main);
    CHECK(result.width == 1'280);
    CHECK(result.height == 720);
    CHECK(result.key_frame);
    CHECK(result.avcc.size() == 11U + sps.size() + pps.size());
    CHECK(result.avcc[1] == std::byte {77});
    CHECK(result.avcc[3] == std::byte {31});
}

void RejectsUnsupportedChromaFormatAndBitDepth()
{
    const auto pps = Bytes(Pps);
    const auto idr = Bytes(Idr);
    H264AnnexBConversionResult result {};

    test::SpsSettings chroma_422;
    chroma_422.chroma_format_idc = 2;
    const auto chroma_sps = test::MakeSps(chroma_422);
    CHECK(ConvertH264AnnexBToAvcc(
              AnnexBBytes(
                  {std::span<const std::byte> {chroma_sps},
                   std::span<const std::byte> {pps},
                   std::span<const std::byte> {idr}}),
              16,
              16,
              H264Profile::High,
              result) == VRREC_STATUS_INVALID_ARGUMENT);

    test::SpsSettings ten_bit;
    ten_bit.bit_depth_luma_minus8 = 2;
    ten_bit.bit_depth_chroma_minus8 = 2;
    const auto ten_bit_sps = test::MakeSps(ten_bit);
    CHECK(ConvertH264AnnexBToAvcc(
              AnnexBBytes(
                  {std::span<const std::byte> {ten_bit_sps},
                   std::span<const std::byte> {pps},
                   std::span<const std::byte> {idr}}),
              16,
              16,
              H264Profile::High,
              result) == VRREC_STATUS_INVALID_ARGUMENT);
}

void RejectsParameterSetsThatExceedAvccLengthFields()
{
    auto oversized_sps = Bytes(Sps16x16);
    oversized_sps.resize(65'536U, std::byte {0x55});
    const auto pps = Bytes(Pps);
    const auto idr = Bytes(Idr);
    H264AnnexBConversionResult result {};
    CHECK(ConvertH264AnnexBToAvcc(
              AnnexBBytes(
                  {std::span<const std::byte> {oversized_sps},
                   std::span<const std::byte> {pps},
                   std::span<const std::byte> {idr}}),
              16,
              16,
              H264Profile::High,
              result) == VRREC_STATUS_INVALID_ARGUMENT);

    auto oversized_pps = pps;
    oversized_pps.resize(65'536U, std::byte {0x55});
    const auto sps = Bytes(Sps16x16);
    CHECK(ConvertH264AnnexBToAvcc(
              AnnexBBytes(
                  {std::span<const std::byte> {sps},
                   std::span<const std::byte> {oversized_pps},
                   std::span<const std::byte> {idr}}),
              16,
              16,
              H264Profile::High,
              result) == VRREC_STATUS_INVALID_ARGUMENT);
}

void SortsDistinctParameterSetsByIdentifier()
{
    test::SpsSettings sps_zero_settings;
    test::SpsSettings sps_one_settings;
    sps_one_settings.sequence_parameter_set_id = 1;
    test::PpsSettings pps_zero_settings;
    test::PpsSettings pps_one_settings;
    pps_one_settings.picture_parameter_set_id = 1;
    pps_one_settings.sequence_parameter_set_id = 1;
    const auto sps_zero = test::MakeSps(sps_zero_settings);
    const auto sps_one = test::MakeSps(sps_one_settings);
    const auto pps_zero = test::MakePps(pps_zero_settings);
    const auto pps_one = test::MakePps(pps_one_settings);
    const auto idr = Bytes(Idr);

    H264AnnexBConversionResult reverse_order {};
    CHECK(ConvertH264AnnexBToAvcc(
              AnnexBBytes(
                  {std::span<const std::byte> {sps_one},
                   std::span<const std::byte> {pps_one},
                   std::span<const std::byte> {sps_zero},
                   std::span<const std::byte> {pps_zero},
                   std::span<const std::byte> {idr}}),
              16,
              16,
              H264Profile::High,
              reverse_order) == VRREC_STATUS_OK);
    CHECK(reverse_order.avcc == AvccParameterSets(
                                    {std::span<const std::byte> {sps_zero},
                                     std::span<const std::byte> {sps_one}},
                                    {std::span<const std::byte> {pps_zero},
                                     std::span<const std::byte> {pps_one}}));

    H264AnnexBConversionResult forward_order {};
    CHECK(ConvertH264AnnexBToAvcc(
              AnnexBBytes(
                  {std::span<const std::byte> {sps_zero},
                   std::span<const std::byte> {pps_zero},
                   std::span<const std::byte> {sps_one},
                   std::span<const std::byte> {pps_one},
                   std::span<const std::byte> {idr}}),
              16,
              16,
              H264Profile::High,
              forward_order) == VRREC_STATUS_OK);
    CHECK(forward_order.avcc == reverse_order.avcc);
}

void RejectsPictureParameterSetWithMissingSequenceParameterSet()
{
    const auto sps = test::MakeSps(test::SpsSettings {});
    test::PpsSettings pps_settings;
    pps_settings.sequence_parameter_set_id = 1;
    const auto pps = test::MakePps(pps_settings);
    const auto idr = Bytes(Idr);
    H264AnnexBConversionResult result {};
    CHECK(ConvertH264AnnexBToAvcc(
              AnnexBBytes(
                  {std::span<const std::byte> {sps},
                   std::span<const std::byte> {pps},
                   std::span<const std::byte> {idr}}),
              16,
              16,
              H264Profile::High,
              result) == VRREC_STATUS_INVALID_ARGUMENT);
}

void RejectsParameterSetCountOverflow()
{
    std::vector<std::vector<std::byte>> sequence_parameter_sets;
    sequence_parameter_sets.reserve(32);
    std::vector<std::byte> too_many_sequence_parameter_sets;
    for (std::uint32_t identifier = 0; identifier < 32U; ++identifier) {
        test::SpsSettings settings;
        settings.sequence_parameter_set_id = identifier;
        sequence_parameter_sets.push_back(test::MakeSps(settings));
        AppendAnnexBNal(
            too_many_sequence_parameter_sets,
            sequence_parameter_sets.back());
    }
    const auto pps = test::MakePps(test::PpsSettings {});
    const auto idr = Bytes(Idr);
    AppendAnnexBNal(too_many_sequence_parameter_sets, pps);
    AppendAnnexBNal(too_many_sequence_parameter_sets, idr);
    H264AnnexBConversionResult result {};
    CHECK(ConvertH264AnnexBToAvcc(
              too_many_sequence_parameter_sets,
              16,
              16,
              H264Profile::High,
              result) == VRREC_STATUS_INVALID_ARGUMENT);

    const auto sps = test::MakeSps(test::SpsSettings {});
    std::vector<std::vector<std::byte>> picture_parameter_sets;
    picture_parameter_sets.reserve(256);
    std::vector<std::byte> too_many_picture_parameter_sets;
    AppendAnnexBNal(too_many_picture_parameter_sets, sps);
    for (std::uint32_t identifier = 0; identifier < 256U; ++identifier) {
        test::PpsSettings settings;
        settings.picture_parameter_set_id = identifier;
        picture_parameter_sets.push_back(test::MakePps(settings));
        AppendAnnexBNal(
            too_many_picture_parameter_sets,
            picture_parameter_sets.back());
    }
    AppendAnnexBNal(too_many_picture_parameter_sets, idr);
    CHECK(ConvertH264AnnexBToAvcc(
              too_many_picture_parameter_sets,
              16,
              16,
              H264Profile::High,
              result) == VRREC_STATUS_INVALID_ARGUMENT);
}

void RejectsDifferentParameterSetsWithTheSameIdentifier()
{
    test::SpsSettings first_sps_settings;
    test::SpsSettings conflicting_sps_settings;
    conflicting_sps_settings.level_idc = 41;
    const auto first_sps = test::MakeSps(first_sps_settings);
    const auto conflicting_sps = test::MakeSps(conflicting_sps_settings);
    const auto pps = test::MakePps(test::PpsSettings {});
    const auto idr = Bytes(Idr);
    H264AnnexBConversionResult result {};
    CHECK(ConvertH264AnnexBToAvcc(
              AnnexBBytes(
                  {std::span<const std::byte> {first_sps},
                   std::span<const std::byte> {conflicting_sps},
                   std::span<const std::byte> {pps},
                   std::span<const std::byte> {idr}}),
              16,
              16,
              H264Profile::High,
              result) == VRREC_STATUS_INVALID_ARGUMENT);

    test::SpsSettings second_sps_settings;
    second_sps_settings.sequence_parameter_set_id = 1;
    const auto second_sps = test::MakeSps(second_sps_settings);
    test::PpsSettings first_pps_settings;
    test::PpsSettings conflicting_pps_settings;
    conflicting_pps_settings.sequence_parameter_set_id = 1;
    const auto first_pps = test::MakePps(first_pps_settings);
    const auto conflicting_pps = test::MakePps(conflicting_pps_settings);
    CHECK(ConvertH264AnnexBToAvcc(
              AnnexBBytes(
                  {std::span<const std::byte> {first_sps},
                   std::span<const std::byte> {second_sps},
                   std::span<const std::byte> {first_pps},
                   std::span<const std::byte> {conflicting_pps},
                   std::span<const std::byte> {idr}}),
              16,
              16,
              H264Profile::High,
              result) == VRREC_STATUS_INVALID_ARGUMENT);
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
    ConvertsCroppedHighProfileSpsAtRecordingResolution();
    ConvertsMainProfileSpsAtRecordingResolution();
    RejectsUnsupportedChromaFormatAndBitDepth();
    RejectsParameterSetsThatExceedAvccLengthFields();
    SortsDistinctParameterSetsByIdentifier();
    RejectsPictureParameterSetWithMissingSequenceParameterSet();
    RejectsParameterSetCountOverflow();
    RejectsDifferentParameterSetsWithTheSameIdentifier();
    DedupesRepeatedIdenticalParameterSets();
    RejectsMalformedOrIncompleteAnnexB();
    return 0;
}
