#include "ffmpeg_system_memory_encoder_probe_session.hpp"

#include <cstdint>
#include <memory>
#include <new>
#include <utility>

#include "encoder_probe_identity.hpp"
#include "encoder_probe_synthetic_nv12.hpp"
#include "ffmpeg_h264_nv12_frame.hpp"

extern "C" {
#include <libavutil/frame.h>
#include <libavutil/pixfmt.h>
}

namespace vrrecorder::native {
namespace {

constexpr std::int64_t MicrosecondsPerSecond = INT64_C(1000000);

bool IsSoftwareIdentityValid(
    const EncoderProbeOpenedIdentity &identity) noexcept
{
    return identity.actual_encoder_kind ==
            VRREC_ENCODER_MEDIA_FOUNDATION_SOFTWARE &&
        identity.codec_name == "h264_mf" &&
        !identity.hardware_accelerated &&
        identity.source_adapter_luid != 0 &&
        identity.processor_adapter_luid == identity.source_adapter_luid &&
        identity.encoder_adapter_luid == 0 &&
        identity.opened_input_format ==
            VRREC_ENCODER_INPUT_SYSTEM_MEMORY_NV12 &&
        identity.width != 0 && identity.height != 0 &&
        (identity.width & 1U) == 0 && (identity.height & 1U) == 0 &&
        identity.fps_numerator >= 30 && identity.fps_numerator <= 120 &&
        identity.fps_denominator == 1 &&
        (identity.profile == H264Profile::Main ||
            identity.profile == H264Profile::High) &&
        identity.maximum_b_frame_count == 0 &&
        !identity.driver_identity.empty() &&
        !identity.ffmpeg_build_identity.empty() &&
        !identity.device_identity.empty();
}

bool IsFrameStorageValid(
    const EncoderProbeOpenedIdentity &identity,
    const AVFrame *frame) noexcept
{
    return frame != nullptr && frame->format == AV_PIX_FMT_NV12 &&
        frame->width == static_cast<int>(identity.width) &&
        frame->height == static_cast<int>(identity.height) &&
        frame->data[0] != nullptr && frame->data[1] != nullptr &&
        frame->linesize[0] >= static_cast<int>(identity.width) &&
        frame->linesize[1] >= static_cast<int>(identity.width);
}

class FfmpegSystemMemoryEncoderProbeSession final
    : public EncoderProbeEncodeSession {
public:
    FfmpegSystemMemoryEncoderProbeSession(
        EncoderProbeOpenedIdentity identity,
        std::unique_ptr<FfmpegH264CodecSession> codec,
        AVFrame *frame) noexcept
        : identity_(std::move(identity)),
          codec_(std::move(codec)),
          frame_(frame)
    {
    }

    ~FfmpegSystemMemoryEncoderProbeSession() override
    {
        Abort();
        av_frame_free(&frame_);
    }

    const EncoderProbeOpenedIdentity &OpenedIdentity()
        const noexcept override
    {
        return identity_;
    }

    FfmpegEncodeBatch EncodeSyntheticFrame(
        const EncoderProbeSyntheticFrame &frame) noexcept override
    {
        if (state_ != State::Active || frame.frame_index != next_frame_ ||
            frame.width != identity_.width ||
            frame.height != identity_.height) {
            return Fail(VRREC_STATUS_INVALID_ARGUMENT);
        }
        const auto expected_start =
            static_cast<std::int64_t>(frame.frame_index) *
            MicrosecondsPerSecond / identity_.fps_numerator;
        const auto expected_end =
            static_cast<std::int64_t>(frame.frame_index + 1U) *
            MicrosecondsPerSecond / identity_.fps_numerator;
        if (frame.pts_microseconds != expected_start ||
            frame.duration_microseconds != expected_end - expected_start) {
            return Fail(VRREC_STATUS_INVALID_ARGUMENT);
        }

        OwnedEncoderProbeNv12Frame generated;
        auto status = CreateEncoderProbeSyntheticNv12Frame(frame, generated);
        if (status == VRREC_STATUS_OK) {
            status = CopySystemMemoryNv12FrameToFfmpeg(
                generated.View(),
                *frame_);
        }
        if (status == VRREC_STATUS_OK) {
            status = codec_->PrepareFrame(*frame_);
        }
        if (status != VRREC_STATUS_OK) {
            return Fail(status);
        }

        auto batch = codec_->EncodePreparedFrame();
        if (batch.status != VRREC_STATUS_OK) {
            return Fail(batch.status);
        }
        ++next_frame_;
        return batch;
    }

    FfmpegEncodeBatch Finish() noexcept override
    {
        if (state_ != State::Active ||
            next_frame_ != EncoderProbeSyntheticFrameCount) {
            return Fail(VRREC_STATUS_INVALID_STATE);
        }
        auto batch = codec_->Finish();
        if (batch.status != VRREC_STATUS_OK) {
            return Fail(batch.status);
        }
        state_ = State::Finished;
        return batch;
    }

    void Abort() noexcept override
    {
        if (!codec_aborted_ && state_ != State::Finished) {
            codec_aborted_ = true;
            codec_->Abort();
        }
        if (state_ != State::Finished) {
            state_ = State::Aborted;
        }
    }

private:
    enum class State { Active, Finished, Failed, Aborted };

    FfmpegEncodeBatch Fail(vrrec_status_t status) noexcept
    {
        if (!codec_aborted_) {
            codec_aborted_ = true;
            codec_->Abort();
        }
        state_ = State::Failed;
        return {status, {}};
    }

    EncoderProbeOpenedIdentity identity_;
    std::unique_ptr<FfmpegH264CodecSession> codec_;
    AVFrame *frame_ = nullptr;
    std::uint32_t next_frame_ = 0;
    State state_ = State::Active;
    bool codec_aborted_ = false;
};

}

EncoderProbeEncodeSessionCreateResult
CreateFfmpegSystemMemoryEncoderProbeSession(
    EncoderProbeOpenedIdentity opened_identity,
    std::unique_ptr<FfmpegH264CodecSession> codec_session,
    AVFrame *owned_frame) noexcept
{
    if (!IsSoftwareIdentityValid(opened_identity) ||
        codec_session == nullptr ||
        !IsFrameStorageValid(opened_identity, owned_frame)) {
        if (codec_session != nullptr) {
            codec_session->Abort();
        }
        av_frame_free(&owned_frame);
        return {VRREC_STATUS_INVALID_ARGUMENT, nullptr};
    }

    std::unique_ptr<EncoderProbeEncodeSession> session(
        new (std::nothrow) FfmpegSystemMemoryEncoderProbeSession(
            std::move(opened_identity),
            std::move(codec_session),
            owned_frame));
    if (session == nullptr) {
        codec_session->Abort();
        av_frame_free(&owned_frame);
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr};
    }
    return {VRREC_STATUS_OK, std::move(session)};
}

}
