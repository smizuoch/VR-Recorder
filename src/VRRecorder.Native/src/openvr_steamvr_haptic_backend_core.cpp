#include "openvr_steamvr_haptic_backend_core.hpp"

#include <cstdint>
#include <memory>
#include <new>
#include <string>
#include <utility>

#include "steamvr_manifest_paths.hpp"

namespace vrrecorder::native {
namespace {

class OpenVrSteamVrHapticBackend final : public SteamVrHapticBackend {
public:
    OpenVrSteamVrHapticBackend(
        std::unique_ptr<OpenVrHapticPort> port,
        std::uint64_t action_handle,
        std::uint64_t source_handle) noexcept
        : port_(std::move(port)),
          action_handle_(action_handle),
          source_handle_(source_handle)
    {
    }

    ~OpenVrSteamVrHapticBackend() override
    {
        port_->Shutdown();
    }

    vrrec_status_t Trigger(
        const OpenVrHapticPulse &pulse) noexcept override
    {
        return IsValidOpenVrHapticPulse(pulse)
            ? port_->TriggerHapticVibrationAction(
                action_handle_,
                source_handle_,
                pulse)
            : VRREC_STATUS_INVALID_ARGUMENT;
    }

private:
    std::unique_ptr<OpenVrHapticPort> port_;
    std::uint64_t action_handle_;
    std::uint64_t source_handle_;
};

}

std::unique_ptr<SteamVrHapticBackend> CreateOpenVrSteamVrHapticBackend(
    const SteamVrHapticConfig &config,
    std::unique_ptr<OpenVrHapticPort> port,
    vrrec_status_t &status) noexcept
{
    status = VRREC_STATUS_INVALID_ARGUMENT;
    if (!port || config.action_manifest_path.empty() ||
        config.haptic_action_path.empty() ||
        config.input_source_path.empty()) {
        return nullptr;
    }

    std::string application_manifest_path;
    status = ResolveSteamVrApplicationManifestPath(
        config.action_manifest_path,
        application_manifest_path);
    if (status != VRREC_STATUS_OK) {
        return nullptr;
    }

    status = port->Initialize();
    if (status != VRREC_STATUS_OK) {
        return nullptr;
    }

    status = port->AddApplicationManifest(
        application_manifest_path,
        true);
    if (status != VRREC_STATUS_OK) {
        port->Shutdown();
        return nullptr;
    }

    status = port->SetActionManifestPath(config.action_manifest_path);
    if (status != VRREC_STATUS_OK) {
        port->Shutdown();
        return nullptr;
    }

    auto action_handle = std::uint64_t {0};
    status = port->GetHapticActionHandle(
        config.haptic_action_path,
        action_handle);
    if (status != VRREC_STATUS_OK || action_handle == 0) {
        status = status == VRREC_STATUS_OK
            ? VRREC_STATUS_INTERNAL_ERROR
            : status;
        port->Shutdown();
        return nullptr;
    }

    auto source_handle = std::uint64_t {0};
    status = port->GetInputSourceHandle(
        config.input_source_path,
        source_handle);
    if (status != VRREC_STATUS_OK || source_handle == 0) {
        status = status == VRREC_STATUS_OK
            ? VRREC_STATUS_INTERNAL_ERROR
            : status;
        port->Shutdown();
        return nullptr;
    }

    auto backend = std::unique_ptr<SteamVrHapticBackend>(
        new (std::nothrow) OpenVrSteamVrHapticBackend(
            std::move(port),
            action_handle,
            source_handle));
    if (!backend) {
        port->Shutdown();
        status = VRREC_STATUS_OUT_OF_MEMORY;
        return nullptr;
    }

    status = VRREC_STATUS_OK;
    return backend;
}

}
