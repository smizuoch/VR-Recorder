#include "steamvr_input_backend.hpp"

namespace vrrecorder::native {

std::unique_ptr<SteamVrInputBackend> CreateSteamVrInputBackend(
    const vrrec_steamvr_input_config_v1 &config,
    vrrec_status_t &status)
{
    (void)config;
    status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    return nullptr;
}

}
