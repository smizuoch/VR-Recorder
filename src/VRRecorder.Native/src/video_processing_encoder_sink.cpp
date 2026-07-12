#include "video_processing_encoder_sink.hpp"

#include <utility>

namespace vrrecorder::native {

ProcessingVideoEncoderSink::ProcessingVideoEncoderSink(
    VideoFrameProcessor &processor,
    VideoEncoderSink &encoder,
    std::uint32_t output_width,
    std::uint32_t output_height) noexcept
    : processor_(processor),
      encoder_(encoder),
      output_width_(output_width),
      output_height_(output_height)
{
}

VideoEncoderWrite ProcessingVideoEncoderSink::Write(
    const ScheduledVideoFrame &frame) noexcept
{
    if (aborted_.load() || !frame.surface) {
        return {
            VRREC_STATUS_INVALID_STATE,
            0,
            0,
            VideoEncoderFailureStage::Processing,
        };
    }

    const auto descriptor = frame.surface->Descriptor();
    VideoProcessingPlan plan {};
    const auto plan_status = CreateSingleFileVideoProcessingPlan(
        descriptor,
        output_width_,
        output_height_,
        plan);
    if (plan_status != VRREC_STATUS_OK) {
        return {plan_status, 0, 0, VideoEncoderFailureStage::Processing};
    }

    std::shared_ptr<VideoSurface> output;
    const auto processing_status = processor_.Process(
        frame.surface,
        plan,
        output);
    if (processing_status != VRREC_STATUS_OK) {
        return {
            processing_status,
            0,
            0,
            VideoEncoderFailureStage::Processing,
        };
    }

    if (!IsOutputValid(output, descriptor.adapter_luid)) {
        return {
            VRREC_STATUS_INTERNAL_ERROR,
            0,
            0,
            VideoEncoderFailureStage::Processing,
        };
    }

    auto processed = frame;
    processed.surface = std::move(output);
    auto write = encoder_.Write(processed);
    if (write.status != VRREC_STATUS_OK &&
        write.failure_stage == VideoEncoderFailureStage::None) {
        write.failure_stage = VideoEncoderFailureStage::Encoding;
    }
    return write;
}

VideoEncoderWrite ProcessingVideoEncoderSink::Finish() noexcept
{
    return encoder_.Finish();
}

void ProcessingVideoEncoderSink::Abort() noexcept
{
    if (aborted_.exchange(true)) {
        return;
    }

    processor_.Abort();
    encoder_.Abort();
}

bool ProcessingVideoEncoderSink::IsOutputValid(
    const std::shared_ptr<VideoSurface> &surface,
    std::uint64_t adapter_luid) const noexcept
{
    if (!surface || surface->NativeHandle() == nullptr) {
        return false;
    }

    const auto descriptor = surface->Descriptor();
    return descriptor.adapter_luid == adapter_luid &&
           descriptor.width == output_width_ &&
           descriptor.height == output_height_ &&
           descriptor.pixel_format == VRREC_SOURCE_PIXEL_FORMAT_NV12;
}

}
