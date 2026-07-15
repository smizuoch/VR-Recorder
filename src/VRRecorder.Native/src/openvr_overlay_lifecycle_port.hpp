#ifndef VRRECORDER_NATIVE_OPENVR_OVERLAY_LIFECYCLE_PORT_HPP
#define VRRECORDER_NATIVE_OPENVR_OVERLAY_LIFECYCLE_PORT_HPP

#include <cstdint>
#include <string_view>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

class OpenVrOverlayLifecyclePort {
public:
    virtual ~OpenVrOverlayLifecyclePort() = default;

    virtual vrrec_status_t CreateOverlay(
        std::string_view key,
        std::string_view name,
        std::uint64_t &handle) noexcept = 0;
    virtual vrrec_status_t SetOverlayWidthInMeters(
        std::uint64_t handle,
        float width) noexcept = 0;
    virtual vrrec_status_t ShowOverlay(std::uint64_t handle) noexcept = 0;
    virtual vrrec_status_t HideOverlay(std::uint64_t handle) noexcept = 0;
    virtual vrrec_status_t DestroyOverlay(std::uint64_t handle) noexcept = 0;
};

}

#endif
