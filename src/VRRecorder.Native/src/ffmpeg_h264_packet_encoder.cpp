#include "ffmpeg_h264_packet_encoder.hpp"

#include <cerrno>
#include <cstring>
#include <mutex>
#include <new>
#include <utility>

#include "ffmpeg_h264_media_foundation_configuration.hpp"
#include "ffmpeg_libavcodec_encoder_port.hpp"
#include "muxing_video_encoder_sink.hpp"

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

class FfmpegH264PacketEncoder::Impl final {
public:
    enum class State { Active, Finished, Failed, Aborted };

    Impl(
        H264VideoEncoderConfig config,
        std::unique_ptr<FfmpegH264CodecSession> session,
        AVFrame *frame) noexcept
        : session(std::move(session)),
          frame(frame),
          normalizer(config)
    {
    }

    ~Impl()
    {
        av_frame_free(&frame);
    }

    vrrec_status_t RefreshExtradata() noexcept
    {
        std::vector<std::byte> extradata;
        const auto status = session->CopyCodecExtradata(extradata);
        if (status != VRREC_STATUS_OK || extradata.empty()) {
            return status;
        }
        return normalizer.InitializeFromAnnexBExtradata(extradata);
    }

    FfmpegH264PacketEncoderWrite Normalize(FfmpegEncodeBatch batch) noexcept
    {
        if (batch.status != VRREC_STATUS_OK) {
            return Fail(batch.status);
        }
        const auto extradata_status = RefreshExtradata();
        if (extradata_status != VRREC_STATUS_OK) {
            return Fail(extradata_status);
        }
        FfmpegH264PacketEncoderWrite output {VRREC_STATUS_OK, false, {}};
        try {
            output.packets.reserve(batch.packets.size());
            for (const auto &packet : batch.packets) {
                auto normalized = normalizer.Normalize(packet);
                if (normalized.status != VRREC_STATUS_OK) {
                    return Fail(normalized.status);
                }
                output.descriptor_became_ready =
                    output.descriptor_became_ready ||
                    normalized.descriptor_became_ready;
                output.packets.push_back(std::move(normalized.packet));
            }
            return output;
        } catch (const std::bad_alloc &) {
            return Fail(VRREC_STATUS_OUT_OF_MEMORY);
        } catch (...) {
            return Fail(VRREC_STATUS_INTERNAL_ERROR);
        }
    }

    FfmpegH264PacketEncoderWrite Fail(vrrec_status_t status) noexcept
    {
        if (!session_aborted) {
            session_aborted = true;
            session->Abort();
        }
        normalizer.Abort();
        state = State::Failed;
        return {status, false, {}};
    }

    std::unique_ptr<FfmpegH264CodecSession> session;
    AVFrame *frame = nullptr;
    H264PacketNormalizer normalizer;
    mutable std::mutex mutex;
    State state = State::Active;
    bool session_aborted = false;
};

FfmpegH264PacketEncoder::FfmpegH264PacketEncoder(
    std::unique_ptr<Impl> impl) noexcept
    : impl_(std::move(impl))
{
}

FfmpegH264PacketEncoder::~FfmpegH264PacketEncoder()
{
    Abort();
}

FfmpegH264PacketEncoderCreateResult FfmpegH264PacketEncoder::Create(
    const H264VideoEncoderConfig &config) noexcept
{
    if (!IsH264VideoEncoderConfigValid(config)) {
        return {VRREC_STATUS_INVALID_ARGUMENT, nullptr};
    }
    const AVCodec *codec = avcodec_find_encoder_by_name(
        FfmpegH264MediaFoundationEncoderName.data());
    if (codec == nullptr) {
        return {VRREC_STATUS_BACKEND_UNAVAILABLE, nullptr};
    }
    AVCodecContext *context = avcodec_alloc_context3(codec);
    if (context == nullptr) {
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr};
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
        return {status, nullptr};
    }

    AVFrame *frame = av_frame_alloc();
    if (frame == nullptr) {
        avcodec_free_context(&context);
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr};
    }
    frame->format = AV_PIX_FMT_NV12;
    frame->width = static_cast<int>(config.width);
    frame->height = static_cast<int>(config.height);
    const auto buffer_result = av_frame_get_buffer(frame, 32);
    if (buffer_result < 0) {
        av_frame_free(&frame);
        avcodec_free_context(&context);
        return {ErrorStatus(buffer_result), nullptr};
    }
    auto port = LibavcodecEncoderPort::Create(context);
    if (port.status != VRREC_STATUS_OK || port.port == nullptr) {
        av_frame_free(&frame);
        return {port.status, nullptr};
    }
    std::unique_ptr<FfmpegH264CodecSession> session(
        new (std::nothrow) LibavcodecH264CodecSession(std::move(port.port)));
    if (session == nullptr) {
        av_frame_free(&frame);
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr};
    }
    return CreateWithSession(config, std::move(session), frame);
}

#if defined(VRRECORDER_NATIVE_TESTING)
FfmpegH264PacketEncoderCreateResult
FfmpegH264PacketEncoder::CreateForTesting(
    const H264VideoEncoderConfig &config,
    std::unique_ptr<FfmpegH264CodecSession> session,
    AVFrame *frame) noexcept
{
    return CreateWithSession(config, std::move(session), frame);
}
#endif

FfmpegH264PacketEncoderCreateResult
FfmpegH264PacketEncoder::CreateWithSession(
    const H264VideoEncoderConfig &config,
    std::unique_ptr<FfmpegH264CodecSession> session,
    AVFrame *frame) noexcept
{
    if (!IsH264VideoEncoderConfigValid(config) || session == nullptr ||
        frame == nullptr) {
        av_frame_free(&frame);
        return {VRREC_STATUS_INVALID_ARGUMENT, nullptr};
    }
    std::unique_ptr<Impl> impl(new (std::nothrow) Impl(
        config,
        std::move(session),
        frame));
    if (impl == nullptr) {
        av_frame_free(&frame);
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr};
    }
    const auto extradata_status = impl->RefreshExtradata();
    if (extradata_status != VRREC_STATUS_OK) {
        return {extradata_status, nullptr};
    }
    std::unique_ptr<FfmpegH264PacketEncoder> encoder(
        new (std::nothrow) FfmpegH264PacketEncoder(std::move(impl)));
    return encoder == nullptr
        ? FfmpegH264PacketEncoderCreateResult {
            VRREC_STATUS_OUT_OF_MEMORY,
            nullptr,
        }
        : FfmpegH264PacketEncoderCreateResult {
            VRREC_STATUS_OK,
            std::move(encoder),
        };
}

FfmpegH264PacketEncoderWrite FfmpegH264PacketEncoder::EncodeNv12(
    const SystemMemoryNv12FrameView &frame) noexcept
{
    if (impl_ == nullptr) {
        return {VRREC_STATUS_INVALID_STATE, false, {}};
    }
    const std::lock_guard lock(impl_->mutex);
    if (impl_->state != Impl::State::Active) {
        return {VRREC_STATUS_INVALID_STATE, false, {}};
    }
    auto status = CopySystemMemoryNv12FrameToFfmpeg(frame, *impl_->frame);
    if (status == VRREC_STATUS_OK) {
        status = impl_->session->PrepareFrame(*impl_->frame);
    }
    if (status != VRREC_STATUS_OK) {
        return impl_->Fail(status);
    }
    return impl_->Normalize(impl_->session->EncodePreparedFrame());
}

FfmpegH264PacketEncoderWrite FfmpegH264PacketEncoder::Finish() noexcept
{
    if (impl_ == nullptr) {
        return {VRREC_STATUS_INVALID_STATE, false, {}};
    }
    const std::lock_guard lock(impl_->mutex);
    if (impl_->state != Impl::State::Active) {
        return {VRREC_STATUS_INVALID_STATE, false, {}};
    }
    auto output = impl_->Normalize(impl_->session->Finish());
    if (output.status != VRREC_STATUS_OK) {
        return output;
    }
    if (impl_->normalizer.Descriptor() == nullptr) {
        return impl_->Fail(VRREC_STATUS_INVALID_STATE);
    }
    impl_->state = Impl::State::Finished;
    return output;
}

void FfmpegH264PacketEncoder::Abort() noexcept
{
    if (impl_ == nullptr) {
        return;
    }
    const std::lock_guard lock(impl_->mutex);
    if (impl_->state == Impl::State::Finished || impl_->session_aborted) {
        return;
    }
    impl_->session_aborted = true;
    impl_->state = Impl::State::Aborted;
    impl_->normalizer.Abort();
    impl_->session->Abort();
}

const H264StreamDescriptor *FfmpegH264PacketEncoder::Descriptor() const noexcept
{
    if (impl_ == nullptr) {
        return nullptr;
    }
    const std::lock_guard lock(impl_->mutex);
    return impl_->normalizer.Descriptor();
}

PacketVideoEncoderWrite MakeMuxingVideoEncoderWrite(
    const FfmpegH264PacketEncoder &encoder,
    FfmpegH264PacketEncoderWrite write,
    std::uint64_t encode_latency_microseconds) noexcept
{
    const auto *descriptor = write.descriptor_became_ready
        ? encoder.Descriptor()
        : nullptr;
    if (write.status == VRREC_STATUS_OK &&
        write.descriptor_became_ready && descriptor == nullptr) {
        write.status = VRREC_STATUS_INTERNAL_ERROR;
    }
    const auto publish_descriptor = write.status == VRREC_STATUS_OK &&
        write.descriptor_became_ready && descriptor != nullptr;
    return {
        write.status,
        encode_latency_microseconds,
        std::move(write.packets),
        publish_descriptor,
        publish_descriptor ? &encoder : nullptr,
        publish_descriptor ? descriptor : nullptr,
    };
}

}
