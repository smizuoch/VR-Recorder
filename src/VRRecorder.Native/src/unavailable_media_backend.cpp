#include "media_backend.hpp"

namespace vrrecorder::native {

std::unique_ptr<MediaBackend> CreateMediaBackend(
    const vrrec_session_config_v1 &config,
    MediaEventSink &events,
    vrrec_status_t &status)
{
    (void)config;
    (void)events;
    status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    return nullptr;
}

}
