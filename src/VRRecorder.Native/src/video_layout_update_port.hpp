#ifndef VRRECORDER_NATIVE_VIDEO_LAYOUT_UPDATE_PORT_HPP
#define VRRECORDER_NATIVE_VIDEO_LAYOUT_UPDATE_PORT_HPP

#include "vrrecorder_native.h"

namespace vrrecorder::native {

class VideoLayoutUpdatePort {
public:
    virtual ~VideoLayoutUpdatePort() = default;
    virtual vrrec_status_t UpdateVideoLayout(
        const vrrec_video_layout_v1 &layout) noexcept = 0;
};

}

#endif
