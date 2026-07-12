#ifndef VRRECORDER_NATIVE_VIDEO_ENCODING_PUMP_HPP
#define VRRECORDER_NATIVE_VIDEO_ENCODING_PUMP_HPP

#include <atomic>
#include <chrono>
#include <cstdint>

#include "video_cfr_scheduler.hpp"

namespace vrrecorder::native {

struct VideoEncoderWrite final {
    vrrec_status_t status;
    std::uint64_t muxed_packet_count;
    std::uint64_t encode_latency_microseconds;
};

class VideoEncoderSink {
public:
    virtual ~VideoEncoderSink() = default;

    virtual VideoEncoderWrite Write(
        const ScheduledVideoFrame &frame) noexcept = 0;
    virtual VideoEncoderWrite Finish() noexcept = 0;
    virtual void Abort() noexcept = 0;
};

enum class VideoEncodingResult {
    Submitted,
    NoFrame,
    SurfaceTimeout,
    SurfaceFailed,
    InvalidTick,
    EncoderFailed,
    Failed,
};

struct VideoEncodingRead final {
    ScheduledVideoFrame scheduled {};
    std::uint64_t muxed_packet_count = 0;
    std::uint64_t encode_latency_microseconds = 0;
    vrrec_status_t encoder_status = VRREC_STATUS_OK;
    bool first_packet_muxed = false;
};

struct VideoEncodingStatistics final {
    VideoCfrStatistics scheduler {};
    std::uint64_t muxed_packet_count = 0;
    std::uint64_t latest_encode_latency_microseconds = 0;
    std::uint64_t maximum_encode_latency_microseconds = 0;
};

class VideoEncodingPump final {
public:
    VideoEncodingPump(
        VideoCfrScheduler &scheduler,
        VideoEncoderSink &sink,
        std::chrono::milliseconds surface_acquire_timeout =
            std::chrono::milliseconds(5)) noexcept;

    VideoEncodingResult PumpTick(
        std::uint64_t output_tick,
        VideoEncodingRead &read) noexcept;
    VideoEncodingStatistics Statistics() const noexcept;

private:
    VideoCfrScheduler &scheduler_;
    VideoEncoderSink &sink_;
    std::chrono::milliseconds surface_acquire_timeout_;
    std::atomic<std::uint64_t> muxed_packet_count_ {0};
    std::atomic<std::uint64_t> latest_encode_latency_microseconds_ {0};
    std::atomic<std::uint64_t> maximum_encode_latency_microseconds_ {0};
    std::atomic_bool first_packet_seen_ = false;
};

}

#endif
