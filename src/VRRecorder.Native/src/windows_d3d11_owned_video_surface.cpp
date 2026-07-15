#include "windows_d3d11_owned_video_surface.hpp"

#if !defined(_WIN32)
#error "The owned D3D11 video surface requires Windows"
#endif

#include <d3d11.h>
#include <dxgi1_2.h>

#include <chrono>
#include <cstdint>
#include <memory>
#include <mutex>
#include <new>

namespace vrrecorder::native {
namespace {

template <typename T>
class ComOwner final {
public:
    ~ComOwner()
    {
        if (value_ != nullptr) {
            value_->Release();
        }
    }

    T *Get() const noexcept
    {
        return value_;
    }

    T **Put() noexcept
    {
        if (value_ != nullptr) {
            value_->Release();
            value_ = nullptr;
        }
        return &value_;
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

DXGI_FORMAT ExpectedFormat(vrrec_source_pixel_format_t format) noexcept
{
    switch (format) {
    case VRREC_SOURCE_PIXEL_FORMAT_BGRA8:
        return DXGI_FORMAT_B8G8R8A8_UNORM;
    case VRREC_SOURCE_PIXEL_FORMAT_RGBA8:
        return DXGI_FORMAT_R8G8B8A8_UNORM;
    case VRREC_SOURCE_PIXEL_FORMAT_NV12:
        return DXGI_FORMAT_NV12;
    }
    return DXGI_FORMAT_UNKNOWN;
}

vrrec_status_t MapFailure(HRESULT result) noexcept
{
    if (result == E_OUTOFMEMORY) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    }
    if (result == DXGI_ERROR_DEVICE_REMOVED ||
        result == DXGI_ERROR_DEVICE_RESET) {
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    }
    return VRREC_STATUS_INTERNAL_ERROR;
}

class OwnedD3d11VideoSurface final : public VideoSurface {
public:
    OwnedD3d11VideoSurface(
        ID3D11Texture2D *texture,
        VideoSurfaceDescriptor descriptor) noexcept
        : texture_(texture), descriptor_(descriptor)
    {
        texture_->AddRef();
    }

    ~OwnedD3d11VideoSurface() override
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
        std::chrono::milliseconds timeout) noexcept override
    {
        const std::lock_guard lock(mutex_);
        if (timeout.count() < 0 || acquired_) {
            return VideoSurfaceAcquireResult::Failed;
        }
        acquired_ = true;
        return VideoSurfaceAcquireResult::Acquired;
    }

    vrrec_status_t ReleaseFromRead() noexcept override
    {
        const std::lock_guard lock(mutex_);
        if (!acquired_) {
            return VRREC_STATUS_INVALID_STATE;
        }
        acquired_ = false;
        return VRREC_STATUS_OK;
    }

private:
    ID3D11Texture2D *texture_;
    VideoSurfaceDescriptor descriptor_;
    std::mutex mutex_;
    bool acquired_ = false;
};

}

std::shared_ptr<VideoSurface> CreateWindowsD3d11OwnedVideoSurface(
    void *d3d11_texture,
    VideoSurfaceDescriptor descriptor,
    vrrec_status_t &status) noexcept
{
    status = VRREC_STATUS_INVALID_ARGUMENT;
    if (d3d11_texture == nullptr || descriptor.adapter_luid == 0 ||
        descriptor.width == 0 || descriptor.height == 0 ||
        descriptor.generation_id == 0) {
        return {};
    }

    auto *texture = static_cast<ID3D11Texture2D *>(d3d11_texture);
    D3D11_TEXTURE2D_DESC texture_descriptor {};
    texture->GetDesc(&texture_descriptor);
    const auto expected_format = ExpectedFormat(descriptor.pixel_format);
    if (expected_format == DXGI_FORMAT_UNKNOWN ||
        texture_descriptor.Width != descriptor.width ||
        texture_descriptor.Height != descriptor.height ||
        texture_descriptor.Format != expected_format ||
        texture_descriptor.MipLevels != 1 ||
        texture_descriptor.ArraySize != 1 ||
        texture_descriptor.SampleDesc.Count != 1 ||
        texture_descriptor.Usage != D3D11_USAGE_DEFAULT) {
        return {};
    }

    ComOwner<ID3D11Device> device;
    texture->GetDevice(device.Put());
    if (device.Get() == nullptr) {
        status = VRREC_STATUS_INTERNAL_ERROR;
        return {};
    }
    ComOwner<IDXGIDevice> dxgi_device;
    auto result = device.Get()->QueryInterface(
        __uuidof(IDXGIDevice),
        reinterpret_cast<void **>(dxgi_device.Put()));
    if (FAILED(result)) {
        status = MapFailure(result);
        return {};
    }
    ComOwner<IDXGIAdapter> adapter;
    result = dxgi_device.Get()->GetAdapter(adapter.Put());
    if (FAILED(result)) {
        status = MapFailure(result);
        return {};
    }
    DXGI_ADAPTER_DESC adapter_descriptor {};
    result = adapter.Get()->GetDesc(&adapter_descriptor);
    if (FAILED(result)) {
        status = MapFailure(result);
        return {};
    }
    if (PackLuid(adapter_descriptor.AdapterLuid) !=
        descriptor.adapter_luid) {
        return {};
    }

    auto *owned = new (std::nothrow) OwnedD3d11VideoSurface(
        texture,
        descriptor);
    if (owned == nullptr) {
        status = VRREC_STATUS_OUT_OF_MEMORY;
        return {};
    }
    try {
        auto surface = std::shared_ptr<VideoSurface>(owned);
        status = VRREC_STATUS_OK;
        return surface;
    } catch (const std::bad_alloc &) {
        delete owned;
        status = VRREC_STATUS_OUT_OF_MEMORY;
        return {};
    } catch (...) {
        delete owned;
        status = VRREC_STATUS_INTERNAL_ERROR;
        return {};
    }
}

}
