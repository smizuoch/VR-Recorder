#include "annex_b_encoder_probe_decoder.hpp"
#include "allocation_failure_test_support.hpp"

#include <cstddef>
#include <cstdlib>
#include <functional>
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

H264StreamDescriptor Descriptor()
{
    return {
        MicrosecondPacketTimeBase,
        16,
        16,
        H264Profile::High,
        H264PacketFormat::AvccLengthPrefixed,
        {
            std::byte {1}, std::byte {100}, std::byte {0}, std::byte {40},
            std::byte {0xff}, std::byte {0xe1},
            std::byte {0}, std::byte {2}, std::byte {0x67}, std::byte {1},
            std::byte {1},
            std::byte {0}, std::byte {2}, std::byte {0x68}, std::byte {2},
        },
    };
}

EncodedMediaPacket Packet(std::int64_t pts, std::byte nal)
{
    return {
        MediaStreamKind::Video,
        pts,
        pts,
        33'333,
        pts == 0,
        {
            std::byte {0}, std::byte {0}, std::byte {0}, std::byte {2},
            nal, std::byte {3},
        },
    };
}

class FakePort final : public AnnexBH264DecodePort {
public:
    vrrec_status_t Begin(
        std::uint32_t width,
        std::uint32_t height) noexcept override
    {
        ++begin_calls;
        observed_width = width;
        observed_height = height;
        return begin_status;
    }

    vrrec_status_t Submit(
        std::span<const std::byte> access_unit,
        std::int64_t pts_microseconds,
        std::int64_t duration_microseconds) noexcept override
    {
        ++submit_calls;
        if (submit_calls == fail_submit_call) {
            return VRREC_STATUS_BACKEND_UNAVAILABLE;
        }
        try {
            access_units.emplace_back(access_unit.begin(), access_unit.end());
            pts.push_back(pts_microseconds);
            durations.push_back(duration_microseconds);
        } catch (...) {
            return VRREC_STATUS_OUT_OF_MEMORY;
        }
        return VRREC_STATUS_OK;
    }

    EncoderProbeDecodeResult Finish() noexcept override
    {
        ++finish_calls;
        return finish_result;
    }

    void Abort() noexcept override
    {
        ++abort_calls;
    }

    vrrec_status_t begin_status = VRREC_STATUS_OK;
    EncoderProbeDecodeResult finish_result {
        VRREC_STATUS_OK,
        16,
        16,
        2,
        0,
    };
    std::vector<std::vector<std::byte>> access_units;
    std::vector<std::int64_t> pts;
    std::vector<std::int64_t> durations;
    std::size_t begin_calls = 0;
    std::size_t submit_calls = 0;
    std::size_t finish_calls = 0;
    std::size_t abort_calls = 0;
    std::size_t fail_submit_call = 0;
    std::uint32_t observed_width = 0;
    std::uint32_t observed_height = 0;
};

void PrependsParameterSetsOnlyToTheFirstAccessUnit()
{
    FakePort port;
    AnnexBEncoderProbeDecoder decoder(port);
    const std::vector packets {Packet(0, std::byte {0x65}),
                               Packet(33'333, std::byte {0x41})};

    const auto result = decoder.Decode(Descriptor(), packets);
    CHECK(result.status == VRREC_STATUS_OK);
    CHECK(port.begin_calls == 1);
    CHECK(port.observed_width == 16);
    CHECK(port.observed_height == 16);
    CHECK(port.submit_calls == 2);
    CHECK(port.finish_calls == 1);
    CHECK(port.abort_calls == 0);
    CHECK(port.pts == std::vector<std::int64_t>({0, 33'333}));
    CHECK(port.durations ==
          std::vector<std::int64_t>({33'333, 33'333}));
    const std::vector<std::byte> first_prefix {
        std::byte {0}, std::byte {0}, std::byte {0}, std::byte {1},
        std::byte {0x67}, std::byte {1},
        std::byte {0}, std::byte {0}, std::byte {0}, std::byte {1},
        std::byte {0x68}, std::byte {2},
    };
    CHECK(port.access_units[0].size() > first_prefix.size());
    CHECK(std::equal(
        first_prefix.begin(),
        first_prefix.end(),
        port.access_units[0].begin()));
    CHECK(port.access_units[1].size() == 6);
}

void AbortsExactlyOnceOnBeginSubmitAndFinishFailure()
{
    const std::vector packets {Packet(0, std::byte {0x65}),
                               Packet(33'333, std::byte {0x41})};
    {
        FakePort port;
        port.begin_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
        AnnexBEncoderProbeDecoder decoder(port);
        CHECK(decoder.Decode(Descriptor(), packets).status ==
              VRREC_STATUS_BACKEND_UNAVAILABLE);
        CHECK(port.submit_calls == 0);
        CHECK(port.abort_calls == 1);
    }
    {
        FakePort port;
        port.fail_submit_call = 2;
        AnnexBEncoderProbeDecoder decoder(port);
        CHECK(decoder.Decode(Descriptor(), packets).status ==
              VRREC_STATUS_BACKEND_UNAVAILABLE);
        CHECK(port.finish_calls == 0);
        CHECK(port.abort_calls == 1);
    }
    {
        FakePort port;
        port.finish_result.status = VRREC_STATUS_INTERNAL_ERROR;
        AnnexBEncoderProbeDecoder decoder(port);
        CHECK(decoder.Decode(Descriptor(), packets).status ==
              VRREC_STATUS_INTERNAL_ERROR);
        CHECK(port.abort_calls == 1);
    }
}

void RejectsInvalidPacketContractBeforeStartingThePort()
{
    auto descriptor = Descriptor();
    auto packet = Packet(0, std::byte {0x65});
    const std::vector<EncodedMediaPacket> valid {packet};
    FakePort port;
    AnnexBEncoderProbeDecoder decoder(port);

    descriptor.packet_format = H264PacketFormat::AnnexB;
    CHECK(decoder.Decode(descriptor, valid).status ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(port.begin_calls == 0);

    descriptor = Descriptor();
    descriptor.profile = static_cast<H264Profile>(99);
    CHECK(decoder.Decode(descriptor, valid).status ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(port.begin_calls == 0);

    descriptor = Descriptor();
    packet.stream = MediaStreamKind::Audio;
    CHECK(decoder.Decode(descriptor, std::span(&packet, 1)).status ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(port.begin_calls == 0);
}

void RejectsEveryDescriptorAndPacketContractBoundary()
{
    using DescriptorMutation =
        std::function<void(H264StreamDescriptor &)>;
    const std::vector<DescriptorMutation> descriptor_mutations {
        [](auto &value) { value.packet_time_base.denominator = 2; },
        [](auto &value) { value.width = 0; },
        [](auto &value) { value.width = 16'385; },
        [](auto &value) { value.width = 15; },
        [](auto &value) { value.height = 0; },
        [](auto &value) { value.height = 16'385; },
        [](auto &value) { value.height = 15; },
        [](auto &value) { value.codec_extradata.clear(); },
    };
    const std::vector<EncodedMediaPacket> valid {Packet(0, std::byte {0x65})};
    for (const auto &mutate : descriptor_mutations) {
        auto descriptor = Descriptor();
        mutate(descriptor);
        FakePort port;
        AnnexBEncoderProbeDecoder decoder(port);
        CHECK(decoder.Decode(descriptor, valid).status ==
              VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(port.begin_calls == 0);
    }

    auto main_profile = Descriptor();
    main_profile.profile = H264Profile::Main;
    FakePort main_port;
    AnnexBEncoderProbeDecoder main_decoder(main_port);
    CHECK(main_decoder.Decode(main_profile, valid).status ==
          VRREC_STATUS_OK);

    using PacketMutation = std::function<void(EncodedMediaPacket &)>;
    const std::vector<PacketMutation> packet_mutations {
        [](auto &value) { value.pts_microseconds = UnknownMediaTimestamp; },
        [](auto &value) { value.dts_microseconds = UnknownMediaTimestamp; },
        [](auto &value) {
            value.pts_microseconds = 0;
            value.dts_microseconds = 1;
        },
        [](auto &value) { value.duration_microseconds = 0; },
        [](auto &value) { value.payload.clear(); },
        [](auto &value) {
            value.side_data.push_back({
                EncodedPacketSideDataKind::SkipSamples,
                {std::byte {0}},
            });
        },
    };
    for (const auto &mutate : packet_mutations) {
        auto packet = Packet(0, std::byte {0x65});
        mutate(packet);
        FakePort port;
        AnnexBEncoderProbeDecoder decoder(port);
        CHECK(decoder.Decode(
                  Descriptor(),
                  std::span(&packet, 1)).status ==
              VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(port.begin_calls == 0);
    }

    FakePort empty_port;
    AnnexBEncoderProbeDecoder empty_decoder(empty_port);
    CHECK(empty_decoder.Decode(Descriptor(), {}).status ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(empty_port.begin_calls == 0);
}

void RejectsMalformedH264BeforeSubmittingToTheDecodePort()
{
    auto malformed_descriptor = Descriptor();
    malformed_descriptor.codec_extradata[0] = std::byte {2};
    FakePort descriptor_port;
    AnnexBEncoderProbeDecoder descriptor_decoder(descriptor_port);
    const std::vector valid {Packet(0, std::byte {0x65})};
    CHECK(descriptor_decoder.Decode(malformed_descriptor, valid).status ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(descriptor_port.begin_calls == 0);

    auto malformed_packet = Packet(0, std::byte {0x65});
    malformed_packet.payload.pop_back();
    FakePort packet_port;
    AnnexBEncoderProbeDecoder packet_decoder(packet_port);
    CHECK(packet_decoder.Decode(
              Descriptor(),
              std::span(&malformed_packet, 1)).status ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(packet_port.begin_calls == 1);
    CHECK(packet_port.submit_calls == 0);
    CHECK(packet_port.abort_calls == 1);
}

void ReportsPrefixAllocationFailureAndAbortsTheDecodePort()
{
    const std::vector valid {Packet(0, std::byte {0x65})};
    FakePort port;
    AnnexBEncoderProbeDecoder decoder(port);

    allocation_failure::fail_on_allocation = 3;
    const auto result = decoder.Decode(Descriptor(), valid);
    allocation_failure::fail_on_allocation = 0;

    CHECK(result.status == VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(port.begin_calls == 1);
    CHECK(port.submit_calls == 0);
    CHECK(port.abort_calls == 1);
}

}

int main()
{
    PrependsParameterSetsOnlyToTheFirstAccessUnit();
    AbortsExactlyOnceOnBeginSubmitAndFinishFailure();
    RejectsInvalidPacketContractBeforeStartingThePort();
    RejectsEveryDescriptorAndPacketContractBoundary();
    RejectsMalformedH264BeforeSubmittingToTheDecodePort();
    ReportsPrefixAllocationFailureAndAbortsTheDecodePort();
    return 0;
}
