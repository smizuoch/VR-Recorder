#ifndef VRRECORDER_NATIVE_OPENVR_HAPTIC_PORT_HPP
#define VRRECORDER_NATIVE_OPENVR_HAPTIC_PORT_HPP

#include <cmath>
#include <cstdint>
#include <string_view>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

struct OpenVrHapticPulse final {
    float duration_seconds;
    float frequency_hertz;
    float amplitude;
};

inline bool IsValidOpenVrHapticPulse(
    const OpenVrHapticPulse &pulse) noexcept
{
    return std::isfinite(pulse.duration_seconds) &&
           pulse.duration_seconds > 0.0F &&
           std::isfinite(pulse.frequency_hertz) &&
           pulse.frequency_hertz >= 0.0F &&
           std::isfinite(pulse.amplitude) &&
           pulse.amplitude > 0.0F &&
           pulse.amplitude <= 1.0F;
}

class OpenVrHapticPort {
public:
    virtual ~OpenVrHapticPort() = default;

    virtual vrrec_status_t Initialize() noexcept = 0;
    virtual vrrec_status_t AddApplicationManifest(
        std::string_view absolute_path,
        bool temporary) noexcept = 0;
    virtual vrrec_status_t SetActionManifestPath(
        std::string_view absolute_path) noexcept = 0;
    virtual vrrec_status_t GetHapticActionHandle(
        std::string_view action_path,
        std::uint64_t &handle) noexcept = 0;
    virtual vrrec_status_t GetInputSourceHandle(
        std::string_view input_source_path,
        std::uint64_t &handle) noexcept = 0;
    virtual vrrec_status_t TriggerHapticVibrationAction(
        std::uint64_t action_handle,
        std::uint64_t source_handle,
        const OpenVrHapticPulse &pulse) noexcept = 0;
    virtual void Shutdown() noexcept = 0;
};

}

#endif
