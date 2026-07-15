#include "windows_d3d11_nv12_readback_port.hpp"

#if !defined(_WIN32)
#error "The Windows D3D11 NV12 readback Port requires Windows"
#endif

#include <d3d11.h>
#include <dxgi1_2.h>

#include <atomic>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <memory>
#include <mutex>
#include <new>
#include <utility>

namespace vrrecorder::native {
namespace {

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

    void Attach(T *value) noexcept
    {
        Reset();
        value_ = value;
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

vrrec_status_t MapStatus(HRESULT result) noexcept
{
    if (result == E_OUTOFMEMORY) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    }
    if (result == DXGI_ERROR_DEVICE_REMOVED ||
        result == DXGI_ERROR_DEVICE_RESET) {
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    }
    if (result == E_INVALIDARG) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }
    return VRREC_STATUS_INTERNAL_ERROR;
}

std::uint64_t PackLuid(const LUID &luid) noexcept
{
    return static_cast<std::uint64_t>(luid.LowPart) |
        (static_cast<std::uint64_t>(
             static_cast<std::uint32_t>(luid.HighPart)) << 32U);
}

bool TryGetAdapterLuid(
    ID3D11Device *device,
    std::uint64_t &adapter_luid,
    vrrec_status_t &status) noexcept
{
    adapter_luid = 0;
    ComOwner<IDXGIDevice> dxgi_device;
    auto result = device->QueryInterface(
        __uuidof(IDXGIDevice),
        reinterpret_cast<void **>(dxgi_device.Put()));
    if (FAILED(result)) {
        status = MapStatus(result);
        return false;
    }
    ComOwner<IDXGIAdapter> adapter;
    result = dxgi_device.Get()->GetAdapter(adapter.Put());
    if (FAILED(result)) {
        status = MapStatus(result);
        return false;
    }
    DXGI_ADAPTER_DESC descriptor {};
    result = adapter.Get()->GetDesc(&descriptor);
    if (FAILED(result)) {
        status = MapStatus(result);
        return false;
    }

    adapter_luid = PackLuid(descriptor.AdapterLuid);
    if (adapter_luid == 0) {
        status = VRREC_STATUS_INTERNAL_ERROR;
        return false;
    }
    status = VRREC_STATUS_OK;
    return true;
}

bool IsSameComObject(IUnknown *left, IUnknown *right) noexcept
{
    if (left == nullptr || right == nullptr) {
        return false;
    }
    ComOwner<IUnknown> left_identity;
    ComOwner<IUnknown> right_identity;
    if (FAILED(left->QueryInterface(
            __uuidof(IUnknown),
            reinterpret_cast<void **>(left_identity.Put()))) ||
        FAILED(right->QueryInterface(
            __uuidof(IUnknown),
            reinterpret_cast<void **>(right_identity.Put())))) {
        return false;
    }
    return left_identity.Get() == right_identity.Get();
}

class WindowsD3d11ReadbackResource;

class WindowsD3d11Nv12Mapping final
    : public SystemMemoryNv12FrameMapping {
public:
    WindowsD3d11Nv12Mapping(
        std::shared_ptr<WindowsD3d11ReadbackResource> resource,
        SystemMemoryNv12FrameView view) noexcept
        : resource_(std::move(resource)), view_(view)
    {
    }

    ~WindowsD3d11Nv12Mapping() override;

    SystemMemoryNv12FrameView View() const noexcept override
    {
        return view_;
    }

private:
    std::shared_ptr<WindowsD3d11ReadbackResource> resource_;
    SystemMemoryNv12FrameView view_;
};

class WindowsD3d11ReadbackResource final
    : public std::enable_shared_from_this<WindowsD3d11ReadbackResource> {
public:
    WindowsD3d11ReadbackResource(
        ComOwner<ID3D11Device> device,
        ComOwner<ID3D11DeviceContext> context,
        ComOwner<ID3D11Texture2D> staging,
        std::uint32_t width,
        std::uint32_t height) noexcept
        : device_(std::move(device)),
          context_(std::move(context)),
          staging_(std::move(staging)),
          width_(width),
          height_(height)
    {
    }

    ~WindowsD3d11ReadbackResource()
    {
        if (mapped_) {
            context_.Get()->Unmap(staging_.Get(), 0);
        }
    }

    bool Matches(
        ID3D11Device *device,
        std::uint32_t width,
        std::uint32_t height) const noexcept
    {
        return width_ == width && height_ == height &&
            IsSameComObject(device_.Get(), device);
    }

    SystemMemoryNv12FrameMapResult Read(
        ID3D11Texture2D *source) noexcept
    {
        const std::lock_guard lock(mutex_);
        if (mapped_) {
            return {VRREC_STATUS_INVALID_STATE, {}};
        }

        context_.Get()->CopyResource(staging_.Get(), source);
        D3D11_MAPPED_SUBRESOURCE mapped {};
        const auto result = context_.Get()->Map(
            staging_.Get(),
            0,
            D3D11_MAP_READ,
            0,
            &mapped);
        if (FAILED(result)) {
            return {MapStatus(result), {}};
        }
        if (mapped.pData == nullptr || mapped.RowPitch < width_) {
            context_.Get()->Unmap(staging_.Get(), 0);
            return {VRREC_STATUS_INTERNAL_ERROR, {}};
        }

        const auto row_pitch = static_cast<std::size_t>(mapped.RowPitch);
        const auto y_rows = static_cast<std::size_t>(height_);
        const auto uv_rows = static_cast<std::size_t>(height_ / 2U);
        if (y_rows > std::numeric_limits<std::size_t>::max() / row_pitch ||
            uv_rows > std::numeric_limits<std::size_t>::max() / row_pitch) {
            context_.Get()->Unmap(staging_.Get(), 0);
            return {VRREC_STATUS_INTERNAL_ERROR, {}};
        }
        const auto y_size = y_rows * row_pitch;
        const auto uv_size = uv_rows * row_pitch;
        auto resource = weak_from_this().lock();
        if (!resource) {
            context_.Get()->Unmap(staging_.Get(), 0);
            return {VRREC_STATUS_INTERNAL_ERROR, {}};
        }

        const auto *y_data = static_cast<const std::byte *>(mapped.pData);
        const auto view = SystemMemoryNv12FrameView {
            width_,
            height_,
            mapped.RowPitch,
            mapped.RowPitch,
            std::span<const std::byte>(y_data, y_size),
            std::span<const std::byte>(y_data + y_size, uv_size),
            -1,
        };
        auto *created = new (std::nothrow) WindowsD3d11Nv12Mapping(
            std::move(resource),
            view);
        if (created == nullptr) {
            context_.Get()->Unmap(staging_.Get(), 0);
            return {VRREC_STATUS_OUT_OF_MEMORY, {}};
        }

        mapped_ = true;
        return {
            VRREC_STATUS_OK,
            std::unique_ptr<SystemMemoryNv12FrameMapping>(created),
        };
    }

    void ReleaseMapping() noexcept
    {
        const std::lock_guard lock(mutex_);
        if (mapped_) {
            context_.Get()->Unmap(staging_.Get(), 0);
            mapped_ = false;
        }
    }

private:
    ComOwner<ID3D11Device> device_;
    ComOwner<ID3D11DeviceContext> context_;
    ComOwner<ID3D11Texture2D> staging_;
    const std::uint32_t width_;
    const std::uint32_t height_;
    mutable std::mutex mutex_;
    bool mapped_ = false;
};

WindowsD3d11Nv12Mapping::~WindowsD3d11Nv12Mapping()
{
    resource_->ReleaseMapping();
}

std::shared_ptr<WindowsD3d11ReadbackResource> CreateReadbackResource(
    ID3D11Device *device,
    std::uint32_t width,
    std::uint32_t height,
    vrrec_status_t &status) noexcept
{
    ComOwner<ID3D11Device> owned_device;
    device->AddRef();
    owned_device.Attach(device);

    ComOwner<ID3D11DeviceContext> context;
    device->GetImmediateContext(context.Put());
    if (!context) {
        status = VRREC_STATUS_INTERNAL_ERROR;
        return {};
    }

    const D3D11_TEXTURE2D_DESC staging_descriptor {
        width,
        height,
        1,
        1,
        DXGI_FORMAT_NV12,
        {1, 0},
        D3D11_USAGE_STAGING,
        0,
        D3D11_CPU_ACCESS_READ,
        0,
    };
    ComOwner<ID3D11Texture2D> staging;
    const auto create = device->CreateTexture2D(
        &staging_descriptor,
        nullptr,
        staging.Put());
    if (FAILED(create)) {
        status = MapStatus(create);
        return {};
    }

    try {
        auto resource = std::make_shared<WindowsD3d11ReadbackResource>(
            std::move(owned_device),
            std::move(context),
            std::move(staging),
            width,
            height);
        status = VRREC_STATUS_OK;
        return resource;
    } catch (const std::bad_alloc &) {
        status = VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        status = VRREC_STATUS_INTERNAL_ERROR;
    }
    return {};
}

bool IsSourceTextureValid(
    ID3D11Texture2D *texture,
    const VideoSurfaceDescriptor &surface_descriptor) noexcept
{
    D3D11_TEXTURE2D_DESC descriptor {};
    texture->GetDesc(&descriptor);
    return descriptor.Width == surface_descriptor.width &&
        descriptor.Height == surface_descriptor.height &&
        descriptor.MipLevels == 1 && descriptor.ArraySize == 1 &&
        descriptor.Format == DXGI_FORMAT_NV12 &&
        descriptor.SampleDesc.Count == 1 &&
        descriptor.Usage == D3D11_USAGE_DEFAULT;
}

class WindowsD3d11Nv12ReadbackPort final
    : public D3d11Nv12ReadbackPort {
public:
    explicit WindowsD3d11Nv12ReadbackPort(
        std::uint64_t adapter_luid) noexcept
        : adapter_luid_(adapter_luid)
    {
    }

    SystemMemoryNv12FrameMapResult Read(
        const std::shared_ptr<VideoSurface> &surface) noexcept override
    {
        if (aborted_.load(std::memory_order_acquire) || !surface ||
            surface->NativeHandle() == nullptr) {
            return {VRREC_STATUS_INVALID_STATE, {}};
        }
        const auto descriptor = surface->Descriptor();
        if (descriptor.adapter_luid != adapter_luid_ ||
            descriptor.pixel_format != VRREC_SOURCE_PIXEL_FORMAT_NV12 ||
            descriptor.width == 0 || descriptor.height == 0) {
            return {VRREC_STATUS_INVALID_ARGUMENT, {}};
        }

        auto *source = static_cast<ID3D11Texture2D *>(
            surface->NativeHandle());
        if (!IsSourceTextureValid(source, descriptor)) {
            return {VRREC_STATUS_INVALID_ARGUMENT, {}};
        }
        ComOwner<ID3D11Device> device;
        source->GetDevice(device.Put());
        if (!device) {
            return {VRREC_STATUS_INTERNAL_ERROR, {}};
        }
        std::uint64_t actual_adapter_luid = 0;
        auto status = VRREC_STATUS_INTERNAL_ERROR;
        if (!TryGetAdapterLuid(
                device.Get(),
                actual_adapter_luid,
                status)) {
            return {status, {}};
        }
        if (actual_adapter_luid != adapter_luid_) {
            return {VRREC_STATUS_INVALID_ARGUMENT, {}};
        }

        std::shared_ptr<WindowsD3d11ReadbackResource> resource;
        {
            const std::lock_guard lock(resource_mutex_);
            if (!resource_ || !resource_->Matches(
                    device.Get(),
                    descriptor.width,
                    descriptor.height)) {
                auto created = CreateReadbackResource(
                    device.Get(),
                    descriptor.width,
                    descriptor.height,
                    status);
                if (!created) {
                    return {status, {}};
                }
                resource_ = std::move(created);
            }
            resource = resource_;
        }

        if (aborted_.load(std::memory_order_acquire)) {
            return {VRREC_STATUS_INVALID_STATE, {}};
        }
        auto result = resource->Read(source);
        if (aborted_.load(std::memory_order_acquire)) {
            return {VRREC_STATUS_INVALID_STATE, {}};
        }
        return result;
    }

    void Abort() noexcept override
    {
        aborted_.store(true, std::memory_order_release);
    }

private:
    const std::uint64_t adapter_luid_;
    std::mutex resource_mutex_;
    std::shared_ptr<WindowsD3d11ReadbackResource> resource_;
    std::atomic_bool aborted_ = false;
};

}

std::unique_ptr<D3d11Nv12ReadbackPort>
CreateWindowsD3d11Nv12ReadbackPort(
    std::uint64_t adapter_luid,
    vrrec_status_t &status) noexcept
{
    status = VRREC_STATUS_INVALID_ARGUMENT;
    if (adapter_luid == 0) {
        return {};
    }

    auto *created = new (std::nothrow)
        WindowsD3d11Nv12ReadbackPort(adapter_luid);
    if (created == nullptr) {
        status = VRREC_STATUS_OUT_OF_MEMORY;
        return {};
    }
    status = VRREC_STATUS_OK;
    return std::unique_ptr<D3d11Nv12ReadbackPort>(created);
}

}
