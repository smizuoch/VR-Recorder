#include "ffmpeg_h264_nv12_frame.hpp"

#include <cerrno>
#include <cstddef>
#include <cstring>
#include <limits>

extern "C" {
#include <libavutil/error.h>
#include <libavutil/frame.h>
#include <libavutil/pixfmt.h>
}

namespace vrrecorder::native {
namespace {

bool TryGetRequiredPlaneBytes(
    std::uint32_t active_row_bytes,
    std::uint32_t row_count,
    std::uint32_t stride_bytes,
    std::size_t &required_bytes) noexcept
{
    required_bytes = 0;
    if (active_row_bytes == 0 || row_count == 0 ||
        stride_bytes < active_row_bytes) {
        return false;
    }

    const auto preceding_rows = static_cast<std::size_t>(row_count - 1U);
    const auto stride = static_cast<std::size_t>(stride_bytes);
    const auto active = static_cast<std::size_t>(active_row_bytes);
    if (preceding_rows != 0 &&
        stride > (std::numeric_limits<std::size_t>::max() - active) /
            preceding_rows) {
        return false;
    }
    required_bytes = preceding_rows * stride + active;
    return true;
}

bool IsDestinationCompatible(
    const SystemMemoryNv12FrameView &source,
    const AVFrame &destination) noexcept
{
    return destination.format == AV_PIX_FMT_NV12 &&
        destination.width == static_cast<int>(source.width) &&
        destination.height == static_cast<int>(source.height) &&
        destination.data[0] != nullptr && destination.data[1] != nullptr &&
        destination.linesize[0] >= static_cast<int>(source.width) &&
        destination.linesize[1] >= static_cast<int>(source.width);
}

}

vrrec_status_t CopySystemMemoryNv12FrameToFfmpeg(
    const SystemMemoryNv12FrameView &source,
    AVFrame &destination) noexcept
{
    constexpr std::uint32_t maximum_dimension = 16'384;
    if (source.width == 0 || source.height == 0 ||
        source.width > maximum_dimension ||
        source.height > maximum_dimension ||
        (source.width & 1U) != 0 || (source.height & 1U) != 0 ||
        source.pts < 0) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    std::size_t required_y_bytes = 0;
    std::size_t required_uv_bytes = 0;
    if (!TryGetRequiredPlaneBytes(
            source.width,
            source.height,
            source.y_stride_bytes,
            required_y_bytes) ||
        !TryGetRequiredPlaneBytes(
            source.width,
            source.height / 2U,
            source.uv_stride_bytes,
            required_uv_bytes) ||
        source.y_plane.size() < required_y_bytes ||
        source.uv_plane.size() < required_uv_bytes ||
        !IsDestinationCompatible(source, destination)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    const auto writable_result = av_frame_make_writable(&destination);
    if (writable_result < 0) {
        return writable_result == AVERROR(ENOMEM)
            ? VRREC_STATUS_OUT_OF_MEMORY
            : VRREC_STATUS_INTERNAL_ERROR;
    }
    if (!IsDestinationCompatible(source, destination)) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }

    for (std::uint32_t row = 0; row < source.height; ++row) {
        std::memcpy(
            destination.data[0] +
                static_cast<std::size_t>(row) * destination.linesize[0],
            source.y_plane.data() +
                static_cast<std::size_t>(row) * source.y_stride_bytes,
            source.width);
    }
    for (std::uint32_t row = 0; row < source.height / 2U; ++row) {
        std::memcpy(
            destination.data[1] +
                static_cast<std::size_t>(row) * destination.linesize[1],
            source.uv_plane.data() +
                static_cast<std::size_t>(row) * source.uv_stride_bytes,
            source.width);
    }
    destination.pts = source.pts;
    return VRREC_STATUS_OK;
}

}
