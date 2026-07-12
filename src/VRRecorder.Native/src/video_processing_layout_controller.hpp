#ifndef VRRECORDER_NATIVE_VIDEO_PROCESSING_LAYOUT_CONTROLLER_HPP
#define VRRECORDER_NATIVE_VIDEO_PROCESSING_LAYOUT_CONTROLLER_HPP

#include "video_layout_update_port.hpp"

namespace vrrecorder::native {

class ProcessingVideoEncoderSink;

class ProcessingVideoLayoutController final : public VideoLayoutUpdatePort {
public:
    explicit ProcessingVideoLayoutController(
        ProcessingVideoEncoderSink &sink) noexcept;
    vrrec_status_t UpdateVideoLayout(
        const vrrec_video_layout_v1 &layout) noexcept override;

private:
    ProcessingVideoEncoderSink &sink_;
};

}

#endif
