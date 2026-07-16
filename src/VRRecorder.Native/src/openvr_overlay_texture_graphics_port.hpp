#ifndef VRRECORDER_NATIVE_OPENVR_OVERLAY_TEXTURE_GRAPHICS_PORT_HPP
#define VRRECORDER_NATIVE_OPENVR_OVERLAY_TEXTURE_GRAPHICS_PORT_HPP

#include <cstdint>
#include <memory>

#include "openvr_overlay_texture_port.hpp"

namespace vrrecorder::native {

class OpenVrOverlayTextureGraphicsResource {
public:
    virtual ~OpenVrOverlayTextureGraphicsResource() = default;
};

class OpenVrOverlayTextureGraphicsPort {
public:
    virtual ~OpenVrOverlayTextureGraphicsPort() = default;

    virtual vrrec_status_t CreateBgraTexture(
        std::uint32_t width,
        std::uint32_t height,
        std::unique_ptr<OpenVrOverlayTextureGraphicsResource> &resource)
        noexcept = 0;
    virtual vrrec_status_t UploadBgraTexture(
        OpenVrOverlayTextureGraphicsResource &resource,
        const OpenVrBgraTextureFrame &frame) noexcept = 0;
    virtual vrrec_status_t SubmitOverlayTexture(
        std::uint64_t handle,
        OpenVrOverlayTextureGraphicsResource &resource) noexcept = 0;
    virtual vrrec_status_t ClearOverlayTexture(
        std::uint64_t handle) noexcept = 0;
};

}

#endif
