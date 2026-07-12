#include "audio_capture_timeline.hpp"

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
    if ((has_packet_ &&
         (packet.start_frame_48k < write_end_position_ ||
          packet.clock.device_position < last_clock_.device_position ||
          packet.clock.qpc_ticks < last_clock_.qpc_ticks ||
          packet.clock.qpc_frequency != last_clock_.qpc_frequency)) ||
        packet.start_frame_48k < read_position_) {
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

    has_gap_ = has_gap_ || packet.start_frame_48k > write_end_position_;
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
        return has_packet_ && write_end_position_ >= end_position;
    });

    const auto start_position = read_position_;
    if (buffer_.Read(
            start_position,
            frame_count,
            output_interleaved) != VRREC_STATUS_OK) {
        return AudioTimelineResult::InvalidPacket;
    }

    read = AudioTimelineRead {
        start_position,
        true,
        has_gap_,
    };
    read_position_ = end_position;
    if (read_position_ >= write_end_position_) {
        has_gap_ = false;
    }

    return AudioTimelineResult::Ready;
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
