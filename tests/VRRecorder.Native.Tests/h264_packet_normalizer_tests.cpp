#include "h264_packet_normalizer.hpp"
#include "h264_test_vectors.hpp"

#include <cstddef>
#include <cstdint>
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
using namespace vrrecorder::native::test;

H264VideoEncoderConfig ExactConfig()
{
    H264VideoEncoderConfig config {};
    CHECK(CreateH264VideoEncoderConfig(
              32,
              16,
              30,
              true,
              config) == VRREC_STATUS_OK);
    return config;
}

std::vector<std::byte> AnnexB(
    std::initializer_list<std::span<const std::byte>> nals)
{
    std::vector<std::byte> bytes;
    for (const auto nal : nals) {
        bytes.insert(
            bytes.end(),
            {std::byte {0}, std::byte {0}, std::byte {0}, std::byte {1}});
        bytes.insert(bytes.end(), nal.begin(), nal.end());
    }
    return bytes;
}

std::vector<std::byte> LengthPrefixed(std::span<const std::byte> nal)
{
    const auto size = static_cast<std::uint32_t>(nal.size());
    std::vector<std::byte> bytes {
        static_cast<std::byte>((size >> 24U) & 0xffU),
        static_cast<std::byte>((size >> 16U) & 0xffU),
        static_cast<std::byte>((size >> 8U) & 0xffU),
        static_cast<std::byte>(size & 0xffU),
    };
    bytes.insert(bytes.end(), nal.begin(), nal.end());
    return bytes;
}

std::vector<std::byte> Sps(std::uint8_t compatibility = 0)
{
    SpsSettings settings;
    settings.compatibility = compatibility;
    settings.pic_width_in_mbs_minus1 = 1;
    settings.pic_height_in_map_units_minus1 = 0;
    return MakeSps(settings);
}

std::vector<std::byte> Pps()
{
    return MakePps({});
}

EncodedMediaPacket Packet(
    std::vector<std::byte> payload,
    bool key_frame,
    std::int64_t pts = 0)
{
    return {
        MediaStreamKind::Video,
        pts,
        pts,
        33'333,
        key_frame,
        std::move(payload),
        {},
    };
}

void DerivesTheDescriptorFromTheFirstIdrAndConvertsThePacket()
{
    const auto sps = Sps();
    const auto pps = Pps();
    const std::vector<std::byte> idr {std::byte {0x65}, std::byte {0x88}};
    H264PacketNormalizer normalizer(ExactConfig());

    const auto normalized = normalizer.Normalize(
        Packet(AnnexB({sps, pps, idr}), true));
    CHECK(normalized.status == VRREC_STATUS_OK);
    CHECK(normalized.descriptor_became_ready);
    CHECK(normalized.packet.stream == MediaStreamKind::Video);
    CHECK(normalized.packet.pts_microseconds == 0);
    CHECK(normalized.packet.dts_microseconds == 0);
    CHECK(normalized.packet.duration_microseconds == 33'333);
    CHECK(normalized.packet.key_frame);
    CHECK(normalized.packet.payload == LengthPrefixed(idr));
    CHECK(normalized.packet.side_data.empty());

    const auto *descriptor = normalizer.Descriptor();
    CHECK(descriptor != nullptr);
    CHECK(descriptor->packet_time_base == MicrosecondPacketTimeBase);
    CHECK(descriptor->width == 32);
    CHECK(descriptor->height == 16);
    CHECK(descriptor->profile == H264Profile::High);
    CHECK(descriptor->packet_format == H264PacketFormat::AvccLengthPrefixed);
    CHECK(!descriptor->codec_extradata.empty());
    CHECK(descriptor->codec_extradata[0] == std::byte {1});
}

void ConvertsFollowingNonIdrAndIdrWithoutRepublishedParameterSets()
{
    const auto sps = Sps();
    const auto pps = Pps();
    const std::vector<std::byte> idr {std::byte {0x65}, std::byte {0x88}};
    const std::vector<std::byte> non_idr {
        std::byte {0x41},
        std::byte {0x9a},
    };
    H264PacketNormalizer normalizer(ExactConfig());
    CHECK(normalizer.Normalize(
              Packet(AnnexB({sps, pps, idr}), true))
              .status == VRREC_STATUS_OK);

    const auto following = normalizer.Normalize(
        Packet(AnnexB({non_idr}), false, 33'333));
    CHECK(following.status == VRREC_STATUS_OK);
    CHECK(!following.descriptor_became_ready);
    CHECK(following.packet.payload == LengthPrefixed(non_idr));
    const auto repeated_idr = normalizer.Normalize(
        Packet(AnnexB({idr}), true, 66'666));
    CHECK(repeated_idr.status == VRREC_STATUS_OK);
    CHECK(!repeated_idr.descriptor_became_ready);
    CHECK(repeated_idr.packet.payload == LengthPrefixed(idr));
}

void InitializesTheDescriptorFromOpenedContextExtradata()
{
    const auto sps = Sps();
    const auto pps = Pps();
    const std::vector<std::byte> idr {std::byte {0x65}, std::byte {0x88}};
    H264PacketNormalizer normalizer(ExactConfig());

    CHECK(normalizer.InitializeFromAnnexBExtradata(
              AnnexB({sps, pps})) == VRREC_STATUS_OK);
    const auto *descriptor = normalizer.Descriptor();
    CHECK(descriptor != nullptr);
    CHECK(!descriptor->codec_extradata.empty());
    const auto extradata = descriptor->codec_extradata;
    CHECK(normalizer.InitializeFromAnnexBExtradata(
              AnnexB({sps, pps})) == VRREC_STATUS_OK);
    CHECK(normalizer.Descriptor()->codec_extradata == extradata);

    const auto normalized = normalizer.Normalize(
        Packet(AnnexB({idr}), true));
    CHECK(normalized.status == VRREC_STATUS_OK);
    CHECK(!normalized.descriptor_became_ready);
    CHECK(normalized.packet.payload == LengthPrefixed(idr));
}

void RejectsIncompleteOrConflictingContextExtradata()
{
    const auto sps = Sps();
    const auto changed_sps = Sps(4);
    const auto pps = Pps();
    H264PacketNormalizer incomplete(ExactConfig());
    CHECK(incomplete.InitializeFromAnnexBExtradata(
              AnnexB({sps})) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(incomplete.Descriptor() == nullptr);

    const std::vector<std::byte> idr {std::byte {0x65}, std::byte {0x88}};
    H264PacketNormalizer contains_vcl(ExactConfig());
    CHECK(contains_vcl.InitializeFromAnnexBExtradata(
              AnnexB({sps, pps, idr})) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(contains_vcl.Descriptor() == nullptr);

    H264PacketNormalizer conflicting(ExactConfig());
    CHECK(conflicting.InitializeFromAnnexBExtradata(
              AnnexB({sps, pps})) == VRREC_STATUS_OK);
    const auto original = conflicting.Descriptor()->codec_extradata;
    CHECK(conflicting.InitializeFromAnnexBExtradata(
              AnnexB({changed_sps, pps})) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(conflicting.Descriptor()->codec_extradata == original);
    CHECK(conflicting.InitializeFromAnnexBExtradata(
              AnnexB({sps, pps})) == VRREC_STATUS_INVALID_STATE);
}

void RejectsAFirstPacketWithoutACompleteDescriptor()
{
    const std::vector<std::byte> non_idr {
        std::byte {0x41},
        std::byte {0x9a},
    };
    H264PacketNormalizer normalizer(ExactConfig());

    CHECK(normalizer.Normalize(Packet(AnnexB({non_idr}), false)).status ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(normalizer.Descriptor() == nullptr);
    CHECK(normalizer.Normalize(Packet(AnnexB({non_idr}), false)).status ==
          VRREC_STATUS_INVALID_STATE);
}

void RejectsPacketFlagsThatDisagreeWithTheBitstream()
{
    const auto sps = Sps();
    const auto pps = Pps();
    const std::vector<std::byte> idr {std::byte {0x65}, std::byte {0x88}};
    H264PacketNormalizer normalizer(ExactConfig());

    CHECK(normalizer.Normalize(
              Packet(AnnexB({sps, pps, idr}), false))
              .status == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(normalizer.Descriptor() == nullptr);
}

void RejectsParameterSetChangesAfterDescriptorReadiness()
{
    const auto sps = Sps();
    const auto changed_sps = Sps(4);
    const auto pps = Pps();
    const std::vector<std::byte> idr {std::byte {0x65}, std::byte {0x88}};
    H264PacketNormalizer normalizer(ExactConfig());
    CHECK(normalizer.Normalize(
              Packet(AnnexB({sps, pps, idr}), true))
              .status == VRREC_STATUS_OK);
    const auto original_extradata =
        normalizer.Descriptor()->codec_extradata;

    CHECK(normalizer.Normalize(
              Packet(AnnexB({changed_sps, pps, idr}), true, 33'333))
              .status == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(normalizer.Descriptor()->codec_extradata == original_extradata);
}

void RejectsInvalidPacketMetadataAndConfig()
{
    const auto sps = Sps();
    const auto pps = Pps();
    const std::vector<std::byte> idr {std::byte {0x65}, std::byte {0x88}};
    auto packet = Packet(AnnexB({sps, pps, idr}), true);
    packet.stream = MediaStreamKind::Audio;
    H264PacketNormalizer wrong_stream(ExactConfig());
    CHECK(wrong_stream.Normalize(packet).status ==
          VRREC_STATUS_INVALID_ARGUMENT);

    packet = Packet(AnnexB({sps, pps, idr}), true);
    packet.side_data.push_back({
        EncodedPacketSideDataKind::SkipSamples,
        std::vector<std::byte>(SkipSamplesSideDataSize),
    });
    H264PacketNormalizer side_data(ExactConfig());
    CHECK(side_data.Normalize(packet).status ==
          VRREC_STATUS_INVALID_ARGUMENT);

    auto config = ExactConfig();
    config.input_pixel_format = VRREC_SOURCE_PIXEL_FORMAT_RGBA8;
    H264PacketNormalizer invalid_config(config);
    CHECK(invalid_config.Normalize(
              Packet(AnnexB({sps, pps, idr}), true))
              .status == VRREC_STATUS_INVALID_ARGUMENT);
}

void AbortMakesTheNormalizerTerminal()
{
    const std::vector<std::byte> non_idr {
        std::byte {0x41},
        std::byte {0x9a},
    };
    H264PacketNormalizer normalizer(ExactConfig());
    normalizer.Abort();
    normalizer.Abort();

    CHECK(normalizer.Normalize(Packet(AnnexB({non_idr}), false)).status ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(normalizer.Descriptor() == nullptr);
}

}

int main()
{
    DerivesTheDescriptorFromTheFirstIdrAndConvertsThePacket();
    ConvertsFollowingNonIdrAndIdrWithoutRepublishedParameterSets();
    InitializesTheDescriptorFromOpenedContextExtradata();
    RejectsIncompleteOrConflictingContextExtradata();
    RejectsAFirstPacketWithoutACompleteDescriptor();
    RejectsPacketFlagsThatDisagreeWithTheBitstream();
    RejectsParameterSetChangesAfterDescriptorReadiness();
    RejectsInvalidPacketMetadataAndConfig();
    AbortMakesTheNormalizerTerminal();
    return 0;
}
