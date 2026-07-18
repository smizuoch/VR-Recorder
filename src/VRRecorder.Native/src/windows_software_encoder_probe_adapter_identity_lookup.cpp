#include "windows_software_encoder_probe_adapter_identity_lookup.hpp"

#if !defined(_WIN32)
#error "The software encoder probe adapter lookup requires Windows"
#endif

#include <d3d11.h>
#include <dxgi1_2.h>
#include <windows.h>

#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <new>
#include <string>
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

private:
    T *value_ = nullptr;
};

std::uint64_t PackLuid(const LUID &luid) noexcept
{
    return static_cast<std::uint64_t>(luid.LowPart) |
        (static_cast<std::uint64_t>(
             static_cast<std::uint32_t>(luid.HighPart))
         << 32U);
}

bool TryWideToUtf8(const wchar_t *value, std::string &output)
{
    if (value == nullptr || value[0] == L'\0') {
        return false;
    }
    const auto required = WideCharToMultiByte(
        CP_UTF8,
        WC_ERR_INVALID_CHARS,
        value,
        -1,
        nullptr,
        0,
        nullptr,
        nullptr);
    if (required <= 1) {
        return false;
    }
    output.resize(static_cast<std::size_t>(required));
    const auto converted = WideCharToMultiByte(
        CP_UTF8,
        WC_ERR_INVALID_CHARS,
        value,
        -1,
        output.data(),
        required,
        nullptr,
        nullptr);
    if (converted != required) {
        output.clear();
        return false;
    }
    output.resize(static_cast<std::size_t>(required - 1));
    return true;
}

bool TryFormatGpuIdentity(
    const DXGI_ADAPTER_DESC1 &descriptor,
    std::string &identity)
{
    if (!TryWideToUtf8(descriptor.Description, identity)) {
        identity = "unidentified-adapter";
    }
    char suffix[64] {};
    const auto written = std::snprintf(
        suffix,
        sizeof(suffix),
        " [vendor:%04X device:%04X]",
        descriptor.VendorId,
        descriptor.DeviceId);
    if (written <= 0 ||
        static_cast<std::size_t>(written) >= sizeof(suffix)) {
        return false;
    }
    identity.append(suffix, static_cast<std::size_t>(written));
    return true;
}

bool TryFormatDriverIdentity(
    IDXGIAdapter1 &adapter,
    std::string &identity)
{
    LARGE_INTEGER version {};
    if (FAILED(adapter.CheckInterfaceSupport(
            __uuidof(IDXGIDevice),
            &version))) {
        return false;
    }
    const auto raw = static_cast<std::uint64_t>(version.QuadPart);
    char text[96] {};
    const auto written = std::snprintf(
        text,
        sizeof(text),
        "dxgi-driver:%u.%u.%u.%u",
        static_cast<unsigned int>((raw >> 48U) & 0xffffU),
        static_cast<unsigned int>((raw >> 32U) & 0xffffU),
        static_cast<unsigned int>((raw >> 16U) & 0xffffU),
        static_cast<unsigned int>(raw & 0xffffU));
    if (written <= 0 || static_cast<std::size_t>(written) >= sizeof(text)) {
        return false;
    }
    identity.assign(text, static_cast<std::size_t>(written));
    return true;
}

bool TryFormatDeviceIdentity(
    const DXGI_ADAPTER_DESC1 &descriptor,
    std::string &identity)
{
    char text[128] {};
    const auto written = std::snprintf(
        text,
        sizeof(text),
        "pci\\ven_%04x&dev_%04x&subsys_%08x&rev_%02x",
        descriptor.VendorId,
        descriptor.DeviceId,
        descriptor.SubSysId,
        descriptor.Revision);
    if (written <= 0 || static_cast<std::size_t>(written) >= sizeof(text)) {
        return false;
    }
    identity.assign(text, static_cast<std::size_t>(written));
    return true;
}

}

SoftwareEncoderProbeAdapterIdentityResult
WindowsSoftwareEncoderProbeAdapterIdentityLookup::Lookup(
    std::uint64_t adapter_luid) noexcept
{
    if (adapter_luid == 0) {
        return {VRREC_STATUS_INVALID_ARGUMENT, {}};
    }

    try {
        ComOwner<IDXGIFactory1> factory;
        const auto factory_result = CreateDXGIFactory1(
            __uuidof(IDXGIFactory1),
            reinterpret_cast<void **>(factory.Put()));
        if (FAILED(factory_result)) {
            return {VRREC_STATUS_BACKEND_UNAVAILABLE, {}};
        }

        for (UINT index = 0;; ++index) {
            ComOwner<IDXGIAdapter1> adapter;
            const auto enumeration_result =
                factory.Get()->EnumAdapters1(index, adapter.Put());
            if (enumeration_result == DXGI_ERROR_NOT_FOUND) {
                return {VRREC_STATUS_BACKEND_UNAVAILABLE, {}};
            }
            if (FAILED(enumeration_result)) {
                return {VRREC_STATUS_INTERNAL_ERROR, {}};
            }

            DXGI_ADAPTER_DESC1 descriptor {};
            if (FAILED(adapter.Get()->GetDesc1(&descriptor))) {
                return {VRREC_STATUS_INTERNAL_ERROR, {}};
            }
            const auto found_luid = PackLuid(descriptor.AdapterLuid);
            if (found_luid != adapter_luid) {
                continue;
            }

            SoftwareEncoderProbeAdapterIdentity identity;
            identity.adapter_luid = found_luid;
            if (!TryFormatGpuIdentity(descriptor, identity.gpu_identity) ||
                !TryFormatDriverIdentity(
                    *adapter.Get(),
                    identity.driver_identity) ||
                !TryFormatDeviceIdentity(
                    descriptor,
                    identity.device_identity)) {
                return {VRREC_STATUS_BACKEND_UNAVAILABLE, {}};
            }
            return {VRREC_STATUS_OK, std::move(identity)};
        }
    } catch (const std::bad_alloc &) {
        return {VRREC_STATUS_OUT_OF_MEMORY, {}};
    } catch (...) {
        return {VRREC_STATUS_INTERNAL_ERROR, {}};
    }
}

}
