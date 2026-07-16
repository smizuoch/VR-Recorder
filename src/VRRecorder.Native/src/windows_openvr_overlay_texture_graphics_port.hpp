#ifndef VRRECORDER_NATIVE_WINDOWS_OPENVR_OVERLAY_TEXTURE_GRAPHICS_PORT_HPP
#define VRRECORDER_NATIVE_WINDOWS_OPENVR_OVERLAY_TEXTURE_GRAPHICS_PORT_HPP

#include <cstdint>
#include <memory>

#include "openvr.h"
#include "openvr_overlay_texture_graphics_port.hpp"

namespace vrrecorder::native {

std::unique_ptr<OpenVrOverlayTextureGraphicsPort>
CreateWindowsOpenVrOverlayTextureGraphicsPort(
    vr::IVROverlay *overlay,
    std::int32_t adapter_index,
    vrrec_status_t &status) noexcept;

}

#endif
