#include "audio_capture_timeline.hpp"

#include <algorithm>
#include <limits>

namespace vrrecorder::native {

StereoCaptureTimeline::StereoCaptureTimeline(
    std::size_t capacity_frames)
    : buffer_(capacity_frames),
      capacity_frames_(capacity_frames)
{
}

AudioTimelineResult StereoCaptureTimeline::Push(
    const NormalizedStereoPacket &packet) noexcept
{
    std::size_t frame_count = 0;
    std::uint64_t end_position = 0;
    if (!TryGetFrameCount(packet.interleaved, frame_count) ||
        !TryAddFrames(packet.start_frame_48k, frame_count, end_position) ||
        packet.clock.qpc_ticks < 0 || packet.clock.qpc_frequency == 0) {
        return AudioTimelineResult::InvalidPacket;
    }

    if (packet.discontinuity) {
        return AudioTimelineResult::Discontinuity;
    }

    const std::lock_guard lock(mutex_);
    if (aborted_) {
        return AudioTimelineResult::Aborted;
    }

    if ((has_packet_ &&
         (packet.start_frame_48k < write_end_position_ ||
          packet.clock.device_position < last_clock_.device_position ||
          packet.clock.qpc_ticks < last_clock_.qpc_ticks ||
          packet.clock.qpc_frequency != last_clock_.qpc_frequency)) ||
        packet.start_frame_48k < read_position_ ||
        (!input_available_ && end_position > unavailable_from_)) {
        return AudioTimelineResult::InvalidPacket;
    }

    if (end_position - read_position_ > capacity_frames_) {
        return AudioTimelineResult::Overrun;
    }

    const auto status = buffer_.Write(
        packet.start_frame_48k,
        packet.interleaved);
    if (status != VRREC_STATUS_OK) {
        return AudioTimelineResult::InvalidPacket;
    }

    write_end_position_ = end_position;
    last_clock_ = packet.clock;
    has_packet_ = true;
    data_available_.notify_one();
    return AudioTimelineResult::Ready;
}

AudioTimelineResult StereoCaptureTimeline::WaitRead(
    std::size_t frame_count,
    std::span<float> output_interleaved,
    AudioTimelineRead &read) noexcept
{
    std::uint64_t end_position = 0;
    if (frame_count == 0 ||
        frame_count >
            std::numeric_limits<std::size_t>::max() /
                StereoAudioTimelineBuffer::ChannelCount ||
        output_interleaved.size() !=
            frame_count * StereoAudioTimelineBuffer::ChannelCount) {
        return AudioTimelineResult::InvalidPacket;
    }

    std::unique_lock lock(mutex_);
    if (!TryAddFrames(read_position_, frame_count, end_position)) {
        return AudioTimelineResult::InvalidPacket;
    }

    data_available_.wait(lock, [&] {
        return aborted_ ||
               (has_packet_ && write_end_position_ >= end_position) ||
               (!input_available_ && unavailable_from_ < end_position);
    });

    if (aborted_) {
        return AudioTimelineResult::Aborted;
    }

    const auto start_position = read_position_;
    bool had_missing_frames = false;
    if (buffer_.Read(
            start_position,
            frame_count,
            output_interleaved,
            had_missing_frames) != VRREC_STATUS_OK) {
        return AudioTimelineResult::InvalidPacket;
    }

    const auto includes_unavailable_frames =
        !input_available_ && unavailable_from_ < end_position;
    if (includes_unavailable_frames) {
        const auto first_silent_frame = unavailable_from_ <= start_position
            ? 0U
            : static_cast<std::size_t>(
                  unavailable_from_ - start_position);
        const auto first_silent_sample = first_silent_frame *
            StereoAudioTimelineBuffer::ChannelCount;
        std::fill(
            output_interleaved.begin() +
                static_cast<std::ptrdiff_t>(first_silent_sample),
            output_interleaved.end(),
            0.0F);
    }

    read = AudioTimelineRead {
        start_position,
        !includes_unavailable_frames,
        had_missing_frames || includes_unavailable_frames,
    };
    read_position_ = end_position;

    return AudioTimelineResult::Ready;
}

AudioTimelineResult StereoCaptureTimeline::SetAvailable(
    bool available,
    std::uint64_t effective_frame_48k) noexcept
{
    {
        const std::lock_guard lock(mutex_);
        if (aborted_) {
            return AudioTimelineResult::Aborted;
        }

        if (effective_frame_48k < read_position_) {
            return AudioTimelineResult::InvalidPacket;
        }

        if (available) {
            input_available_ = true;
        } else {
            if (!input_available_ &&
                effective_frame_48k != unavailable_from_) {
                return AudioTimelineResult::InvalidPacket;
            }

            input_available_ = false;
            unavailable_from_ = effective_frame_48k;
        }
    }

    data_available_.notify_all();
    return AudioTimelineResult::Ready;
}

void StereoCaptureTimeline::Abort() noexcept
{
    {
        const std::lock_guard lock(mutex_);
        aborted_ = true;
    }

    data_available_.notify_all();
}

std::size_t StereoCaptureTimeline::BufferedFrames() const noexcept
{
    const std::lock_guard lock(mutex_);
    if (!has_packet_ || write_end_position_ <= read_position_) {
        return 0;
    }

    return static_cast<std::size_t>(
        write_end_position_ - read_position_);
}

std::uint64_t StereoCaptureTimeline::FramePosition() const noexcept
{
    const std::lock_guard lock(mutex_);
    return read_position_;
}

bool StereoCaptureTimeline::TryGetFrameCount(
    std::span<const float> samples,
    std::size_t &frame_count) noexcept
{
    if (samples.empty() ||
        samples.size() % StereoAudioTimelineBuffer::ChannelCount != 0) {
        return false;
    }

    frame_count = samples.size() /
                  StereoAudioTimelineBuffer::ChannelCount;
    return true;
}

bool StereoCaptureTimeline::TryAddFrames(
    std::uint64_t position,
    std::size_t frame_count,
    std::uint64_t &end_position) noexcept
{
    if (frame_count == 0 ||
        frame_count > std::numeric_limits<std::uint64_t>::max()) {
        return false;
    }

    const auto count = static_cast<std::uint64_t>(frame_count);
    if (position > std::numeric_limits<std::uint64_t>::max() - count) {
        return false;
    }

    end_position = position + count;
    return true;
}

}
