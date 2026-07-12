#ifndef VRRECORDER_NATIVE_VIDEO_SURFACE_HPP
#define VRRECORDER_NATIVE_VIDEO_SURFACE_HPP

#include <cstdint>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

struct VideoSurfaceDescriptor final {
    std::uint64_t adapter_luid;
    std::uint32_t width;
    std::uint32_t height;
    vrrec_source_pixel_format_t pixel_format;
};

class VideoSurface {
public:
    virtual ~VideoSurface() = default;

    virtual VideoSurfaceDescriptor Descriptor() const noexcept = 0;
    virtual void *NativeHandle() const noexcept = 0;
};

}

#endif
