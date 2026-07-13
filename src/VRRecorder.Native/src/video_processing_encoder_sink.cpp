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
    if (aborted_.load() || finished_.load() || !frame.surface) {
        return {
            VRREC_STATUS_INVALID_STATE,
            0,
            0,
            VideoEncoderFailureStage::Processing,
        };
    }

    const auto descriptor = frame.surface->Descriptor();
    VideoProcessingPlan plan {};
    std::optional<vrrec_video_layout_v1> layout;
    {
        const std::lock_guard lock(layout_mutex_);
        layout = layout_;
    }
    const auto plan_status = layout.has_value()
        ? CreateExplicitVideoProcessingPlan(descriptor, *layout, plan)
        : CreateSingleFileVideoProcessingPlan(
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

vrrec_status_t ProcessingVideoEncoderSink::UpdateVideoLayout(
    const vrrec_video_layout_v1 &layout) noexcept
{
    if (aborted_.load() || finished_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (layout.struct_size < sizeof(vrrec_video_layout_v1) ||
        layout.abi_version != VRREC_ABI_V1 ||
        layout.source_width == 0 || layout.source_height == 0 ||
        layout.canvas_width != output_width_ ||
        layout.canvas_height != output_height_ ||
        layout.destination_width == 0 || layout.destination_height == 0 ||
        (layout.destination_width & 1U) != 0 ||
        (layout.destination_height & 1U) != 0 ||
        layout.destination_x > layout.canvas_width ||
        layout.destination_y > layout.canvas_height ||
        layout.destination_width > layout.canvas_width - layout.destination_x ||
        layout.destination_height > layout.canvas_height - layout.destination_y ||
        layout.canvas_background != VRREC_CANVAS_BACKGROUND_BLACK ||
        layout.rotation != VRREC_VIDEO_ROTATION_NONE) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    const std::lock_guard lock(layout_mutex_);
    layout_ = layout;
    return VRREC_STATUS_OK;
}

VideoEncoderWrite ProcessingVideoEncoderSink::Finish() noexcept
{
    if (aborted_.load() || finished_.exchange(true)) {
        return {
            VRREC_STATUS_INVALID_STATE,
            0,
            0,
            VideoEncoderFailureStage::Encoding,
        };
    }
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
