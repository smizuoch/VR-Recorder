#ifndef VRRECORDER_NATIVE_OPENVR_OVERLAY_POSE_PORT_HPP
#define VRRECORDER_NATIVE_OPENVR_OVERLAY_POSE_PORT_HPP

#include <array>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <string>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

enum class OpenVrOverlayPlacementMode : std::uint32_t {
    WristDock = 1,
    WorldPin = 2,
};

enum class OpenVrHand : std::uint32_t {
    None = 0,
    Left = 1,
    Right = 2,
};

enum class OpenVrTrackingOrigin : std::uint32_t {
    None = 0,
    Standing = 1,
};

struct OpenVrMatrix34 final {
    std::array<float, 12> values {};

    bool operator==(const OpenVrMatrix34 &) const = default;
};

struct OpenVrOverlayPose final {
    OpenVrOverlayPlacementMode placement_mode {};
    OpenVrHand hand {};
    OpenVrTrackingOrigin tracking_origin {};
    OpenVrMatrix34 transform {};

    bool operator==(const OpenVrOverlayPose &) const = default;
};

inline constexpr auto MaximumOpenVrDeviceProfileTextBytes =
    std::size_t {2048};

struct OpenVrDeviceProfile final {
    std::string tracking_system_name;
    std::string hmd_model_number;
    std::string controller_input_profile_path;

    bool operator==(const OpenVrDeviceProfile &) const = default;
};

inline bool IsValidOpenVrDeviceProfile(
    const OpenVrDeviceProfile &profile) noexcept
{
    const auto valid_text = [](const std::string &value) {
        return !value.empty() &&
            value.size() <= MaximumOpenVrDeviceProfileTextBytes &&
            value.find('\0') == std::string::npos;
    };
    return valid_text(profile.tracking_system_name) &&
        valid_text(profile.hmd_model_number) &&
        valid_text(profile.controller_input_profile_path);
}

inline bool IsValidOpenVrOverlayPose(
    const OpenVrOverlayPose &pose) noexcept
{
    constexpr auto maximum_absolute_position_meters = 100.0F;
    constexpr auto rotation_tolerance = 0.001F;
    const auto valid_mode =
        pose.placement_mode == OpenVrOverlayPlacementMode::WristDock ||
        pose.placement_mode == OpenVrOverlayPlacementMode::WorldPin;
    const auto valid_binding =
        pose.placement_mode == OpenVrOverlayPlacementMode::WristDock
            ? (pose.hand == OpenVrHand::Left ||
               pose.hand == OpenVrHand::Right) &&
                  pose.tracking_origin == OpenVrTrackingOrigin::None
            : pose.hand == OpenVrHand::None &&
                  pose.tracking_origin == OpenVrTrackingOrigin::Standing;
    if (!valid_mode || !valid_binding) {
        return false;
    }
    for (const auto value : pose.transform.values) {
        if (!std::isfinite(value)) {
            return false;
        }
    }
    if (std::abs(pose.transform.values[3]) >
            maximum_absolute_position_meters ||
        std::abs(pose.transform.values[7]) >
            maximum_absolute_position_meters ||
        std::abs(pose.transform.values[11]) >
            maximum_absolute_position_meters) {
        return false;
    }

    const auto &m = pose.transform.values;
    const auto dot = [&m](std::size_t left, std::size_t right) {
        return m[left] * m[right] +
            m[left + 1] * m[right + 1] +
            m[left + 2] * m[right + 2];
    };
    if (std::abs(dot(0, 0) - 1.0F) > rotation_tolerance ||
        std::abs(dot(4, 4) - 1.0F) > rotation_tolerance ||
        std::abs(dot(8, 8) - 1.0F) > rotation_tolerance ||
        std::abs(dot(0, 4)) > rotation_tolerance ||
        std::abs(dot(0, 8)) > rotation_tolerance ||
        std::abs(dot(4, 8)) > rotation_tolerance) {
        return false;
    }
    const auto determinant =
        m[0] * (m[5] * m[10] - m[6] * m[9]) -
        m[1] * (m[4] * m[10] - m[6] * m[8]) +
        m[2] * (m[4] * m[9] - m[5] * m[8]);
    return std::abs(determinant - 1.0F) <= rotation_tolerance;
}

class OpenVrOverlayPosePort {
public:
    virtual ~OpenVrOverlayPosePort() = default;

    virtual vrrec_status_t SetOverlayPose(
        std::uint64_t handle,
        const OpenVrOverlayPose &pose) noexcept = 0;

    virtual vrrec_status_t GetOverlayPose(
        std::uint64_t handle,
        OpenVrOverlayPose &pose) noexcept = 0;

    virtual vrrec_status_t GetDeviceProfile(
        OpenVrHand hand,
        OpenVrDeviceProfile &profile) noexcept = 0;
};

}

#endif
