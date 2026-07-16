#ifndef VRRECORDER_NATIVE_OPENVR_OVERLAY_TEXTURE_PORT_HPP
#define VRRECORDER_NATIVE_OPENVR_OVERLAY_TEXTURE_PORT_HPP

#include <cstddef>
#include <cstdint>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

struct OpenVrBgraTextureFrame final {
    const std::uint8_t *pixel_bytes;
    std::size_t pixel_bytes_size;
    std::uint32_t width;
    std::uint32_t height;
    std::uint32_t stride_bytes;
};

class OpenVrOverlayTexturePort {
public:
    virtual ~OpenVrOverlayTexturePort() = default;

    virtual vrrec_status_t SetOverlayBgraTexture(
        std::uint64_t handle,
        const OpenVrBgraTextureFrame &frame) noexcept = 0;
    virtual vrrec_status_t ClearOverlayTexture(
        std::uint64_t handle) noexcept = 0;
};

}

#endif
