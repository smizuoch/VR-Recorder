#ifndef VRRECORDER_NATIVE_AUDIO_CAPTURE_TIMELINE_HPP
#define VRRECORDER_NATIVE_AUDIO_CAPTURE_TIMELINE_HPP

#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <mutex>
#include <span>

#include "audio_timeline_buffer.hpp"

namespace vrrecorder::native {

struct CaptureClockAnchor final {
    std::uint64_t device_position;
    std::int64_t qpc_ticks;
    std::uint64_t qpc_frequency;
};

struct NormalizedStereoPacket final {
    std::uint64_t start_frame_48k;
    CaptureClockAnchor clock;
    std::span<const float> interleaved;
    bool discontinuity;
};

enum class AudioTimelineResult {
    Ready,
    Aborted,
    InvalidPacket,
    Discontinuity,
    Overrun,
};

struct AudioTimelineRead final {
    std::uint64_t start_frame_48k;
    bool input_available;
    bool underrun;
};

class StereoCaptureTimeline final {
public:
    explicit StereoCaptureTimeline(std::size_t capacity_frames);

    AudioTimelineResult Push(
        const NormalizedStereoPacket &packet) noexcept;

    AudioTimelineResult WaitRead(
        std::size_t frame_count,
        std::span<float> output_interleaved,
        AudioTimelineRead &read) noexcept;

    AudioTimelineResult SetAvailable(
        bool available,
        std::uint64_t effective_frame_48k) noexcept;

    void Abort() noexcept;

    std::size_t BufferedFrames() const noexcept;
    std::uint64_t FramePosition() const noexcept;

private:
    static bool TryGetFrameCount(
        std::span<const float> samples,
        std::size_t &frame_count) noexcept;
    static bool TryAddFrames(
        std::uint64_t position,
        std::size_t frame_count,
        std::uint64_t &end_position) noexcept;

    mutable std::mutex mutex_;
    std::condition_variable data_available_;
    StereoAudioTimelineBuffer buffer_;
    std::size_t capacity_frames_;
    std::uint64_t read_position_ = 0;
    std::uint64_t write_end_position_ = 0;
    CaptureClockAnchor last_clock_ {};
    bool has_packet_ = false;
    bool input_available_ = true;
    std::uint64_t unavailable_from_ = 0;
    bool aborted_ = false;
};

}

#endif
