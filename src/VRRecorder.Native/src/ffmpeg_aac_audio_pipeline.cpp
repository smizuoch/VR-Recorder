#include "ffmpeg_aac_audio_pipeline.hpp"

#include <new>
#include <utility>

namespace vrrecorder::native {

FfmpegAacAudioPipeline::FfmpegAacAudioPipeline(
    std::unique_ptr<FfmpegAacPacketEncoder> encoder,
    StereoAudioCaptureSessionPort &capture,
    EncodedMediaPacketSubmissionPort &submission) noexcept
    : encoder_(std::move(encoder)),
      sink_(*encoder_, submission),
      session_(capture, sink_)
{
}

StereoAudioPipelineSessionPort &FfmpegAacAudioPipeline::Session() noexcept
{
    return session_;
}

struct FfmpegAacAudioPipelineFactory final {
    static FfmpegAacAudioPipelineCreateResult Create(
        StereoAudioCaptureSessionPort &capture,
        EncodedMediaPacketSubmissionPort &submission,
        FfmpegAacAudioPipelineFailurePoint failure_point) noexcept
    {
        if (failure_point != FfmpegAacAudioPipelineFailurePoint::None &&
            failure_point !=
                FfmpegAacAudioPipelineFailurePoint::AllocatePipeline) {
            return {
                VRREC_STATUS_INVALID_ARGUMENT,
                nullptr,
                std::nullopt,
            };
        }

        AacAudioEncoderConfig config {};
        const auto config_status = CreateAacAudioEncoderConfig(config);
        if (config_status != VRREC_STATUS_OK) {
            return {config_status, nullptr, std::nullopt};
        }

        auto encoder_creation = FfmpegAacPacketEncoder::Create(config);
        if (encoder_creation.status != VRREC_STATUS_OK) {
            if (encoder_creation.encoder != nullptr ||
                encoder_creation.descriptor.has_value()) {
                return {
                    VRREC_STATUS_INTERNAL_ERROR,
                    nullptr,
                    std::nullopt,
                };
            }
            return {
                encoder_creation.status,
                nullptr,
                std::nullopt,
            };
        }
        if (encoder_creation.encoder == nullptr ||
            !encoder_creation.descriptor.has_value()) {
            return {
                VRREC_STATUS_INTERNAL_ERROR,
                nullptr,
                std::nullopt,
            };
        }

        std::unique_ptr<FfmpegAacAudioPipeline> pipeline;
        if (failure_point !=
            FfmpegAacAudioPipelineFailurePoint::AllocatePipeline) {
            pipeline.reset(new (std::nothrow) FfmpegAacAudioPipeline(
                std::move(encoder_creation.encoder),
                capture,
                submission));
        }
        if (pipeline == nullptr) {
            return {
                VRREC_STATUS_OUT_OF_MEMORY,
                nullptr,
                std::nullopt,
            };
        }

        return {
            VRREC_STATUS_OK,
            std::move(pipeline),
            std::move(encoder_creation.descriptor),
        };
    }
};

FfmpegAacAudioPipelineCreateResult CreateFfmpegAacAudioPipeline(
    StereoAudioCaptureSessionPort &capture,
    EncodedMediaPacketSubmissionPort &submission) noexcept
{
    return FfmpegAacAudioPipelineFactory::Create(
        capture,
        submission,
        FfmpegAacAudioPipelineFailurePoint::None);
}

#if defined(VRRECORDER_NATIVE_TESTING)
FfmpegAacAudioPipelineCreateResult
CreateFfmpegAacAudioPipelineForTesting(
    StereoAudioCaptureSessionPort &capture,
    EncodedMediaPacketSubmissionPort &submission,
    FfmpegAacAudioPipelineFailurePoint failure_point) noexcept
{
    return FfmpegAacAudioPipelineFactory::Create(
        capture,
        submission,
        failure_point);
}
#endif

}
