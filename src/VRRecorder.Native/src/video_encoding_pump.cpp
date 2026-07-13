#include "video_encoding_pump.hpp"

#include <algorithm>
#include <limits>

namespace vrrecorder::native {

VideoEncodingPump::VideoEncodingPump(
    VideoCfrScheduler &scheduler,
    VideoEncoderSink &sink,
    std::chrono::milliseconds surface_acquire_timeout) noexcept
    : scheduler_(scheduler),
      sink_(sink),
      surface_acquire_timeout_(surface_acquire_timeout)
{
}

VideoEncodingResult VideoEncodingPump::PumpTick(
    std::uint64_t output_tick,
    VideoEncodingRead &read) noexcept
{
    read = {};
    ScheduledVideoFrame scheduled {};
    const auto schedule_result = scheduler_.Schedule(
        output_tick,
        scheduled);
    if (schedule_result == VideoScheduleResult::NoFrame) {
        return VideoEncodingResult::NoFrame;
    }

    if (schedule_result == VideoScheduleResult::InvalidTick) {
        return VideoEncodingResult::InvalidTick;
    }

    if (schedule_result != VideoScheduleResult::Ready) {
        return VideoEncodingResult::Failed;
    }

    read.scheduled = scheduled;
    if (scheduled.surface) {
        const auto acquire = scheduled.surface->AcquireForRead(
            surface_acquire_timeout_);
        if (acquire == VideoSurfaceAcquireResult::Timeout) {
            read = {};
            return VideoEncodingResult::SurfaceTimeout;
        }
        if (acquire != VideoSurfaceAcquireResult::Acquired) {
            read = {};
            return VideoEncodingResult::SurfaceFailed;
        }
    }

    const auto write = sink_.Write(scheduled);
    if (scheduled.surface) {
        scheduled.surface->ReleaseFromRead();
    }
    read = {
        scheduled,
        write.muxed_packet_count,
        write.encode_latency_microseconds,
        write.status,
        false,
    };
    if (write.status != VRREC_STATUS_OK) {
        if (write.failure_stage == VideoEncoderFailureStage::Processing) {
            return VideoEncodingResult::ProcessorFailed;
        }
        if (write.failure_stage == VideoEncoderFailureStage::Muxing) {
            return VideoEncodingResult::MuxFailed;
        }
        return VideoEncodingResult::EncoderFailed;
    }

    const auto muxed = muxed_packet_count_.load();
    if (muxed > std::numeric_limits<std::uint64_t>::max() -
                    write.muxed_packet_count) {
        return VideoEncodingResult::Failed;
    }

    auto first_packet = false;
    if (write.muxed_packet_count > 0 &&
        !first_packet_seen_.exchange(true)) {
        first_packet = true;
    }

    muxed_packet_count_.store(muxed + write.muxed_packet_count);
    latest_encode_latency_microseconds_.store(
        write.encode_latency_microseconds);
    const auto maximum = maximum_encode_latency_microseconds_.load();
    maximum_encode_latency_microseconds_.store(std::max(
        maximum,
        write.encode_latency_microseconds));
    read.first_packet_muxed = first_packet;
    return VideoEncodingResult::Submitted;
}

VideoEncodingStatistics VideoEncodingPump::Statistics() const noexcept
{
    return {
        scheduler_.Statistics(),
        muxed_packet_count_.load(),
        latest_encode_latency_microseconds_.load(),
        maximum_encode_latency_microseconds_.load(),
    };
}

}
