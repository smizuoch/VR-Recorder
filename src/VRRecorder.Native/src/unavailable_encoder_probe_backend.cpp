#include "encoder_probe_backend.hpp"

namespace vrrecorder::native {

std::unique_ptr<EncoderProbeBackend> CreateEncoderProbeBackend(
    vrrec_status_t &status)
{
    status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    return nullptr;
}

}
