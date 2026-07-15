#ifndef VRRECORDER_NATIVE_VIDEO_ENCODING_PUMP_HPP
#define VRRECORDER_NATIVE_VIDEO_ENCODING_PUMP_HPP

#include <atomic>
#include <chrono>
#include <cstdint>

#include "video_cfr_scheduler.hpp"

namespace vrrecorder::native {

enum class VideoEncoderFailureStage {
    None,
    Processing,
    Encoding,
    Muxing,
};

struct VideoEncoderWrite final {
    vrrec_status_t status;
    std::uint64_t muxed_packet_count;
    std::uint64_t encode_latency_microseconds;
    VideoEncoderFailureStage failure_stage =
        VideoEncoderFailureStage::None;
};

class VideoEncoderSink {
public:
    virtual ~VideoEncoderSink() = default;

    virtual VideoEncoderWrite Write(
        const ScheduledVideoFrame &frame) noexcept = 0;
    virtual VideoEncoderWrite Finish() noexcept = 0;
    virtual void Abort() noexcept = 0;
};

struct VideoFramePreparation final {
    vrrec_status_t status = VRREC_STATUS_OK;
    ScheduledVideoFrame frame {};
};

class VideoFramePreparingEncoderSink : public VideoEncoderSink {
public:
    virtual VideoFramePreparation Prepare(
        const ScheduledVideoFrame &frame) noexcept = 0;
    virtual VideoEncoderWrite WritePrepared(
        const ScheduledVideoFrame &frame) noexcept = 0;

    VideoEncoderWrite Write(
        const ScheduledVideoFrame &frame) noexcept override
    {
        auto preparation = Prepare(frame);
        if (preparation.status != VRREC_STATUS_OK) {
            return {
                preparation.status,
                0,
                0,
                VideoEncoderFailureStage::Processing,
            };
        }

        auto write = WritePrepared(preparation.frame);
        if (write.status != VRREC_STATUS_OK &&
            write.failure_stage == VideoEncoderFailureStage::None) {
            write.failure_stage = VideoEncoderFailureStage::Encoding;
        }
        return write;
    }
};

enum class VideoEncodingResult {
    Submitted,
    NoFrame,
    SurfaceTimeout,
    SurfaceAbandoned,
    SurfaceDeviceRemoved,
    SurfaceDeviceReset,
    SurfaceFailed,
    ProcessorFailed,
    MuxFailed,
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
    VideoEncodingPump(
        VideoCfrScheduler &scheduler,
        VideoFramePreparingEncoderSink &sink,
        std::chrono::milliseconds surface_acquire_timeout =
            std::chrono::milliseconds(5)) noexcept;

    VideoEncodingResult PumpTick(
        std::uint64_t output_tick,
        VideoEncodingRead &read) noexcept;
    VideoEncodingStatistics Statistics() const noexcept;

private:
    VideoCfrScheduler &scheduler_;
    VideoEncoderSink &sink_;
    VideoFramePreparingEncoderSink *preparing_sink_ = nullptr;
    std::chrono::milliseconds surface_acquire_timeout_;
    std::atomic<std::uint64_t> muxed_packet_count_ {0};
    std::atomic<std::uint64_t> latest_encode_latency_microseconds_ {0};
    std::atomic<std::uint64_t> maximum_encode_latency_microseconds_ {0};
    std::atomic_bool first_packet_seen_ = false;
};

}

#endif
