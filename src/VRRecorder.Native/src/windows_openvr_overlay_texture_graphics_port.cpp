#include "windows_openvr_overlay_texture_graphics_port.hpp"

#include <array>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <memory>
#include <new>
#include <utility>

#include <d3d11.h>
#include <dxgi1_2.h>
#include <wrl/client.h>

namespace vrrecorder::native {
namespace {

using Microsoft::WRL::ComPtr;

vrrec_status_t MapGraphicsError(HRESULT error) noexcept
{
    if (error == E_INVALIDARG) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }
    if (error == E_OUTOFMEMORY) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    }
    if (error == DXGI_ERROR_DEVICE_HUNG ||
        error == DXGI_ERROR_DEVICE_REMOVED ||
        error == DXGI_ERROR_DEVICE_RESET ||
        error == DXGI_ERROR_DRIVER_INTERNAL_ERROR ||
        error == DXGI_ERROR_NOT_FOUND ||
        error == DXGI_ERROR_NOT_CURRENTLY_AVAILABLE) {
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    }
    return VRREC_STATUS_INTERNAL_ERROR;
}

vrrec_status_t MapOverlayError(vr::EVROverlayError error) noexcept
{
    if (error == vr::VROverlayError_None) {
        return VRREC_STATUS_OK;
    }
    if (error == vr::VROverlayError_RequestFailed ||
        error == vr::VROverlayError_TimedOut ||
        error == vr::VROverlayError_OverlayLimitExceeded ||
        error == vr::VROverlayError_InvalidTexture) {
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    }
    if (error == vr::VROverlayError_UnknownOverlay ||
        error == vr::VROverlayError_InvalidHandle) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (error == vr::VROverlayError_InvalidParameter) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }
    return VRREC_STATUS_INTERNAL_ERROR;
}

class WindowsOpenVrOverlayTextureResource final
    : public OpenVrOverlayTextureGraphicsResource {
public:
    explicit WindowsOpenVrOverlayTextureResource(
        ComPtr<ID3D11Texture2D> texture) noexcept
        : texture_(std::move(texture))
    {
    }

    ID3D11Texture2D *Texture() const noexcept
    {
        return texture_.Get();
    }

private:
    ComPtr<ID3D11Texture2D> texture_;
};

class WindowsOpenVrOverlayTextureGraphicsPort final
    : public OpenVrOverlayTextureGraphicsPort {
public:
    WindowsOpenVrOverlayTextureGraphicsPort(
        vr::IVROverlay *overlay,
        std::int32_t adapter_index) noexcept
        : overlay_(overlay), adapter_index_(adapter_index)
    {
    }

    vrrec_status_t CreateBgraTexture(
        std::uint32_t width,
        std::uint32_t height,
        std::unique_ptr<OpenVrOverlayTextureGraphicsResource> &resource)
        noexcept override
    {
        resource.reset();
        if (width == 0 || height == 0) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }
        const auto device_status = EnsureDevice();
        if (device_status != VRREC_STATUS_OK) {
            return device_status;
        }

        auto description = D3D11_TEXTURE2D_DESC {};
        description.Width = width;
        description.Height = height;
        description.MipLevels = 1;
        description.ArraySize = 1;
        description.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        description.SampleDesc.Count = 1;
        description.Usage = D3D11_USAGE_DYNAMIC;
        description.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        description.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;

        auto texture = ComPtr<ID3D11Texture2D> {};
        const auto result = device_->CreateTexture2D(
            &description,
            nullptr,
            texture.GetAddressOf());
        if (FAILED(result)) {
            return MapGraphicsError(result);
        }
        auto concrete = std::unique_ptr<WindowsOpenVrOverlayTextureResource>(
            new (std::nothrow) WindowsOpenVrOverlayTextureResource(
                std::move(texture)));
        if (!concrete) {
            return VRREC_STATUS_OUT_OF_MEMORY;
        }
        resource = std::move(concrete);
        return VRREC_STATUS_OK;
    }

    vrrec_status_t UploadBgraTexture(
        OpenVrOverlayTextureGraphicsResource &resource,
        const OpenVrBgraTextureFrame &frame) noexcept override
    {
        if (!context_ || frame.pixel_bytes == nullptr) {
            return VRREC_STATUS_INVALID_STATE;
        }
        auto &native = static_cast<WindowsOpenVrOverlayTextureResource &>(
            resource);
        auto mapped = D3D11_MAPPED_SUBRESOURCE {};
        const auto result = context_->Map(
            native.Texture(),
            0,
            D3D11_MAP_WRITE_DISCARD,
            0,
            &mapped);
        if (FAILED(result)) {
            return MapGraphicsError(result);
        }

        const auto copied_row_bytes =
            static_cast<std::size_t>(frame.width) * 4U;
        if (mapped.pData == nullptr || mapped.RowPitch < copied_row_bytes) {
            context_->Unmap(native.Texture(), 0);
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        auto *destination = static_cast<std::uint8_t *>(mapped.pData);
        for (auto row = std::uint32_t {0}; row < frame.height; ++row) {
            std::memcpy(
                destination + static_cast<std::size_t>(row) * mapped.RowPitch,
                frame.pixel_bytes +
                    static_cast<std::size_t>(row) * frame.stride_bytes,
                copied_row_bytes);
        }
        context_->Unmap(native.Texture(), 0);
        return VRREC_STATUS_OK;
    }

    vrrec_status_t SubmitOverlayTexture(
        std::uint64_t handle,
        OpenVrOverlayTextureGraphicsResource &resource) noexcept override
    {
        if (overlay_ == nullptr || handle == 0) {
            return VRREC_STATUS_INVALID_STATE;
        }
        auto &native = static_cast<WindowsOpenVrOverlayTextureResource &>(
            resource);
        const auto texture = vr::Texture_t {
            native.Texture(),
            vr::TextureType_DirectX,
            vr::ColorSpace_Auto,
        };
        return MapOverlayError(overlay_->SetOverlayTexture(
            static_cast<vr::VROverlayHandle_t>(handle),
            &texture));
    }

    vrrec_status_t ClearOverlayTexture(
        std::uint64_t handle) noexcept override
    {
        return overlay_ == nullptr || handle == 0
            ? VRREC_STATUS_INVALID_STATE
            : MapOverlayError(overlay_->ClearOverlayTexture(
                static_cast<vr::VROverlayHandle_t>(handle)));
    }

private:
    vrrec_status_t EnsureDevice() noexcept
    {
        if (device_ && context_) {
            return VRREC_STATUS_OK;
        }
        if (adapter_index_ < 0) {
            return VRREC_STATUS_BACKEND_UNAVAILABLE;
        }

        auto factory = ComPtr<IDXGIFactory1> {};
        auto result = CreateDXGIFactory1(
            IID_PPV_ARGS(factory.GetAddressOf()));
        if (FAILED(result)) {
            return MapGraphicsError(result);
        }
        auto adapter = ComPtr<IDXGIAdapter1> {};
        result = factory->EnumAdapters1(
            static_cast<UINT>(adapter_index_),
            adapter.GetAddressOf());
        if (FAILED(result)) {
            return MapGraphicsError(result);
        }

        constexpr auto feature_levels =
            std::array {D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0};
        auto selected_level = D3D_FEATURE_LEVEL_11_0;
        result = D3D11CreateDevice(
            adapter.Get(),
            D3D_DRIVER_TYPE_UNKNOWN,
            nullptr,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            feature_levels.data(),
            static_cast<UINT>(feature_levels.size()),
            D3D11_SDK_VERSION,
            device_.GetAddressOf(),
            &selected_level,
            context_.GetAddressOf());
        if (result == E_INVALIDARG) {
            device_.Reset();
            context_.Reset();
            result = D3D11CreateDevice(
                adapter.Get(),
                D3D_DRIVER_TYPE_UNKNOWN,
                nullptr,
                D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                feature_levels.data() + 1,
                1,
                D3D11_SDK_VERSION,
                device_.GetAddressOf(),
                &selected_level,
                context_.GetAddressOf());
        }
        return FAILED(result)
            ? MapGraphicsError(result)
            : VRREC_STATUS_OK;
    }

    vr::IVROverlay *overlay_;
    std::int32_t adapter_index_;
    ComPtr<ID3D11Device> device_;
    ComPtr<ID3D11DeviceContext> context_;
};

}

std::unique_ptr<OpenVrOverlayTextureGraphicsPort>
CreateWindowsOpenVrOverlayTextureGraphicsPort(
    vr::IVROverlay *overlay,
    std::int32_t adapter_index,
    vrrec_status_t &status) noexcept
{
    status = VRREC_STATUS_INVALID_ARGUMENT;
    if (overlay == nullptr) {
        return nullptr;
    }
    auto port = std::unique_ptr<OpenVrOverlayTextureGraphicsPort>(
        new (std::nothrow) WindowsOpenVrOverlayTextureGraphicsPort(
            overlay,
            adapter_index));
    if (!port) {
        status = VRREC_STATUS_OUT_OF_MEMORY;
        return nullptr;
    }
    status = VRREC_STATUS_OK;
    return port;
}

}
