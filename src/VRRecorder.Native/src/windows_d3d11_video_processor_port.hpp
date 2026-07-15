#ifndef VRRECORDER_NATIVE_WINDOWS_D3D11_VIDEO_PROCESSOR_PORT_HPP
#define VRRECORDER_NATIVE_WINDOWS_D3D11_VIDEO_PROCESSOR_PORT_HPP

#include <cstdint>
#include <memory>

#include "d3d11_video_frame_processor.hpp"

namespace vrrecorder::native {

std::unique_ptr<D3d11VideoProcessorPort>
CreateWindowsD3d11VideoProcessorPort(
    void *d3d11_device,
    std::uint64_t adapter_luid,
    vrrec_status_t &status) noexcept;

std::unique_ptr<D3d11VideoProcessorPort>
CreateWindowsAdaptiveD3d11VideoProcessorPort(
    std::uint64_t adapter_luid,
    vrrec_status_t &status) noexcept;

}

#endif
