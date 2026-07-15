#ifndef VRRECORDER_NATIVE_H264_AVCC_TO_ANNEX_B_HPP
#define VRRECORDER_NATIVE_H264_AVCC_TO_ANNEX_B_HPP

#include <cstddef>
#include <span>
#include <vector>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

vrrec_status_t ConvertH264AvccDescriptorToAnnexB(
    std::span<const std::byte> descriptor,
    std::vector<std::byte> &annex_b) noexcept;

vrrec_status_t ConvertH264AvccAccessUnitToAnnexB(
    std::span<const std::byte> access_unit,
    std::vector<std::byte> &annex_b) noexcept;

}

#endif
