#ifndef VRRECORDER_NATIVE_H264_BITSTREAM_CONVERTER_HPP
#define VRRECORDER_NATIVE_H264_BITSTREAM_CONVERTER_HPP

#include <cstdint>
#include <span>
#include <vector>

#include "fragmented_mp4_mux_coordinator.hpp"
#include "video_encoder_config.hpp"
#include "vrrecorder_native.h"

namespace vrrecorder::native {

struct H264AnnexBConversionResult final {
    H264Profile profile = H264Profile::Main;
    std::uint32_t width = 0;
    std::uint32_t height = 0;
    bool key_frame = false;
    std::vector<std::byte> avcc;
    std::vector<std::byte> access_unit;
};

vrrec_status_t ConvertH264AnnexBToAvcc(
    std::span<const std::byte> annex_b_access_unit,
    std::uint32_t expected_width,
    std::uint32_t expected_height,
    H264Profile expected_profile,
    H264AnnexBConversionResult &result) noexcept;

}

#endif
