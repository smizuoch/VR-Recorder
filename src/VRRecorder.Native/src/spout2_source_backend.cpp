#include "spout_source_backend.hpp"

#if !defined(_WIN32)
#error "The production Spout2 source backend requires Windows"
#endif

#include <utility>

#include "spout2_source_backend_core.hpp"
#include "windows_spout2_receiver_port.hpp"

namespace vrrecorder::native {

std::unique_ptr<SpoutSourceBackend> CreateSpoutSourceBackend(
    const vrrec_spout_source_config_v1 &config,
    vrrec_status_t &status)
{
    if (config.reserved_v1 != 0 || config.reserved_v2 != 0) {
        status = VRREC_STATUS_INVALID_ARGUMENT;
        return nullptr;
    }
    auto port = CreateWindowsSpout2ReceiverPort(status);
    if (!port) {
        return nullptr;
    }
    return CreateSpout2SourceBackend(std::move(port), status);
}

}
