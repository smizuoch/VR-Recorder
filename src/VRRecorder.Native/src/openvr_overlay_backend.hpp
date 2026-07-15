#ifndef VRRECORDER_NATIVE_OPENVR_OVERLAY_BACKEND_HPP
#define VRRECORDER_NATIVE_OPENVR_OVERLAY_BACKEND_HPP

#include <memory>
#include <string_view>

#include "openvr_overlay_lifecycle.hpp"

namespace vrrecorder::native {

std::unique_ptr<OpenVrOverlayLifecycle> CreateSteamVrOverlayLifecycle(
    std::string_view application_manifest_path,
    const OpenVrOverlayLifecycleConfig &config,
    vrrec_status_t &status) noexcept;

}

#endif
