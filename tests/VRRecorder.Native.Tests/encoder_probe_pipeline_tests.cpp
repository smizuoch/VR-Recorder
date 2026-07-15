#include "encoder_probe_pipeline.hpp"
#include "h264_test_vectors.hpp"

#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <memory>
#include <span>
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

vrrec_encoder_probe_config_v1 Config()
{
    static constexpr char GpuIdentity[] =
        "pci\\ven_10de&dev_2684|driver-32.0.16.1062";
    return {
        sizeof(vrrec_encoder_probe_config_v1),
        VRREC_ABI_V1,
        VRREC_ENCODER_NVENC,
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

EncoderProbeOpenedIdentity Opened()
{
    return {
        VRREC_ENCODER_NVENC,
        "h264_nvenc",
        true,
        AdapterLuid,
        AdapterLuid,
        AdapterLuid,
        VRREC_ENCODER_INPUT_D3D11_NV12,
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

EncodedMediaPacket Packet(const EncoderProbeSyntheticFrame &frame)
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
    return {
        MediaStreamKind::Video,
        frame.pts_microseconds,
        frame.pts_microseconds,
        frame.duration_microseconds,
        frame.frame_index == 0,
        frame.frame_index == 0
            ? AnnexB({sps, pps, idr})
            : AnnexB({predicted}),
        {},
    };
}

struct SessionState final {
    std::vector<EncoderProbeSyntheticFrame> frames;
    vrrec_status_t encode_status = VRREC_STATUS_OK;
    std::uint32_t fail_encode_at = SyntheticFrameCount;
    vrrec_status_t finish_status = VRREC_STATUS_OK;
    bool buffer_until_finish = false;
    bool emit_pairs = false;
    std::uint32_t finish_count = 0;
    std::uint32_t abort_count = 0;
};

class FakeEncodeSession final : public EncoderProbeEncodeSession {
public:
    FakeEncodeSession(
        SessionState &state,
        EncoderProbeOpenedIdentity opened)
        : state_(state), opened_(std::move(opened))
    {
    }

    const EncoderProbeOpenedIdentity &OpenedIdentity() const noexcept override
    {
        return opened_;
    }

    FfmpegEncodeBatch EncodeSyntheticFrame(
        const EncoderProbeSyntheticFrame &frame) noexcept override
    {
        state_.frames.push_back(frame);
        if (frame.frame_index == state_.fail_encode_at) {
            return {state_.encode_status, {}};
        }
        if (state_.buffer_until_finish) {
            return {VRREC_STATUS_OK, {}};
        }
        std::vector<EncodedMediaPacket> packets;
        if (state_.emit_pairs && (frame.frame_index & 1U) == 0) {
            return {VRREC_STATUS_OK, {}};
        }
        if (state_.emit_pairs) {
            packets.push_back(Packet(
                state_.frames[state_.frames.size() - 2U]));
        }
        packets.push_back(Packet(frame));
        return {VRREC_STATUS_OK, std::move(packets)};
    }

    FfmpegEncodeBatch Finish() noexcept override
    {
        ++state_.finish_count;
        if (state_.finish_status != VRREC_STATUS_OK) {
            return {state_.finish_status, {}};
        }
        std::vector<EncodedMediaPacket> packets;
        if (state_.buffer_until_finish) {
            packets.reserve(state_.frames.size());
            for (const auto &frame : state_.frames) {
                packets.push_back(Packet(frame));
            }
        }
        return {VRREC_STATUS_OK, std::move(packets)};
    }

    void Abort() noexcept override
    {
        ++state_.abort_count;
    }

private:
    SessionState &state_;
    EncoderProbeOpenedIdentity opened_;
};

struct FactoryState final {
    std::uint32_t call_count = 0;
    vrrec_status_t status = VRREC_STATUS_OK;
    bool return_session = true;
    vrrec_encoder_probe_config_v1 observed {};
};

class FakeFactory final : public EncoderProbeEncodeSessionFactoryPort {
public:
    FakeFactory(
        FactoryState &factory,
        SessionState &session,
        EncoderProbeOpenedIdentity opened = Opened())
        : factory_(factory), session_(session), opened_(std::move(opened))
    {
    }

    EncoderProbeEncodeSessionCreateResult Create(
        const vrrec_encoder_probe_config_v1 &config) noexcept override
    {
        ++factory_.call_count;
        factory_.observed = config;
        if (!factory_.return_session) {
            return {factory_.status, nullptr};
        }
        return {
            factory_.status,
            std::make_unique<FakeEncodeSession>(session_, opened_),
        };
    }

private:
    FactoryState &factory_;
    SessionState &session_;
    EncoderProbeOpenedIdentity opened_;
};

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
    std::size_t packet_count = 0;

    EncoderProbeDecodeResult Decode(
        const H264StreamDescriptor &,
        std::span<const EncodedMediaPacket> packets) noexcept override
    {
        ++call_count;
        packet_count = packets.size();
        return result;
    }
};

EncoderProbeEvidence SentinelEvidence()
{
    EncoderProbeEvidence evidence;
    evidence.codec_name = "sentinel";
    evidence.validation_flags = UINT32_C(0xa5a5a5a5);
    return evidence;
}

void EncodesExactlySixteenCanonicalFramesBeforePublishingEvidence()
{
    FactoryState factory_state;
    SessionState session_state;
    FakeFactory factory(factory_state, session_state);
    FakeDecoder decoder;
    auto evidence = SentinelEvidence();

    CHECK(RunVerifiedEncoderProbe(
              Config(),
              factory,
              decoder,
              evidence) == VRREC_STATUS_OK);
    CHECK(factory_state.call_count == 1);
    CHECK(factory_state.observed.encoder_kind == VRREC_ENCODER_NVENC);
    CHECK(session_state.frames.size() == SyntheticFrameCount);
    for (std::uint32_t index = 0; index < SyntheticFrameCount; ++index) {
        const auto &frame = session_state.frames[index];
        const auto expected_start =
            static_cast<std::int64_t>(index) * 1'000'000 /
            FramesPerSecond;
        const auto expected_end =
            static_cast<std::int64_t>(index + 1U) * 1'000'000 /
            FramesPerSecond;
        CHECK(frame.frame_index == index);
        CHECK(frame.width == Width);
        CHECK(frame.height == Height);
        CHECK(frame.pts_microseconds == expected_start);
        CHECK(frame.duration_microseconds == expected_end - expected_start);
    }
    CHECK(session_state.finish_count == 1);
    CHECK(session_state.abort_count == 0);
    CHECK(decoder.call_count == 1);
    CHECK(decoder.packet_count == SyntheticFrameCount);
    CHECK(evidence.codec_name == "h264_nvenc");
    CHECK(evidence.validation_flags == UINT32_C(0x07ff));
}

void FinishDrainPacketsAreIncludedInTheSameDecodeProof()
{
    FactoryState factory_state;
    SessionState session_state;
    session_state.buffer_until_finish = true;
    FakeFactory factory(factory_state, session_state);
    FakeDecoder decoder;
    EncoderProbeEvidence evidence;

    CHECK(RunVerifiedEncoderProbe(
              Config(),
              factory,
              decoder,
              evidence) == VRREC_STATUS_OK);
    CHECK(session_state.frames.size() == SyntheticFrameCount);
    CHECK(session_state.finish_count == 1);
    CHECK(decoder.call_count == 1);
    CHECK(decoder.packet_count == SyntheticFrameCount);
}

void ZeroAndMultiplePacketEncodeBatchesRemainOrdered()
{
    FactoryState factory_state;
    SessionState session_state;
    session_state.emit_pairs = true;
    FakeFactory factory(factory_state, session_state);
    FakeDecoder decoder;
    EncoderProbeEvidence evidence;

    CHECK(RunVerifiedEncoderProbe(
              Config(),
              factory,
              decoder,
              evidence) == VRREC_STATUS_OK);
    CHECK(session_state.frames.size() == SyntheticFrameCount);
    CHECK(session_state.finish_count == 1);
    CHECK(decoder.call_count == 1);
    CHECK(decoder.packet_count == SyntheticFrameCount);
}

void FactoryFailureNeverStartsEncodingOrPublishesEvidence()
{
    FactoryState factory_state;
    factory_state.status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    factory_state.return_session = false;
    SessionState session_state;
    FakeFactory factory(factory_state, session_state);
    FakeDecoder decoder;
    auto evidence = SentinelEvidence();

    CHECK(RunVerifiedEncoderProbe(
              Config(),
              factory,
              decoder,
              evidence) == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(factory_state.call_count == 1);
    CHECK(session_state.frames.empty());
    CHECK(decoder.call_count == 0);
    CHECK(evidence.codec_name == "sentinel");

    factory_state.status = VRREC_STATUS_OK;
    CHECK(RunVerifiedEncoderProbe(
              Config(),
              factory,
              decoder,
              evidence) == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(evidence.codec_name == "sentinel");

    factory_state.status = VRREC_STATUS_INTERNAL_ERROR;
    factory_state.return_session = true;
    CHECK(RunVerifiedEncoderProbe(
              Config(),
              factory,
              decoder,
              evidence) == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(session_state.abort_count == 1);
    CHECK(evidence.codec_name == "sentinel");
}

void EncodeOrFinishFailureAbortsExactlyOnce()
{
    FactoryState factory_state;
    SessionState encode_failure;
    encode_failure.fail_encode_at = 4;
    encode_failure.encode_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    FakeFactory encode_factory(factory_state, encode_failure);
    FakeDecoder decoder;
    auto evidence = SentinelEvidence();

    CHECK(RunVerifiedEncoderProbe(
              Config(),
              encode_factory,
              decoder,
              evidence) == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(encode_failure.frames.size() == 5);
    CHECK(encode_failure.finish_count == 0);
    CHECK(encode_failure.abort_count == 1);
    CHECK(decoder.call_count == 0);
    CHECK(evidence.codec_name == "sentinel");

    SessionState finish_failure;
    finish_failure.finish_status = VRREC_STATUS_INTERNAL_ERROR;
    FakeFactory finish_factory(factory_state, finish_failure);
    CHECK(RunVerifiedEncoderProbe(
              Config(),
              finish_factory,
              decoder,
              evidence) == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(finish_failure.frames.size() == SyntheticFrameCount);
    CHECK(finish_failure.finish_count == 1);
    CHECK(finish_failure.abort_count == 1);
    CHECK(decoder.call_count == 0);
    CHECK(evidence.codec_name == "sentinel");
}

void IdentityOrDecodeFailureAfterFinishDoesNotPublishEvidence()
{
    FactoryState factory_state;
    SessionState identity_session;
    auto mismatched = Opened();
    mismatched.encoder_adapter_luid++;
    FakeFactory identity_factory(
        factory_state,
        identity_session,
        mismatched);
    FakeDecoder decoder;
    auto evidence = SentinelEvidence();

    CHECK(RunVerifiedEncoderProbe(
              Config(),
              identity_factory,
              decoder,
              evidence) == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(identity_session.finish_count == 1);
    CHECK(identity_session.abort_count == 0);
    CHECK(decoder.call_count == 0);
    CHECK(evidence.codec_name == "sentinel");

    SessionState decode_session;
    FakeFactory decode_factory(factory_state, decode_session);
    decoder.result.status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    CHECK(RunVerifiedEncoderProbe(
              Config(),
              decode_factory,
              decoder,
              evidence) == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(decode_session.finish_count == 1);
    CHECK(decode_session.abort_count == 0);
    CHECK(decoder.call_count == 1);
    CHECK(evidence.codec_name == "sentinel");
}

void BackendKeepsLegacyBoolAndStructuredEvidenceOnTheSamePipeline()
{
    FactoryState factory_state;
    SessionState session_state;
    FakeFactory factory(factory_state, session_state);
    FakeDecoder decoder;
    VerifiedEncoderProbeBackend backend(factory, decoder);

    bool packet_produced = false;
    CHECK(backend.Probe(Config(), packet_produced) == VRREC_STATUS_OK);
    CHECK(packet_produced);
    CHECK(factory_state.call_count == 1);
    CHECK(decoder.call_count == 1);

    EncoderProbeEvidence evidence;
    CHECK(backend.ProbeV2(Config(), evidence) == VRREC_STATUS_OK);
    CHECK(factory_state.call_count == 2);
    CHECK(decoder.call_count == 2);
    CHECK(evidence.actual_encoder_kind == VRREC_ENCODER_NVENC);
    CHECK(evidence.codec_name == "h264_nvenc");
    CHECK(evidence.validation_flags == UINT32_C(0x07ff));
}

void BackendNeverPublishesLegacySuccessAfterVerificationFailure()
{
    FactoryState factory_state;
    factory_state.status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    factory_state.return_session = false;
    SessionState session_state;
    FakeFactory factory(factory_state, session_state);
    FakeDecoder decoder;
    VerifiedEncoderProbeBackend backend(factory, decoder);

    bool packet_produced = true;
    CHECK(backend.Probe(Config(), packet_produced) ==
          VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(!packet_produced);
    auto evidence = SentinelEvidence();
    CHECK(backend.ProbeV2(Config(), evidence) ==
          VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(evidence.codec_name == "sentinel");
}

}

int main()
{
    EncodesExactlySixteenCanonicalFramesBeforePublishingEvidence();
    FinishDrainPacketsAreIncludedInTheSameDecodeProof();
    ZeroAndMultiplePacketEncodeBatchesRemainOrdered();
    FactoryFailureNeverStartsEncodingOrPublishesEvidence();
    EncodeOrFinishFailureAbortsExactlyOnce();
    IdentityOrDecodeFailureAfterFinishDoesNotPublishEvidence();
    BackendKeepsLegacyBoolAndStructuredEvidenceOnTheSamePipeline();
    BackendNeverPublishesLegacySuccessAfterVerificationFailure();
}
