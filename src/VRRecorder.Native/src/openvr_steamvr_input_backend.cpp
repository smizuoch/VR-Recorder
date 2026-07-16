#include "openvr_steamvr_input_backend_core.hpp"
#include "openvr_overlay_backend.hpp"

#include <cstdint>
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
        auto *system = vr::VR_Init(&error, vr::VRApplication_Overlay);
        if (error != vr::VRInitError_None || system == nullptr) {
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
        system->GetDXGIOutputInfo(&adapter_index);
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
        initialized_ = false;
        vr::VR_Shutdown();
        texture_presenter_.reset();
    }

private:
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
