#include "video_cfr_scheduler.hpp"

#include <limits>

namespace vrrecorder::native {

vrrec_status_t VideoCfrScheduler::Push(
    const SourceVideoFrame &frame) noexcept
{
    const std::lock_guard lock(mutex_);
    if (frame.timestamp_microseconds < 0 ||
        (has_source_ &&
         (frame.sequence <= last_source_sequence_ ||
          frame.timestamp_microseconds <
              last_source_timestamp_microseconds_)) ||
        source_frame_count_ ==
            std::numeric_limits<std::uint64_t>::max()) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    if (has_pending_) {
        if (dropped_source_frame_count_ ==
                std::numeric_limits<std::uint64_t>::max() ||
            dropped_before_next_output_ ==
                std::numeric_limits<std::uint64_t>::max()) {
            return VRREC_STATUS_INVALID_STATE;
        }

        ++dropped_source_frame_count_;
        ++dropped_before_next_output_;
    }

    pending_ = frame;
    has_pending_ = true;
    has_source_ = true;
    last_source_sequence_ = frame.sequence;
    last_source_timestamp_microseconds_ = frame.timestamp_microseconds;
    ++source_frame_count_;
    return VRREC_STATUS_OK;
}

VideoScheduleResult VideoCfrScheduler::Schedule(
    std::uint64_t output_tick,
    ScheduledVideoFrame &output) noexcept
{
    output = ScheduledVideoFrame {};
    const std::lock_guard lock(mutex_);
    if (has_output_tick_ && output_tick <= last_output_tick_) {
        return VideoScheduleResult::InvalidTick;
    }

    has_output_tick_ = true;
    last_output_tick_ = output_tick;
    if (!has_pending_ && !has_output_) {
        return VideoScheduleResult::NoFrame;
    }

    if (output_frame_count_ ==
        std::numeric_limits<std::uint64_t>::max()) {
        return VideoScheduleResult::Failed;
    }

    if (has_pending_) {
        last_output_ = pending_;
        has_pending_ = false;
        has_output_ = true;
        output = {
            output_tick,
            last_output_.sequence,
            last_output_.timestamp_microseconds,
            dropped_before_next_output_,
            false,
            last_output_.surface,
        };
        dropped_before_next_output_ = 0;
    } else {
        if (duplicated_output_frame_count_ ==
            std::numeric_limits<std::uint64_t>::max()) {
            return VideoScheduleResult::Failed;
        }

        ++duplicated_output_frame_count_;
        output = {
            output_tick,
            last_output_.sequence,
            last_output_.timestamp_microseconds,
            0,
            true,
            last_output_.surface,
        };
    }

    ++output_frame_count_;
    return VideoScheduleResult::Ready;
}

VideoCfrStatistics VideoCfrScheduler::Statistics() const noexcept
{
    const std::lock_guard lock(mutex_);
    return {
        source_frame_count_,
        output_frame_count_,
        dropped_source_frame_count_,
        duplicated_output_frame_count_,
    };
}

}
