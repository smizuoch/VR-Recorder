#include "ffmpeg_libavcodec_encoder_port.hpp"

#include <cerrno>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <limits>
#include <new>
#include <utility>
#include <vector>

extern "C" {
#include <libavcodec/avcodec.h>
#include <libavcodec/packet.h>
#include <libavcodec/version.h>
#include <libavutil/avutil.h>
#include <libavutil/error.h>
#include <libavutil/frame.h>
#include <libavutil/version.h>
}

namespace vrrecorder::native {

namespace {

FfmpegCodecIoResult Ok() noexcept
{
    return {FfmpegCodecIoState::Ok, VRREC_STATUS_OK};
}

FfmpegCodecIoResult Again() noexcept
{
    return {FfmpegCodecIoState::Again, VRREC_STATUS_OK};
}

FfmpegCodecIoResult EndOfStream() noexcept
{
    return {FfmpegCodecIoState::EndOfStream, VRREC_STATUS_OK};
}

FfmpegCodecIoResult Failure(vrrec_status_t status) noexcept
{
    return {FfmpegCodecIoState::Failed, status};
}

vrrec_status_t ErrorStatus(int error) noexcept
{
    return error == AVERROR(ENOMEM)
        ? VRREC_STATUS_OUT_OF_MEMORY
        : VRREC_STATUS_INTERNAL_ERROR;
}

std::int64_t MediaTimestamp(std::int64_t value) noexcept
{
    return value == AV_NOPTS_VALUE ? UnknownMediaTimestamp : value;
}

}

struct LibavcodecEncoderPort::RuntimeIdentity final {
    unsigned int avcodec_version;
    unsigned int avutil_version;
    const char *release_version;
};

class LibavcodecEncoderPort::Impl final {
public:
    enum class State {
        Active,
        Draining,
        Finished,
        Failed,
        Aborted,
    };

    explicit Impl(AVCodecContext *context) noexcept
        : context(context),
          prepared_frame(av_frame_alloc()),
          received_packet(av_packet_alloc())
    {
    }

    ~Impl()
    {
        Release();
    }

    Impl(const Impl &) = delete;
    Impl &operator=(const Impl &) = delete;

    bool HasResources() const noexcept
    {
        return context != nullptr && prepared_frame != nullptr &&
            received_packet != nullptr;
    }

    void Release() noexcept
    {
        side_data_views.clear();
        packet_borrowed = false;
        frame_pending = false;
        av_packet_free(&received_packet);
        av_frame_free(&prepared_frame);
        avcodec_free_context(&context);
    }

    AVCodecContext *context = nullptr;
    AVFrame *prepared_frame = nullptr;
    AVPacket *received_packet = nullptr;
    std::vector<FfmpegReceivedPacketSideDataView> side_data_views;
    State state = State::Active;
    bool frame_pending = false;
    bool packet_borrowed = false;
};

LibavcodecEncoderPort::LibavcodecEncoderPort(
    std::unique_ptr<Impl> impl) noexcept
    : impl_(std::move(impl))
{
}

LibavcodecEncoderPort::~LibavcodecEncoderPort()
{
    Abort();
}

LibavcodecEncoderPortCreateResult LibavcodecEncoderPort::Create(
    AVCodecContext *opened_context) noexcept
{
    const RuntimeIdentity runtime_identity {
        avcodec_version(),
        avutil_version(),
        av_version_info(),
    };
    return CreateWithRuntimeIdentity(opened_context, runtime_identity);
}

#if defined(VRRECORDER_NATIVE_TESTING)
LibavcodecEncoderPortCreateResult LibavcodecEncoderPort::CreateForTesting(
    AVCodecContext *opened_context,
    unsigned int avcodec_version_value,
    unsigned int avutil_version_value,
    const char *release_version) noexcept
{
    const RuntimeIdentity runtime_identity {
        avcodec_version_value,
        avutil_version_value,
        release_version,
    };
    return CreateWithRuntimeIdentity(opened_context, runtime_identity);
}
#endif

LibavcodecEncoderPortCreateResult
LibavcodecEncoderPort::CreateWithRuntimeIdentity(
    AVCodecContext *opened_context,
    const RuntimeIdentity &runtime_identity) noexcept
{
    if (opened_context == nullptr) {
        return {VRREC_STATUS_INVALID_ARGUMENT, nullptr};
    }

    constexpr char PinnedFfmpegVersion[] = "8.1.2";
    if (runtime_identity.avcodec_version != LIBAVCODEC_VERSION_INT ||
        runtime_identity.avutil_version != LIBAVUTIL_VERSION_INT ||
        runtime_identity.release_version == nullptr ||
        std::strcmp(
            runtime_identity.release_version,
            PinnedFfmpegVersion) != 0) {
        avcodec_free_context(&opened_context);
        return {VRREC_STATUS_BACKEND_UNAVAILABLE, nullptr};
    }

    if (avcodec_is_open(opened_context) == 0 ||
        opened_context->codec == nullptr ||
        av_codec_is_encoder(opened_context->codec) == 0 ||
        (opened_context->codec_type != AVMEDIA_TYPE_AUDIO &&
            opened_context->codec_type != AVMEDIA_TYPE_VIDEO) ||
        opened_context->time_base.num <= 0 ||
        opened_context->time_base.den <= 0) {
        avcodec_free_context(&opened_context);
        return {VRREC_STATUS_INVALID_ARGUMENT, nullptr};
    }

    std::unique_ptr<Impl> impl(new (std::nothrow) Impl(opened_context));
    if (impl == nullptr) {
        avcodec_free_context(&opened_context);
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr};
    }
    if (!impl->HasResources()) {
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr};
    }

    std::unique_ptr<LibavcodecEncoderPort> port(
        new (std::nothrow) LibavcodecEncoderPort(std::move(impl)));
    if (port == nullptr) {
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr};
    }
    return {VRREC_STATUS_OK, std::move(port)};
}

vrrec_status_t LibavcodecEncoderPort::PrepareFrame(
    const AVFrame &frame) noexcept
{
    if (impl_ == nullptr || impl_->state != Impl::State::Active ||
        impl_->frame_pending || impl_->prepared_frame == nullptr) {
        return VRREC_STATUS_INVALID_STATE;
    }

    av_frame_unref(impl_->prepared_frame);
    const auto result = av_frame_ref(impl_->prepared_frame, &frame);
    if (result < 0) {
        av_frame_unref(impl_->prepared_frame);
        return result == AVERROR(ENOMEM)
            ? VRREC_STATUS_OUT_OF_MEMORY
            : VRREC_STATUS_INVALID_ARGUMENT;
    }

    impl_->frame_pending = true;
    return VRREC_STATUS_OK;
}

FfmpegCodecIoResult LibavcodecEncoderPort::SendPreparedFrame() noexcept
{
    if (impl_ == nullptr || impl_->state != Impl::State::Active ||
        !impl_->frame_pending || impl_->context == nullptr ||
        impl_->prepared_frame == nullptr) {
        return Failure(VRREC_STATUS_INVALID_STATE);
    }

    const auto result =
        avcodec_send_frame(impl_->context, impl_->prepared_frame);
    if (result == 0) {
        av_frame_unref(impl_->prepared_frame);
        impl_->frame_pending = false;
        return Ok();
    }
    if (result == AVERROR(EAGAIN)) {
        return Again();
    }
    if (result == AVERROR_EOF) {
        impl_->state = Impl::State::Finished;
        return EndOfStream();
    }

    impl_->state = Impl::State::Failed;
    return Failure(ErrorStatus(result));
}

FfmpegCodecIoResult LibavcodecEncoderPort::SendDrain() noexcept
{
    if (impl_ == nullptr || impl_->state != Impl::State::Active ||
        impl_->frame_pending || impl_->context == nullptr) {
        return Failure(VRREC_STATUS_INVALID_STATE);
    }

    const auto result = avcodec_send_frame(impl_->context, nullptr);
    if (result == 0) {
        impl_->state = Impl::State::Draining;
        return Ok();
    }
    if (result == AVERROR(EAGAIN)) {
        return Again();
    }
    if (result == AVERROR_EOF) {
        impl_->state = Impl::State::Finished;
        return EndOfStream();
    }

    impl_->state = Impl::State::Failed;
    return Failure(ErrorStatus(result));
}

FfmpegCodecIoResult LibavcodecEncoderPort::ReceivePacket(
    FfmpegReceivedPacketView &packet) noexcept
{
    packet = {};
    if (impl_ == nullptr) {
        return Failure(VRREC_STATUS_INVALID_STATE);
    }
    if (impl_->state == Impl::State::Finished) {
        return EndOfStream();
    }
    if ((impl_->state != Impl::State::Active &&
            impl_->state != Impl::State::Draining) ||
        impl_->context == nullptr || impl_->received_packet == nullptr ||
        impl_->packet_borrowed) {
        return Failure(VRREC_STATUS_INVALID_STATE);
    }

    const auto result =
        avcodec_receive_packet(impl_->context, impl_->received_packet);
    if (result == AVERROR(EAGAIN)) {
        return Again();
    }
    if (result == AVERROR_EOF) {
        impl_->side_data_views.clear();
        impl_->state = Impl::State::Finished;
        return EndOfStream();
    }
    if (result < 0) {
        impl_->side_data_views.clear();
        impl_->state = Impl::State::Failed;
        return Failure(ErrorStatus(result));
    }

    constexpr int SupportedPacketFlags =
        AV_PKT_FLAG_KEY | AV_PKT_FLAG_CORRUPT;
    if ((impl_->received_packet->flags & ~SupportedPacketFlags) != 0 ||
        impl_->received_packet->side_data_elems < 0 ||
        (impl_->received_packet->side_data_elems > 0 &&
            impl_->received_packet->side_data == nullptr)) {
        av_packet_unref(impl_->received_packet);
        impl_->side_data_views.clear();
        impl_->state = Impl::State::Failed;
        return Failure(VRREC_STATUS_INTERNAL_ERROR);
    }

    const auto side_data_count = static_cast<std::size_t>(
        impl_->received_packet->side_data_elems);
    try {
        impl_->side_data_views.resize(side_data_count);
    } catch (const std::bad_alloc &) {
        av_packet_unref(impl_->received_packet);
        impl_->side_data_views.clear();
        impl_->state = Impl::State::Failed;
        return Failure(VRREC_STATUS_OUT_OF_MEMORY);
    } catch (...) {
        av_packet_unref(impl_->received_packet);
        impl_->side_data_views.clear();
        impl_->state = Impl::State::Failed;
        return Failure(VRREC_STATUS_INTERNAL_ERROR);
    }

    for (std::size_t index = 0; index < side_data_count; ++index) {
        const auto &source = impl_->received_packet->side_data[index];
        if (source.size >
            static_cast<std::size_t>(std::numeric_limits<int>::max())) {
            av_packet_unref(impl_->received_packet);
            impl_->side_data_views.clear();
            impl_->state = Impl::State::Failed;
            return Failure(VRREC_STATUS_INTERNAL_ERROR);
        }
        auto &destination = impl_->side_data_views[index];
        destination.kind = source.type == AV_PKT_DATA_SKIP_SAMPLES
            ? FfmpegReceivedPacketSideDataKind::SkipSamples
            : FfmpegReceivedPacketSideDataKind::Unsupported;
        destination.data = reinterpret_cast<const std::byte *>(source.data);
        destination.size = static_cast<int>(source.size);
    }

    av_packet_rescale_ts(
        impl_->received_packet,
        impl_->context->time_base,
        AV_TIME_BASE_Q);

    packet.data = reinterpret_cast<const std::byte *>(
        impl_->received_packet->data);
    packet.size = impl_->received_packet->size;
    packet.pts_microseconds = MediaTimestamp(impl_->received_packet->pts);
    packet.dts_microseconds = MediaTimestamp(impl_->received_packet->dts);
    packet.duration_microseconds = impl_->received_packet->duration;
    packet.key_frame =
        (impl_->received_packet->flags & AV_PKT_FLAG_KEY) != 0;
    packet.corrupt =
        (impl_->received_packet->flags & AV_PKT_FLAG_CORRUPT) != 0;
    packet.side_data = side_data_count == 0
        ? nullptr
        : impl_->side_data_views.data();
    packet.side_data_count = side_data_count;
    impl_->packet_borrowed = true;
    return Ok();
}

void LibavcodecEncoderPort::UnrefReceivedPacket() noexcept
{
    if (impl_ == nullptr) {
        return;
    }
    if (impl_->received_packet != nullptr) {
        av_packet_unref(impl_->received_packet);
    }
    impl_->side_data_views.clear();
    impl_->packet_borrowed = false;
}

void LibavcodecEncoderPort::Abort() noexcept
{
    if (impl_ == nullptr || impl_->state == Impl::State::Aborted) {
        return;
    }
    impl_->state = Impl::State::Aborted;
    impl_->Release();
}

}
