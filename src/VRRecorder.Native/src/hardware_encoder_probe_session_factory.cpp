#include "hardware_encoder_probe_session_factory.hpp"

#if !defined(_WIN32)
#error "The hardware encoder probe session requires Windows"
#endif

#if !defined(NOMINMAX)
#define NOMINMAX
#endif
#include <Windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>

#include <cstdint>
#include <cstring>
#include <memory>
#include <new>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

#include "encoder_probe_identity.hpp"
#include "ffmpeg_h264_hardware_codec_session.hpp"
#include "production_video_encoder_route.hpp"
#include "video_surface.hpp"

extern "C" {
#include <libavcodec/avcodec.h>
#include <libavutil/frame.h>
}

namespace vrrecorder::native {
namespace {

std::uint64_t PackLuid(const LUID &luid) noexcept
{
    return static_cast<std::uint64_t>(luid.LowPart) |
        (static_cast<std::uint64_t>(
             static_cast<std::uint32_t>(luid.HighPart)) << 32U);
}

template <typename T>
class ComOwner final {
public:
    ComOwner() noexcept = default;
    ~ComOwner()
    {
        Reset();
    }
    ComOwner(const ComOwner &) = delete;
    ComOwner &operator=(const ComOwner &) = delete;

    T *Get() const noexcept
    {
        return value_;
    }

    T **Put() noexcept
    {
        Reset();
        return &value_;
    }

    T *Release() noexcept
    {
        auto *released = value_;
        value_ = nullptr;
        return released;
    }

    void Reset() noexcept
    {
        if (value_ != nullptr) {
            value_->Release();
            value_ = nullptr;
        }
    }

private:
    T *value_ = nullptr;
};

class ProbeTextureSurface final : public VideoSurface {
public:
    ProbeTextureSurface(
        ID3D11Texture2D *texture,
        VideoSurfaceDescriptor descriptor) noexcept
        : texture_(texture), descriptor_(descriptor)
    {
        texture_->AddRef();
    }

    ~ProbeTextureSurface() override
    {
        texture_->Release();
    }

    VideoSurfaceDescriptor Descriptor() const noexcept override
    {
        return descriptor_;
    }

    void *NativeHandle() const noexcept override
    {
        return texture_;
    }

    VideoSurfaceAcquireResult AcquireForRead(
        std::chrono::milliseconds) noexcept override
    {
        return VideoSurfaceAcquireResult::Acquired;
    }

    vrrec_status_t ReleaseFromRead() noexcept override
    {
        return VRREC_STATUS_OK;
    }

private:
    ID3D11Texture2D *texture_;
    VideoSurfaceDescriptor descriptor_;
};

vrrec_status_t CreateDeviceForAdapter(
    std::uint64_t adapter_luid,
    ID3D11Device **device) noexcept
{
    *device = nullptr;
    ComOwner<IDXGIFactory1> factory;
    const auto factory_result = CreateDXGIFactory1(
        __uuidof(IDXGIFactory1),
        reinterpret_cast<void **>(factory.Put()));
    if (FAILED(factory_result)) {
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    }
    for (UINT index = 0;; ++index) {
        ComOwner<IDXGIAdapter1> adapter;
        const auto enumerated = factory.Get()->EnumAdapters1(
            index,
            adapter.Put());
        if (enumerated == DXGI_ERROR_NOT_FOUND) {
            return VRREC_STATUS_BACKEND_UNAVAILABLE;
        }
        if (FAILED(enumerated)) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        DXGI_ADAPTER_DESC1 descriptor {};
        if (FAILED(adapter.Get()->GetDesc1(&descriptor))) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        if (PackLuid(descriptor.AdapterLuid) != adapter_luid ||
            (descriptor.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0) {
            continue;
        }
        D3D_FEATURE_LEVEL opened_level {};
        const auto opened = D3D11CreateDevice(
            adapter.Get(),
            D3D_DRIVER_TYPE_UNKNOWN,
            nullptr,
            D3D11_CREATE_DEVICE_VIDEO_SUPPORT,
            nullptr,
            0,
            D3D11_SDK_VERSION,
            device,
            &opened_level,
            nullptr);
        return SUCCEEDED(opened) && *device != nullptr
            ? VRREC_STATUS_OK
            : VRREC_STATUS_BACKEND_UNAVAILABLE;
    }
}

vrrec_encoder_input_format_t OpenedInputFormat(
    const ProductionVideoEncoderRoute &route) noexcept
{
    return route.input == ProductionVideoEncoderInput::QsvDerivedD3d11Nv12
        ? VRREC_ENCODER_INPUT_QSV_NV12
        : VRREC_ENCODER_INPUT_D3D11_NV12;
}

vrrec_status_t ReadFfmpegBuildIdentity(std::string &identity) noexcept
{
    const auto *release = av_version_info();
    const auto *configuration = avcodec_configuration();
    if (release == nullptr || release[0] == '\0' ||
        configuration == nullptr || configuration[0] == '\0') {
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    }
    try {
        std::string readback("ffmpeg|");
        readback.append(release);
        readback.append("|avcodec-");
        readback.append(std::to_string(avcodec_version()));
        readback.push_back('|');
        readback.append(configuration);
        identity.swap(readback);
        return VRREC_STATUS_OK;
    } catch (const std::bad_alloc &) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

class HardwareEncoderProbeSession final : public EncoderProbeEncodeSession {
public:
    HardwareEncoderProbeSession(
        EncoderProbeOpenedIdentity identity,
        H264VideoEncoderConfig config,
        std::unique_ptr<FfmpegH264CodecSession> codec,
        ID3D11Device *device,
        std::vector<std::byte> extradata) noexcept
        : identity_(std::move(identity)),
          config_(config),
          codec_(std::move(codec)),
          device_(device),
          extradata_(std::move(extradata))
    {
    }

    ~HardwareEncoderProbeSession() override
    {
        Abort();
        if (device_ != nullptr) {
            device_->Release();
        }
        if (texture_ != nullptr) {
            texture_->Release();
        }
    }

    const EncoderProbeOpenedIdentity &OpenedIdentity()
        const noexcept override
    {
        return identity_;
    }

    FfmpegEncodeBatch EncodeSyntheticFrame(
        const EncoderProbeSyntheticFrame &frame) noexcept override
    {
        if (state_ != State::Active || frame.frame_index != next_frame_ ||
            frame.width != config_.width || frame.height != config_.height) {
            return Fail(VRREC_STATUS_INVALID_ARGUMENT);
        }
        const auto expected_start = static_cast<std::int64_t>(
            frame.frame_index) * 1'000'000 / config_.frames_per_second;
        const auto expected_end = static_cast<std::int64_t>(
            frame.frame_index + 1U) * 1'000'000 /
            config_.frames_per_second;
        if (frame.pts_microseconds != expected_start ||
            frame.duration_microseconds != expected_end - expected_start) {
            return Fail(VRREC_STATUS_INVALID_ARGUMENT);
        }

        D3D11_TEXTURE2D_DESC texture_descriptor {};
        texture_descriptor.Width = config_.width;
        texture_descriptor.Height = config_.height;
        texture_descriptor.MipLevels = 1;
        texture_descriptor.ArraySize = 1;
        texture_descriptor.Format = DXGI_FORMAT_NV12;
        texture_descriptor.SampleDesc.Count = 1;
        texture_descriptor.Usage = D3D11_USAGE_DEFAULT;
        texture_descriptor.BindFlags = D3D11_BIND_RENDER_TARGET;
        if (texture_ == nullptr) {
            const auto created = device_->CreateTexture2D(
                &texture_descriptor,
                nullptr,
                &texture_);
            if (FAILED(created)) {
                return Fail(created == E_OUTOFMEMORY
                    ? VRREC_STATUS_OUT_OF_MEMORY
                    : VRREC_STATUS_BACKEND_UNAVAILABLE);
            }
        }

        std::shared_ptr<VideoSurface> surface;
        try {
            surface = std::make_shared<ProbeTextureSurface>(
                texture_,
                VideoSurfaceDescriptor {
                    identity_.source_adapter_luid,
                    config_.width,
                    config_.height,
                    VRREC_SOURCE_PIXEL_FORMAT_NV12,
                    1,
                });
        } catch (const std::bad_alloc &) {
            return Fail(VRREC_STATUS_OUT_OF_MEMORY);
        } catch (...) {
            return Fail(VRREC_STATUS_INTERNAL_ERROR);
        }
        auto status = codec_->PrepareD3d11Frame(
            surface,
            static_cast<std::int64_t>(frame.frame_index));
        if (status != VRREC_STATUS_OK) {
            return Fail(status);
        }
        auto batch = codec_->EncodePreparedFrame();
        if (batch.status != VRREC_STATUS_OK) {
            return Fail(batch.status);
        }
        ++next_frame_;
        return AddExtradataToFirstPacket(std::move(batch));
    }

    FfmpegEncodeBatch Finish() noexcept override
    {
        if (state_ != State::Active ||
            next_frame_ != EncoderProbeSyntheticFrameCount) {
            return Fail(VRREC_STATUS_INVALID_STATE);
        }
        auto batch = codec_->Finish();
        if (batch.status != VRREC_STATUS_OK) {
            return Fail(batch.status);
        }
        auto completed = AddExtradataToFirstPacket(std::move(batch));
        if (completed.status != VRREC_STATUS_OK) {
            return Fail(completed.status);
        }
        state_ = State::Finished;
        return completed;
    }

    void Abort() noexcept override
    {
        if (!codec_aborted_ && state_ != State::Finished) {
            codec_aborted_ = true;
            codec_->Abort();
        }
        if (state_ != State::Finished) {
            state_ = State::Aborted;
        }
    }

private:
    enum class State { Active, Finished, Failed, Aborted };

    FfmpegEncodeBatch AddExtradataToFirstPacket(
        FfmpegEncodeBatch batch) noexcept
    {
        if (extradata_emitted_ || batch.status != VRREC_STATUS_OK ||
            batch.packets.empty()) {
            return batch;
        }
        try {
            auto &payload = batch.packets.front().payload;
            payload.insert(
                payload.begin(),
                extradata_.begin(),
                extradata_.end());
            extradata_emitted_ = true;
            return batch;
        } catch (const std::bad_alloc &) {
            return {VRREC_STATUS_OUT_OF_MEMORY, {}};
        } catch (...) {
            return {VRREC_STATUS_INTERNAL_ERROR, {}};
        }
    }

    FfmpegEncodeBatch Fail(vrrec_status_t status) noexcept
    {
        if (!codec_aborted_) {
            codec_aborted_ = true;
            codec_->Abort();
        }
        state_ = State::Failed;
        return {status, {}};
    }

    EncoderProbeOpenedIdentity identity_;
    H264VideoEncoderConfig config_;
    std::unique_ptr<FfmpegH264CodecSession> codec_;
    ID3D11Device *device_ = nullptr;
    ID3D11Texture2D *texture_ = nullptr;
    std::vector<std::byte> extradata_;
    std::uint32_t next_frame_ = 0;
    State state_ = State::Active;
    bool codec_aborted_ = false;
    bool extradata_emitted_ = false;
};

}

HardwareEncoderProbeSessionFactory::HardwareEncoderProbeSessionFactory(
    SoftwareEncoderProbeAdapterIdentityLookupPort &identity_lookup) noexcept
    : identity_lookup_(identity_lookup)
{
}

EncoderProbeEncodeSessionCreateResult
HardwareEncoderProbeSessionFactory::Create(
    const vrrec_encoder_probe_config_v1 &config) noexcept
{
    if (config.struct_size < sizeof(vrrec_encoder_probe_config_v1)) {
        return {VRREC_STATUS_INVALID_ARGUMENT, nullptr};
    }
    if (config.abi_version != VRREC_ABI_V1) {
        return {VRREC_STATUS_UNSUPPORTED_ABI, nullptr};
    }
    if (!FindExpectedEncoderProbeIdentity(config.encoder_kind).has_value() ||
        config.encoder_kind == VRREC_ENCODER_MEDIA_FOUNDATION_SOFTWARE ||
        config.synthetic_frame_count != EncoderProbeSyntheticFrameCount ||
        config.adapter_luid == 0 || config.fps_denominator != 1 ||
        config.gpu_identity_utf8 == nullptr ||
        config.gpu_identity_utf8[0] == '\0' || config.reserved != 0) {
        return {VRREC_STATUS_INVALID_ARGUMENT, nullptr};
    }

    auto found_identity = identity_lookup_.Lookup(config.adapter_luid);
    if (found_identity.status != VRREC_STATUS_OK) {
        return {found_identity.status, nullptr};
    }
    if (found_identity.identity.adapter_luid != config.adapter_luid ||
        found_identity.identity.gpu_identity !=
            std::string_view(config.gpu_identity_utf8) ||
        found_identity.identity.driver_identity.empty() ||
        found_identity.identity.device_identity.empty()) {
        return {VRREC_STATUS_BACKEND_UNAVAILABLE, nullptr};
    }

    ProductionVideoEncoderRoute route {};
    auto status = ResolveProductionVideoEncoderRoute(
        config.encoder_kind,
        config.adapter_luid,
        config.adapter_luid,
        route);
    H264VideoEncoderConfig encoder_config {};
    if (status == VRREC_STATUS_OK) {
        status = CreateH264VideoEncoderConfig(
            config.width,
            config.height,
            config.fps_numerator,
            true,
            encoder_config);
    }
    if (status != VRREC_STATUS_OK) {
        return {status, nullptr};
    }

    ComOwner<ID3D11Device> device;
    status = CreateDeviceForAdapter(config.adapter_luid, device.Put());
    if (status != VRREC_STATUS_OK) {
        return {status, nullptr};
    }
    auto opened = CreateFfmpegH264HardwareCodecSession(
        encoder_config,
        route,
        device.Get());
    if (opened.status != VRREC_STATUS_OK || opened.session == nullptr) {
        av_frame_free(&opened.owned_frame);
        return {
            opened.status == VRREC_STATUS_OK
                ? VRREC_STATUS_INTERNAL_ERROR
                : opened.status,
            nullptr,
        };
    }
    av_frame_free(&opened.owned_frame);
    std::vector<std::byte> extradata;
    status = opened.session->CopyCodecExtradata(extradata);
    std::string ffmpeg_build_identity;
    if (status == VRREC_STATUS_OK) {
        status = ReadFfmpegBuildIdentity(ffmpeg_build_identity);
    }
    if (status != VRREC_STATUS_OK) {
        opened.session->Abort();
        return {status, nullptr};
    }

    try {
        EncoderProbeOpenedIdentity identity {
            config.encoder_kind,
            route.codec_name,
            true,
            config.adapter_luid,
            config.adapter_luid,
            config.adapter_luid,
            OpenedInputFormat(route),
            encoder_config.width,
            encoder_config.height,
            encoder_config.frames_per_second,
            1,
            encoder_config.profile,
            encoder_config.maximum_b_frame_count,
            std::move(found_identity.identity.driver_identity),
            std::move(ffmpeg_build_identity),
            std::move(found_identity.identity.device_identity),
        };
        std::unique_ptr<EncoderProbeEncodeSession> session(
            new (std::nothrow) HardwareEncoderProbeSession(
                std::move(identity),
                encoder_config,
                std::move(opened.session),
                device.Release(),
                std::move(extradata)));
        return session == nullptr
            ? EncoderProbeEncodeSessionCreateResult {
                VRREC_STATUS_OUT_OF_MEMORY,
                nullptr,
            }
            : EncoderProbeEncodeSessionCreateResult {
                VRREC_STATUS_OK,
                std::move(session),
            };
    } catch (const std::bad_alloc &) {
        opened.session->Abort();
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr};
    } catch (...) {
        opened.session->Abort();
        return {VRREC_STATUS_INTERNAL_ERROR, nullptr};
    }
}

}
