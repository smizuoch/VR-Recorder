#include "audio_timeline_buffer.hpp"

#include <algorithm>
#include <cmath>
#include <limits>

namespace vrrecorder::native {

StereoAudioTimelineBuffer::StereoAudioTimelineBuffer(
    std::size_t capacity_frames)
    : capacity_frames_(IsCapacityValid(capacity_frames)
          ? capacity_frames
          : 0),
      samples_(capacity_frames_ * ChannelCount, 0.0F),
      frame_positions_(capacity_frames_, EmptyFramePosition)
{
}

vrrec_status_t StereoAudioTimelineBuffer::Write(
    std::uint64_t first_frame_position,
    std::span<const float> interleaved_samples) noexcept
{
    const auto frame_count = interleaved_samples.size() / ChannelCount;
    if (capacity_frames_ == 0 || interleaved_samples.empty() ||
        interleaved_samples.size() % ChannelCount != 0 ||
        !FrameRangeIsValid(first_frame_position, frame_count) ||
        !SamplesAreValid(interleaved_samples)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    const std::lock_guard lock(mutex_);
    for (std::size_t frame = 0; frame < frame_count; ++frame) {
        const auto frame_position =
            first_frame_position + static_cast<std::uint64_t>(frame);
        const auto slot = static_cast<std::size_t>(
            frame_position % capacity_frames_);
        const auto source = frame * ChannelCount;
        const auto destination = slot * ChannelCount;
        samples_[destination] = interleaved_samples[source];
        samples_[destination + 1] = interleaved_samples[source + 1];
        frame_positions_[slot] = frame_position;
    }

    return VRREC_STATUS_OK;
}

vrrec_status_t StereoAudioTimelineBuffer::Read(
    std::uint64_t first_frame_position,
    std::size_t frame_count,
    std::span<float> output_interleaved) const noexcept
{
    if (capacity_frames_ == 0 || frame_count == 0 ||
        frame_count > std::numeric_limits<std::size_t>::max() / ChannelCount ||
        output_interleaved.size() != frame_count * ChannelCount ||
        !FrameRangeIsValid(first_frame_position, frame_count)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    const std::lock_guard lock(mutex_);
    std::fill(output_interleaved.begin(), output_interleaved.end(), 0.0F);
    for (std::size_t frame = 0; frame < frame_count; ++frame) {
        const auto frame_position =
            first_frame_position + static_cast<std::uint64_t>(frame);
        const auto slot = static_cast<std::size_t>(
            frame_position % capacity_frames_);
        if (frame_positions_[slot] != frame_position) {
            continue;
        }

        const auto source = slot * ChannelCount;
        const auto destination = frame * ChannelCount;
        output_interleaved[destination] = samples_[source];
        output_interleaved[destination + 1] = samples_[source + 1];
    }

    return VRREC_STATUS_OK;
}

bool StereoAudioTimelineBuffer::IsCapacityValid(
    std::size_t capacity_frames) noexcept
{
    return capacity_frames != 0 &&
           capacity_frames <=
               std::numeric_limits<std::size_t>::max() / ChannelCount;
}

bool StereoAudioTimelineBuffer::SamplesAreValid(
    std::span<const float> samples) noexcept
{
    return std::all_of(
        samples.begin(),
        samples.end(),
        [](float sample) {
            return std::isfinite(sample);
        });
}

bool StereoAudioTimelineBuffer::FrameRangeIsValid(
    std::uint64_t first_frame_position,
    std::size_t frame_count) noexcept
{
    if (frame_count == 0 ||
        frame_count > std::numeric_limits<std::uint64_t>::max()) {
        return false;
    }

    const auto count = static_cast<std::uint64_t>(frame_count);
    return first_frame_position <=
           std::numeric_limits<std::uint64_t>::max() - (count - 1);
}

}
