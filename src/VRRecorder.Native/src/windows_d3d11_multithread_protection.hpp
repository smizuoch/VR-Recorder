#ifndef VRRECORDER_NATIVE_WINDOWS_D3D11_MULTITHREAD_PROTECTION_HPP
#define VRRECORDER_NATIVE_WINDOWS_D3D11_MULTITHREAD_PROTECTION_HPP

#include "vrrecorder_native.h"

namespace vrrecorder::native {

vrrec_status_t EnableWindowsD3d11MultithreadProtection(
    void *d3d11_immediate_context) noexcept;

}

#endif
