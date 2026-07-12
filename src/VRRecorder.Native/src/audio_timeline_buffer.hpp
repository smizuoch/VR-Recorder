#ifndef VRRECORDER_NATIVE_AUDIO_TIMELINE_BUFFER_HPP
#define VRRECORDER_NATIVE_AUDIO_TIMELINE_BUFFER_HPP

#include <cstddef>
#include <cstdint>
#include <mutex>
#include <span>
#include <vector>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

class StereoAudioTimelineBuffer final {
public:
    static constexpr std::size_t ChannelCount = 2;

    explicit StereoAudioTimelineBuffer(std::size_t capacity_frames);

    vrrec_status_t Write(
        std::uint64_t first_frame_position,
        std::span<const float> interleaved_samples) noexcept;

    vrrec_status_t Read(
        std::uint64_t first_frame_position,
        std::size_t frame_count,
        std::span<float> output_interleaved) const noexcept;

private:
    static constexpr std::uint64_t EmptyFramePosition =
        UINT64_MAX;

    static bool IsCapacityValid(std::size_t capacity_frames) noexcept;
    static bool SamplesAreValid(std::span<const float> samples) noexcept;
    static bool FrameRangeIsValid(
        std::uint64_t first_frame_position,
        std::size_t frame_count) noexcept;

    mutable std::mutex mutex_;
    std::size_t capacity_frames_ = 0;
    std::vector<float> samples_;
    std::vector<std::uint64_t> frame_positions_;
};

}

#endif
