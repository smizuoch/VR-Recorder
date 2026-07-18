#ifndef VRRECORDER_NATIVE_PRODUCTION_MEDIA_CONFIGURATION_HPP
#define VRRECORDER_NATIVE_PRODUCTION_MEDIA_CONFIGURATION_HPP

#include <cstdint>

#include "production_video_encoder_route.hpp"
#include "vrrecorder_native.h"

namespace vrrecorder::native {

struct ProductionMediaConfiguration final {
    std::uint32_t frames_per_second = 0;
    ProductionVideoEncoderRoute encoder_route;
    vrrec_video_layout_v1 layout {};
};

vrrec_status_t ValidateProductionMediaConfiguration(
    const vrrec_session_config_v1 &input,
    ProductionMediaConfiguration &output) noexcept;

}

#endif
