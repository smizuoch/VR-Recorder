#ifndef VRRECORDER_NATIVE_WINDOWS_D3D11_KEYED_MUTEX_SURFACE_HPP
#define VRRECORDER_NATIVE_WINDOWS_D3D11_KEYED_MUTEX_SURFACE_HPP

#include <cstdint>
#include <memory>

#include "keyed_mutex_video_surface.hpp"

namespace vrrecorder::native {

std::shared_ptr<KeyedMutexVideoSurface>
CreateWindowsD3d11KeyedMutexVideoSurface(
    void *d3d11_texture,
    VideoSurfaceDescriptor descriptor,
    std::uint64_t acquire_key,
    std::uint64_t release_key,
    vrrec_status_t &status) noexcept;

}

#endif
