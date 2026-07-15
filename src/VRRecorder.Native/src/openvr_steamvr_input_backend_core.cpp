#include "openvr_steamvr_input_backend_core.hpp"

#include <cstdint>
#include <memory>
#include <new>
#include <string_view>
#include <utility>

namespace vrrecorder::native {
namespace {

bool IsPresent(const char *value) noexcept
{
    return value != nullptr && value[0] != '\0';
}

void Reset(vrrec_steamvr_digital_state_v1 &state) noexcept
{
    state.is_active = 0;
    state.state = 0;
    state.changed = 0;
    state.reserved = 0;
}

class OpenVrSteamVrInputBackend final : public SteamVrInputBackend {
public:
    OpenVrSteamVrInputBackend(
        std::unique_ptr<OpenVrInputPort> port,
        std::uint64_t action_set_handle,
        std::uint64_t action_handle) noexcept
        : port_(std::move(port)),
          action_set_handle_(action_set_handle),
          action_handle_(action_handle)
    {
    }

    ~OpenVrSteamVrInputBackend() override
    {
        port_->Shutdown();
    }

    vrrec_status_t Poll(
        vrrec_steamvr_digital_state_v1 &state) noexcept override
    {
        Reset(state);
        auto status = port_->UpdateActionState(action_set_handle_);
        if (status != VRREC_STATUS_OK) {
            return status;
        }

        OpenVrDigitalActionData data {};
        status = port_->GetDigitalActionData(action_handle_, data);
        if (status != VRREC_STATUS_OK) {
            return status;
        }

        state.is_active = data.is_active ? 1 : 0;
        state.state = data.state ? 1 : 0;
        state.changed = data.changed ? 1 : 0;
        return VRREC_STATUS_OK;
    }

private:
    std::unique_ptr<OpenVrInputPort> port_;
    std::uint64_t action_set_handle_;
    std::uint64_t action_handle_;
};

}

std::unique_ptr<SteamVrInputBackend> CreateOpenVrSteamVrInputBackend(
    const vrrec_steamvr_input_config_v1 &config,
    std::unique_ptr<OpenVrInputPort> port,
    vrrec_status_t &status) noexcept
{
    status = VRREC_STATUS_INVALID_ARGUMENT;
    if (!port || !IsPresent(config.action_manifest_path_utf8) ||
        !IsPresent(config.action_set_path_utf8) ||
        !IsPresent(config.digital_action_path_utf8)) {
        return nullptr;
    }

    status = port->Initialize();
    if (status != VRREC_STATUS_OK) {
        return nullptr;
    }

    status = port->SetActionManifestPath(config.action_manifest_path_utf8);
    if (status != VRREC_STATUS_OK) {
        port->Shutdown();
        return nullptr;
    }

    std::uint64_t action_set_handle = 0;
    status = port->GetActionSetHandle(
        config.action_set_path_utf8,
        action_set_handle);
    if (status != VRREC_STATUS_OK || action_set_handle == 0) {
        status = status == VRREC_STATUS_OK
            ? VRREC_STATUS_INTERNAL_ERROR
            : status;
        port->Shutdown();
        return nullptr;
    }

    std::uint64_t action_handle = 0;
    status = port->GetDigitalActionHandle(
        config.digital_action_path_utf8,
        action_handle);
    if (status != VRREC_STATUS_OK || action_handle == 0) {
        status = status == VRREC_STATUS_OK
            ? VRREC_STATUS_INTERNAL_ERROR
            : status;
        port->Shutdown();
        return nullptr;
    }

    auto backend = std::unique_ptr<SteamVrInputBackend>(
        new (std::nothrow) OpenVrSteamVrInputBackend(
            std::move(port),
            action_set_handle,
            action_handle));
    if (!backend) {
        port->Shutdown();
        status = VRREC_STATUS_OUT_OF_MEMORY;
        return nullptr;
    }

    status = VRREC_STATUS_OK;
    return backend;
}

}
