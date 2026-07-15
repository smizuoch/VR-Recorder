#ifndef VRRECORDER_NATIVE_FFMPEG_SYSTEM_MEMORY_ENCODER_PROBE_SESSION_HPP
#define VRRECORDER_NATIVE_FFMPEG_SYSTEM_MEMORY_ENCODER_PROBE_SESSION_HPP

#include <memory>

#include "encoder_probe_identity.hpp"
#include "encoder_probe_pipeline.hpp"
#include "ffmpeg_h264_packet_encoder.hpp"

struct AVFrame;

namespace vrrecorder::native {

EncoderProbeEncodeSessionCreateResult
CreateFfmpegSystemMemoryEncoderProbeSession(
    EncoderProbeOpenedIdentity opened_identity,
    std::unique_ptr<FfmpegH264CodecSession> codec_session,
    AVFrame *owned_frame) noexcept;

}

#endif
