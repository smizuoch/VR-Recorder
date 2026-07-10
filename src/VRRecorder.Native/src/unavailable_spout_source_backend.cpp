#include "spout_source_backend.hpp"

namespace vrrecorder::native {

std::unique_ptr<SpoutSourceBackend> CreateSpoutSourceBackend(
    const vrrec_spout_source_config_v1 &config,
    vrrec_status_t &status)
{
    (void)config;
    status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    return nullptr;
}

}
