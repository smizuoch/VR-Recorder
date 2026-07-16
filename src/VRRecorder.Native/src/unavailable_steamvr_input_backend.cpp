#include "steamvr_input_backend.hpp"
#include "openvr_overlay_backend.hpp"

namespace vrrecorder::native {

std::unique_ptr<SteamVrInputBackend> CreateSteamVrInputBackend(
    const vrrec_steamvr_input_config_v1 &config,
    vrrec_status_t &status)
{
    (void)config;
    status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    return nullptr;
}

std::unique_ptr<OpenVrOverlayLifecycle> CreateSteamVrOverlayLifecycle(
    std::string_view application_manifest_path,
    const OpenVrOverlayLifecycleConfig &config,
    vrrec_status_t &status) noexcept
{
    (void)application_manifest_path;
    (void)config;
    status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    return nullptr;
}

}
