#include "d3d11_video_frame_processor.hpp"
#include "windows_d3d11_video_processor_port.hpp"
#include "windows_d3d11_keyed_mutex_surface.hpp"

#include <d3d11.h>
#include <dxgi1_2.h>

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <memory>
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

template <typename T>
class ComOwner final {
public:
    ComOwner() noexcept = default;

    explicit ComOwner(T *value) noexcept : value_(value)
    {
    }

    ~ComOwner()
    {
        Reset();
    }

    ComOwner(const ComOwner &) = delete;
    ComOwner &operator=(const ComOwner &) = delete;

    ComOwner(ComOwner &&other) noexcept : value_(other.value_)
    {
        other.value_ = nullptr;
    }

    ComOwner &operator=(ComOwner &&other) noexcept
    {
        if (this != &other) {
            Reset();
            value_ = other.value_;
            other.value_ = nullptr;
        }
        return *this;
    }

    T *Get() const noexcept
    {
        return value_;
    }

    T **Put() noexcept
    {
        Reset();
        return &value_;
    }

    void Reset() noexcept
    {
        if (value_ != nullptr) {
            value_->Release();
            value_ = nullptr;
        }
    }

    explicit operator bool() const noexcept
    {
        return value_ != nullptr;
    }

private:
    T *value_ = nullptr;
};

std::uint64_t PackLuid(const LUID &luid) noexcept
{
    return static_cast<std::uint64_t>(luid.LowPart) |
           (static_cast<std::uint64_t>(
                static_cast<std::uint32_t>(luid.HighPart)) << 32U);
}

struct HardwareDevice final {
    ComOwner<ID3D11Device> device;
    ComOwner<ID3D11DeviceContext> context;
    std::uint64_t adapter_luid = 0;
};

HardwareDevice CreateHardwareVideoDevice()
{
    ComOwner<IDXGIFactory1> factory;
    CHECK(SUCCEEDED(CreateDXGIFactory1(
        __uuidof(IDXGIFactory1),
        reinterpret_cast<void **>(factory.Put()))));

    for (UINT index = 0;; ++index) {
        ComOwner<IDXGIAdapter1> adapter;
        const auto enumerate = factory.Get()->EnumAdapters1(
            index,
            adapter.Put());
        if (enumerate == DXGI_ERROR_NOT_FOUND) {
            break;
        }
        CHECK(SUCCEEDED(enumerate));

        DXGI_ADAPTER_DESC1 adapter_descriptor {};
        CHECK(SUCCEEDED(adapter.Get()->GetDesc1(&adapter_descriptor)));
        if ((adapter_descriptor.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0) {
            continue;
        }

        HardwareDevice result;
        D3D_FEATURE_LEVEL opened_feature_level {};
        const D3D_FEATURE_LEVEL requested_feature_levels[] = {
            D3D_FEATURE_LEVEL_11_1,
            D3D_FEATURE_LEVEL_11_0,
        };
        const auto create = D3D11CreateDevice(
            adapter.Get(),
            D3D_DRIVER_TYPE_UNKNOWN,
            nullptr,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT |
                D3D11_CREATE_DEVICE_VIDEO_SUPPORT,
            requested_feature_levels,
            static_cast<UINT>(std::size(requested_feature_levels)),
            D3D11_SDK_VERSION,
            result.device.Put(),
            &opened_feature_level,
            result.context.Put());
        if (FAILED(create)) {
            continue;
        }

        ComOwner<ID3D11VideoDevice> video_device;
        if (FAILED(result.device.Get()->QueryInterface(
                __uuidof(ID3D11VideoDevice),
                reinterpret_cast<void **>(video_device.Put())))) {
            continue;
        }

        result.adapter_luid = PackLuid(adapter_descriptor.AdapterLuid);
        CHECK(result.adapter_luid != 0);
        return result;
    }

    std::cerr << "no hardware D3D11 video processor adapter is available\n";
    std::abort();
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

    ~TextureSurface()
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

std::shared_ptr<VideoSurface> CreateOddColorSurface(
    const HardwareDevice &hardware,
    std::uint64_t generation_id,
    vrrec_source_pixel_format_t pixel_format,
    std::uint8_t red,
    std::uint8_t green,
    std::uint8_t blue)
{
    constexpr std::uint32_t width = 63;
    constexpr std::uint32_t height = 31;
    std::vector<std::uint8_t> pixels(width * height * 4U);
    for (std::size_t index = 0; index < pixels.size(); index += 4) {
        pixels[index] = pixel_format == VRREC_SOURCE_PIXEL_FORMAT_BGRA8
            ? blue
            : red;
        pixels[index + 1] = green;
        pixels[index + 2] =
            pixel_format == VRREC_SOURCE_PIXEL_FORMAT_BGRA8
            ? red
            : blue;
        pixels[index + 3] = 255;
    }

    D3D11_TEXTURE2D_DESC descriptor {};
    descriptor.Width = width;
    descriptor.Height = height;
    descriptor.MipLevels = 1;
    descriptor.ArraySize = 1;
    descriptor.Format =
        pixel_format == VRREC_SOURCE_PIXEL_FORMAT_BGRA8
        ? DXGI_FORMAT_B8G8R8A8_UNORM
        : DXGI_FORMAT_R8G8B8A8_UNORM;
    descriptor.SampleDesc.Count = 1;
    descriptor.Usage = D3D11_USAGE_DEFAULT;
    descriptor.BindFlags = D3D11_BIND_RENDER_TARGET;
    const D3D11_SUBRESOURCE_DATA initial {
        pixels.data(),
        width * 4U,
        0,
    };
    ComOwner<ID3D11Texture2D> texture;
    CHECK(SUCCEEDED(hardware.device.Get()->CreateTexture2D(
        &descriptor,
        &initial,
        texture.Put())));

    return std::make_shared<TextureSurface>(
        texture.Get(),
        VideoSurfaceDescriptor {
            hardware.adapter_luid,
            width,
            height,
            pixel_format,
            generation_id,
        });
}

struct Nv12Readback final {
    std::vector<std::uint8_t> bytes;
    std::uint32_t row_pitch = 0;
    std::uint32_t width = 0;
    std::uint32_t height = 0;
};

Nv12Readback ReadBackNv12(
    const HardwareDevice &hardware,
    const std::shared_ptr<VideoSurface> &surface)
{
    auto *texture = static_cast<ID3D11Texture2D *>(surface->NativeHandle());
    D3D11_TEXTURE2D_DESC descriptor {};
    texture->GetDesc(&descriptor);
    CHECK(descriptor.Format == DXGI_FORMAT_NV12);

    auto staging_descriptor = descriptor;
    staging_descriptor.Usage = D3D11_USAGE_STAGING;
    staging_descriptor.BindFlags = 0;
    staging_descriptor.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    staging_descriptor.MiscFlags = 0;
    ComOwner<ID3D11Texture2D> staging;
    CHECK(SUCCEEDED(hardware.device.Get()->CreateTexture2D(
        &staging_descriptor,
        nullptr,
        staging.Put())));

    hardware.context.Get()->CopyResource(staging.Get(), texture);
    D3D11_MAPPED_SUBRESOURCE mapped {};
    CHECK(SUCCEEDED(hardware.context.Get()->Map(
        staging.Get(),
        0,
        D3D11_MAP_READ,
        0,
        &mapped)));

    Nv12Readback readback;
    readback.row_pitch = mapped.RowPitch;
    readback.width = descriptor.Width;
    readback.height = descriptor.Height;
    const auto byte_count = static_cast<std::size_t>(mapped.RowPitch) *
        (descriptor.Height + descriptor.Height / 2U);
    const auto *begin = static_cast<const std::uint8_t *>(mapped.pData);
    readback.bytes.assign(begin, begin + byte_count);
    hardware.context.Get()->Unmap(staging.Get(), 0);
    return readback;
}

double AverageY(
    const Nv12Readback &readback,
    std::uint32_t first_row,
    std::uint32_t row_count,
    std::uint32_t first_column,
    std::uint32_t column_count)
{
    std::uint64_t sum = 0;
    for (std::uint32_t row = first_row;
         row < first_row + row_count;
         ++row) {
        const auto offset = static_cast<std::size_t>(row) *
            readback.row_pitch;
        for (std::uint32_t column = first_column;
             column < first_column + column_count;
             ++column) {
            sum += readback.bytes[offset + column];
        }
    }
    return static_cast<double>(sum) /
        static_cast<double>(row_count * column_count);
}

void ConvertsOddBgraToOwnedNv12AndPadsBeforeFit()
{
    const auto hardware = CreateHardwareVideoDevice();
    const auto source = CreateOddColorSurface(
        hardware,
        11,
        VRREC_SOURCE_PIXEL_FORMAT_BGRA8,
        0,
        255,
        0);
    VideoProcessingPlan plan {};
    CHECK(CreateSingleFileVideoProcessingPlan(
              source->Descriptor(),
              64,
              64,
              plan) == VRREC_STATUS_OK);
    CHECK(plan.normalized_source_width == 64);
    CHECK(plan.normalized_source_height == 32);
    CHECK(plan.destination_width == 64);
    CHECK(plan.destination_height == 32);
    CHECK(plan.offset_y == 16);

    vrrec_status_t create_status = VRREC_STATUS_INTERNAL_ERROR;
    auto port = CreateWindowsD3d11VideoProcessorPort(
        hardware.device.Get(),
        hardware.adapter_luid,
        create_status);
    CHECK(create_status == VRREC_STATUS_OK);
    CHECK(port != nullptr);
    D3d11VideoFrameProcessor processor(*port);
    std::shared_ptr<VideoSurface> output;

    CHECK(processor.Process(source, plan, output) == VRREC_STATUS_OK);
    CHECK(output != nullptr);
    const auto output_descriptor = output->Descriptor();
    CHECK(output_descriptor.adapter_luid == hardware.adapter_luid);
    CHECK(output_descriptor.generation_id == 11);
    CHECK(output_descriptor.width == 64);
    CHECK(output_descriptor.height == 64);
    CHECK(output_descriptor.pixel_format ==
          VRREC_SOURCE_PIXEL_FORMAT_NV12);

    const auto readback = ReadBackNv12(hardware, output);
    const auto top_black = AverageY(readback, 0, 8, 0, 64);
    const auto middle_green = AverageY(readback, 20, 16, 0, 62);
    const auto right_padding = AverageY(readback, 20, 16, 63, 1);
    const auto bottom_padding = AverageY(readback, 47, 1, 0, 64);
    CHECK(middle_green > top_black + 40.0);
    CHECK(middle_green > right_padding + 40.0);
    CHECK(middle_green > bottom_padding + 40.0);
    const auto uv_offset = static_cast<std::size_t>(readback.row_pitch) *
        readback.height;
    CHECK(readback.bytes[uv_offset] != 0);
    CHECK(readback.bytes[uv_offset + 1] != 0);
}

std::uint8_t Nv12Y(
    const Nv12Readback &readback,
    std::uint32_t row,
    std::uint32_t column)
{
    return readback.bytes[
        static_cast<std::size_t>(row) * readback.row_pitch + column];
}

std::uint8_t Nv12Chroma(
    const Nv12Readback &readback,
    std::uint32_t row,
    std::uint32_t column)
{
    const auto uv_offset = static_cast<std::size_t>(readback.row_pitch) *
        readback.height;
    return readback.bytes[
        uv_offset + static_cast<std::size_t>(row) * readback.row_pitch +
        column];
}

bool IsClose(std::uint8_t left, std::uint8_t right) noexcept
{
    const auto larger = left > right ? left : right;
    const auto smaller = left > right ? right : left;
    return static_cast<unsigned>(larger - smaller) <= 4U;
}

void PreservesLogicalRedAcrossBgraAndRgbaChannelOrder()
{
    const auto hardware = CreateHardwareVideoDevice();
    const auto bgra_source = CreateOddColorSurface(
        hardware,
        21,
        VRREC_SOURCE_PIXEL_FORMAT_BGRA8,
        255,
        0,
        0);
    const auto rgba_source = CreateOddColorSurface(
        hardware,
        22,
        VRREC_SOURCE_PIXEL_FORMAT_RGBA8,
        255,
        0,
        0);
    VideoProcessingPlan bgra_plan {};
    VideoProcessingPlan rgba_plan {};
    CHECK(CreateSingleFileVideoProcessingPlan(
              bgra_source->Descriptor(),
              64,
              64,
              bgra_plan) == VRREC_STATUS_OK);
    CHECK(CreateSingleFileVideoProcessingPlan(
              rgba_source->Descriptor(),
              64,
              64,
              rgba_plan) == VRREC_STATUS_OK);
    CHECK(!bgra_plan.swap_red_blue_channels);
    CHECK(rgba_plan.swap_red_blue_channels);

    vrrec_status_t create_status = VRREC_STATUS_INTERNAL_ERROR;
    auto port = CreateWindowsD3d11VideoProcessorPort(
        hardware.device.Get(),
        hardware.adapter_luid,
        create_status);
    CHECK(create_status == VRREC_STATUS_OK);
    CHECK(port != nullptr);
    D3d11VideoFrameProcessor processor(*port);
    std::shared_ptr<VideoSurface> bgra_output;
    std::shared_ptr<VideoSurface> rgba_output;
    CHECK(processor.Process(bgra_source, bgra_plan, bgra_output) ==
          VRREC_STATUS_OK);
    CHECK(processor.Process(rgba_source, rgba_plan, rgba_output) ==
          VRREC_STATUS_OK);

    const auto bgra = ReadBackNv12(hardware, bgra_output);
    const auto rgba = ReadBackNv12(hardware, rgba_output);
    CHECK(IsClose(Nv12Y(bgra, 30, 30), Nv12Y(rgba, 30, 30)));
    CHECK(IsClose(
        Nv12Chroma(bgra, 15, 30),
        Nv12Chroma(rgba, 15, 30)));
    CHECK(IsClose(
        Nv12Chroma(bgra, 15, 31),
        Nv12Chroma(rgba, 15, 31)));
}

ComOwner<ID3D11Texture2D> CreateKeyedMutexTestTexture(
    ID3D11Device *device,
    bool keyed)
{
    D3D11_TEXTURE2D_DESC descriptor {};
    descriptor.Width = 64;
    descriptor.Height = 32;
    descriptor.MipLevels = 1;
    descriptor.ArraySize = 1;
    descriptor.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    descriptor.SampleDesc.Count = 1;
    descriptor.Usage = D3D11_USAGE_DEFAULT;
    descriptor.BindFlags =
        D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_RENDER_TARGET;
    descriptor.MiscFlags = keyed
        ? D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX
        : 0;

    ComOwner<ID3D11Texture2D> texture;
    CHECK(SUCCEEDED(device->CreateTexture2D(
        &descriptor,
        nullptr,
        texture.Put())));
    return texture;
}

VideoSurfaceDescriptor KeyedMutexSurfaceDescriptor(
    std::uint64_t adapter_luid)
{
    return {
        adapter_luid,
        64,
        32,
        VRREC_SOURCE_PIXEL_FORMAT_BGRA8,
        9,
    };
}

void UsesExactKeyedMutexKeysAndRetriesAfterARealTimeout()
{
    const auto producer = CreateHardwareVideoDevice();
    const auto consumer = CreateHardwareVideoDevice();
    CHECK(producer.adapter_luid == consumer.adapter_luid);
    const auto producer_texture = CreateKeyedMutexTestTexture(
        producer.device.Get(),
        true);

    ComOwner<IDXGIResource> shared_resource;
    CHECK(SUCCEEDED(producer_texture.Get()->QueryInterface(
        __uuidof(IDXGIResource),
        reinterpret_cast<void **>(shared_resource.Put()))));
    HANDLE shared_handle = nullptr;
    CHECK(SUCCEEDED(shared_resource.Get()->GetSharedHandle(
        &shared_handle)));
    CHECK(shared_handle != nullptr);

    ComOwner<ID3D11Texture2D> consumer_texture;
    CHECK(SUCCEEDED(consumer.device.Get()->OpenSharedResource(
        shared_handle,
        __uuidof(ID3D11Texture2D),
        reinterpret_cast<void **>(consumer_texture.Put()))));

    ComOwner<IDXGIKeyedMutex> producer_mutex;
    CHECK(SUCCEEDED(producer_texture.Get()->QueryInterface(
        __uuidof(IDXGIKeyedMutex),
        reinterpret_cast<void **>(producer_mutex.Put()))));
    CHECK(producer_mutex.Get()->AcquireSync(0, 0) == S_OK);

    vrrec_status_t status = VRREC_STATUS_INTERNAL_ERROR;
    auto surface = CreateWindowsD3d11KeyedMutexVideoSurface(
        consumer_texture.Get(),
        KeyedMutexSurfaceDescriptor(consumer.adapter_luid),
        1,
        2,
        status);
    CHECK(status == VRREC_STATUS_OK);
    CHECK(surface != nullptr);
    CHECK(surface->NativeHandle() == consumer_texture.Get());
    CHECK(surface->Descriptor().generation_id == 9);

    CHECK(surface->AcquireForRead(std::chrono::milliseconds(1)) ==
          VideoSurfaceAcquireResult::Timeout);
    CHECK(producer_mutex.Get()->ReleaseSync(1) == S_OK);
    CHECK(surface->AcquireForRead(std::chrono::milliseconds(20)) ==
          VideoSurfaceAcquireResult::Acquired);
    CHECK(surface->ReleaseFromRead() == VRREC_STATUS_OK);

    CHECK(producer_mutex.Get()->AcquireSync(2, 20) == S_OK);
    CHECK(producer_mutex.Get()->ReleaseSync(0) == S_OK);
}

void RejectsNonKeyedAndMismatchedTextures()
{
    const auto hardware = CreateHardwareVideoDevice();
    vrrec_status_t status = VRREC_STATUS_OK;

    const auto plain_texture = CreateKeyedMutexTestTexture(
        hardware.device.Get(),
        false);
    CHECK(!CreateWindowsD3d11KeyedMutexVideoSurface(
        plain_texture.Get(),
        KeyedMutexSurfaceDescriptor(hardware.adapter_luid),
        0,
        1,
        status));
    CHECK(status == VRREC_STATUS_INVALID_ARGUMENT);

    const auto keyed_texture = CreateKeyedMutexTestTexture(
        hardware.device.Get(),
        true);
    auto wrong_dimensions = KeyedMutexSurfaceDescriptor(
        hardware.adapter_luid);
    ++wrong_dimensions.width;
    CHECK(!CreateWindowsD3d11KeyedMutexVideoSurface(
        keyed_texture.Get(),
        wrong_dimensions,
        0,
        1,
        status));
    CHECK(status == VRREC_STATUS_INVALID_ARGUMENT);

    auto wrong_adapter = KeyedMutexSurfaceDescriptor(
        hardware.adapter_luid);
    ++wrong_adapter.adapter_luid;
    CHECK(!CreateWindowsD3d11KeyedMutexVideoSurface(
        keyed_texture.Get(),
        wrong_adapter,
        0,
        1,
        status));
    CHECK(status == VRREC_STATUS_INVALID_ARGUMENT);
}

}

int main()
{
    ConvertsOddBgraToOwnedNv12AndPadsBeforeFit();
    PreservesLogicalRedAcrossBgraAndRgbaChannelOrder();
    UsesExactKeyedMutexKeysAndRetriesAfterARealTimeout();
    RejectsNonKeyedAndMismatchedTextures();
    return 0;
}
