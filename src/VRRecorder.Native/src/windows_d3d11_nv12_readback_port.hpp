#ifndef VRRECORDER_NATIVE_WINDOWS_D3D11_NV12_READBACK_PORT_HPP
#define VRRECORDER_NATIVE_WINDOWS_D3D11_NV12_READBACK_PORT_HPP

#include <cstdint>
#include <memory>

#include "d3d11_nv12_frame_mapper.hpp"

namespace vrrecorder::native {

std::unique_ptr<D3d11Nv12ReadbackPort>
CreateWindowsD3d11Nv12ReadbackPort(
    std::uint64_t adapter_luid,
    vrrec_status_t &status) noexcept;

}

#endif
