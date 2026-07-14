#ifndef VRRECORDER_NATIVE_FFMPEG_AAC_AUDIO_PIPELINE_HPP
#define VRRECORDER_NATIVE_FFMPEG_AAC_AUDIO_PIPELINE_HPP

#include <memory>
#include <optional>

#include "audio_pipeline_session.hpp"
#include "ffmpeg_aac_packet_encoder.hpp"

namespace vrrecorder::native {

enum class FfmpegAacAudioPipelineFailurePoint {
    None,
    AllocatePipeline,
};

struct FfmpegAacAudioPipelineFactory;

class FfmpegAacAudioPipeline final {
public:
    FfmpegAacAudioPipeline(const FfmpegAacAudioPipeline &) = delete;
    FfmpegAacAudioPipeline &operator=(
        const FfmpegAacAudioPipeline &) = delete;
    FfmpegAacAudioPipeline(FfmpegAacAudioPipeline &&) = delete;
    FfmpegAacAudioPipeline &operator=(
        FfmpegAacAudioPipeline &&) = delete;

    StereoAudioPipelineSessionPort &Session() noexcept;

private:
    friend struct FfmpegAacAudioPipelineFactory;

    FfmpegAacAudioPipeline(
        std::unique_ptr<FfmpegAacPacketEncoder> encoder,
        StereoAudioCaptureSessionPort &capture,
        EncodedMediaPacketSubmissionPort &submission) noexcept;

    // Declaration order is the lifetime contract. Destruction runs session,
    // then sink, then encoder so an active worker cannot observe a dead sink
    // or encoder while Abort/Join is in progress.
    std::unique_ptr<FfmpegAacPacketEncoder> encoder_;
    MuxingAudioEncoderSink sink_;
    StereoAudioPipelineSession session_;
};

struct FfmpegAacAudioPipelineCreateResult final {
    vrrec_status_t status = VRREC_STATUS_INTERNAL_ERROR;
    std::unique_ptr<FfmpegAacAudioPipeline> pipeline;
    std::optional<AacStreamDescriptor> descriptor;
};

FfmpegAacAudioPipelineCreateResult CreateFfmpegAacAudioPipeline(
    StereoAudioCaptureSessionPort &capture,
    EncodedMediaPacketSubmissionPort &submission) noexcept;

#if defined(VRRECORDER_NATIVE_TESTING)
FfmpegAacAudioPipelineCreateResult
CreateFfmpegAacAudioPipelineForTesting(
    StereoAudioCaptureSessionPort &capture,
    EncodedMediaPacketSubmissionPort &submission,
    FfmpegAacAudioPipelineFailurePoint failure_point) noexcept;
#endif

}

#endif
