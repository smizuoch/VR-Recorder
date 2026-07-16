#ifndef VRRECORDER_NATIVE_OPENVR_STEAMVR_HAPTIC_BACKEND_CORE_HPP
#define VRRECORDER_NATIVE_OPENVR_STEAMVR_HAPTIC_BACKEND_CORE_HPP

#include <memory>

#include "steamvr_haptic_backend.hpp"

namespace vrrecorder::native {

std::unique_ptr<SteamVrHapticBackend> CreateOpenVrSteamVrHapticBackend(
    const SteamVrHapticConfig &config,
    std::unique_ptr<OpenVrHapticPort> port,
    vrrec_status_t &status) noexcept;

}

#endif
