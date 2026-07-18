#include "production_video_encoder_route.hpp"

namespace vrrecorder::native {

vrrec_status_t ResolveProductionVideoEncoderRoute(
    vrrec_encoder_kind_t requested_kind,
    std::uint64_t source_adapter_luid,
    std::uint64_t requested_encoder_adapter_luid,
    ProductionVideoEncoderRoute &route) noexcept
{
    route = {};
    if (source_adapter_luid == 0 ||
        requested_encoder_adapter_luid != source_adapter_luid) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    switch (requested_kind) {
    case VRREC_ENCODER_NVENC:
        route = {
            requested_kind,
            "h264_nvenc",
            true,
            ProductionVideoEncoderInput::D3d11Nv12,
            source_adapter_luid,
            requested_encoder_adapter_luid,
        };
        return VRREC_STATUS_OK;
    case VRREC_ENCODER_MEDIA_FOUNDATION_SOFTWARE:
        route = {
            requested_kind,
            "h264_mf",
            false,
            ProductionVideoEncoderInput::SystemMemoryNv12,
            source_adapter_luid,
            0,
        };
        return VRREC_STATUS_OK;
    case VRREC_ENCODER_AMF:
    case VRREC_ENCODER_QSV:
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    default:
        return VRREC_STATUS_INVALID_ARGUMENT;
    }
}

}
