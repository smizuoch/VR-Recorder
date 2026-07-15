#ifndef VRRECORDER_NATIVE_H264_SPS_PARSER_HPP
#define VRRECORDER_NATIVE_H264_SPS_PARSER_HPP

#include <cstdint>
#include <span>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

struct H264SpsInfo final {
    std::uint32_t sequence_parameter_set_id = 0;
    std::uint8_t profile_idc = 0;
    std::uint8_t profile_compatibility = 0;
    std::uint8_t level_idc = 0;
    std::uint32_t chroma_format_idc = 1;
    std::uint32_t bit_depth_luma = 8;
    std::uint32_t bit_depth_chroma = 8;
    std::uint32_t width = 0;
    std::uint32_t height = 0;
    bool frame_mbs_only = true;
};

struct H264PpsInfo final {
    std::uint32_t picture_parameter_set_id = 0;
    std::uint32_t sequence_parameter_set_id = 0;
};

vrrec_status_t ParseH264Sps(
    std::span<const std::byte> sps_nal,
    H264SpsInfo &result) noexcept;

vrrec_status_t ParseH264Pps(
    std::span<const std::byte> pps_nal,
    H264PpsInfo &result) noexcept;

}

#endif
