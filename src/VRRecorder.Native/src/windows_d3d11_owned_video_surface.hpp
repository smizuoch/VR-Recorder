#ifndef VRRECORDER_NATIVE_WINDOWS_D3D11_OWNED_VIDEO_SURFACE_HPP
#define VRRECORDER_NATIVE_WINDOWS_D3D11_OWNED_VIDEO_SURFACE_HPP

#include <memory>

#include "video_surface.hpp"

namespace vrrecorder::native {

std::shared_ptr<VideoSurface> CreateWindowsD3d11OwnedVideoSurface(
    void *d3d11_texture,
    VideoSurfaceDescriptor descriptor,
    vrrec_status_t &status) noexcept;

}

#endif
