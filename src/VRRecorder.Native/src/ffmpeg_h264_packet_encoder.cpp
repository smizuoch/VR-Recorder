#include "ffmpeg_h264_packet_encoder.hpp"

#include <chrono>
#include <limits>
#include <mutex>
#include <new>
#include <utility>

#include "ffmpeg_h264_software_codec_session.hpp"
#include "ffmpeg_h264_system_memory_packet_encoder_adapter.hpp"
#include "muxing_video_encoder_sink.hpp"

extern "C" {
#include <libavutil/frame.h>
}

namespace vrrecorder::native {

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
    auto opened = CreateFfmpegH264SoftwareCodecSession(config);
    if (opened.status != VRREC_STATUS_OK || opened.session == nullptr ||
        opened.owned_frame == nullptr) {
        av_frame_free(&opened.owned_frame);
        return {opened.status, nullptr};
    }
    return CreateWithSession(
        config,
        std::move(opened.session),
        opened.owned_frame);
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

FfmpegH264SystemMemoryPacketEncoderAdapter::
FfmpegH264SystemMemoryPacketEncoderAdapter(
    FfmpegH264PacketEncoder &encoder,
    SystemMemoryNv12FrameMapper &mapper,
    std::uint32_t frames_per_second) noexcept
    : encoder_(encoder),
      mapper_(mapper),
      frames_per_second_(frames_per_second)
{
}

PacketVideoEncoderWrite
FfmpegH264SystemMemoryPacketEncoderAdapter::Encode(
    const ScheduledVideoFrame &frame) noexcept
{
    if (aborted_.load() || finished_.load() ||
        frames_per_second_ == 0 ||
        frame.output_tick >
            std::numeric_limits<std::uint64_t>::max() / 1'000'000U) {
        return {VRREC_STATUS_INVALID_STATE, 0, {}};
    }
    const auto timestamp =
        frame.output_tick * 1'000'000U / frames_per_second_;
    if (timestamp > static_cast<std::uint64_t>(
            std::numeric_limits<std::int64_t>::max())) {
        return {VRREC_STATUS_INVALID_ARGUMENT, 0, {}};
    }

    auto mapped = mapper_.Map(frame);
    if (aborted_.load()) {
        return {VRREC_STATUS_INVALID_STATE, 0, {}};
    }
    if (mapped.status != VRREC_STATUS_OK || mapped.mapping == nullptr) {
        return {
            mapped.status != VRREC_STATUS_OK
                ? mapped.status
                : VRREC_STATUS_INTERNAL_ERROR,
            0,
            {},
        };
    }
    auto view = mapped.mapping->View();
    view.pts = static_cast<std::int64_t>(timestamp);

    const auto started = std::chrono::steady_clock::now();
    auto encoded = encoder_.EncodeNv12(view);
    const auto completed = std::chrono::steady_clock::now();
    const auto elapsed = std::chrono::duration_cast<
        std::chrono::microseconds>(completed - started).count();
    const auto latency = elapsed > 0
        ? static_cast<std::uint64_t>(elapsed)
        : 0U;
    return MakeMuxingVideoEncoderWrite(
        encoder_,
        std::move(encoded),
        latency);
}

PacketVideoEncoderWrite
FfmpegH264SystemMemoryPacketEncoderAdapter::Finish() noexcept
{
    if (aborted_.load() || finished_.exchange(true)) {
        return {VRREC_STATUS_INVALID_STATE, 0, {}};
    }
    const auto started = std::chrono::steady_clock::now();
    auto encoded = encoder_.Finish();
    const auto completed = std::chrono::steady_clock::now();
    const auto elapsed = std::chrono::duration_cast<
        std::chrono::microseconds>(completed - started).count();
    const auto latency = elapsed > 0
        ? static_cast<std::uint64_t>(elapsed)
        : 0U;
    return MakeMuxingVideoEncoderWrite(
        encoder_,
        std::move(encoded),
        latency);
}

void FfmpegH264SystemMemoryPacketEncoderAdapter::Abort() noexcept
{
    if (aborted_.exchange(true)) {
        return;
    }
    mapper_.Abort();
    encoder_.Abort();
}

}
