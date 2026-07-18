#include "ffmpeg_h264_d3d11_packet_encoder_adapter.hpp"
#include "ffmpeg_h264_d3d11_hardware_tests.hpp"
#include "annex_b_encoder_probe_decoder.hpp"
#include "encoder_probe_identity.hpp"
#include "encoder_probe_pipeline.hpp"
#include "hardware_encoder_probe_session_factory.hpp"
#include "video_surface.hpp"
#include "windows_media_foundation_h264_decode_port.hpp"
#include "windows_software_encoder_probe_adapter_identity_lookup.hpp"

#if !defined(NOMINMAX)
#define NOMINMAX
#endif
#include <Windows.h>
#include <d3d11.h>
#include <dxgi.h>

#include <chrono>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <iostream>
#include <memory>

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

std::uint64_t PackLuid(const LUID &luid) noexcept
{
    return static_cast<std::uint64_t>(luid.LowPart) |
        (static_cast<std::uint64_t>(
             static_cast<std::uint32_t>(luid.HighPart)) << 32U);
}

class TextureSurface final : public VideoSurface {
public:
    TextureSurface(
        ID3D11Texture2D *texture,
        VideoSurfaceDescriptor descriptor) noexcept
        : texture_(texture), descriptor_(descriptor)
    {
        texture_->AddRef();
    }

    ~TextureSurface() override
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

bool CreateNvidiaDevice(
    ID3D11Device **device,
    IDXGIAdapter **selected_adapter,
    std::uint64_t &adapter_luid) noexcept
{
    *device = nullptr;
    *selected_adapter = nullptr;
    adapter_luid = 0;
    IDXGIFactory1 *factory = nullptr;
    if (FAILED(CreateDXGIFactory1(
            __uuidof(IDXGIFactory1),
            reinterpret_cast<void **>(&factory)))) {
        return false;
    }
    IDXGIAdapter1 *adapter = nullptr;
    for (UINT index = 0;
         factory->EnumAdapters1(index, &adapter) != DXGI_ERROR_NOT_FOUND;
         ++index) {
        DXGI_ADAPTER_DESC1 descriptor {};
        const auto described = SUCCEEDED(adapter->GetDesc1(&descriptor));
        if (described && descriptor.VendorId == 0x10DE &&
            (descriptor.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) == 0) {
            D3D_FEATURE_LEVEL opened_level {};
            const auto opened = D3D11CreateDevice(
                adapter,
                D3D_DRIVER_TYPE_UNKNOWN,
                nullptr,
                D3D11_CREATE_DEVICE_VIDEO_SUPPORT,
                nullptr,
                0,
                D3D11_SDK_VERSION,
                device,
                &opened_level,
                nullptr);
            if (SUCCEEDED(opened) && *device != nullptr) {
                adapter_luid = PackLuid(descriptor.AdapterLuid);
                *selected_adapter = adapter;
                factory->Release();
                return true;
            }
        }
        adapter->Release();
        adapter = nullptr;
    }
    factory->Release();
    return false;
}

void EncodesAnNv12D3d11TextureWithTheRealNvencBackend(bool required)
{
    ID3D11Device *device = nullptr;
    IDXGIAdapter *adapter = nullptr;
    std::uint64_t adapter_luid = 0;
    if (!CreateNvidiaDevice(&device, &adapter, adapter_luid)) {
        CHECK(!required);
        std::cout << "SKIP: NVIDIA D3D11 device unavailable\n";
        return;
    }

    constexpr std::uint32_t width = 1'280;
    constexpr std::uint32_t height = 720;
    D3D11_TEXTURE2D_DESC texture_descriptor {};
    texture_descriptor.Width = width;
    texture_descriptor.Height = height;
    texture_descriptor.MipLevels = 1;
    texture_descriptor.ArraySize = 1;
    texture_descriptor.Format = DXGI_FORMAT_NV12;
    texture_descriptor.SampleDesc.Count = 1;
    texture_descriptor.Usage = D3D11_USAGE_DEFAULT;
    texture_descriptor.BindFlags = D3D11_BIND_RENDER_TARGET;
    ID3D11Texture2D *texture = nullptr;
    CHECK(SUCCEEDED(device->CreateTexture2D(
        &texture_descriptor,
        nullptr,
        &texture)));
    CHECK(texture != nullptr);

    ProductionVideoEncoderRoute route {};
    CHECK(ResolveProductionVideoEncoderRoute(
              VRREC_ENCODER_NVENC,
              adapter_luid,
              adapter_luid,
              route) == VRREC_STATUS_OK);
    H264VideoEncoderConfig config {};
    CHECK(CreateH264VideoEncoderConfig(
              width,
              height,
              60,
              true,
              config) == VRREC_STATUS_OK);
    auto surface = std::make_shared<TextureSurface>(
        texture,
        VideoSurfaceDescriptor {
            adapter_luid,
            width,
            height,
            VRREC_SOURCE_PIXEL_FORMAT_NV12,
            1,
        });
    texture->Release();
    adapter->Release();
    device->Release();

    FfmpegH264D3d11PacketEncoderAdapter encoder(route, config);
    const ScheduledVideoFrame frame {0, 1, 0, 0, false, surface};
    auto encoded = encoder.Encode(frame);
    if (encoded.status != VRREC_STATUS_OK) {
        std::cerr << "NVENC encode status: " << encoded.status << '\n';
    }
    CHECK(encoded.status == VRREC_STATUS_OK);
    auto packet_count = encoded.packets.size();
    auto descriptor_published = encoded.descriptor_became_ready;
    if (descriptor_published) {
        CHECK(encoded.encoder_identity == &encoder);
        CHECK(encoded.descriptor != nullptr);
        CHECK(!encoded.descriptor->codec_extradata.empty());
    }
    auto finished = encoder.Finish();
    if (finished.status != VRREC_STATUS_OK) {
        std::cerr << "NVENC finish status: " << finished.status << '\n';
    }
    CHECK(finished.status == VRREC_STATUS_OK);
    packet_count += finished.packets.size();
    if (finished.descriptor_became_ready) {
        CHECK(!descriptor_published);
        CHECK(finished.encoder_identity == &encoder);
        CHECK(finished.descriptor != nullptr);
        CHECK(!finished.descriptor->codec_extradata.empty());
        descriptor_published = true;
    }
    CHECK(packet_count > 0);
    CHECK(descriptor_published);
}

}

void RunFfmpegH264D3d11HardwareTest(bool required)
{
    EncodesAnNv12D3d11TextureWithTheRealNvencBackend(required);
}

void RunFfmpegH264HardwareProbeTest(bool required)
{
    ID3D11Device *device = nullptr;
    IDXGIAdapter *adapter = nullptr;
    std::uint64_t adapter_luid = 0;
    if (!CreateNvidiaDevice(&device, &adapter, adapter_luid)) {
        CHECK(!required);
        return;
    }
    adapter->Release();
    device->Release();

    WindowsSoftwareEncoderProbeAdapterIdentityLookup lookup;
    auto platform = lookup.Lookup(adapter_luid);
    if (platform.status != VRREC_STATUS_OK) {
        std::cerr << "hardware probe identity status: "
                  << platform.status << '\n';
    }
    CHECK(platform.status == VRREC_STATUS_OK);
    CHECK(!platform.identity.gpu_identity.empty());
    const vrrec_encoder_probe_config_v1 config {
        sizeof(vrrec_encoder_probe_config_v1),
        VRREC_ABI_V1,
        VRREC_ENCODER_NVENC,
        EncoderProbeSyntheticFrameCount,
        adapter_luid,
        1'280,
        720,
        30,
        1,
        platform.identity.gpu_identity.c_str(),
        0,
    };
    HardwareEncoderProbeSessionFactory factory(lookup);
    WindowsMediaFoundationH264DecodePort media_foundation_decoder;
    AnnexBEncoderProbeDecoder decoder(media_foundation_decoder);
    VerifiedEncoderProbeBackend probe(factory, decoder);
    EncoderProbeEvidence evidence;

    const auto probe_status = probe.ProbeV2(config, evidence);
    if (probe_status != VRREC_STATUS_OK) {
        std::cerr << "hardware probe status: " << probe_status << '\n';
    }
    CHECK(probe_status == VRREC_STATUS_OK);
    CHECK(evidence.actual_encoder_kind == VRREC_ENCODER_NVENC);
    CHECK(evidence.hardware_accelerated);
    CHECK(evidence.adapter_luid == adapter_luid);
    CHECK(evidence.opened_input_format == VRREC_ENCODER_INPUT_D3D11_NV12);
    CHECK(evidence.codec_name == "h264_nvenc");
    CHECK(evidence.validation_flags ==
        (VRREC_ENCODER_PROBE_VALIDATION_NONEMPTY_PACKET |
         VRREC_ENCODER_PROBE_VALIDATION_PARSEABLE_ACCESS_UNIT |
         VRREC_ENCODER_PROBE_VALIDATION_SPS |
         VRREC_ENCODER_PROBE_VALIDATION_PPS |
         VRREC_ENCODER_PROBE_VALIDATION_IDR |
         VRREC_ENCODER_PROBE_VALIDATION_DISPLAY_DIMENSIONS |
         VRREC_ENCODER_PROBE_VALIDATION_PROFILE |
         VRREC_ENCODER_PROBE_VALIDATION_FRAME_RATE |
         VRREC_ENCODER_PROBE_VALIDATION_ZERO_B_FRAMES |
         VRREC_ENCODER_PROBE_VALIDATION_DECODED |
         VRREC_ENCODER_PROBE_VALIDATION_SAME_ADAPTER));
}
