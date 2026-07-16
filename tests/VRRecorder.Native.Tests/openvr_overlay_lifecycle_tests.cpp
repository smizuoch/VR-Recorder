#include "openvr_overlay_lifecycle.hpp"

#include <array>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <limits>
#include <memory>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace {

#define CHECK(condition)                                                        \
    do {                                                                        \
        if (!(condition)) {                                                     \
            std::cerr << "check failed at " << __FILE__ << ':' << __LINE__      \
                      << ": " #condition << '\n';                              \
            std::abort();                                                       \
        }                                                                       \
    } while (false)

using namespace vrrecorder::native;

struct FakeState final {
    std::vector<std::string> calls;
    std::size_t destroy_calls = 0;
};

class FakePort final : public OpenVrOverlayLifecyclePort {
public:
    explicit FakePort(std::shared_ptr<FakeState> state)
        : state_(std::move(state))
    {
    }

    vrrec_status_t CreateOverlay(
        std::string_view key,
        std::string_view name,
        std::uint64_t &handle) noexcept override
    {
        state_->calls.emplace_back(
            "create:" + std::string(key) + ':' + std::string(name));
        handle = create_handle;
        return create_status;
    }

    vrrec_status_t SetOverlayWidthInMeters(
        std::uint64_t handle,
        float width) noexcept override
    {
        state_->calls.emplace_back(
            "width:" + std::to_string(handle) + ':' +
            std::to_string(width));
        return width_status;
    }

    vrrec_status_t ShowOverlay(std::uint64_t handle) noexcept override
    {
        state_->calls.emplace_back("show:" + std::to_string(handle));
        return show_status;
    }

    vrrec_status_t HideOverlay(std::uint64_t handle) noexcept override
    {
        state_->calls.emplace_back("hide:" + std::to_string(handle));
        return hide_status;
    }

    vrrec_status_t DestroyOverlay(std::uint64_t handle) noexcept override
    {
        state_->calls.emplace_back("destroy:" + std::to_string(handle));
        ++state_->destroy_calls;
        return destroy_status;
    }

    std::uint64_t create_handle = 73;
    vrrec_status_t create_status = VRREC_STATUS_OK;
    vrrec_status_t width_status = VRREC_STATUS_OK;
    vrrec_status_t show_status = VRREC_STATUS_OK;
    vrrec_status_t hide_status = VRREC_STATUS_OK;
    vrrec_status_t destroy_status = VRREC_STATUS_OK;

private:
    std::shared_ptr<FakeState> state_;
};

class FakeTexturePort final : public OpenVrOverlayTexturePort {
public:
    explicit FakeTexturePort(std::shared_ptr<FakeState> state)
        : state_(std::move(state))
    {
    }

    vrrec_status_t SetOverlayBgraTexture(
        std::uint64_t handle,
        const OpenVrBgraTextureFrame &frame) noexcept override
    {
        state_->calls.emplace_back(
            "texture:" + std::to_string(handle) + ':' +
            std::to_string(frame.width) + 'x' +
            std::to_string(frame.height) + ':' +
            std::to_string(frame.stride_bytes) + ':' +
            std::to_string(frame.pixel_bytes_size));
        return set_status;
    }

    vrrec_status_t ClearOverlayTexture(
        std::uint64_t handle) noexcept override
    {
        state_->calls.emplace_back("clear:" + std::to_string(handle));
        ++clear_calls;
        return clear_status;
    }

    vrrec_status_t set_status = VRREC_STATUS_OK;
    vrrec_status_t clear_status = VRREC_STATUS_OK;
    std::size_t clear_calls = 0;

private:
    std::shared_ptr<FakeState> state_;
};

class FakeEventPort final : public OpenVrOverlayEventPort {
public:
    explicit FakeEventPort(std::shared_ptr<FakeState> state)
        : state_(std::move(state))
    {
    }

    vrrec_status_t ConfigureOverlayPointerInput(
        std::uint64_t handle,
        std::uint32_t pixel_width,
        std::uint32_t pixel_height) noexcept override
    {
        state_->calls.emplace_back(
            "input:" + std::to_string(handle) + ':' +
            std::to_string(pixel_width) + 'x' +
            std::to_string(pixel_height));
        return configure_status;
    }

    vrrec_status_t PollNextOverlayPointerEvent(
        std::uint64_t handle,
        OpenVrOverlayPointerEvent &event,
        bool &has_event) noexcept override
    {
        state_->calls.emplace_back("poll:" + std::to_string(handle));
        event = next_event;
        has_event = publish_event;
        return poll_status;
    }

    vrrec_status_t configure_status = VRREC_STATUS_OK;
    vrrec_status_t poll_status = VRREC_STATUS_OK;
    OpenVrOverlayPointerEvent next_event {};
    bool publish_event = false;

private:
    std::shared_ptr<FakeState> state_;
};

class FakePosePort final : public OpenVrOverlayPosePort {
public:
    explicit FakePosePort(std::shared_ptr<FakeState> state)
        : state_(std::move(state))
    {
    }

    vrrec_status_t SetOverlayPose(
        std::uint64_t handle,
        const OpenVrOverlayPose &pose) noexcept override
    {
        state_->calls.emplace_back("pose-set:" + std::to_string(handle));
        current_pose = pose;
        return set_status;
    }

    vrrec_status_t GetOverlayPose(
        std::uint64_t handle,
        OpenVrOverlayPose &pose) noexcept override
    {
        state_->calls.emplace_back("pose-get:" + std::to_string(handle));
        pose = current_pose;
        return get_status;
    }

    vrrec_status_t GetDeviceProfile(
        OpenVrHand hand,
        OpenVrDeviceProfile &profile) noexcept override
    {
        state_->calls.emplace_back(
            "profile-get:" + std::to_string(static_cast<std::uint32_t>(hand)));
        profile = current_profile;
        return profile_status;
    }

    OpenVrOverlayPose current_pose {};
    OpenVrDeviceProfile current_profile {
        "lighthouse",
        "Valve Index",
        "{indexcontroller}/input/index_controller_profile.json",
    };
    vrrec_status_t set_status = VRREC_STATUS_OK;
    vrrec_status_t get_status = VRREC_STATUS_OK;
    vrrec_status_t profile_status = VRREC_STATUS_OK;

private:
    std::shared_ptr<FakeState> state_;
};

OpenVrOverlayLifecycleConfig Config()
{
    return OpenVrOverlayLifecycleConfig {
        "com.vrrecorder.desktop.wrist",
        "VR Recorder Wrist",
        0.22F,
    };
}

OpenVrOverlayPose WristPose()
{
    return OpenVrOverlayPose {
        OpenVrOverlayPlacementMode::WristDock,
        OpenVrHand::Left,
        OpenVrTrackingOrigin::None,
        OpenVrMatrix34 {{
            1, 0, 0, 0.03F,
            0, 1, 0, 0.05F,
            0, 0, 1, -0.08F,
        }},
    };
}

std::unique_ptr<OpenVrOverlayLifecycle> Create(
    std::unique_ptr<FakePort> port,
    vrrec_status_t &status)
{
    return CreateOpenVrOverlayLifecycle(
        Config(),
        std::move(port),
        status);
}

void CreatesHiddenOverlayAndTransitionsIdempotently()
{
    auto state = std::make_shared<FakeState>();
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto overlay = Create(std::make_unique<FakePort>(state), status);

    CHECK(status == VRREC_STATUS_OK);
    CHECK(overlay != nullptr);
    CHECK((state->calls == std::vector<std::string> {
        "create:com.vrrecorder.desktop.wrist:VR Recorder Wrist",
        "width:73:0.220000",
    }));

    CHECK(overlay->Show() == VRREC_STATUS_OK);
    CHECK(overlay->Show() == VRREC_STATUS_OK);
    CHECK(overlay->Hide() == VRREC_STATUS_OK);
    CHECK(overlay->Hide() == VRREC_STATUS_OK);
    CHECK(overlay->Close() == VRREC_STATUS_OK);
    CHECK(overlay->Close() == VRREC_STATUS_OK);
    CHECK((state->calls == std::vector<std::string> {
        "create:com.vrrecorder.desktop.wrist:VR Recorder Wrist",
        "width:73:0.220000",
        "show:73",
        "hide:73",
        "destroy:73",
    }));
    CHECK(state->destroy_calls == 1);
    CHECK(overlay->Show() == VRREC_STATUS_INVALID_STATE);
}

void RejectsInvalidConfigurationBeforeCallingThePort()
{
    for (const auto invalid_case : {
             std::size_t {0},
             std::size_t {1},
             std::size_t {2},
             std::size_t {3},
             std::size_t {4},
             std::size_t {5},
         }) {
        auto config = Config();
        if (invalid_case == 0) {
            config.overlay_key_utf8 = nullptr;
        } else if (invalid_case == 1) {
            config.overlay_name_utf8 = "";
        } else if (invalid_case == 2) {
            config.width_in_meters = 0.17F;
        } else if (invalid_case == 3) {
            config.width_in_meters = 0.33F;
        } else if (invalid_case == 4) {
            config.width_in_meters =
                std::numeric_limits<float>::quiet_NaN();
        } else {
            config.width_in_meters =
                std::numeric_limits<float>::infinity();
        }
        auto state = std::make_shared<FakeState>();
        auto status = VRREC_STATUS_OK;
        auto overlay = CreateOpenVrOverlayLifecycle(
            config,
            std::make_unique<FakePort>(state),
            status);
        CHECK(overlay == nullptr);
        CHECK(status == VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(state->calls.empty());
    }

    auto status = VRREC_STATUS_OK;
    CHECK(CreateOpenVrOverlayLifecycle(Config(), nullptr, status) == nullptr);
    CHECK(status == VRREC_STATUS_INVALID_ARGUMENT);
}

void RollsBackExactlyOnceWhenCreationCannotBeCompleted()
{
    {
        auto state = std::make_shared<FakeState>();
        auto port = std::make_unique<FakePort>(state);
        port->create_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
        auto status = VRREC_STATUS_OK;
        auto overlay = Create(std::move(port), status);
        CHECK(overlay == nullptr);
        CHECK(status == VRREC_STATUS_BACKEND_UNAVAILABLE);
        CHECK(state->destroy_calls == 0);
    }
    {
        auto state = std::make_shared<FakeState>();
        auto port = std::make_unique<FakePort>(state);
        port->create_handle = 0;
        auto status = VRREC_STATUS_OK;
        auto overlay = Create(std::move(port), status);
        CHECK(overlay == nullptr);
        CHECK(status == VRREC_STATUS_INTERNAL_ERROR);
        CHECK(state->destroy_calls == 0);
    }
    {
        auto state = std::make_shared<FakeState>();
        auto port = std::make_unique<FakePort>(state);
        port->width_status = VRREC_STATUS_INTERNAL_ERROR;
        auto status = VRREC_STATUS_OK;
        auto overlay = Create(std::move(port), status);
        CHECK(overlay == nullptr);
        CHECK(status == VRREC_STATUS_INTERNAL_ERROR);
        CHECK((state->calls == std::vector<std::string> {
            "create:com.vrrecorder.desktop.wrist:VR Recorder Wrist",
            "width:73:0.220000",
            "destroy:73",
        }));
        CHECK(state->destroy_calls == 1);
    }
}

void CommitsVisibilityOnlyAfterThePortSucceeds()
{
    auto state = std::make_shared<FakeState>();
    auto port = std::make_unique<FakePort>(state);
    auto *raw = port.get();
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto overlay = Create(std::move(port), status);

    raw->show_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    CHECK(overlay->Show() == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(overlay->Show() == VRREC_STATUS_BACKEND_UNAVAILABLE);
    raw->show_status = VRREC_STATUS_OK;
    CHECK(overlay->Show() == VRREC_STATUS_OK);
    CHECK(overlay->Show() == VRREC_STATUS_OK);

    raw->hide_status = VRREC_STATUS_INTERNAL_ERROR;
    CHECK(overlay->Hide() == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(overlay->Hide() == VRREC_STATUS_INTERNAL_ERROR);
    raw->hide_status = VRREC_STATUS_OK;
    CHECK(overlay->Hide() == VRREC_STATUS_OK);
    CHECK(overlay->Hide() == VRREC_STATUS_OK);

    CHECK(state->calls.size() == 8);
    CHECK(overlay->Close() == VRREC_STATUS_OK);
}

void CloseDestroysOnceAndCachesTheFirstCleanupResult()
{
    auto state = std::make_shared<FakeState>();
    auto port = std::make_unique<FakePort>(state);
    auto *raw = port.get();
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto overlay = Create(std::move(port), status);
    CHECK(overlay->Show() == VRREC_STATUS_OK);
    raw->hide_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    raw->destroy_status = VRREC_STATUS_INTERNAL_ERROR;

    CHECK(overlay->Close() == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(overlay->Close() == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(state->calls[state->calls.size() - 2] == "hide:73");
    CHECK(state->calls.back() == "destroy:73");
    CHECK(state->destroy_calls == 1);
    overlay.reset();
    CHECK(state->destroy_calls == 1);
}

void DestructorClosesAnOpenOverlay()
{
    auto state = std::make_shared<FakeState>();
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    {
        auto overlay = Create(std::make_unique<FakePort>(state), status);
        CHECK(overlay->Show() == VRREC_STATUS_OK);
    }
    CHECK(state->calls[state->calls.size() - 2] == "hide:73");
    CHECK(state->calls.back() == "destroy:73");
    CHECK(state->destroy_calls == 1);
}

void OwnsBgraTextureAndClearsItBeforeOverlayDestruction()
{
    auto state = std::make_shared<FakeState>();
    auto texture_port = std::make_unique<FakeTexturePort>(state);
    auto *raw_texture = texture_port.get();
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto overlay = CreateOpenVrOverlayLifecycle(
        Config(),
        std::make_unique<FakePort>(state),
        std::move(texture_port),
        status);
    std::vector<std::uint8_t> pixels(1024U * 512U * 4U, 0x7f);
    const auto frame = OpenVrBgraTextureFrame {
        pixels.data(),
        pixels.size(),
        1024,
        512,
        4096,
    };

    CHECK(status == VRREC_STATUS_OK);
    CHECK(overlay->UpdateBgraTexture(frame) == VRREC_STATUS_OK);
    CHECK(overlay->UpdateBgraTexture(frame) == VRREC_STATUS_OK);
    CHECK(overlay->ClearTexture() == VRREC_STATUS_OK);
    CHECK(overlay->ClearTexture() == VRREC_STATUS_OK);
    CHECK(raw_texture->clear_calls == 1);
    CHECK(overlay->UpdateBgraTexture(frame) == VRREC_STATUS_OK);
    CHECK(overlay->Show() == VRREC_STATUS_OK);
    CHECK(overlay->Close() == VRREC_STATUS_OK);
    CHECK(overlay->Close() == VRREC_STATUS_OK);
    CHECK(raw_texture->clear_calls == 2);
    CHECK((state->calls == std::vector<std::string> {
        "create:com.vrrecorder.desktop.wrist:VR Recorder Wrist",
        "width:73:0.220000",
        "texture:73:1024x512:4096:2097152",
        "texture:73:1024x512:4096:2097152",
        "clear:73",
        "texture:73:1024x512:4096:2097152",
        "show:73",
        "clear:73",
        "hide:73",
        "destroy:73",
    }));
}

void RejectsInvalidBgraFramesBeforeCallingTexturePort()
{
    auto state = std::make_shared<FakeState>();
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto overlay = CreateOpenVrOverlayLifecycle(
        Config(),
        std::make_unique<FakePort>(state),
        std::make_unique<FakeTexturePort>(state),
        status);
    std::vector<std::uint8_t> pixels(1024U * 512U * 4U);
    auto frame = OpenVrBgraTextureFrame {
        pixels.data(),
        pixels.size(),
        1024,
        512,
        4096,
    };
    const auto initial_call_count = state->calls.size();
    for (const auto invalid_case : {
             std::size_t {0},
             std::size_t {1},
             std::size_t {2},
             std::size_t {3},
             std::size_t {4},
         }) {
        auto invalid = frame;
        if (invalid_case == 0) {
            invalid.pixel_bytes = nullptr;
        } else if (invalid_case == 1) {
            invalid.pixel_bytes_size -= 1;
        } else if (invalid_case == 2) {
            invalid.width -= 1;
        } else if (invalid_case == 3) {
            invalid.height -= 1;
        } else {
            invalid.stride_bytes -= 1;
        }
        CHECK(overlay->UpdateBgraTexture(invalid) ==
              VRREC_STATUS_INVALID_ARGUMENT);
    }
    CHECK(state->calls.size() == initial_call_count);
    CHECK(overlay->ClearTexture() == VRREC_STATUS_OK);
}

void ClearFailureDoesNotSkipHideOrDestroy()
{
    auto state = std::make_shared<FakeState>();
    auto texture_port = std::make_unique<FakeTexturePort>(state);
    auto *raw_texture = texture_port.get();
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto overlay = CreateOpenVrOverlayLifecycle(
        Config(),
        std::make_unique<FakePort>(state),
        std::move(texture_port),
        status);
    std::vector<std::uint8_t> pixels(1024U * 512U * 4U);
    const auto frame = OpenVrBgraTextureFrame {
        pixels.data(),
        pixels.size(),
        1024,
        512,
        4096,
    };
    CHECK(overlay->UpdateBgraTexture(frame) == VRREC_STATUS_OK);
    CHECK(overlay->Show() == VRREC_STATUS_OK);
    raw_texture->clear_status = VRREC_STATUS_BACKEND_UNAVAILABLE;

    CHECK(overlay->Close() == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(overlay->Close() == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(state->calls[state->calls.size() - 3] == "clear:73");
    CHECK(state->calls[state->calls.size() - 2] == "hide:73");
    CHECK(state->calls.back() == "destroy:73");
    CHECK(state->destroy_calls == 1);
    CHECK(raw_texture->clear_calls == 1);
}

void ConfiguresAndPollsTopLeftPixelPointerEvents()
{
    auto state = std::make_shared<FakeState>();
    auto event_port = std::make_unique<FakeEventPort>(state);
    auto *raw_event = event_port.get();
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto overlay = CreateOpenVrOverlayLifecycle(
        Config(),
        std::make_unique<FakePort>(state),
        std::make_unique<FakeTexturePort>(state),
        std::move(event_port),
        status);

    CHECK(status == VRREC_STATUS_OK);
    CHECK(state->calls.back() == "input:73:1024x512");
    raw_event->next_event = OpenVrOverlayPointerEvent {
        OpenVrOverlayPointerEventKind::ButtonDown,
        511,
        255,
        1,
        9,
    };
    raw_event->publish_event = true;
    auto event = OpenVrOverlayPointerEvent {};
    auto has_event = false;
    CHECK(overlay->PollPointerEvent(event, has_event) == VRREC_STATUS_OK);
    CHECK(has_event);
    CHECK(event.kind == OpenVrOverlayPointerEventKind::ButtonDown);
    CHECK(event.pixel_x == 511);
    CHECK(event.pixel_y == 255);
    CHECK(event.button == 1);
    CHECK(event.cursor_index == 9);

    raw_event->publish_event = false;
    event = raw_event->next_event;
    has_event = true;
    CHECK(overlay->PollPointerEvent(event, has_event) == VRREC_STATUS_OK);
    CHECK(!has_event);
    CHECK(event == OpenVrOverlayPointerEvent {});
}

void RejectsInvalidPointerEventsReturnedByTheRuntime()
{
    auto state = std::make_shared<FakeState>();
    auto event_port = std::make_unique<FakeEventPort>(state);
    auto *raw_event = event_port.get();
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto overlay = CreateOpenVrOverlayLifecycle(
        Config(),
        std::make_unique<FakePort>(state),
        std::make_unique<FakeTexturePort>(state),
        std::move(event_port),
        status);
    raw_event->publish_event = true;
    auto event = OpenVrOverlayPointerEvent {};
    auto has_event = true;

    for (const auto invalid : {
             OpenVrOverlayPointerEvent {
                 static_cast<OpenVrOverlayPointerEventKind>(99),
                 1, 1, 0, 0},
             OpenVrOverlayPointerEvent {
                 OpenVrOverlayPointerEventKind::Move,
                 1024, 1, 0, 0},
             OpenVrOverlayPointerEvent {
                 OpenVrOverlayPointerEventKind::Move,
                 1, 512, 0, 0},
             OpenVrOverlayPointerEvent {
                 OpenVrOverlayPointerEventKind::ButtonDown,
                 1, 1, 0, 0},
         }) {
        raw_event->next_event = invalid;
        CHECK(overlay->PollPointerEvent(event, has_event) ==
              VRREC_STATUS_INTERNAL_ERROR);
        CHECK(!has_event);
        CHECK(event == OpenVrOverlayPointerEvent {});
    }
}

void PointerInputConfigurationFailureRollsBackTheOverlay()
{
    auto state = std::make_shared<FakeState>();
    auto event_port = std::make_unique<FakeEventPort>(state);
    event_port->configure_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    auto status = VRREC_STATUS_OK;

    auto overlay = CreateOpenVrOverlayLifecycle(
        Config(),
        std::make_unique<FakePort>(state),
        std::make_unique<FakeTexturePort>(state),
        std::move(event_port),
        status);

    CHECK(overlay == nullptr);
    CHECK(status == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK((state->calls == std::vector<std::string> {
        "create:com.vrrecorder.desktop.wrist:VR Recorder Wrist",
        "width:73:0.220000",
        "input:73:1024x512",
        "destroy:73",
    }));
}

void AppliesAndReadsOnlyValidatedOverlayPoses()
{
    auto state = std::make_shared<FakeState>();
    auto pose_port = std::make_unique<FakePosePort>(state);
    auto *raw_pose = pose_port.get();
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto overlay = CreateOpenVrOverlayLifecycle(
        Config(),
        std::make_unique<FakePort>(state),
        std::make_unique<FakeTexturePort>(state),
        std::make_unique<FakeEventPort>(state),
        std::move(pose_port),
        status);

    CHECK(status == VRREC_STATUS_OK);
    auto pose = WristPose();
    auto invalid = pose;
    invalid.transform.values[0] = 2;
    CHECK(overlay->SetPose(invalid) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(state->calls.back() == "input:73:1024x512");

    CHECK(overlay->SetPose(pose) == VRREC_STATUS_OK);
    CHECK(raw_pose->current_pose == pose);
    auto readback = OpenVrOverlayPose {};
    CHECK(overlay->GetPose(readback) == VRREC_STATUS_OK);
    CHECK(readback == pose);

    raw_pose->current_pose.transform.values[3] =
        std::numeric_limits<float>::quiet_NaN();
    readback = pose;
    CHECK(overlay->GetPose(readback) == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(readback == OpenVrOverlayPose {});

    CHECK(overlay->Close() == VRREC_STATUS_OK);
    CHECK(overlay->SetPose(pose) == VRREC_STATUS_INVALID_STATE);
    readback = pose;
    CHECK(overlay->GetPose(readback) == VRREC_STATUS_INVALID_STATE);
    CHECK(readback == OpenVrOverlayPose {});
}

void ReadsOnlyValidatedDeviceProfilesForASelectedHand()
{
    auto state = std::make_shared<FakeState>();
    auto pose_port = std::make_unique<FakePosePort>(state);
    auto *raw_pose = pose_port.get();
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto overlay = CreateOpenVrOverlayLifecycle(
        Config(),
        std::make_unique<FakePort>(state),
        std::make_unique<FakeTexturePort>(state),
        std::make_unique<FakeEventPort>(state),
        std::move(pose_port),
        status);

    auto profile = OpenVrDeviceProfile {};
    CHECK(overlay->GetDeviceProfile(OpenVrHand::Right, profile) ==
          VRREC_STATUS_OK);
    CHECK(profile == raw_pose->current_profile);
    CHECK(state->calls.back() == "profile-get:2");

    profile = raw_pose->current_profile;
    CHECK(overlay->GetDeviceProfile(OpenVrHand::None, profile) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(profile == OpenVrDeviceProfile {});

    raw_pose->current_profile.hmd_model_number.clear();
    CHECK(overlay->GetDeviceProfile(OpenVrHand::Left, profile) ==
          VRREC_STATUS_INTERNAL_ERROR);
    CHECK(profile == OpenVrDeviceProfile {});

    CHECK(overlay->Close() == VRREC_STATUS_OK);
    CHECK(overlay->GetDeviceProfile(OpenVrHand::Left, profile) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(profile == OpenVrDeviceProfile {});
}

}

int main()
{
    CreatesHiddenOverlayAndTransitionsIdempotently();
    RejectsInvalidConfigurationBeforeCallingThePort();
    RollsBackExactlyOnceWhenCreationCannotBeCompleted();
    CommitsVisibilityOnlyAfterThePortSucceeds();
    CloseDestroysOnceAndCachesTheFirstCleanupResult();
    DestructorClosesAnOpenOverlay();
    OwnsBgraTextureAndClearsItBeforeOverlayDestruction();
    RejectsInvalidBgraFramesBeforeCallingTexturePort();
    ClearFailureDoesNotSkipHideOrDestroy();
    ConfiguresAndPollsTopLeftPixelPointerEvents();
    RejectsInvalidPointerEventsReturnedByTheRuntime();
    PointerInputConfigurationFailureRollsBackTheOverlay();
    AppliesAndReadsOnlyValidatedOverlayPoses();
    ReadsOnlyValidatedDeviceProfilesForASelectedHand();
    return 0;
}
