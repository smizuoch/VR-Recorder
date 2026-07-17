#include "openvr_overlay_pose_conversion.hpp"

#include <cmath>
#include <cstdlib>
#include <iostream>
#include <limits>

using vrrecorder::native::ConvertOpenVrOverlayPose;
using vrrecorder::native::OpenVrHand;
using vrrecorder::native::OpenVrMatrix34;
using vrrecorder::native::OpenVrOverlayPlacementMode;
using vrrecorder::native::OpenVrOverlayPose;
using vrrecorder::native::OpenVrTrackingOrigin;

namespace {

void Check(bool condition, const char *expression, int line)
{
    if (!condition) {
        std::cerr << "CHECK failed at line " << line << ": "
                  << expression << '\n';
        std::exit(1);
    }
}

#define CHECK(expression) Check((expression), #expression, __LINE__)

bool Near(float left, float right)
{
    return std::abs(left - right) <= 0.00001F;
}

OpenVrMatrix34 Translation(float x, float y, float z)
{
    return OpenVrMatrix34 {{
        1, 0, 0, x,
        0, 1, 0, y,
        0, 0, 1, z,
    }};
}

void ConvertsDockRelativePoseToStandingAbsoluteAndBack()
{
    const auto controller = Translation(1, 2, 3);
    const auto dock = OpenVrOverlayPose {
        OpenVrOverlayPlacementMode::WristDock,
        OpenVrHand::Right,
        OpenVrTrackingOrigin::None,
        Translation(0.1F, 0.2F, -0.3F),
    };
    auto pin = OpenVrOverlayPose {};

    CHECK(ConvertOpenVrOverlayPose(
              dock,
              controller,
              OpenVrOverlayPlacementMode::WorldPin,
              OpenVrHand::Right,
              pin) == VRREC_STATUS_OK);
    CHECK(pin.placement_mode == OpenVrOverlayPlacementMode::WorldPin);
    CHECK(pin.hand == OpenVrHand::None);
    CHECK(pin.tracking_origin == OpenVrTrackingOrigin::Standing);
    CHECK(Near(pin.transform.values[3], 1.1F));
    CHECK(Near(pin.transform.values[7], 2.2F));
    CHECK(Near(pin.transform.values[11], 2.7F));

    auto restored = OpenVrOverlayPose {};
    CHECK(ConvertOpenVrOverlayPose(
              pin,
              controller,
              OpenVrOverlayPlacementMode::WristDock,
              OpenVrHand::Right,
              restored) == VRREC_STATUS_OK);
    for (auto index = std::size_t {0};
         index < dock.transform.values.size();
         ++index) {
        CHECK(Near(
            restored.transform.values[index],
            dock.transform.values[index]));
    }
}

void ControllerRotationRotatesDockTranslation()
{
    const auto controller = OpenVrMatrix34 {{
        0, 0, 1, 1,
        0, 1, 0, 2,
        -1, 0, 0, 3,
    }};
    const auto dock = OpenVrOverlayPose {
        OpenVrOverlayPlacementMode::WristDock,
        OpenVrHand::Left,
        OpenVrTrackingOrigin::None,
        Translation(0, 0, -0.5F),
    };
    auto pin = OpenVrOverlayPose {};

    CHECK(ConvertOpenVrOverlayPose(
              dock,
              controller,
              OpenVrOverlayPlacementMode::WorldPin,
              OpenVrHand::Left,
              pin) == VRREC_STATUS_OK);
    CHECK(Near(pin.transform.values[3], 0.5F));
    CHECK(Near(pin.transform.values[7], 2));
    CHECK(Near(pin.transform.values[11], 3));
}

void RejectsMismatchedDockHand()
{
    const auto dock = OpenVrOverlayPose {
        OpenVrOverlayPlacementMode::WristDock,
        OpenVrHand::Left,
        OpenVrTrackingOrigin::None,
        Translation(0, 0, 0),
    };
    auto converted = OpenVrOverlayPose {};

    CHECK(ConvertOpenVrOverlayPose(
              dock,
              Translation(0, 0, 0),
              OpenVrOverlayPlacementMode::WorldPin,
              OpenVrHand::Right,
              converted) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(converted == OpenVrOverlayPose {});
}

void RejectsEveryPoseConversionBoundary()
{
    const auto dock = OpenVrOverlayPose {
        OpenVrOverlayPlacementMode::WristDock,
        OpenVrHand::Left,
        OpenVrTrackingOrigin::None,
        Translation(1, 0, 0),
    };
    auto converted = OpenVrOverlayPose {};

    CHECK(ConvertOpenVrOverlayPose(
              {},
              Translation(0, 0, 0),
              OpenVrOverlayPlacementMode::WorldPin,
              OpenVrHand::Left,
              converted) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(ConvertOpenVrOverlayPose(
              dock,
              Translation(0, 0, 0),
              static_cast<OpenVrOverlayPlacementMode>(99),
              OpenVrHand::Left,
              converted) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(ConvertOpenVrOverlayPose(
              dock,
              Translation(0, 0, 0),
              OpenVrOverlayPlacementMode::WorldPin,
              OpenVrHand::None,
              converted) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(ConvertOpenVrOverlayPose(
              dock,
              {},
              OpenVrOverlayPlacementMode::WristDock,
              OpenVrHand::Left,
              converted) == VRREC_STATUS_OK);
    CHECK(converted == dock);

    auto invalid_controller = Translation(0, 0, 0);
    invalid_controller.values[0] =
        std::numeric_limits<float>::quiet_NaN();
    CHECK(ConvertOpenVrOverlayPose(
              dock,
              invalid_controller,
              OpenVrOverlayPlacementMode::WorldPin,
              OpenVrHand::Left,
              converted) == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(converted == OpenVrOverlayPose {});

    CHECK(ConvertOpenVrOverlayPose(
              dock,
              Translation(100, 0, 0),
              OpenVrOverlayPlacementMode::WorldPin,
              OpenVrHand::Left,
              converted) == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(converted == OpenVrOverlayPose {});
}

}

int main()
{
    ConvertsDockRelativePoseToStandingAbsoluteAndBack();
    ControllerRotationRotatesDockTranslation();
    RejectsMismatchedDockHand();
    RejectsEveryPoseConversionBoundary();
    return 0;
}
