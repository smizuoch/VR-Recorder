#include "d3d11_nv12_frame_mapper.hpp"

#include <cstddef>
#include <cstdint>
#include <limits>

namespace vrrecorder::native {
namespace {

constexpr std::uint32_t maximum_dimension = 16'384;

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

bool IsSurfaceValid(const std::shared_ptr<VideoSurface> &surface) noexcept
{
    if (!surface || surface->NativeHandle() == nullptr) {
        return false;
    }
    const auto descriptor = surface->Descriptor();
    return descriptor.adapter_luid != 0 &&
        descriptor.width != 0 && descriptor.width <= maximum_dimension &&
        descriptor.height != 0 && descriptor.height <= maximum_dimension &&
        (descriptor.width & 1U) == 0 &&
        (descriptor.height & 1U) == 0 &&
        descriptor.pixel_format == VRREC_SOURCE_PIXEL_FORMAT_NV12 &&
        descriptor.generation_id != 0;
}

bool IsMappingValid(
    const VideoSurfaceDescriptor &descriptor,
    const SystemMemoryNv12FrameView &view) noexcept
{
    if (view.width != descriptor.width ||
        view.height != descriptor.height) {
        return false;
    }

    std::size_t required_y_bytes = 0;
    std::size_t required_uv_bytes = 0;
    return TryGetRequiredPlaneBytes(
               view.width,
               view.height,
               view.y_stride_bytes,
               required_y_bytes) &&
        TryGetRequiredPlaneBytes(
               view.width,
               view.height / 2U,
               view.uv_stride_bytes,
               required_uv_bytes) &&
        view.y_plane.size() >= required_y_bytes &&
        view.uv_plane.size() >= required_uv_bytes;
}

}

D3d11SystemMemoryNv12FrameMapper::D3d11SystemMemoryNv12FrameMapper(
    D3d11Nv12ReadbackPort &port) noexcept
    : port_(port)
{
}

SystemMemoryNv12FrameMapResult
D3d11SystemMemoryNv12FrameMapper::Map(
    const ScheduledVideoFrame &frame) noexcept
{
    if (aborted_.load(std::memory_order_acquire)) {
        return {VRREC_STATUS_INVALID_STATE, {}};
    }
    if (!IsSurfaceValid(frame.surface)) {
        return {VRREC_STATUS_INVALID_ARGUMENT, {}};
    }

    auto result = port_.Read(frame.surface);
    if (aborted_.load(std::memory_order_acquire)) {
        return {VRREC_STATUS_INVALID_STATE, {}};
    }
    if (result.status != VRREC_STATUS_OK) {
        if (result.mapping != nullptr) {
            return {VRREC_STATUS_INTERNAL_ERROR, {}};
        }
        return result;
    }
    if (result.mapping == nullptr ||
        !IsMappingValid(
            frame.surface->Descriptor(),
            result.mapping->View())) {
        return {VRREC_STATUS_INTERNAL_ERROR, {}};
    }

    return result;
}

void D3d11SystemMemoryNv12FrameMapper::Abort() noexcept
{
    if (!aborted_.exchange(true, std::memory_order_acq_rel)) {
        port_.Abort();
    }
}

}
