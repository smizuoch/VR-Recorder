#ifndef VRRECORDER_NATIVE_STEAMVR_HAPTIC_BACKEND_HPP
#define VRRECORDER_NATIVE_STEAMVR_HAPTIC_BACKEND_HPP

#include <memory>
#include <string_view>

#include "openvr_haptic_port.hpp"

namespace vrrecorder::native {

struct SteamVrHapticConfig final {
    std::string_view action_manifest_path;
    std::string_view haptic_action_path;
    std::string_view input_source_path;
};

class SteamVrHapticBackend {
public:
    virtual ~SteamVrHapticBackend() = default;

    virtual vrrec_status_t Trigger(
        const OpenVrHapticPulse &pulse) noexcept = 0;
};

std::unique_ptr<SteamVrHapticBackend> CreateSteamVrHapticBackend(
    const SteamVrHapticConfig &config,
    vrrec_status_t &status) noexcept;

}

#endif
