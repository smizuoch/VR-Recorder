#include "encoder_probe_synthetic_nv12.hpp"

#include <cstddef>
#include <cstdint>
#include <limits>
#include <new>
#include <utility>

#include "encoder_probe_identity.hpp"

namespace vrrecorder::native {
namespace {

constexpr std::uint32_t MaximumDimension = 16'384;

std::byte Luma(
    std::uint32_t index,
    std::uint32_t x,
    std::uint32_t y) noexcept
{
    return static_cast<std::byte>(
        16U + (x * 7U + y * 13U + index * 17U) % 220U);
}

std::byte ChromaU(
    std::uint32_t index,
    std::uint32_t pair,
    std::uint32_t y) noexcept
{
    return static_cast<std::byte>(
        16U + (pair * 11U + y * 5U + index * 19U) % 225U);
}

std::byte ChromaV(
    std::uint32_t index,
    std::uint32_t pair,
    std::uint32_t y) noexcept
{
    return static_cast<std::byte>(
        16U + (pair * 3U + y * 17U + index * 23U) % 225U);
}

}

SystemMemoryNv12FrameView OwnedEncoderProbeNv12Frame::View() const noexcept
{
    return {
        width_,
        height_,
        width_,
        width_,
        y_plane_,
        uv_plane_,
        codec_pts_,
    };
}

vrrec_status_t CreateEncoderProbeSyntheticNv12Frame(
    const EncoderProbeSyntheticFrame &frame,
    OwnedEncoderProbeNv12Frame &output) noexcept
{
    if (frame.frame_index >= EncoderProbeSyntheticFrameCount ||
        frame.width == 0 || frame.width > MaximumDimension ||
        (frame.width & 1U) != 0 || frame.height == 0 ||
        frame.height > MaximumDimension || (frame.height & 1U) != 0 ||
        frame.pts_microseconds < 0 || frame.duration_microseconds <= 0 ||
        frame.pts_microseconds >
            std::numeric_limits<std::int64_t>::max() -
                frame.duration_microseconds) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    try {
        const auto y_size = static_cast<std::size_t>(frame.width) *
            static_cast<std::size_t>(frame.height);
        const auto uv_size = y_size / 2U;
        OwnedEncoderProbeNv12Frame created;
        created.width_ = frame.width;
        created.height_ = frame.height;
        created.codec_pts_ = frame.frame_index;
        created.y_plane_.resize(y_size);
        created.uv_plane_.resize(uv_size);

        for (std::uint32_t y = 0; y < frame.height; ++y) {
            for (std::uint32_t x = 0; x < frame.width; ++x) {
                created.y_plane_[
                    static_cast<std::size_t>(y) * frame.width + x] =
                    Luma(frame.frame_index, x, y);
            }
        }
        for (std::uint32_t y = 0; y < frame.height / 2U; ++y) {
            for (std::uint32_t pair = 0;
                 pair < frame.width / 2U;
                 ++pair) {
                const auto offset = static_cast<std::size_t>(y) *
                    frame.width + pair * 2U;
                created.uv_plane_[offset] =
                    ChromaU(frame.frame_index, pair, y);
                created.uv_plane_[offset + 1U] =
                    ChromaV(frame.frame_index, pair, y);
            }
        }
        output = std::move(created);
        return VRREC_STATUS_OK;
    } catch (const std::bad_alloc &) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

}
