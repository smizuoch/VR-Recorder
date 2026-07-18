#ifndef VRRECORDER_NATIVE_PRODUCTION_VIDEO_ENCODER_ROUTE_HPP
#define VRRECORDER_NATIVE_PRODUCTION_VIDEO_ENCODER_ROUTE_HPP

#include <cstdint>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

enum class ProductionVideoEncoderInput {
    None,
    SystemMemoryNv12,
    D3d11Nv12,
};

struct ProductionVideoEncoderRoute final {
    vrrec_encoder_kind_t requested_kind = 0;
    const char *codec_name = nullptr;
    bool hardware_accelerated = false;
    ProductionVideoEncoderInput input = ProductionVideoEncoderInput::None;
    std::uint64_t source_adapter_luid = 0;
    std::uint64_t encoder_adapter_luid = 0;
};

vrrec_status_t ResolveProductionVideoEncoderRoute(
    vrrec_encoder_kind_t requested_kind,
    std::uint64_t source_adapter_luid,
    std::uint64_t requested_encoder_adapter_luid,
    ProductionVideoEncoderRoute &route) noexcept;

}

#endif
