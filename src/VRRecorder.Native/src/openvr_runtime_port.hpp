#ifndef VRRECORDER_NATIVE_OPENVR_RUNTIME_PORT_HPP
#define VRRECORDER_NATIVE_OPENVR_RUNTIME_PORT_HPP

#include "openvr_haptic_port.hpp"
#include "openvr_input_port.hpp"
#include "openvr_overlay_event_port.hpp"
#include "openvr_overlay_lifecycle_port.hpp"
#include "openvr_overlay_pose_port.hpp"
#include "openvr_overlay_texture_port.hpp"

namespace vrrecorder::native {

class OpenVrRuntimePort
    : public OpenVrInputPort,
      public OpenVrHapticPort,
      public OpenVrOverlayLifecyclePort,
      public OpenVrOverlayTexturePort,
      public OpenVrOverlayEventPort,
      public OpenVrOverlayPosePort {
public:
    ~OpenVrRuntimePort() override = default;

    virtual vrrec_status_t Initialize() noexcept override = 0;
    virtual vrrec_status_t AddApplicationManifest(
        std::string_view absolute_path,
        bool temporary) noexcept override = 0;
    virtual vrrec_status_t SetActionManifestPath(
        std::string_view absolute_path) noexcept override = 0;
    virtual void Shutdown() noexcept override = 0;
};

}

#endif
