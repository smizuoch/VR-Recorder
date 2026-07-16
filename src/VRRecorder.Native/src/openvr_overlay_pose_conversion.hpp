#ifndef VRRECORDER_NATIVE_OPENVR_OVERLAY_POSE_CONVERSION_HPP
#define VRRECORDER_NATIVE_OPENVR_OVERLAY_POSE_CONVERSION_HPP

#include "openvr_overlay_pose_port.hpp"

namespace vrrecorder::native {

vrrec_status_t ConvertOpenVrOverlayPose(
    const OpenVrOverlayPose &current,
    const OpenVrMatrix34 &controller_absolute,
    OpenVrOverlayPlacementMode target_mode,
    OpenVrHand hand,
    OpenVrOverlayPose &converted) noexcept;

}

#endif
