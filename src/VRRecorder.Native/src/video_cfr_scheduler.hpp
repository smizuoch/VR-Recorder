#ifndef VRRECORDER_NATIVE_VIDEO_CFR_SCHEDULER_HPP
#define VRRECORDER_NATIVE_VIDEO_CFR_SCHEDULER_HPP

#include <cstdint>
#include <mutex>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

struct SourceVideoFrame final {
    std::uint64_t sequence;
    std::int64_t timestamp_microseconds;
};

struct ScheduledVideoFrame final {
    std::uint64_t output_tick = 0;
    std::uint64_t source_sequence = 0;
    std::int64_t source_timestamp_microseconds = 0;
    std::uint64_t dropped_before_output = 0;
    bool duplicated = false;
};

enum class VideoScheduleResult {
    Ready,
    NoFrame,
    InvalidTick,
    Failed,
};

struct VideoCfrStatistics final {
    std::uint64_t source_frame_count;
    std::uint64_t output_frame_count;
    std::uint64_t dropped_source_frame_count;
    std::uint64_t duplicated_output_frame_count;
};

class VideoCfrScheduler final {
public:
    vrrec_status_t Push(const SourceVideoFrame &frame) noexcept;
    VideoScheduleResult Schedule(
        std::uint64_t output_tick,
        ScheduledVideoFrame &output) noexcept;
    VideoCfrStatistics Statistics() const noexcept;

private:
    mutable std::mutex mutex_;
    SourceVideoFrame pending_ {};
    SourceVideoFrame last_output_ {};
    std::uint64_t last_output_tick_ = 0;
    std::uint64_t source_frame_count_ = 0;
    std::uint64_t output_frame_count_ = 0;
    std::uint64_t dropped_source_frame_count_ = 0;
    std::uint64_t duplicated_output_frame_count_ = 0;
    std::uint64_t dropped_before_next_output_ = 0;
    std::uint64_t last_source_sequence_ = 0;
    std::int64_t last_source_timestamp_microseconds_ = 0;
    bool has_pending_ = false;
    bool has_output_ = false;
    bool has_output_tick_ = false;
    bool has_source_ = false;
};

}

#endif
