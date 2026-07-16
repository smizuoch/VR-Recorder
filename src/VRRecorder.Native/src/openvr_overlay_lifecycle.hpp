#ifndef VRRECORDER_NATIVE_OPENVR_OVERLAY_LIFECYCLE_HPP
#define VRRECORDER_NATIVE_OPENVR_OVERLAY_LIFECYCLE_HPP

#include <memory>

#include "openvr_overlay_lifecycle_port.hpp"
#include "openvr_overlay_event_port.hpp"
#include "openvr_overlay_texture_port.hpp"

namespace vrrecorder::native {

struct OpenVrOverlayLifecycleConfig final {
    const char *overlay_key_utf8;
    const char *overlay_name_utf8;
    float width_in_meters;
};

class OpenVrOverlayLifecycle {
public:
    virtual ~OpenVrOverlayLifecycle() = default;

    virtual vrrec_status_t Show() noexcept = 0;
    virtual vrrec_status_t Hide() noexcept = 0;
    virtual vrrec_status_t UpdateBgraTexture(
        const OpenVrBgraTextureFrame &frame) noexcept = 0;
    virtual vrrec_status_t ClearTexture() noexcept = 0;
    virtual vrrec_status_t PollPointerEvent(
        OpenVrOverlayPointerEvent &event,
        bool &has_event) noexcept = 0;
    virtual vrrec_status_t Close() noexcept = 0;
};

std::unique_ptr<OpenVrOverlayLifecycle> CreateOpenVrOverlayLifecycle(
    const OpenVrOverlayLifecycleConfig &config,
    std::unique_ptr<OpenVrOverlayLifecyclePort> port,
    vrrec_status_t &status) noexcept;

std::unique_ptr<OpenVrOverlayLifecycle> CreateOpenVrOverlayLifecycle(
    const OpenVrOverlayLifecycleConfig &config,
    std::unique_ptr<OpenVrOverlayLifecyclePort> lifecycle_port,
    std::unique_ptr<OpenVrOverlayTexturePort> texture_port,
    std::unique_ptr<OpenVrOverlayEventPort> event_port,
    vrrec_status_t &status) noexcept;

std::unique_ptr<OpenVrOverlayLifecycle> CreateOpenVrOverlayLifecycle(
    const OpenVrOverlayLifecycleConfig &config,
    std::unique_ptr<OpenVrOverlayLifecyclePort> lifecycle_port,
    std::unique_ptr<OpenVrOverlayTexturePort> texture_port,
    vrrec_status_t &status) noexcept;

}

#endif
