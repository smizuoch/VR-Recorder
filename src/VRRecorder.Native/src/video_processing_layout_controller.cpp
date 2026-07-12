#include "video_processing_layout_controller.hpp"

#include "video_processing_encoder_sink.hpp"

namespace vrrecorder::native {

ProcessingVideoLayoutController::ProcessingVideoLayoutController(
    ProcessingVideoEncoderSink &sink) noexcept
    : sink_(sink)
{
}

vrrec_status_t ProcessingVideoLayoutController::UpdateVideoLayout(
    const vrrec_video_layout_v1 &layout) noexcept
{
    return sink_.UpdateVideoLayout(layout);
}

}
