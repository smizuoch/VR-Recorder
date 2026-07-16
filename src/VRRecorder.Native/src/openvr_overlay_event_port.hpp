#ifndef VRRECORDER_NATIVE_OPENVR_OVERLAY_EVENT_PORT_HPP
#define VRRECORDER_NATIVE_OPENVR_OVERLAY_EVENT_PORT_HPP

#include <cstdint>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

enum class OpenVrOverlayPointerEventKind : std::uint32_t {
    Move = 1,
    ButtonDown = 2,
    ButtonUp = 3,
};

struct OpenVrOverlayPointerEvent final {
    OpenVrOverlayPointerEventKind kind {};
    std::uint32_t pixel_x = 0;
    std::uint32_t pixel_y = 0;
    std::uint32_t button = 0;
    std::uint32_t cursor_index = 0;

    bool operator==(const OpenVrOverlayPointerEvent &) const = default;
};

class OpenVrOverlayEventPort {
public:
    virtual ~OpenVrOverlayEventPort() = default;

    virtual vrrec_status_t ConfigureOverlayPointerInput(
        std::uint64_t handle,
        std::uint32_t pixel_width,
        std::uint32_t pixel_height) noexcept = 0;
    virtual vrrec_status_t PollNextOverlayPointerEvent(
        std::uint64_t handle,
        OpenVrOverlayPointerEvent &event,
        bool &has_event) noexcept = 0;
};

}

#endif
