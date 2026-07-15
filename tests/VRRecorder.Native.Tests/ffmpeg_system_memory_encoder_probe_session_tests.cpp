#include "ffmpeg_system_memory_encoder_probe_session.hpp"
#include "h264_test_vectors.hpp"

#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <memory>
#include <span>
#include <utility>
#include <vector>

extern "C" {
#include <libavutil/frame.h>
#include <libavutil/pixfmt.h>
}

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

vrrec_encoder_probe_config_v1 Config()
{
    static constexpr char GpuIdentity[] = "software-probe-adapter";
    return {
        sizeof(vrrec_encoder_probe_config_v1),
        VRREC_ABI_V1,
        VRREC_ENCODER_MEDIA_FOUNDATION_SOFTWARE,
        EncoderProbeSyntheticFrameCount,
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
        VRREC_ENCODER_MEDIA_FOUNDATION_SOFTWARE,
        "h264_mf",
        false,
        AdapterLuid,
        AdapterLuid,
        0,
        VRREC_ENCODER_INPUT_SYSTEM_MEMORY_NV12,
        Width,
        Height,
        FramesPerSecond,
        1,
        H264Profile::High,
        0,
        "windows-media-foundation|software",
        "ffmpeg|8.1.2|contract-id",
        "software-encoder",
    };
}

AVFrame *AllocateFrame()
{
    auto *frame = av_frame_alloc();
    CHECK(frame != nullptr);
    frame->format = AV_PIX_FMT_NV12;
    frame->width = Width;
    frame->height = Height;
    CHECK(av_frame_get_buffer(frame, 32) == 0);
    return frame;
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

struct CodecState final {
    vrrec_status_t prepare_status = VRREC_STATUS_OK;
    std::uint32_t fail_prepare_at = EncoderProbeSyntheticFrameCount;
    vrrec_status_t encode_status = VRREC_STATUS_OK;
    std::uint32_t fail_encode_at = EncoderProbeSyntheticFrameCount;
    vrrec_status_t finish_status = VRREC_STATUS_OK;
    std::vector<std::int64_t> prepared_pts;
    std::vector<std::byte> first_luma;
    std::vector<std::byte> first_chroma;
    std::uint32_t encode_count = 0;
    std::uint32_t finish_count = 0;
    std::uint32_t abort_count = 0;
};

class FakeCodecSession final : public FfmpegH264CodecSession {
public:
    explicit FakeCodecSession(CodecState &state) : state_(state) {}

    vrrec_status_t PrepareFrame(const AVFrame &frame) noexcept override
    {
        const auto index = static_cast<std::uint32_t>(frame.pts);
        state_.prepared_pts.push_back(frame.pts);
        state_.first_luma.push_back(
            static_cast<std::byte>(frame.data[0][0]));
        state_.first_chroma.push_back(
            static_cast<std::byte>(frame.data[1][0]));
        return index == state_.fail_prepare_at
            ? state_.prepare_status
            : VRREC_STATUS_OK;
    }

    FfmpegEncodeBatch EncodePreparedFrame() noexcept override
    {
        const auto index = state_.encode_count++;
        if (index == state_.fail_encode_at) {
            return {state_.encode_status, {}};
        }

        SpsSettings settings;
        settings.pic_width_in_mbs_minus1 = 1;
        settings.pic_height_in_map_units_minus1 = 0;
        const auto sps = MakeSps(settings);
        const auto pps = MakePps({});
        const std::vector<std::byte> idr {
            std::byte {0x65},
            std::byte {0x88},
        };
        const std::vector<std::byte> predicted {
            std::byte {0x41},
            std::byte {0x9a},
        };
        const auto start = static_cast<std::int64_t>(index) * 1'000'000 /
            FramesPerSecond;
        const auto end = static_cast<std::int64_t>(index + 1U) * 1'000'000 /
            FramesPerSecond;
        std::vector<EncodedMediaPacket> packets;
        packets.push_back({
            MediaStreamKind::Video,
            start,
            start,
            end - start,
            index == 0,
            index == 0
                ? AnnexB({sps, pps, idr})
                : AnnexB({predicted}),
            {},
        });
        return {VRREC_STATUS_OK, std::move(packets)};
    }

    FfmpegEncodeBatch Finish() noexcept override
    {
        ++state_.finish_count;
        return {state_.finish_status, {}};
    }

    vrrec_status_t CopyCodecExtradata(
        std::vector<std::byte> &extradata) const noexcept override
    {
        extradata.clear();
        return VRREC_STATUS_OK;
    }

    void Abort() noexcept override
    {
        ++state_.abort_count;
    }

private:
    CodecState &state_;
};

struct FactoryState final {
    CodecState codec;
    std::uint32_t call_count = 0;
};

class AdapterFactory final : public EncoderProbeEncodeSessionFactoryPort {
public:
    explicit AdapterFactory(FactoryState &state) : state_(state) {}

    EncoderProbeEncodeSessionCreateResult Create(
        const vrrec_encoder_probe_config_v1 &) noexcept override
    {
        ++state_.call_count;
        return CreateFfmpegSystemMemoryEncoderProbeSession(
            Opened(),
            std::make_unique<FakeCodecSession>(state_.codec),
            AllocateFrame());
    }

private:
    FactoryState &state_;
};

class FakeDecoder final : public EncoderProbeDecodePort {
public:
    std::uint32_t call_count = 0;

    EncoderProbeDecodeResult Decode(
        const H264StreamDescriptor &,
        std::span<const EncodedMediaPacket> packets) noexcept override
    {
        ++call_count;
        CHECK(packets.size() == EncoderProbeSyntheticFrameCount);
        return {
            VRREC_STATUS_OK,
            Width,
            Height,
            EncoderProbeSyntheticFrameCount,
            0,
        };
    }
};

void SoftwareAdapterFeedsOwnedSyntheticFramesIntoTheVerifiedPipeline()
{
    FactoryState state;
    AdapterFactory factory(state);
    FakeDecoder decoder;
    EncoderProbeEvidence evidence;

    CHECK(RunVerifiedEncoderProbe(
              Config(),
              factory,
              decoder,
              evidence) == VRREC_STATUS_OK);
    CHECK(state.call_count == 1);
    CHECK(state.codec.prepared_pts.size() ==
          EncoderProbeSyntheticFrameCount);
    CHECK(state.codec.first_luma.size() ==
          EncoderProbeSyntheticFrameCount);
    CHECK(state.codec.first_chroma.size() ==
          EncoderProbeSyntheticFrameCount);
    for (std::uint32_t index = 0;
         index < EncoderProbeSyntheticFrameCount;
         ++index) {
        CHECK(state.codec.prepared_pts[index] == index);
        CHECK(state.codec.first_luma[index] == static_cast<std::byte>(
            16U + index * 17U % 220U));
        CHECK(state.codec.first_chroma[index] == static_cast<std::byte>(
            16U + index * 19U % 225U));
    }
    CHECK(state.codec.encode_count == EncoderProbeSyntheticFrameCount);
    CHECK(state.codec.finish_count == 1);
    CHECK(state.codec.abort_count == 0);
    CHECK(decoder.call_count == 1);
    CHECK(evidence.codec_name == "h264_mf");
    CHECK(!evidence.hardware_accelerated);
    CHECK(evidence.validation_flags == UINT32_C(0x03ff));
}

void PrepareEncodeAndFinishFailuresAbortTheCodecExactlyOnce()
{
    const auto run = [](FactoryState &state) {
        AdapterFactory factory(state);
        FakeDecoder decoder;
        EncoderProbeEvidence evidence;
        return RunVerifiedEncoderProbe(
            Config(),
            factory,
            decoder,
            evidence);
    };

    FactoryState prepare;
    prepare.codec.fail_prepare_at = 4;
    prepare.codec.prepare_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    CHECK(run(prepare) == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(prepare.codec.prepared_pts.size() == 5);
    CHECK(prepare.codec.encode_count == 4);
    CHECK(prepare.codec.finish_count == 0);
    CHECK(prepare.codec.abort_count == 1);

    FactoryState encode;
    encode.codec.fail_encode_at = 4;
    encode.codec.encode_status = VRREC_STATUS_INTERNAL_ERROR;
    CHECK(run(encode) == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(encode.codec.prepared_pts.size() == 5);
    CHECK(encode.codec.encode_count == 5);
    CHECK(encode.codec.finish_count == 0);
    CHECK(encode.codec.abort_count == 1);

    FactoryState finish;
    finish.codec.finish_status = VRREC_STATUS_INTERNAL_ERROR;
    CHECK(run(finish) == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(finish.codec.encode_count == EncoderProbeSyntheticFrameCount);
    CHECK(finish.codec.finish_count == 1);
    CHECK(finish.codec.abort_count == 1);
}

void AdapterRejectsHardwareIdentityAndOutOfOrderFrames()
{
    CodecState codec;
    auto hardware = Opened();
    hardware.actual_encoder_kind = VRREC_ENCODER_NVENC;
    hardware.codec_name = "h264_nvenc";
    hardware.hardware_accelerated = true;
    hardware.encoder_adapter_luid = AdapterLuid;
    hardware.opened_input_format = VRREC_ENCODER_INPUT_D3D11_NV12;
    auto invalid = CreateFfmpegSystemMemoryEncoderProbeSession(
        hardware,
        std::make_unique<FakeCodecSession>(codec),
        AllocateFrame());
    CHECK(invalid.status == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(invalid.session == nullptr);
    CHECK(codec.abort_count == 1);

    CodecState ordered_codec;
    auto created = CreateFfmpegSystemMemoryEncoderProbeSession(
        Opened(),
        std::make_unique<FakeCodecSession>(ordered_codec),
        AllocateFrame());
    CHECK(created.status == VRREC_STATUS_OK);
    CHECK(created.session != nullptr);
    EncoderProbeSyntheticFrame second {
        1,
        Width,
        Height,
        33'333,
        33'334,
    };
    CHECK(created.session->EncodeSyntheticFrame(second).status ==
          VRREC_STATUS_INVALID_ARGUMENT);
    created.session->Abort();
    created.session->Abort();
    CHECK(ordered_codec.abort_count == 1);
}

}

int main()
{
    SoftwareAdapterFeedsOwnedSyntheticFramesIntoTheVerifiedPipeline();
    PrepareEncodeAndFinishFailuresAbortTheCodecExactlyOnce();
    AdapterRejectsHardwareIdentityAndOutOfOrderFrames();
}
