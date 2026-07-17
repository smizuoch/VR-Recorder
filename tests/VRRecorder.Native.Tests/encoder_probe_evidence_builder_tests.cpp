#include "encoder_probe_evidence_builder.hpp"
#include "allocation_failure_test_support.hpp"
#include "h264_test_vectors.hpp"

#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <functional>
#include <iostream>
#include <span>
#include <string>
#include <utility>
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

constexpr std::uint64_t AdapterLuid = UINT64_C(0x00000001abcdef01);
constexpr std::uint32_t Width = 32;
constexpr std::uint32_t Height = 16;
constexpr std::uint32_t FramesPerSecond = 30;
constexpr std::uint32_t SyntheticFrameCount = 16;

struct ExpectedIdentity final {
    vrrec_encoder_kind_t kind;
    const char *codec_name;
    bool hardware_accelerated;
    vrrec_encoder_input_format_t input_format;
};

constexpr ExpectedIdentity ExpectedIdentities[] {
    {
        VRREC_ENCODER_NVENC,
        "h264_nvenc",
        true,
        VRREC_ENCODER_INPUT_D3D11_NV12,
    },
    {
        VRREC_ENCODER_AMF,
        "h264_amf",
        true,
        VRREC_ENCODER_INPUT_D3D11_NV12,
    },
    {
        VRREC_ENCODER_QSV,
        "h264_qsv",
        true,
        VRREC_ENCODER_INPUT_QSV_NV12,
    },
    {
        VRREC_ENCODER_MEDIA_FOUNDATION_SOFTWARE,
        "h264_mf",
        false,
        VRREC_ENCODER_INPUT_SYSTEM_MEMORY_NV12,
    },
};

vrrec_encoder_probe_config_v1 Config(vrrec_encoder_kind_t kind)
{
    static constexpr char GpuIdentity[] =
        "pci\\ven_10de&dev_2684|driver-32.0.16.1062";
    return {
        sizeof(vrrec_encoder_probe_config_v1),
        VRREC_ABI_V1,
        kind,
        SyntheticFrameCount,
        AdapterLuid,
        Width,
        Height,
        FramesPerSecond,
        1,
        GpuIdentity,
        0,
    };
}

EncoderProbeOpenedIdentity Opened(const ExpectedIdentity &expected)
{
    return {
        expected.kind,
        expected.codec_name,
        expected.hardware_accelerated,
        AdapterLuid,
        AdapterLuid,
        expected.hardware_accelerated ? AdapterLuid : 0,
        expected.input_format,
        Width,
        Height,
        FramesPerSecond,
        1,
        H264Profile::High,
        0,
        "nvidia|32.0.16.1062",
        "ffmpeg|8.1.2|contract-id",
        "pci\\ven_10de&dev_2684",
    };
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

std::vector<EncodedMediaPacket> Packets()
{
    SpsSettings settings;
    settings.pic_width_in_mbs_minus1 = 1;
    settings.pic_height_in_map_units_minus1 = 0;
    const auto sps = MakeSps(settings);
    const auto pps = MakePps({});
    const std::vector<std::byte> idr {std::byte {0x65}, std::byte {0x88}};
    const std::vector<std::byte> predicted {
        std::byte {0x41},
        std::byte {0x9a},
    };

    std::vector<EncodedMediaPacket> packets;
    packets.reserve(SyntheticFrameCount);
    for (std::uint32_t index = 0; index < SyntheticFrameCount; ++index) {
        const auto timestamp = static_cast<std::int64_t>(index) * 33'333;
        packets.push_back({
            MediaStreamKind::Video,
            timestamp,
            timestamp,
            33'333,
            index == 0,
            index == 0
                ? AnnexB({sps, pps, idr})
                : AnnexB({predicted}),
            {},
        });
    }
    return packets;
}

class FakeDecoder final : public EncoderProbeDecodePort {
public:
    EncoderProbeDecodeResult result {
        VRREC_STATUS_OK,
        Width,
        Height,
        SyntheticFrameCount,
        0,
    };
    std::uint32_t call_count = 0;
    H264StreamDescriptor descriptor;
    std::vector<EncodedMediaPacket> packets;

    EncoderProbeDecodeResult Decode(
        const H264StreamDescriptor &input_descriptor,
        std::span<const EncodedMediaPacket> input_packets) noexcept override
    {
        ++call_count;
        descriptor = input_descriptor;
        packets.assign(input_packets.begin(), input_packets.end());
        return result;
    }
};

EncoderProbeEvidence SentinelEvidence()
{
    EncoderProbeEvidence evidence;
    evidence.actual_encoder_kind = VRREC_ENCODER_AMF;
    evidence.codec_name = "sentinel";
    evidence.validation_flags = UINT32_C(0xa5a5a5a5);
    return evidence;
}

void BuildsEvidenceOnlyFromParsedAndDecodedPackets()
{
    const auto &expected = ExpectedIdentities[0];
    const auto config = Config(expected.kind);
    const auto opened = Opened(expected);
    const auto packets = Packets();
    FakeDecoder decoder;
    auto evidence = SentinelEvidence();

    CHECK(BuildVerifiedEncoderProbeEvidence(
              config,
              opened,
              packets,
              decoder,
              evidence) == VRREC_STATUS_OK);
    CHECK(decoder.call_count == 1);
    CHECK(decoder.descriptor.width == Width);
    CHECK(decoder.descriptor.height == Height);
    CHECK(decoder.descriptor.profile == H264Profile::High);
    CHECK(decoder.descriptor.packet_format ==
          H264PacketFormat::AvccLengthPrefixed);
    CHECK(!decoder.descriptor.codec_extradata.empty());
    CHECK(decoder.packets.size() == SyntheticFrameCount);
    CHECK(decoder.packets[0].payload.size() >= 6);
    CHECK(decoder.packets[0].payload[0] == std::byte {0});
    CHECK(decoder.packets[0].payload[1] == std::byte {0});
    CHECK(decoder.packets[0].payload[2] == std::byte {0});
    CHECK(decoder.packets[0].payload[3] == std::byte {2});
    CHECK(decoder.packets[0].payload[4] == std::byte {0x65});

    CHECK(evidence.actual_encoder_kind == expected.kind);
    CHECK(evidence.codec_name == expected.codec_name);
    CHECK(evidence.hardware_accelerated);
    CHECK(evidence.adapter_luid == AdapterLuid);
    CHECK(evidence.opened_input_format == expected.input_format);
    CHECK(evidence.width == Width);
    CHECK(evidence.height == Height);
    CHECK(evidence.fps_numerator == FramesPerSecond);
    CHECK(evidence.fps_denominator == 1);
    CHECK(evidence.validation_flags == UINT32_C(0x07ff));
    CHECK(evidence.driver_identity == "nvidia|32.0.16.1062");
    CHECK(evidence.ffmpeg_build_identity ==
          "ffmpeg|8.1.2|contract-id");
    CHECK(evidence.profile == "high");
    CHECK(evidence.device_identity == "pci\\ven_10de&dev_2684");
}

void AcceptsOnlyTheExactIdentityForEachPublicEncoder()
{
    for (const auto &expected : ExpectedIdentities) {
        const auto config = Config(expected.kind);
        const auto opened = Opened(expected);
        const auto packets = Packets();
        FakeDecoder decoder;
        EncoderProbeEvidence evidence;

        CHECK(BuildVerifiedEncoderProbeEvidence(
                  config,
                  opened,
                  packets,
                  decoder,
                  evidence) == VRREC_STATUS_OK);
        CHECK(evidence.actual_encoder_kind == expected.kind);
        CHECK(evidence.codec_name == expected.codec_name);
        CHECK(evidence.hardware_accelerated ==
              expected.hardware_accelerated);
        CHECK(evidence.opened_input_format == expected.input_format);
        CHECK(evidence.validation_flags ==
              (expected.hardware_accelerated
                   ? UINT32_C(0x07ff)
                   : UINT32_C(0x03ff)));
    }
}

void RejectsIdentityAndAdapterMismatchesBeforeDecode()
{
    const auto &expected = ExpectedIdentities[0];
    const auto config = Config(expected.kind);
    using Mutation = std::function<void(EncoderProbeOpenedIdentity &)>;
    const std::vector<Mutation> mutations {
        [](auto &value) { value.actual_encoder_kind = VRREC_ENCODER_AMF; },
        [](auto &value) { value.codec_name = "h264_mf"; },
        [](auto &value) { value.hardware_accelerated = false; },
        [](auto &value) { value.source_adapter_luid++; },
        [](auto &value) { value.processor_adapter_luid++; },
        [](auto &value) { value.encoder_adapter_luid++; },
        [](auto &value) {
            value.opened_input_format =
                VRREC_ENCODER_INPUT_SYSTEM_MEMORY_NV12;
        },
        [](auto &value) { value.width += 2; },
        [](auto &value) { value.height += 2; },
        [](auto &value) { value.fps_numerator++; },
        [](auto &value) { value.fps_denominator++; },
        [](auto &value) { value.profile = H264Profile::Main; },
        [](auto &value) {
            value.profile = static_cast<H264Profile>(UINT32_MAX);
        },
        [](auto &value) { value.maximum_b_frame_count = 1; },
        [](auto &value) { value.driver_identity.clear(); },
        [](auto &value) { value.ffmpeg_build_identity.clear(); },
        [](auto &value) { value.device_identity.clear(); },
    };

    for (const auto &mutate : mutations) {
        auto opened = Opened(expected);
        mutate(opened);
        FakeDecoder decoder;
        auto evidence = SentinelEvidence();
        CHECK(BuildVerifiedEncoderProbeEvidence(
                  config,
                  opened,
                  Packets(),
                  decoder,
                  evidence) == VRREC_STATUS_INTERNAL_ERROR);
        CHECK(decoder.call_count == 0);
        CHECK(evidence.codec_name == "sentinel");
        CHECK(evidence.validation_flags == UINT32_C(0xa5a5a5a5));
    }

    const auto &software = ExpectedIdentities[3];
    const std::vector<Mutation> software_adapter_mutations {
        [](auto &value) { value.source_adapter_luid++; },
        [](auto &value) { value.processor_adapter_luid++; },
        [](auto &value) { value.encoder_adapter_luid = AdapterLuid; },
    };
    for (const auto &mutate : software_adapter_mutations) {
        const auto software_config = Config(software.kind);
        auto opened = Opened(software);
        mutate(opened);
        FakeDecoder decoder;
        auto evidence = SentinelEvidence();
        CHECK(BuildVerifiedEncoderProbeEvidence(
                  software_config,
                  opened,
                  Packets(),
                  decoder,
                  evidence) == VRREC_STATUS_INTERNAL_ERROR);
        CHECK(decoder.call_count == 0);
        CHECK(evidence.codec_name == "sentinel");
    }
}

void RejectsEveryMalformedProbeConfigurationBeforeDecode()
{
    const auto &expected = ExpectedIdentities[0];
    using Mutation =
        std::function<void(vrrec_encoder_probe_config_v1 &)>;
    const std::vector<Mutation> mutations {
        [](auto &value) { value.struct_size -= 1; },
        [](auto &value) { value.abi_version += 1; },
        [](auto &value) { value.synthetic_frame_count -= 1; },
        [](auto &value) { value.adapter_luid = 0; },
        [](auto &value) { value.fps_denominator = 2; },
        [](auto &value) { value.gpu_identity_utf8 = nullptr; },
        [](auto &value) { value.gpu_identity_utf8 = ""; },
        [](auto &value) { value.reserved = 1; },
        [](auto &value) { value.encoder_kind = UINT32_MAX; },
        [](auto &value) { value.width = 0; },
        [](auto &value) { value.height = 0; },
        [](auto &value) { value.fps_numerator = 0; },
    };

    for (const auto &mutate : mutations) {
        auto config = Config(expected.kind);
        mutate(config);
        FakeDecoder decoder;
        auto evidence = SentinelEvidence();
        CHECK(BuildVerifiedEncoderProbeEvidence(
                  config,
                  Opened(expected),
                  Packets(),
                  decoder,
                  evidence) == VRREC_STATUS_INTERNAL_ERROR);
        CHECK(decoder.call_count == 0);
        CHECK(evidence.codec_name == "sentinel");
    }
}

void RejectsMalformedPacketAndDecodeMismatchWithoutPublishingEvidence()
{
    const auto &expected = ExpectedIdentities[0];
    const auto config = Config(expected.kind);
    const auto opened = Opened(expected);

    FakeDecoder empty_decoder;
    auto evidence = SentinelEvidence();
    CHECK(BuildVerifiedEncoderProbeEvidence(
              config,
              opened,
              {},
              empty_decoder,
              evidence) == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(empty_decoder.call_count == 0);
    CHECK(evidence.codec_name == "sentinel");

    auto malformed = Packets();
    malformed[0].payload = {std::byte {0x65}, std::byte {0x88}};
    FakeDecoder uncalled_decoder;
    CHECK(BuildVerifiedEncoderProbeEvidence(
              config,
              opened,
              malformed,
              uncalled_decoder,
              evidence) == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(uncalled_decoder.call_count == 0);
    CHECK(evidence.codec_name == "sentinel");

    FakeDecoder failed_decoder;
    failed_decoder.result.status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    CHECK(BuildVerifiedEncoderProbeEvidence(
              config,
              opened,
              Packets(),
              failed_decoder,
              evidence) == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(failed_decoder.call_count == 1);
    CHECK(evidence.codec_name == "sentinel");

    FakeDecoder wrong_dimensions;
    wrong_dimensions.result.width += 2;
    CHECK(BuildVerifiedEncoderProbeEvidence(
              config,
              opened,
              Packets(),
              wrong_dimensions,
              evidence) == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(evidence.codec_name == "sentinel");

    FakeDecoder wrong_height;
    wrong_height.result.height += 2;
    CHECK(BuildVerifiedEncoderProbeEvidence(
              config,
              opened,
              Packets(),
              wrong_height,
              evidence) == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(evidence.codec_name == "sentinel");

    FakeDecoder wrong_frame_count;
    wrong_frame_count.result.decoded_frame_count--;
    CHECK(BuildVerifiedEncoderProbeEvidence(
              config,
              opened,
              Packets(),
              wrong_frame_count,
              evidence) == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(evidence.codec_name == "sentinel");

    FakeDecoder wrong_presentation_start;
    wrong_presentation_start.result.presentation_start_microseconds = 1;
    CHECK(BuildVerifiedEncoderProbeEvidence(
              config,
              opened,
              Packets(),
              wrong_presentation_start,
              evidence) == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(evidence.codec_name == "sentinel");
}

void ReportsAllocationFailureWithoutPublishingPartialEvidence()
{
    const auto &expected = ExpectedIdentities[0];
    const auto config = Config(expected.kind);
    const auto opened = Opened(expected);
    const auto packets = Packets();
    FakeDecoder decoder;
    auto evidence = SentinelEvidence();

    allocation_failure::fail_on_allocation = 1;
    const auto status = BuildVerifiedEncoderProbeEvidence(
        config,
        opened,
        packets,
        decoder,
        evidence);
    allocation_failure::fail_on_allocation = 0;

    CHECK(status == VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(decoder.call_count == 0);
    CHECK(evidence.codec_name == "sentinel");
    CHECK(evidence.validation_flags == UINT32_C(0xa5a5a5a5));
}

}

int main()
{
    BuildsEvidenceOnlyFromParsedAndDecodedPackets();
    AcceptsOnlyTheExactIdentityForEachPublicEncoder();
    RejectsIdentityAndAdapterMismatchesBeforeDecode();
    RejectsEveryMalformedProbeConfigurationBeforeDecode();
    RejectsMalformedPacketAndDecodeMismatchWithoutPublishingEvidence();
    ReportsAllocationFailureWithoutPublishingPartialEvidence();
}
