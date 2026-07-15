#include "windows_d3d11_keyed_mutex_surface.hpp"

#if !defined(_WIN32)
#error "The Windows D3D11 keyed-mutex surface requires Windows"
#endif

#include <d3d11.h>
#include <dxgi1_2.h>

#include <algorithm>
#include <chrono>
#include <cstdint>
#include <limits>
#include <memory>
#include <new>
#include <utility>

namespace vrrecorder::native {
namespace {

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

    T *Detach() noexcept
    {
        auto *value = value_;
        value_ = nullptr;
        return value;
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

std::uint64_t PackLuid(const LUID &luid) noexcept
{
    return static_cast<std::uint64_t>(luid.LowPart) |
           (static_cast<std::uint64_t>(
                static_cast<std::uint32_t>(luid.HighPart)) << 32U);
}

DXGI_FORMAT TextureFormat(
    vrrec_source_pixel_format_t pixel_format) noexcept
{
    switch (pixel_format) {
    case VRREC_SOURCE_PIXEL_FORMAT_BGRA8:
        return DXGI_FORMAT_B8G8R8A8_UNORM;
    case VRREC_SOURCE_PIXEL_FORMAT_RGBA8:
        return DXGI_FORMAT_R8G8B8A8_UNORM;
    case VRREC_SOURCE_PIXEL_FORMAT_NV12:
        return DXGI_FORMAT_NV12;
    }
    return DXGI_FORMAT_UNKNOWN;
}

KeyedMutexOperationResult DeviceFailure(
    ID3D11Device *device,
    HRESULT result) noexcept
{
    if (result == DXGI_ERROR_DEVICE_REMOVED) {
        return KeyedMutexOperationResult::DeviceRemoved;
    }
    if (result == DXGI_ERROR_DEVICE_RESET) {
        return KeyedMutexOperationResult::DeviceReset;
    }

    const auto reason = device->GetDeviceRemovedReason();
    if (reason == DXGI_ERROR_DEVICE_REMOVED) {
        return KeyedMutexOperationResult::DeviceRemoved;
    }
    if (reason == DXGI_ERROR_DEVICE_RESET) {
        return KeyedMutexOperationResult::DeviceReset;
    }
    return KeyedMutexOperationResult::Failed;
}

class WindowsD3d11KeyedMutexPort final : public KeyedMutexPort {
public:
    WindowsD3d11KeyedMutexPort(
        ID3D11Texture2D *texture,
        IDXGIKeyedMutex *keyed_mutex,
        ID3D11Device *device,
        std::uint64_t acquire_key,
        std::uint64_t release_key) noexcept
        : texture_(texture),
          keyed_mutex_(keyed_mutex),
          device_(device),
          acquire_key_(acquire_key),
          release_key_(release_key)
    {
    }

    ~WindowsD3d11KeyedMutexPort() override
    {
        device_->Release();
        keyed_mutex_->Release();
        texture_->Release();
    }

    void *NativeHandle() const noexcept override
    {
        return texture_;
    }

    KeyedMutexOperationResult Acquire(
        std::chrono::milliseconds timeout) noexcept override
    {
        constexpr auto max_bounded_timeout =
            static_cast<std::chrono::milliseconds::rep>(INFINITE - 1U);
        const auto timeout_count = std::min(
            timeout.count(),
            max_bounded_timeout);
        const auto result = keyed_mutex_->AcquireSync(
            acquire_key_,
            static_cast<DWORD>(timeout_count));
        if (result == S_OK) {
            return KeyedMutexOperationResult::Succeeded;
        }
        if (result == static_cast<HRESULT>(WAIT_TIMEOUT) ||
            result == HRESULT_FROM_WIN32(WAIT_TIMEOUT)) {
            return KeyedMutexOperationResult::Timeout;
        }
        if (result == static_cast<HRESULT>(WAIT_ABANDONED) ||
            result == HRESULT_FROM_WIN32(WAIT_ABANDONED)) {
            return KeyedMutexOperationResult::Abandoned;
        }
        return DeviceFailure(device_, result);
    }

    KeyedMutexOperationResult Release() noexcept override
    {
        const auto result = keyed_mutex_->ReleaseSync(release_key_);
        if (result == S_OK) {
            return KeyedMutexOperationResult::Succeeded;
        }
        return DeviceFailure(device_, result);
    }

private:
    ID3D11Texture2D *texture_;
    IDXGIKeyedMutex *keyed_mutex_;
    ID3D11Device *device_;
    std::uint64_t acquire_key_;
    std::uint64_t release_key_;
};

vrrec_status_t FactoryFailureStatus(HRESULT result) noexcept
{
    if (result == E_OUTOFMEMORY) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    }
    if (result == DXGI_ERROR_DEVICE_REMOVED ||
        result == DXGI_ERROR_DEVICE_RESET) {
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    }
    return VRREC_STATUS_INVALID_ARGUMENT;
}

}

std::shared_ptr<KeyedMutexVideoSurface>
CreateWindowsD3d11KeyedMutexVideoSurface(
    void *d3d11_texture,
    VideoSurfaceDescriptor descriptor,
    std::uint64_t acquire_key,
    std::uint64_t release_key,
    vrrec_status_t &status) noexcept
{
    status = VRREC_STATUS_INVALID_ARGUMENT;
    if (d3d11_texture == nullptr) {
        return {};
    }

    auto *texture = static_cast<ID3D11Texture2D *>(d3d11_texture);
    D3D11_TEXTURE2D_DESC texture_descriptor {};
    texture->GetDesc(&texture_descriptor);
    const auto expected_format = TextureFormat(descriptor.pixel_format);
    if (descriptor.adapter_luid == 0 || descriptor.width == 0 ||
        descriptor.height == 0 || descriptor.generation_id == 0 ||
        expected_format == DXGI_FORMAT_UNKNOWN ||
        texture_descriptor.Width != descriptor.width ||
        texture_descriptor.Height != descriptor.height ||
        texture_descriptor.Format != expected_format ||
        texture_descriptor.MipLevels != 1 ||
        texture_descriptor.ArraySize != 1 ||
        texture_descriptor.SampleDesc.Count != 1 ||
        texture_descriptor.Usage != D3D11_USAGE_DEFAULT ||
        (texture_descriptor.MiscFlags &
         D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX) == 0) {
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
        status = FactoryFailureStatus(result);
        return {};
    }
    ComOwner<IDXGIAdapter> adapter;
    result = dxgi_device.Get()->GetAdapter(adapter.Put());
    if (FAILED(result)) {
        status = FactoryFailureStatus(result);
        return {};
    }
    DXGI_ADAPTER_DESC adapter_descriptor {};
    result = adapter.Get()->GetDesc(&adapter_descriptor);
    if (FAILED(result)) {
        status = FactoryFailureStatus(result);
        return {};
    }
    if (PackLuid(adapter_descriptor.AdapterLuid) !=
        descriptor.adapter_luid) {
        return {};
    }

    ComOwner<IDXGIKeyedMutex> keyed_mutex;
    result = texture->QueryInterface(
        __uuidof(IDXGIKeyedMutex),
        reinterpret_cast<void **>(keyed_mutex.Put()));
    if (FAILED(result)) {
        status = FactoryFailureStatus(result);
        return {};
    }

    texture->AddRef();
    auto port = std::unique_ptr<KeyedMutexPort>(
        new (std::nothrow) WindowsD3d11KeyedMutexPort(
            texture,
            keyed_mutex.Detach(),
            device.Detach(),
            acquire_key,
            release_key));
    if (!port) {
        texture->Release();
        status = VRREC_STATUS_OUT_OF_MEMORY;
        return {};
    }
    return CreateKeyedMutexVideoSurface(
        std::move(port),
        descriptor,
        status);
}

}
