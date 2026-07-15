#include "d3d11_video_frame_processor.hpp"

#include <cstdint>

namespace vrrecorder::native {

D3d11VideoFrameProcessor::D3d11VideoFrameProcessor(
    D3d11VideoProcessorPort &port) noexcept
    : port_(port)
{
}

D3d11VideoFrameProcessor::~D3d11VideoFrameProcessor()
{
    Abort();
}

vrrec_status_t D3d11VideoFrameProcessor::Process(
    const std::shared_ptr<VideoSurface> &source,
    const VideoProcessingPlan &plan,
    std::shared_ptr<VideoSurface> &output) noexcept
{
    output.reset();
    if (terminal_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (!source || source->NativeHandle() == nullptr ||
        !IsPlanValid(source->Descriptor(), plan)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    const auto result = port_.Convert(source, plan, output);
    if (terminal_.load()) {
        output.reset();
        return VRREC_STATUS_INVALID_STATE;
    }

    switch (result) {
    case D3d11VideoProcessorResult::Converted:
        if (!IsOutputValid(source, plan, output)) {
            return Fail(
                D3d11VideoProcessorResult::Failed,
                VRREC_STATUS_INTERNAL_ERROR,
                output);
        }
        last_result_.store(D3d11VideoProcessorResult::Converted);
        return VRREC_STATUS_OK;
    case D3d11VideoProcessorResult::DeviceRemoved:
    case D3d11VideoProcessorResult::DeviceReset:
        return Fail(result, VRREC_STATUS_BACKEND_UNAVAILABLE, output);
    case D3d11VideoProcessorResult::OutOfMemory:
        return Fail(result, VRREC_STATUS_OUT_OF_MEMORY, output);
    case D3d11VideoProcessorResult::Aborted:
        return Fail(result, VRREC_STATUS_INVALID_STATE, output);
    case D3d11VideoProcessorResult::None:
    case D3d11VideoProcessorResult::Failed:
        return Fail(
            D3d11VideoProcessorResult::Failed,
            VRREC_STATUS_INTERNAL_ERROR,
            output);
    }

    return Fail(
        D3d11VideoProcessorResult::Failed,
        VRREC_STATUS_INTERNAL_ERROR,
        output);
}

void D3d11VideoFrameProcessor::Abort() noexcept
{
    terminal_.store(true);
    if (abort_sent_.exchange(true)) {
        return;
    }

    last_result_.store(D3d11VideoProcessorResult::Aborted);
    port_.Abort();
}

D3d11VideoProcessorResult
D3d11VideoFrameProcessor::LastResult() const noexcept
{
    return last_result_.load();
}

bool D3d11VideoFrameProcessor::IsPlanValid(
    const VideoSurfaceDescriptor &source,
    const VideoProcessingPlan &plan) noexcept
{
    const auto normalized_width =
        static_cast<std::uint64_t>(source.width) + (source.width & 1U);
    const auto normalized_height =
        static_cast<std::uint64_t>(source.height) + (source.height & 1U);
    return source.adapter_luid != 0 && source.generation_id != 0 &&
           source.width != 0 && source.height != 0 &&
           plan.adapter_luid == source.adapter_luid &&
           plan.source_generation_id == source.generation_id &&
           plan.source_width == source.width &&
           plan.source_height == source.height &&
           plan.normalized_source_width == normalized_width &&
           plan.normalized_source_height == normalized_height &&
           plan.input_pixel_format == source.pixel_format &&
           plan.output_pixel_format == VRREC_SOURCE_PIXEL_FORMAT_NV12 &&
           plan.output_width != 0 && plan.output_height != 0 &&
           (plan.output_width & 1U) == 0 &&
           (plan.output_height & 1U) == 0 &&
           plan.destination_width != 0 &&
           plan.destination_height != 0 &&
           plan.offset_x <= plan.output_width &&
           plan.offset_y <= plan.output_height &&
           plan.destination_width <= plan.output_width - plan.offset_x &&
           plan.destination_height <= plan.output_height - plan.offset_y;
}

bool D3d11VideoFrameProcessor::IsOutputValid(
    const std::shared_ptr<VideoSurface> &source,
    const VideoProcessingPlan &plan,
    const std::shared_ptr<VideoSurface> &output) noexcept
{
    if (!output || output == source || output->NativeHandle() == nullptr) {
        return false;
    }

    const auto descriptor = output->Descriptor();
    return descriptor.adapter_luid == plan.adapter_luid &&
           descriptor.width == plan.output_width &&
           descriptor.height == plan.output_height &&
           descriptor.pixel_format == VRREC_SOURCE_PIXEL_FORMAT_NV12 &&
           descriptor.generation_id == plan.source_generation_id;
}

vrrec_status_t D3d11VideoFrameProcessor::Fail(
    D3d11VideoProcessorResult result,
    vrrec_status_t status,
    std::shared_ptr<VideoSurface> &output) noexcept
{
    output.reset();
    terminal_.store(true);
    if (!abort_sent_.exchange(true)) {
        port_.Abort();
    }
    last_result_.store(result);
    return status;
}

}
