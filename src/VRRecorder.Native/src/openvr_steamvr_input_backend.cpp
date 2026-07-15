#include "openvr_steamvr_input_backend_core.hpp"

#include <cstdint>
#include <memory>
#include <new>
#include <string>
#include <string_view>
#include <utility>

#include "openvr.h"
#include "openvr_process_runtime.hpp"

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

class WindowsOpenVrApi final : public OpenVrInputPort {
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
        input_ = vr::VRInput();
        if (input_ == nullptr) {
            Shutdown();
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        return VRREC_STATUS_OK;
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
        initialized_ = false;
        vr::VR_Shutdown();
    }

private:
    vr::IVRInput *input_ = nullptr;
    bool initialized_ = false;
};

struct ProcessRuntimeResult final {
    std::shared_ptr<OpenVrProcessRuntime> runtime;
    vrrec_status_t status;
};

const ProcessRuntimeResult &GetProcessRuntime() noexcept
{
    static const auto result = [] {
        auto api = std::unique_ptr<OpenVrInputPort>(
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

}
