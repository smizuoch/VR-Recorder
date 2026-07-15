#ifndef VRRECORDER_NATIVE_VIDEO_SURFACE_HPP
#define VRRECORDER_NATIVE_VIDEO_SURFACE_HPP

#include <chrono>
#include <cstdint>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

struct VideoSurfaceDescriptor final {
    std::uint64_t adapter_luid;
    std::uint32_t width;
    std::uint32_t height;
    vrrec_source_pixel_format_t pixel_format;
    std::uint64_t generation_id = 0;
};

enum class VideoSurfaceAcquireResult {
    Acquired,
    Timeout,
    Abandoned,
    DeviceLost,
    Failed,
};

class VideoSurface {
public:
    virtual ~VideoSurface() = default;

    virtual VideoSurfaceDescriptor Descriptor() const noexcept = 0;
    virtual void *NativeHandle() const noexcept = 0;
    virtual VideoSurfaceAcquireResult AcquireForRead(
        std::chrono::milliseconds timeout) noexcept = 0;
    virtual vrrec_status_t ReleaseFromRead() noexcept = 0;
};

}

#endif
