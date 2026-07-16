#include "windows_d3d11_multithread_protection.hpp"

#if !defined(_WIN32)
#error "D3D11 multithread protection requires Windows"
#endif

#include <d3d11_4.h>

namespace vrrecorder::native {

vrrec_status_t EnableWindowsD3d11MultithreadProtection(
    void *d3d11_immediate_context) noexcept
{
    if (d3d11_immediate_context == nullptr) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    auto *context = static_cast<ID3D11DeviceContext *>(
        d3d11_immediate_context);
    ID3D11Multithread *multithread = nullptr;
    const auto result = context->QueryInterface(
        __uuidof(ID3D11Multithread),
        reinterpret_cast<void **>(&multithread));
    if (FAILED(result) || multithread == nullptr) {
        if (multithread != nullptr) {
            multithread->Release();
        }
        if (result == E_OUTOFMEMORY) {
            return VRREC_STATUS_OUT_OF_MEMORY;
        }
        return result == E_NOINTERFACE
            ? VRREC_STATUS_BACKEND_UNAVAILABLE
            : VRREC_STATUS_INTERNAL_ERROR;
    }

    multithread->SetMultithreadProtected(TRUE);
    const auto enabled = multithread->GetMultithreadProtected() != FALSE;
    multithread->Release();
    return enabled ? VRREC_STATUS_OK : VRREC_STATUS_INTERNAL_ERROR;
}

}
