#ifndef VRRECORDER_NATIVE_FFMPEG_H264_NV12_FRAME_HPP
#define VRRECORDER_NATIVE_FFMPEG_H264_NV12_FRAME_HPP

#include <cstddef>
#include <cstdint>
#include <span>

#include "vrrecorder_native.h"

struct AVFrame;

namespace vrrecorder::native {

struct SystemMemoryNv12FrameView final {
    std::uint32_t width = 0;
    std::uint32_t height = 0;
    std::uint32_t y_stride_bytes = 0;
    std::uint32_t uv_stride_bytes = 0;
    std::span<const std::byte> y_plane;
    std::span<const std::byte> uv_plane;
    std::int64_t pts = -1;
};

vrrec_status_t CopySystemMemoryNv12FrameToFfmpeg(
    const SystemMemoryNv12FrameView &source,
    AVFrame &destination) noexcept;

}

#endif
