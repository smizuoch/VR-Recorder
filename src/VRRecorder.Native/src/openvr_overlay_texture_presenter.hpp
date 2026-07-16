#ifndef VRRECORDER_NATIVE_OPENVR_OVERLAY_TEXTURE_PRESENTER_HPP
#define VRRECORDER_NATIVE_OPENVR_OVERLAY_TEXTURE_PRESENTER_HPP

#include <memory>

#include "openvr_overlay_texture_graphics_port.hpp"

namespace vrrecorder::native {

std::unique_ptr<OpenVrOverlayTexturePort>
CreateOpenVrOverlayTexturePresenter(
    std::unique_ptr<OpenVrOverlayTextureGraphicsPort> graphics_port,
    vrrec_status_t &status) noexcept;

}

#endif
