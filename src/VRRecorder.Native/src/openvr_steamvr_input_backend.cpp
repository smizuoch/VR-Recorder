#include "openvr_steamvr_input_backend_core.hpp"
#include "openvr_steamvr_haptic_backend_core.hpp"
#include "openvr_overlay_backend.hpp"

#include <array>
#include <cstdint>
#include <cmath>
#include <memory>
#include <new>
#include <string>
#include <string_view>
#include <utility>

#include "openvr.h"
#include "openvr_overlay_texture_presenter.hpp"
#include "openvr_process_runtime.hpp"
#include "windows_openvr_overlay_texture_graphics_port.hpp"

namespace vrrecorder::native {
namespace {

vrrec_status_t MapInputError(vr::EVRInputError error) noexcept
{
    if (error == vr::VRInputError_None) {
        return VRREC_STATUS_OK;
    }
    if (error == vr::VRInputError_NoSteam) {
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    }
    return VRREC_STATUS_INTERNAL_ERROR;
}

vrrec_status_t MapApplicationError(vr::EVRApplicationError error) noexcept
{
    if (error == vr::VRApplicationError_None) {
        return VRREC_STATUS_OK;
    }
    if (error == vr::VRApplicationError_IPCFailed ||
        error == vr::VRApplicationError_SteamVRIsExiting) {
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    }
    return VRREC_STATUS_INTERNAL_ERROR;
}

vrrec_status_t MapOverlayError(vr::EVROverlayError error) noexcept
{
    if (error == vr::VROverlayError_None) {
        return VRREC_STATUS_OK;
    }
    if (error == vr::VROverlayError_RequestFailed ||
        error == vr::VROverlayError_TimedOut ||
        error == vr::VROverlayError_OverlayLimitExceeded) {
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    }
    if (error == vr::VROverlayError_UnknownOverlay ||
        error == vr::VROverlayError_InvalidHandle) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (error == vr::VROverlayError_InvalidParameter ||
        error == vr::VROverlayError_KeyTooLong ||
        error == vr::VROverlayError_NameTooLong) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }
    return VRREC_STATUS_INTERNAL_ERROR;
}

class WindowsOpenVrApi final : public OpenVrRuntimePort {
public:
    ~WindowsOpenVrApi() override
    {
        Shutdown();
    }

    vrrec_status_t Initialize() noexcept override
    {
        if (initialized_) {
            return VRREC_STATUS_INVALID_STATE;
        }

        auto error = vr::VRInitError_None;
        system_ = vr::VR_Init(&error, vr::VRApplication_Overlay);
        if (error != vr::VRInitError_None || system_ == nullptr) {
            system_ = nullptr;
            return VRREC_STATUS_BACKEND_UNAVAILABLE;
        }

        initialized_ = true;
        applications_ = vr::VRApplications();
        input_ = vr::VRInput();
        overlay_ = vr::VROverlay();
        if (applications_ == nullptr || input_ == nullptr ||
            overlay_ == nullptr) {
            Shutdown();
            return VRREC_STATUS_INTERNAL_ERROR;
        }

        auto adapter_index = std::int32_t {-1};
        system_->GetDXGIOutputInfo(&adapter_index);
        auto texture_status = VRREC_STATUS_INTERNAL_ERROR;
        auto graphics_port =
            CreateWindowsOpenVrOverlayTextureGraphicsPort(
                overlay_,
                adapter_index,
                texture_status);
        if (!graphics_port) {
            Shutdown();
            return texture_status;
        }
        texture_presenter_ = CreateOpenVrOverlayTexturePresenter(
            std::move(graphics_port),
            texture_status);
        if (!texture_presenter_) {
            Shutdown();
            return texture_status;
        }
        return VRREC_STATUS_OK;
    }

    vrrec_status_t CreateOverlay(
        std::string_view key,
        std::string_view name,
        std::uint64_t &handle) noexcept override
    {
        handle = 0;
        if (!initialized_ || overlay_ == nullptr) {
            return VRREC_STATUS_INVALID_STATE;
        }
        try {
            const std::string owned_key(key);
            const std::string owned_name(name);
            auto openvr_handle = vr::k_ulOverlayHandleInvalid;
            const auto status = MapOverlayError(overlay_->CreateOverlay(
                owned_key.c_str(),
                owned_name.c_str(),
                &openvr_handle));
            if (status == VRREC_STATUS_OK) {
                handle = openvr_handle;
            }
            return status;
        } catch (const std::bad_alloc &) {
            return VRREC_STATUS_OUT_OF_MEMORY;
        } catch (...) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
    }

    vrrec_status_t SetOverlayWidthInMeters(
        std::uint64_t handle,
        float width) noexcept override
    {
        return !initialized_ || overlay_ == nullptr
            ? VRREC_STATUS_INVALID_STATE
            : MapOverlayError(overlay_->SetOverlayWidthInMeters(
                static_cast<vr::VROverlayHandle_t>(handle),
                width));
    }

    vrrec_status_t ShowOverlay(std::uint64_t handle) noexcept override
    {
        return !initialized_ || overlay_ == nullptr
            ? VRREC_STATUS_INVALID_STATE
            : MapOverlayError(overlay_->ShowOverlay(
                static_cast<vr::VROverlayHandle_t>(handle)));
    }

    vrrec_status_t HideOverlay(std::uint64_t handle) noexcept override
    {
        return !initialized_ || overlay_ == nullptr
            ? VRREC_STATUS_INVALID_STATE
            : MapOverlayError(overlay_->HideOverlay(
                static_cast<vr::VROverlayHandle_t>(handle)));
    }

    vrrec_status_t DestroyOverlay(std::uint64_t handle) noexcept override
    {
        return !initialized_ || overlay_ == nullptr
            ? VRREC_STATUS_INVALID_STATE
            : MapOverlayError(overlay_->DestroyOverlay(
                static_cast<vr::VROverlayHandle_t>(handle)));
    }

    vrrec_status_t SetOverlayBgraTexture(
        std::uint64_t handle,
        const OpenVrBgraTextureFrame &frame) noexcept override
    {
        return !initialized_ || !texture_presenter_
            ? VRREC_STATUS_INVALID_STATE
            : texture_presenter_->SetOverlayBgraTexture(handle, frame);
    }

    vrrec_status_t ClearOverlayTexture(
        std::uint64_t handle) noexcept override
    {
        return !initialized_ || !texture_presenter_
            ? VRREC_STATUS_INVALID_STATE
            : texture_presenter_->ClearOverlayTexture(handle);
    }

    vrrec_status_t ConfigureOverlayPointerInput(
        std::uint64_t handle,
        std::uint32_t pixel_width,
        std::uint32_t pixel_height) noexcept override
    {
        if (!initialized_ || overlay_ == nullptr) {
            return VRREC_STATUS_INVALID_STATE;
        }
        if (pixel_width != 1024 || pixel_height != 512) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }
        const auto openvr_handle =
            static_cast<vr::VROverlayHandle_t>(handle);
        auto status = MapOverlayError(overlay_->SetOverlayInputMethod(
            openvr_handle,
            vr::VROverlayInputMethod_Mouse));
        if (status != VRREC_STATUS_OK) {
            return status;
        }
        const auto mouse_scale = vr::HmdVector2_t {
            {static_cast<float>(pixel_width),
             static_cast<float>(pixel_height)},
        };
        status = MapOverlayError(overlay_->SetOverlayMouseScale(
            openvr_handle,
            &mouse_scale));
        if (status != VRREC_STATUS_OK) {
            static_cast<void>(overlay_->SetOverlayInputMethod(
                openvr_handle,
                vr::VROverlayInputMethod_None));
        }
        return status;
    }

    vrrec_status_t PollNextOverlayPointerEvent(
        std::uint64_t handle,
        OpenVrOverlayPointerEvent &event,
        bool &has_event) noexcept override
    {
        event = {};
        has_event = false;
        if (!initialized_ || overlay_ == nullptr || handle == 0) {
            return VRREC_STATUS_INVALID_STATE;
        }

        auto openvr_event = vr::VREvent_t {};
        while (overlay_->PollNextOverlayEvent(
            static_cast<vr::VROverlayHandle_t>(handle),
            &openvr_event,
            sizeof(openvr_event))) {
            auto kind = OpenVrOverlayPointerEventKind {};
            switch (openvr_event.eventType) {
                case vr::VREvent_MouseMove:
                    kind = OpenVrOverlayPointerEventKind::Move;
                    break;
                case vr::VREvent_MouseButtonDown:
                    kind = OpenVrOverlayPointerEventKind::ButtonDown;
                    break;
                case vr::VREvent_MouseButtonUp:
                    kind = OpenVrOverlayPointerEventKind::ButtonUp;
                    break;
                default:
                    openvr_event = {};
                    continue;
            }

            const auto x = openvr_event.data.mouse.x;
            const auto y = openvr_event.data.mouse.y;
            if (!std::isfinite(x) || !std::isfinite(y) ||
                x < 0.0F || x >= 1024.0F ||
                y < 0.0F || y >= 512.0F) {
                return VRREC_STATUS_INTERNAL_ERROR;
            }
            event = OpenVrOverlayPointerEvent {
                kind,
                static_cast<std::uint32_t>(std::floor(x)),
                511U - static_cast<std::uint32_t>(std::floor(y)),
                kind == OpenVrOverlayPointerEventKind::Move
                    ? 0U
                    : openvr_event.data.mouse.button,
                openvr_event.data.mouse.cursorIndex,
            };
            has_event = true;
            return VRREC_STATUS_OK;
        }
        return VRREC_STATUS_OK;
    }

    vrrec_status_t SetOverlayPose(
        std::uint64_t handle,
        const OpenVrOverlayPose &pose) noexcept override
    {
        if (!initialized_ || system_ == nullptr || overlay_ == nullptr) {
            return VRREC_STATUS_INVALID_STATE;
        }
        auto matrix = vr::HmdMatrix34_t {};
        for (auto row = std::size_t {0}; row < 3; ++row) {
            for (auto column = std::size_t {0}; column < 4; ++column) {
                matrix.m[row][column] =
                    pose.transform.values[(row * 4) + column];
            }
        }
        const auto openvr_handle =
            static_cast<vr::VROverlayHandle_t>(handle);
        if (pose.placement_mode == OpenVrOverlayPlacementMode::WorldPin) {
            return MapOverlayError(overlay_->SetOverlayTransformAbsolute(
                openvr_handle,
                vr::TrackingUniverseStanding,
                &matrix));
        }

        const auto role = pose.hand == OpenVrHand::Left
            ? vr::TrackedControllerRole_LeftHand
            : vr::TrackedControllerRole_RightHand;
        const auto device =
            system_->GetTrackedDeviceIndexForControllerRole(role);
        return device == vr::k_unTrackedDeviceIndexInvalid
            ? VRREC_STATUS_BACKEND_UNAVAILABLE
            : MapOverlayError(
                overlay_->SetOverlayTransformTrackedDeviceRelative(
                    openvr_handle,
                    device,
                    &matrix));
    }

    vrrec_status_t GetOverlayPose(
        std::uint64_t handle,
        OpenVrOverlayPose &pose) noexcept override
    {
        pose = {};
        if (!initialized_ || system_ == nullptr || overlay_ == nullptr) {
            return VRREC_STATUS_INVALID_STATE;
        }
        const auto openvr_handle =
            static_cast<vr::VROverlayHandle_t>(handle);
        auto type = vr::VROverlayTransform_Invalid;
        auto status = MapOverlayError(
            overlay_->GetOverlayTransformType(openvr_handle, &type));
        if (status != VRREC_STATUS_OK) {
            return status;
        }
        auto matrix = vr::HmdMatrix34_t {};
        if (type == vr::VROverlayTransform_Absolute) {
            auto origin = vr::TrackingUniverseRawAndUncalibrated;
            status = MapOverlayError(overlay_->GetOverlayTransformAbsolute(
                openvr_handle,
                &origin,
                &matrix));
            if (status != VRREC_STATUS_OK) {
                return status;
            }
            if (origin != vr::TrackingUniverseStanding) {
                return VRREC_STATUS_INTERNAL_ERROR;
            }
            pose.placement_mode = OpenVrOverlayPlacementMode::WorldPin;
            pose.hand = OpenVrHand::None;
            pose.tracking_origin = OpenVrTrackingOrigin::Standing;
        } else if (type == vr::VROverlayTransform_TrackedDeviceRelative) {
            auto device = vr::k_unTrackedDeviceIndexInvalid;
            status = MapOverlayError(
                overlay_->GetOverlayTransformTrackedDeviceRelative(
                    openvr_handle,
                    &device,
                    &matrix));
            if (status != VRREC_STATUS_OK) {
                return status;
            }
            const auto role =
                system_->GetControllerRoleForTrackedDeviceIndex(device);
            if (role != vr::TrackedControllerRole_LeftHand &&
                role != vr::TrackedControllerRole_RightHand) {
                return VRREC_STATUS_INTERNAL_ERROR;
            }
            pose.placement_mode = OpenVrOverlayPlacementMode::WristDock;
            pose.hand = role == vr::TrackedControllerRole_LeftHand
                ? OpenVrHand::Left
                : OpenVrHand::Right;
            pose.tracking_origin = OpenVrTrackingOrigin::None;
        } else {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        for (auto row = std::size_t {0}; row < 3; ++row) {
            for (auto column = std::size_t {0}; column < 4; ++column) {
                pose.transform.values[(row * 4) + column] =
                    matrix.m[row][column];
            }
        }
        return VRREC_STATUS_OK;
    }

    vrrec_status_t GetDeviceProfile(
        OpenVrHand hand,
        OpenVrDeviceProfile &profile) noexcept override
    {
        profile = {};
        if (!initialized_ || system_ == nullptr) {
            return VRREC_STATUS_INVALID_STATE;
        }
        if (hand != OpenVrHand::Left && hand != OpenVrHand::Right) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }
        const auto role = hand == OpenVrHand::Left
            ? vr::TrackedControllerRole_LeftHand
            : vr::TrackedControllerRole_RightHand;
        const auto controller =
            system_->GetTrackedDeviceIndexForControllerRole(role);
        if (controller == vr::k_unTrackedDeviceIndexInvalid) {
            return VRREC_STATUS_BACKEND_UNAVAILABLE;
        }
        auto status = ReadTrackedDeviceString(
            vr::k_unTrackedDeviceIndex_Hmd,
            vr::Prop_TrackingSystemName_String,
            profile.tracking_system_name);
        if (status == VRREC_STATUS_OK) {
            status = ReadTrackedDeviceString(
                vr::k_unTrackedDeviceIndex_Hmd,
                vr::Prop_ModelNumber_String,
                profile.hmd_model_number);
        }
        if (status == VRREC_STATUS_OK) {
            status = ReadTrackedDeviceString(
                controller,
                vr::Prop_InputProfilePath_String,
                profile.controller_input_profile_path);
        }
        if (status != VRREC_STATUS_OK ||
            !IsValidOpenVrDeviceProfile(profile)) {
            profile = {};
            return status == VRREC_STATUS_OK
                ? VRREC_STATUS_INTERNAL_ERROR
                : status;
        }
        return VRREC_STATUS_OK;
    }

    vrrec_status_t AddApplicationManifest(
        std::string_view absolute_path,
        bool temporary) noexcept override
    {
        if (!initialized_ || applications_ == nullptr) {
            return VRREC_STATUS_INVALID_STATE;
        }
        try {
            const std::string path(absolute_path);
            return MapApplicationError(
                applications_->AddApplicationManifest(
                    path.c_str(),
                    temporary));
        } catch (const std::bad_alloc &) {
            return VRREC_STATUS_OUT_OF_MEMORY;
        } catch (...) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
    }

    vrrec_status_t SetActionManifestPath(
        std::string_view absolute_path) noexcept override
    {
        if (!initialized_ || input_ == nullptr) {
            return VRREC_STATUS_INVALID_STATE;
        }
        try {
            const std::string path(absolute_path);
            return MapInputError(input_->SetActionManifestPath(path.c_str()));
        } catch (const std::bad_alloc &) {
            return VRREC_STATUS_OUT_OF_MEMORY;
        } catch (...) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
    }

    vrrec_status_t GetActionSetHandle(
        std::string_view action_set_path,
        std::uint64_t &handle) noexcept override
    {
        handle = 0;
        if (!initialized_ || input_ == nullptr) {
            return VRREC_STATUS_INVALID_STATE;
        }
        try {
            const std::string path(action_set_path);
            vr::VRActionSetHandle_t openvr_handle =
                vr::k_ulInvalidActionSetHandle;
            const auto status = MapInputError(
                input_->GetActionSetHandle(path.c_str(), &openvr_handle));
            if (status == VRREC_STATUS_OK) {
                handle = openvr_handle;
            }
            return status;
        } catch (const std::bad_alloc &) {
            return VRREC_STATUS_OUT_OF_MEMORY;
        } catch (...) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
    }

    vrrec_status_t GetDigitalActionHandle(
        std::string_view action_path,
        std::uint64_t &handle) noexcept override
    {
        handle = 0;
        if (!initialized_ || input_ == nullptr) {
            return VRREC_STATUS_INVALID_STATE;
        }
        try {
            const std::string path(action_path);
            vr::VRActionHandle_t openvr_handle = vr::k_ulInvalidActionHandle;
            const auto status = MapInputError(
                input_->GetActionHandle(path.c_str(), &openvr_handle));
            if (status == VRREC_STATUS_OK) {
                handle = openvr_handle;
            }
            return status;
        } catch (const std::bad_alloc &) {
            return VRREC_STATUS_OUT_OF_MEMORY;
        } catch (...) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
    }

    vrrec_status_t GetHapticActionHandle(
        std::string_view action_path,
        std::uint64_t &handle) noexcept override
    {
        handle = 0;
        if (!initialized_ || input_ == nullptr) {
            return VRREC_STATUS_INVALID_STATE;
        }
        try {
            const std::string path(action_path);
            auto openvr_handle = vr::VRActionHandle_t {
                vr::k_ulInvalidActionHandle,
            };
            const auto status = MapInputError(
                input_->GetActionHandle(path.c_str(), &openvr_handle));
            if (status == VRREC_STATUS_OK) {
                handle = openvr_handle;
            }
            return status;
        } catch (const std::bad_alloc &) {
            return VRREC_STATUS_OUT_OF_MEMORY;
        } catch (...) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
    }

    vrrec_status_t GetInputSourceHandle(
        std::string_view input_source_path,
        std::uint64_t &handle) noexcept override
    {
        handle = 0;
        if (!initialized_ || input_ == nullptr) {
            return VRREC_STATUS_INVALID_STATE;
        }
        try {
            const std::string path(input_source_path);
            auto openvr_handle = vr::VRInputValueHandle_t {
                vr::k_ulInvalidInputValueHandle,
            };
            const auto status = MapInputError(
                input_->GetInputSourceHandle(path.c_str(), &openvr_handle));
            if (status == VRREC_STATUS_OK) {
                handle = openvr_handle;
            }
            return status;
        } catch (const std::bad_alloc &) {
            return VRREC_STATUS_OUT_OF_MEMORY;
        } catch (...) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
    }

    vrrec_status_t TriggerHapticVibrationAction(
        std::uint64_t action_handle,
        std::uint64_t source_handle,
        const OpenVrHapticPulse &pulse) noexcept override
    {
        if (!initialized_ || input_ == nullptr) {
            return VRREC_STATUS_INVALID_STATE;
        }
        if (action_handle == 0 || source_handle == 0 ||
            !IsValidOpenVrHapticPulse(pulse)) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }
        return MapInputError(input_->TriggerHapticVibrationAction(
            static_cast<vr::VRActionHandle_t>(action_handle),
            0.0F,
            pulse.duration_seconds,
            pulse.frequency_hertz,
            pulse.amplitude,
            static_cast<vr::VRInputValueHandle_t>(source_handle)));
    }

    vrrec_status_t UpdateActionState(
        std::uint64_t action_set_handle) noexcept override
    {
        if (!initialized_ || input_ == nullptr || action_set_handle == 0) {
            return VRREC_STATUS_INVALID_STATE;
        }
        auto active_set = vr::VRActiveActionSet_t {};
        active_set.ulActionSet =
            static_cast<vr::VRActionSetHandle_t>(action_set_handle);
        active_set.ulRestrictedToDevice = vr::k_ulInvalidInputValueHandle;
        active_set.ulSecondaryActionSet = vr::k_ulInvalidActionSetHandle;
        active_set.nPriority = 0;
        return MapInputError(input_->UpdateActionState(
            &active_set,
            sizeof(active_set),
            1));
    }

    vrrec_status_t GetDigitalActionData(
        std::uint64_t action_handle,
        OpenVrDigitalActionData &data) noexcept override
    {
        data = {};
        if (!initialized_ || input_ == nullptr || action_handle == 0) {
            return VRREC_STATUS_INVALID_STATE;
        }
        auto openvr_data = vr::InputDigitalActionData_t {};
        const auto status = MapInputError(input_->GetDigitalActionData(
            static_cast<vr::VRActionHandle_t>(action_handle),
            &openvr_data,
            sizeof(openvr_data),
            vr::k_ulInvalidInputValueHandle));
        if (status == VRREC_STATUS_OK) {
            data = OpenVrDigitalActionData {
                openvr_data.bActive,
                openvr_data.bState,
                openvr_data.bChanged,
            };
        }
        return status;
    }

    void Shutdown() noexcept override
    {
        if (!initialized_) {
            return;
        }
        input_ = nullptr;
        applications_ = nullptr;
        overlay_ = nullptr;
        system_ = nullptr;
        initialized_ = false;
        vr::VR_Shutdown();
        texture_presenter_.reset();
    }

private:
    vrrec_status_t ReadTrackedDeviceString(
        vr::TrackedDeviceIndex_t device,
        vr::ETrackedDeviceProperty property,
        std::string &value) noexcept
    {
        value.clear();
        auto buffer = std::array<char, vr::k_unMaxPropertyStringSize> {};
        auto error = vr::TrackedProp_Success;
        const auto size = system_->GetStringTrackedDeviceProperty(
            device,
            property,
            buffer.data(),
            static_cast<std::uint32_t>(buffer.size()),
            &error);
        if (error != vr::TrackedProp_Success) {
            return error == vr::TrackedProp_InvalidDevice ||
                    error == vr::TrackedProp_CouldNotContactServer ||
                    error == vr::TrackedProp_NotYetAvailable ||
                    error == vr::TrackedProp_IPCReadFailure
                ? VRREC_STATUS_BACKEND_UNAVAILABLE
                : VRREC_STATUS_INTERNAL_ERROR;
        }
        if (size <= 1 || size > buffer.size() || buffer[size - 1] != '\0') {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        try {
            value.assign(buffer.data(), size - 1);
            return VRREC_STATUS_OK;
        } catch (const std::bad_alloc &) {
            return VRREC_STATUS_OUT_OF_MEMORY;
        } catch (...) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
    }

    vr::IVRSystem *system_ = nullptr;
    vr::IVRApplications *applications_ = nullptr;
    vr::IVRInput *input_ = nullptr;
    vr::IVROverlay *overlay_ = nullptr;
    std::unique_ptr<OpenVrOverlayTexturePort> texture_presenter_;
    bool initialized_ = false;
};

struct ProcessRuntimeResult final {
    std::shared_ptr<OpenVrProcessRuntime> runtime;
    vrrec_status_t status;
};

const ProcessRuntimeResult &GetProcessRuntime() noexcept
{
    static const auto result = [] {
        auto api = std::unique_ptr<OpenVrRuntimePort>(
            new (std::nothrow) WindowsOpenVrApi());
        if (!api) {
            return ProcessRuntimeResult {
                nullptr,
                VRREC_STATUS_OUT_OF_MEMORY,
            };
        }
        auto status = VRREC_STATUS_INTERNAL_ERROR;
        auto runtime = CreateOpenVrProcessRuntime(std::move(api), status);
        return ProcessRuntimeResult {std::move(runtime), status};
    }();
    return result;
}

}

std::unique_ptr<SteamVrInputBackend> CreateSteamVrInputBackend(
    const vrrec_steamvr_input_config_v1 &config,
    vrrec_status_t &status)
{
    const auto &process = GetProcessRuntime();
    if (!process.runtime) {
        status = process.status;
        return nullptr;
    }
    auto port = CreateOpenVrProcessInputPort(process.runtime, status);
    if (!port) {
        return nullptr;
    }
    return CreateOpenVrSteamVrInputBackend(
        config,
        std::move(port),
        status);
}

std::unique_ptr<SteamVrHapticBackend> CreateSteamVrHapticBackend(
    const SteamVrHapticConfig &config,
    vrrec_status_t &status) noexcept
{
    const auto &process = GetProcessRuntime();
    if (!process.runtime) {
        status = process.status;
        return nullptr;
    }
    auto port = CreateOpenVrProcessHapticPort(process.runtime, status);
    if (!port) {
        return nullptr;
    }
    return CreateOpenVrSteamVrHapticBackend(
        config,
        std::move(port),
        status);
}

std::unique_ptr<OpenVrOverlayLifecycle> CreateSteamVrOverlayLifecycle(
    std::string_view application_manifest_path,
    const OpenVrOverlayLifecycleConfig &config,
    vrrec_status_t &status) noexcept
{
    const auto &process = GetProcessRuntime();
    if (!process.runtime) {
        status = process.status;
        return nullptr;
    }
    return CreateOpenVrProcessOverlayLifecycle(
        process.runtime,
        application_manifest_path,
        config,
        status);
}

}
