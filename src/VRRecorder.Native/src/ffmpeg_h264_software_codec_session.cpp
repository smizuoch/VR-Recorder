#include "ffmpeg_h264_software_codec_session.hpp"

#include <cerrno>
#include <cstdint>
#include <cstring>
#include <memory>
#include <new>
#include <utility>

#include "ffmpeg_h264_media_foundation_configuration.hpp"
#include "ffmpeg_libavcodec_encoder_port.hpp"

extern "C" {
#include <libavcodec/avcodec.h>
#include <libavutil/dict.h>
#include <libavutil/error.h>
#include <libavutil/frame.h>
#include <libavutil/opt.h>
#include <libavutil/pixfmt.h>
}

namespace vrrecorder::native {
namespace {

vrrec_status_t ErrorStatus(int error) noexcept
{
    return error == AVERROR(ENOMEM)
        ? VRREC_STATUS_OUT_OF_MEMORY
        : VRREC_STATUS_BACKEND_UNAVAILABLE;
}

bool IsOpenedContextValid(
    const H264VideoEncoderConfig &config,
    const AVCodec &codec,
    AVCodecContext &context) noexcept
{
    if (context.priv_data == nullptr) {
        return false;
    }
    std::int64_t hardware_encoding = -1;
    std::int64_t rate_control = -2;
    const auto hardware_status = av_opt_get_int(
        context.priv_data,
        "hw_encoding",
        0,
        &hardware_encoding);
    const auto rate_control_status = av_opt_get_int(
        context.priv_data,
        "rate_control",
        0,
        &rate_control);
    const auto *quality_option = av_opt_find(
        context.priv_data,
        "quality",
        "rate_control",
        0,
        0);
    const auto software_options_are_valid =
        hardware_status == 0 && hardware_encoding == 0 &&
        rate_control_status == 0 && quality_option != nullptr &&
        quality_option->type == AV_OPT_TYPE_CONST &&
        rate_control == quality_option->default_val.i64;

    return software_options_are_valid &&
        avcodec_is_open(&context) != 0 && context.codec == &codec &&
        codec.name != nullptr &&
        std::strcmp(codec.name, "h264_mf") == 0 &&
        codec.type == AVMEDIA_TYPE_VIDEO && codec.id == AV_CODEC_ID_H264 &&
        av_codec_is_encoder(&codec) != 0 &&
        context.codec_type == AVMEDIA_TYPE_VIDEO &&
        context.codec_id == AV_CODEC_ID_H264 &&
        context.width == static_cast<int>(config.width) &&
        context.height == static_cast<int>(config.height) &&
        context.pix_fmt == AV_PIX_FMT_NV12 &&
        context.time_base.num == 1 &&
        context.time_base.den == static_cast<int>(config.frames_per_second) &&
        context.framerate.num == static_cast<int>(config.frames_per_second) &&
        context.framerate.den == 1 &&
        context.bit_rate == static_cast<std::int64_t>(
            config.target_bitrate_bits_per_second) &&
        context.rc_max_rate == static_cast<std::int64_t>(
            config.maximum_bitrate_bits_per_second) &&
        context.gop_size == static_cast<int>(config.gop_frame_count) &&
        context.max_b_frames == 0 &&
        context.profile == (config.profile == H264Profile::High
            ? AV_PROFILE_H264_HIGH
            : AV_PROFILE_H264_MAIN) &&
        (context.flags & AV_CODEC_FLAG_GLOBAL_HEADER) != 0;
}

class LibavcodecH264CodecSession final : public FfmpegH264CodecSession {
public:
    explicit LibavcodecH264CodecSession(
        std::unique_ptr<LibavcodecEncoderPort> port) noexcept
        : port_(std::move(port)),
          state_machine_(*port_, MediaStreamKind::Video)
    {
    }

    vrrec_status_t PrepareFrame(const AVFrame &frame) noexcept override
    {
        return port_->PrepareFrame(frame);
    }

    FfmpegEncodeBatch EncodePreparedFrame() noexcept override
    {
        return state_machine_.EncodePreparedFrame();
    }

    FfmpegEncodeBatch Finish() noexcept override
    {
        return state_machine_.Finish();
    }

    vrrec_status_t CopyCodecExtradata(
        std::vector<std::byte> &extradata) const noexcept override
    {
        return port_->CopyCodecExtradata(extradata);
    }

    void Abort() noexcept override
    {
        state_machine_.Abort();
    }

private:
    std::unique_ptr<LibavcodecEncoderPort> port_;
    FfmpegEncoderStateMachine state_machine_;
};

}

FfmpegH264SoftwareCodecSessionCreateResult
CreateFfmpegH264SoftwareCodecSession(
    const H264VideoEncoderConfig &config) noexcept
{
    if (!IsH264VideoEncoderConfigValid(config)) {
        return {VRREC_STATUS_INVALID_ARGUMENT, nullptr, nullptr};
    }
    const AVCodec *codec = avcodec_find_encoder_by_name(
        FfmpegH264MediaFoundationEncoderName.data());
    if (codec == nullptr) {
        return {VRREC_STATUS_BACKEND_UNAVAILABLE, nullptr, nullptr};
    }
    AVCodecContext *context = avcodec_alloc_context3(codec);
    if (context == nullptr) {
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr, nullptr};
    }
    AVDictionary *options = nullptr;
    auto status = ConfigureFfmpegH264MediaFoundationContext(
        config,
        *context,
        options);
    if (status == VRREC_STATUS_OK) {
        const auto open_result = avcodec_open2(context, codec, &options);
        status = open_result < 0 ? ErrorStatus(open_result) : VRREC_STATUS_OK;
    }
    if (status == VRREC_STATUS_OK &&
        (av_dict_count(options) != 0 ||
            !IsOpenedContextValid(config, *codec, *context))) {
        status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    }
    av_dict_free(&options);
    if (status != VRREC_STATUS_OK) {
        avcodec_free_context(&context);
        return {status, nullptr, nullptr};
    }

    AVFrame *frame = av_frame_alloc();
    if (frame == nullptr) {
        avcodec_free_context(&context);
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr, nullptr};
    }
    frame->format = AV_PIX_FMT_NV12;
    frame->width = static_cast<int>(config.width);
    frame->height = static_cast<int>(config.height);
    const auto buffer_result = av_frame_get_buffer(frame, 32);
    if (buffer_result < 0) {
        av_frame_free(&frame);
        avcodec_free_context(&context);
        return {ErrorStatus(buffer_result), nullptr, nullptr};
    }
    auto port = LibavcodecEncoderPort::Create(context);
    if (port.status != VRREC_STATUS_OK || port.port == nullptr) {
        av_frame_free(&frame);
        return {port.status, nullptr, nullptr};
    }
    std::unique_ptr<FfmpegH264CodecSession> session(
        new (std::nothrow) LibavcodecH264CodecSession(std::move(port.port)));
    if (session == nullptr) {
        av_frame_free(&frame);
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr, nullptr};
    }
    return {VRREC_STATUS_OK, std::move(session), frame};
}

}
