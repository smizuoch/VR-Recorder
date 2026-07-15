#ifndef VRRECORDER_NATIVE_ENCODER_PROBE_SYNTHETIC_NV12_HPP
#define VRRECORDER_NATIVE_ENCODER_PROBE_SYNTHETIC_NV12_HPP

#include <cstddef>
#include <cstdint>
#include <vector>

#include "encoder_probe_identity.hpp"
#include "encoder_probe_pipeline.hpp"
#include "ffmpeg_h264_nv12_frame.hpp"

namespace vrrecorder::native {

class OwnedEncoderProbeNv12Frame final {
public:
    SystemMemoryNv12FrameView View() const noexcept;

private:
    friend vrrec_status_t CreateEncoderProbeSyntheticNv12Frame(
        const EncoderProbeSyntheticFrame &frame,
        OwnedEncoderProbeNv12Frame &output) noexcept;

    std::uint32_t width_ = 0;
    std::uint32_t height_ = 0;
    std::vector<std::byte> y_plane_;
    std::vector<std::byte> uv_plane_;
    std::int64_t codec_pts_ = -1;
};

vrrec_status_t CreateEncoderProbeSyntheticNv12Frame(
    const EncoderProbeSyntheticFrame &frame,
    OwnedEncoderProbeNv12Frame &output) noexcept;

}

#endif
