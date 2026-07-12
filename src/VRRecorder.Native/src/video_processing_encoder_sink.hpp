#ifndef VRRECORDER_NATIVE_VIDEO_PROCESSING_ENCODER_SINK_HPP
#define VRRECORDER_NATIVE_VIDEO_PROCESSING_ENCODER_SINK_HPP

#include <atomic>
#include <cstdint>
#include <memory>

#include "video_encoding_pump.hpp"
#include "video_processing_plan.hpp"

namespace vrrecorder::native {

class VideoFrameProcessor {
public:
    virtual ~VideoFrameProcessor() = default;

    virtual vrrec_status_t Process(
        const std::shared_ptr<VideoSurface> &source,
        const VideoProcessingPlan &plan,
        std::shared_ptr<VideoSurface> &output) noexcept = 0;
    virtual void Abort() noexcept = 0;
};

class ProcessingVideoEncoderSink final : public VideoEncoderSink {
public:
    ProcessingVideoEncoderSink(
        VideoFrameProcessor &processor,
        VideoEncoderSink &encoder,
        std::uint32_t output_width,
        std::uint32_t output_height) noexcept;

    VideoEncoderWrite Write(
        const ScheduledVideoFrame &frame) noexcept override;
    VideoEncoderWrite Finish() noexcept override;
    void Abort() noexcept override;

private:
    bool IsOutputValid(
        const std::shared_ptr<VideoSurface> &surface,
        std::uint64_t adapter_luid) const noexcept;

    VideoFrameProcessor &processor_;
    VideoEncoderSink &encoder_;
    std::uint32_t output_width_;
    std::uint32_t output_height_;
    std::atomic_bool aborted_ = false;
};

}

#endif
