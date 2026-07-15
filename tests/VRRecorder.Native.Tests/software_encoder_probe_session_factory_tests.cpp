#include "software_encoder_probe_session_factory.hpp"

#include "encoder_probe_identity.hpp"

#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <functional>
#include <iostream>
#include <memory>
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

constexpr std::uint64_t AdapterLuid = UINT64_C(0x00000001abcdef01);
constexpr std::uint32_t Width = 32;
constexpr std::uint32_t Height = 16;
constexpr std::uint32_t FramesPerSecond = 30;
constexpr char GpuIdentity[] = "pci\\ven_10de&dev_2684|driver-32.0.16.1062";

vrrec_encoder_probe_config_v1 Config()
{
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

struct CodecState final {
    std::uint32_t abort_count = 0;
};

class FakeCodecSession final : public FfmpegH264CodecSession {
public:
    explicit FakeCodecSession(CodecState &state) : state_(state) {}

    vrrec_status_t PrepareFrame(const AVFrame &) noexcept override
    {
        return VRREC_STATUS_OK;
    }

    FfmpegEncodeBatch EncodePreparedFrame() noexcept override
    {
        return {VRREC_STATUS_OK, {}};
    }

    FfmpegEncodeBatch Finish() noexcept override
    {
        return {VRREC_STATUS_OK, {}};
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

FfmpegH264SoftwareOpenedIdentity OpenedCodecIdentity()
{
    H264VideoEncoderConfig config;
    CHECK(CreateH264VideoEncoderConfig(
              Width,
              Height,
              FramesPerSecond,
              true,
              config) == VRREC_STATUS_OK);
    return {
        "h264_mf",
        false,
        VRREC_ENCODER_INPUT_SYSTEM_MEMORY_NV12,
        config,
        "ffmpeg|8.1.2|contract-id",
    };
}

class FakePlatformIdentity final
    : public SoftwareEncoderProbePlatformIdentityPort {
public:
    std::uint32_t call_count = 0;
    SoftwareEncoderProbePlatformIdentityResult result {
        VRREC_STATUS_OK,
        {
            AdapterLuid,
            AdapterLuid,
            GpuIdentity,
            "nvidia|32.0.16.1062",
            "pci\\ven_10de&dev_2684",
        },
    };

    SoftwareEncoderProbePlatformIdentityResult Resolve(
        const vrrec_encoder_probe_config_v1 &) noexcept override
    {
        ++call_count;
        return result;
    }
};

class FakeCodecFactory final
    : public FfmpegH264SoftwareCodecSessionFactoryPort {
public:
    std::uint32_t call_count = 0;
    vrrec_status_t status = VRREC_STATUS_OK;
    FfmpegH264SoftwareOpenedIdentity identity = OpenedCodecIdentity();
    CodecState codec_state;

    FfmpegH264SoftwareCodecSessionCreateResult Create(
        const H264VideoEncoderConfig &) noexcept override
    {
        ++call_count;
        if (status != VRREC_STATUS_OK) {
            return {status, nullptr, nullptr, {}};
        }
        return {
            VRREC_STATUS_OK,
            std::make_unique<FakeCodecSession>(codec_state),
            AllocateFrame(),
            identity,
        };
    }
};

void CreatesOnlyTheExactReadBackSoftwareSession()
{
    FakePlatformIdentity platform;
    FakeCodecFactory codec;
    SoftwareEncoderProbeSessionFactory factory(platform, codec);

    auto created = factory.Create(Config());
    CHECK(created.status == VRREC_STATUS_OK);
    CHECK(created.session != nullptr);
    CHECK(platform.call_count == 1);
    CHECK(codec.call_count == 1);
    const auto &identity = created.session->OpenedIdentity();
    CHECK(identity.actual_encoder_kind ==
          VRREC_ENCODER_MEDIA_FOUNDATION_SOFTWARE);
    CHECK(identity.codec_name == "h264_mf");
    CHECK(!identity.hardware_accelerated);
    CHECK(identity.source_adapter_luid == AdapterLuid);
    CHECK(identity.processor_adapter_luid == AdapterLuid);
    CHECK(identity.encoder_adapter_luid == 0);
    CHECK(identity.opened_input_format ==
          VRREC_ENCODER_INPUT_SYSTEM_MEMORY_NV12);
    CHECK(identity.driver_identity == "nvidia|32.0.16.1062");
    CHECK(identity.ffmpeg_build_identity ==
          "ffmpeg|8.1.2|contract-id");
    CHECK(identity.device_identity == "pci\\ven_10de&dev_2684");
    created.session->Abort();
    CHECK(codec.codec_state.abort_count == 1);
}

void RejectsWrongKindAndAdapterIdentityBeforeOpeningTheCodec()
{
    FakePlatformIdentity platform;
    FakeCodecFactory codec;
    SoftwareEncoderProbeSessionFactory factory(platform, codec);

    auto wrong_kind = Config();
    wrong_kind.encoder_kind = VRREC_ENCODER_NVENC;
    CHECK(factory.Create(wrong_kind).status == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(platform.call_count == 0);
    CHECK(codec.call_count == 0);

    platform.result.identity.processor_adapter_luid++;
    CHECK(factory.Create(Config()).status ==
          VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(platform.call_count == 1);
    CHECK(codec.call_count == 0);
}

void PreservesAbiValidationOrderWithoutCallingDependencies()
{
    FakePlatformIdentity platform;
    FakeCodecFactory codec;
    SoftwareEncoderProbeSessionFactory factory(platform, codec);

    auto invalid = Config();
    invalid.struct_size = sizeof(invalid) - 1;
    invalid.abi_version++;
    CHECK(factory.Create(invalid).status == VRREC_STATUS_INVALID_ARGUMENT);
    invalid = Config();
    invalid.abi_version++;
    CHECK(factory.Create(invalid).status == VRREC_STATUS_UNSUPPORTED_ABI);
    CHECK(platform.call_count == 0);
    CHECK(codec.call_count == 0);
}

void RejectsEveryPlatformIdentityDriftBeforeOpeningTheCodec()
{
    using Mutation = std::function<void(
        SoftwareEncoderProbePlatformIdentity &)>;
    const std::vector<Mutation> mutations {
        [](auto &value) { ++value.source_adapter_luid; },
        [](auto &value) { ++value.processor_adapter_luid; },
        [](auto &value) { value.gpu_identity = "different-gpu"; },
        [](auto &value) { value.driver_identity.clear(); },
        [](auto &value) { value.device_identity.clear(); },
    };
    for (const auto &mutate : mutations) {
        FakePlatformIdentity platform;
        FakeCodecFactory codec;
        mutate(platform.result.identity);
        SoftwareEncoderProbeSessionFactory factory(platform, codec);
        CHECK(factory.Create(Config()).status ==
              VRREC_STATUS_BACKEND_UNAVAILABLE);
        CHECK(platform.call_count == 1);
        CHECK(codec.call_count == 0);
    }
}

void AbortsOpenedCodecWhenReadBackIdentityDrifts()
{
    using Mutation = std::function<void(
        FfmpegH264SoftwareOpenedIdentity &)>;
    const std::vector<Mutation> mutations {
        [](auto &value) { value.codec_name = "h264_nvenc"; },
        [](auto &value) { value.hardware_accelerated = true; },
        [](auto &value) {
            value.opened_input_format = VRREC_ENCODER_INPUT_D3D11_NV12;
        },
        [](auto &value) { value.opened_config.width += 2; },
        [](auto &value) { value.opened_config.height += 2; },
        [](auto &value) { ++value.opened_config.frames_per_second; },
        [](auto &value) { ++value.opened_config.gop_frame_count; },
        [](auto &value) {
            ++value.opened_config.target_bitrate_bits_per_second;
        },
        [](auto &value) {
            ++value.opened_config.maximum_bitrate_bits_per_second;
        },
        [](auto &value) {
            value.opened_config.input_pixel_format =
                VRREC_SOURCE_PIXEL_FORMAT_RGBA8;
        },
        [](auto &value) { value.opened_config.profile = H264Profile::Main; },
        [](auto &value) { value.opened_config.maximum_b_frame_count = 1; },
        [](auto &value) { value.ffmpeg_build_identity.clear(); },
    };
    for (const auto &mutate : mutations) {
        FakePlatformIdentity platform;
        FakeCodecFactory codec;
        mutate(codec.identity);
        SoftwareEncoderProbeSessionFactory factory(platform, codec);

        auto rejected = factory.Create(Config());
        CHECK(rejected.status == VRREC_STATUS_BACKEND_UNAVAILABLE);
        CHECK(rejected.session == nullptr);
        CHECK(codec.call_count == 1);
        CHECK(codec.codec_state.abort_count == 1);
    }
}

}

int main()
{
    CreatesOnlyTheExactReadBackSoftwareSession();
    RejectsWrongKindAndAdapterIdentityBeforeOpeningTheCodec();
    PreservesAbiValidationOrderWithoutCallingDependencies();
    RejectsEveryPlatformIdentityDriftBeforeOpeningTheCodec();
    AbortsOpenedCodecWhenReadBackIdentityDrifts();
}
