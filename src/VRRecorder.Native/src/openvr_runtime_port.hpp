#ifndef VRRECORDER_NATIVE_OPENVR_RUNTIME_PORT_HPP
#define VRRECORDER_NATIVE_OPENVR_RUNTIME_PORT_HPP

#include "openvr_input_port.hpp"
#include "openvr_overlay_lifecycle_port.hpp"
#include "openvr_overlay_texture_port.hpp"

namespace vrrecorder::native {

class OpenVrRuntimePort
    : public OpenVrInputPort,
      public OpenVrOverlayLifecyclePort,
      public OpenVrOverlayTexturePort {
public:
    ~OpenVrRuntimePort() override = default;
};

}

#endif
