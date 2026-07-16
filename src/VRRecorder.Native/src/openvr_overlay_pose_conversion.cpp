#include "openvr_overlay_pose_conversion.hpp"

#include <cstddef>

namespace vrrecorder::native {
namespace {

OpenVrMatrix34 Compose(
    const OpenVrMatrix34 &parent,
    const OpenVrMatrix34 &child) noexcept
{
    auto result = OpenVrMatrix34 {};
    for (auto row = std::size_t {0}; row < 3; ++row) {
        for (auto column = std::size_t {0}; column < 3; ++column) {
            auto value = 0.0F;
            for (auto index = std::size_t {0}; index < 3; ++index) {
                value += parent.values[row * 4 + index] *
                    child.values[index * 4 + column];
            }
            result.values[row * 4 + column] = value;
        }
        auto translation = parent.values[row * 4 + 3];
        for (auto index = std::size_t {0}; index < 3; ++index) {
            translation += parent.values[row * 4 + index] *
                child.values[index * 4 + 3];
        }
        result.values[row * 4 + 3] = translation;
    }
    return result;
}

OpenVrMatrix34 InvertRigid(const OpenVrMatrix34 &matrix) noexcept
{
    auto inverse = OpenVrMatrix34 {};
    for (auto row = std::size_t {0}; row < 3; ++row) {
        for (auto column = std::size_t {0}; column < 3; ++column) {
            inverse.values[row * 4 + column] =
                matrix.values[column * 4 + row];
        }
        auto translation = 0.0F;
        for (auto index = std::size_t {0}; index < 3; ++index) {
            translation -= inverse.values[row * 4 + index] *
                matrix.values[index * 4 + 3];
        }
        inverse.values[row * 4 + 3] = translation;
    }
    return inverse;
}

bool IsValidControllerTransform(const OpenVrMatrix34 &matrix) noexcept
{
    return IsValidOpenVrOverlayPose(OpenVrOverlayPose {
        OpenVrOverlayPlacementMode::WorldPin,
        OpenVrHand::None,
        OpenVrTrackingOrigin::Standing,
        matrix,
    });
}

}

vrrec_status_t ConvertOpenVrOverlayPose(
    const OpenVrOverlayPose &current,
    const OpenVrMatrix34 &controller_absolute,
    OpenVrOverlayPlacementMode target_mode,
    OpenVrHand hand,
    OpenVrOverlayPose &converted) noexcept
{
    converted = {};
    if (!IsValidOpenVrOverlayPose(current) ||
        (target_mode != OpenVrOverlayPlacementMode::WristDock &&
         target_mode != OpenVrOverlayPlacementMode::WorldPin) ||
        (hand != OpenVrHand::Left && hand != OpenVrHand::Right) ||
        (current.placement_mode ==
             OpenVrOverlayPlacementMode::WristDock &&
         current.hand != hand)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }
    if (current.placement_mode == target_mode) {
        converted = current;
        return VRREC_STATUS_OK;
    }
    if (!IsValidControllerTransform(controller_absolute)) {
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    }

    if (target_mode == OpenVrOverlayPlacementMode::WorldPin) {
        converted = OpenVrOverlayPose {
            OpenVrOverlayPlacementMode::WorldPin,
            OpenVrHand::None,
            OpenVrTrackingOrigin::Standing,
            Compose(controller_absolute, current.transform),
        };
    } else {
        converted = OpenVrOverlayPose {
            OpenVrOverlayPlacementMode::WristDock,
            hand,
            OpenVrTrackingOrigin::None,
            Compose(
                InvertRigid(controller_absolute),
                current.transform),
        };
    }
    if (!IsValidOpenVrOverlayPose(converted)) {
        converted = {};
        return VRREC_STATUS_INTERNAL_ERROR;
    }
    return VRREC_STATUS_OK;
}

}
