#include "ffmpeg_aac_packet_encoder.hpp"

#include <algorithm>
#include <cerrno>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <limits>
#include <mutex>
#include <new>
#include <utility>

#include "ffmpeg_libavcodec_encoder_port.hpp"

extern "C" {
#include <libavcodec/avcodec.h>
#include <libavcodec/codec.h>
#include <libavcodec/version.h>
#include <libavutil/audio_fifo.h>
#include <libavutil/avutil.h>
#include <libavutil/channel_layout.h>
#include <libavutil/error.h>
#include <libavutil/frame.h>
#include <libavutil/samplefmt.h>
#include <libavutil/version.h>
#include <libswresample/swresample.h>
#include <libswresample/version.h>
}

namespace vrrecorder::native {

namespace {

constexpr char PinnedFfmpegVersion[] = "8.1.2";
constexpr int AacSampleRate = 48'000;
constexpr int AacChannelCount = 2;
constexpr int AacBitRate = 192'000;
constexpr int AacFrameSize = 1'024;
constexpr int AacInitialPadding = 1'024;
constexpr std::byte ExpectedAudioSpecificConfig[] {
    std::byte {0x11},
    std::byte {0x90},
    std::byte {0x56},
    std::byte {0xe5},
    std::byte {0x00},
};

vrrec_status_t ErrorStatus(int error) noexcept
{
    return error == AVERROR(ENOMEM)
        ? VRREC_STATUS_OUT_OF_MEMORY
        : VRREC_STATUS_INTERNAL_ERROR;
}

PacketAudioEncoderWrite Failure(vrrec_status_t status) noexcept
{
    return {status, {}};
}

bool IsConfigValid(const AacAudioEncoderConfig &config) noexcept
{
    return config.profile == AacProfile::LowComplexity &&
        config.sample_rate == static_cast<std::uint32_t>(AacSampleRate) &&
        config.channel_count == static_cast<std::uint32_t>(AacChannelCount) &&
        config.channel_layout == AudioChannelLayout::Stereo &&
        config.bitrate_bits_per_second ==
            static_cast<std::uint32_t>(AacBitRate) &&
        config.source_sample_format ==
            AudioSampleFormat::Float32Interleaved;
}

bool CanRescaleSampleTimestampToMicroseconds(
    std::uint64_t frame_48k) noexcept
{
    constexpr std::uint64_t FramesPerSecond = 48'000;
    constexpr std::uint64_t MicrosecondsPerSecond = 1'000'000;
    constexpr auto MaximumTimestamp = static_cast<std::uint64_t>(
        std::numeric_limits<std::int64_t>::max());
    const auto seconds = frame_48k / FramesPerSecond;
    const auto remaining_frames = frame_48k % FramesPerSecond;
    if (seconds > MaximumTimestamp / MicrosecondsPerSecond) {
        return false;
    }
    const auto whole_microseconds = seconds * MicrosecondsPerSecond;
    const auto rounded_microseconds =
        (remaining_frames * MicrosecondsPerSecond +
            FramesPerSecond / 2U) /
        FramesPerSecond;
    return rounded_microseconds <= MaximumTimestamp - whole_microseconds;
}

bool SupportsSampleFormat(
    const AVCodec &codec,
    AVSampleFormat expected) noexcept
{
    const void *configurations = nullptr;
    int configuration_count = 0;
    if (avcodec_get_supported_config(
            nullptr,
            &codec,
            AV_CODEC_CONFIG_SAMPLE_FORMAT,
            0,
            &configurations,
            &configuration_count) < 0 ||
        configurations == nullptr || configuration_count <= 0) {
        return false;
    }
    const auto *sample_formats =
        static_cast<const AVSampleFormat *>(configurations);
    return std::find(
               sample_formats,
               sample_formats + configuration_count,
               expected) != sample_formats + configuration_count;
}

bool SupportsSampleRate(const AVCodec &codec, int expected) noexcept
{
    const void *configurations = nullptr;
    int configuration_count = 0;
    if (avcodec_get_supported_config(
            nullptr,
            &codec,
            AV_CODEC_CONFIG_SAMPLE_RATE,
            0,
            &configurations,
            &configuration_count) < 0) {
        return false;
    }
    if (configurations == nullptr) {
        return true;
    }
    const auto *sample_rates = static_cast<const int *>(configurations);
    return std::find(
               sample_rates,
               sample_rates + configuration_count,
               expected) != sample_rates + configuration_count;
}

bool IsOpenedContextValid(
    AVCodecContext &context,
    const AVCodec &codec) noexcept
{
    const AVChannelLayout stereo = AV_CHANNEL_LAYOUT_STEREO;
    return avcodec_is_open(&context) != 0 &&
        context.codec == &codec &&
        context.codec_type == AVMEDIA_TYPE_AUDIO &&
        context.codec_id == AV_CODEC_ID_AAC &&
        context.sample_fmt == AV_SAMPLE_FMT_FLTP &&
        context.sample_rate == AacSampleRate &&
        context.bit_rate == AacBitRate &&
        context.profile == AV_PROFILE_AAC_LOW &&
        context.time_base.num == 1 &&
        context.time_base.den == AacSampleRate &&
        av_channel_layout_compare(&context.ch_layout, &stereo) == 0 &&
        context.frame_size == AacFrameSize &&
        context.initial_padding == AacInitialPadding &&
        (context.flags & AV_CODEC_FLAG_GLOBAL_HEADER) != 0 &&
        context.extradata != nullptr &&
        context.extradata_size ==
            static_cast<int>(std::size(ExpectedAudioSpecificConfig)) &&
        std::memcmp(
            context.extradata,
            ExpectedAudioSpecificConfig,
            std::size(ExpectedAudioSpecificConfig)) == 0;
}

void FreePreparationResources(
    SwrContext *&resampler,
    AVAudioFifo *&fifo,
    AVFrame *&frame) noexcept
{
    av_frame_free(&frame);
    av_audio_fifo_free(fifo);
    fifo = nullptr;
    swr_free(&resampler);
}

vrrec_status_t AppendPackets(
    std::vector<EncodedMediaPacket> &destination,
    FfmpegEncodeBatch source) noexcept
{
    if (source.status != VRREC_STATUS_OK) {
        return source.status;
    }
    if (source.packets.empty()) {
        return VRREC_STATUS_OK;
    }
    if (source.packets.size() >
        destination.max_size() - destination.size()) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
    try {
        destination.reserve(destination.size() + source.packets.size());
        for (auto &packet : source.packets) {
            destination.push_back(std::move(packet));
        }
        return VRREC_STATUS_OK;
    } catch (const std::bad_alloc &) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

}

struct FfmpegAacPacketEncoder::RuntimeIdentity final {
    unsigned int avcodec_version;
    unsigned int avutil_version;
    unsigned int swresample_version;
    const char *release_version;
};

class FfmpegAacPacketEncoder::Impl final {
public:
    enum class State {
        Active,
        Finished,
        Failed,
        Aborted,
    };

    Impl(
        std::unique_ptr<LibavcodecEncoderPort> port_value,
        SwrContext *resampler_value,
        AVAudioFifo *fifo_value,
        AVFrame *frame_value,
        FfmpegAacPacketEncoderFailurePoint failure_point_value,
        FfmpegAacPreparedFrameObserver *observer_value,
        std::size_t fail_on_occurrence_value,
        std::size_t failure_occurrence_value,
        FfmpegAacSerializationObserver *serialization_observer_value) noexcept
        : port(std::move(port_value)),
          state_machine(*port, MediaStreamKind::Audio),
          resampler(resampler_value),
          fifo(fifo_value),
          frame(frame_value),
          failure_point(failure_point_value),
          observer(observer_value),
          fail_on_occurrence(fail_on_occurrence_value),
          failure_occurrence(failure_occurrence_value),
          serialization_observer(serialization_observer_value)
    {
    }

    ~Impl()
    {
        FreePreparationResources(resampler, fifo, frame);
    }

    Impl(const Impl &) = delete;
    Impl &operator=(const Impl &) = delete;

    PacketAudioEncoderWrite FailLocked(vrrec_status_t status) noexcept
    {
        state = State::Failed;
        state_machine.Abort();
        if (fifo != nullptr) {
            av_audio_fifo_reset(fifo);
        }
        return Failure(status);
    }

    vrrec_status_t QueueConverted(
        std::span<const float> interleaved_samples) noexcept
    {
        const auto frame_count = interleaved_samples.size() /
            static_cast<std::size_t>(AacChannelCount);
        if (frame_count >
            static_cast<std::size_t>(std::numeric_limits<int>::max())) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }
        const auto input_count = static_cast<int>(frame_count);
        const auto output_capacity =
            ShouldFail(
                FfmpegAacPacketEncoderFailurePoint::QueryResamplerOutput)
            ? AVERROR(EIO)
            : swr_get_out_samples(resampler, input_count);
        if (output_capacity <= 0) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }

        AVFrame *converted =
            ShouldFail(
                FfmpegAacPacketEncoderFailurePoint::AllocateConvertedFrame)
            ? nullptr
            : av_frame_alloc();
        if (converted == nullptr) {
            return VRREC_STATUS_OUT_OF_MEMORY;
        }
        converted->format = AV_SAMPLE_FMT_FLTP;
        converted->sample_rate = AacSampleRate;
        converted->nb_samples = output_capacity;
        const AVChannelLayout stereo = AV_CHANNEL_LAYOUT_STEREO;
        auto result =
            ShouldFail(
                FfmpegAacPacketEncoderFailurePoint::
                    CopyConvertedChannelLayout)
            ? AVERROR(ENOMEM)
            : av_channel_layout_copy(
                &converted->ch_layout,
                &stereo);
        if (result >= 0) {
            result =
                ShouldFail(
                    FfmpegAacPacketEncoderFailurePoint::
                        AllocateConvertedFrameBuffer)
                ? AVERROR(ENOMEM)
                : av_frame_get_buffer(converted, 0);
        }
        if (result < 0) {
            av_frame_free(&converted);
            return ErrorStatus(result);
        }

        const std::uint8_t *input_data[] {
            reinterpret_cast<const std::uint8_t *>(
                interleaved_samples.data()),
        };
        const auto converted_count =
            ShouldFail(FfmpegAacPacketEncoderFailurePoint::ConvertSamples)
            ? AVERROR(EIO)
            : swr_convert(
                resampler,
                converted->extended_data,
                output_capacity,
                input_data,
                input_count);
        if (converted_count < 0 || converted_count > output_capacity) {
            av_frame_free(&converted);
            return converted_count < 0
                ? ErrorStatus(converted_count)
                : VRREC_STATUS_INTERNAL_ERROR;
        }

        if (converted_count > 0) {
            const auto written =
                ShouldFail(FfmpegAacPacketEncoderFailurePoint::WriteFifo)
                ? AVERROR(EIO)
                : av_audio_fifo_write(
                    fifo,
                    reinterpret_cast<void *const *>(
                        converted->extended_data),
                    converted_count);
            if (written != converted_count) {
                av_frame_free(&converted);
                return written < 0
                    ? ErrorStatus(written)
                    : VRREC_STATUS_INTERNAL_ERROR;
            }
        }
        av_frame_free(&converted);
        return VRREC_STATUS_OK;
    }

    vrrec_status_t FlushResamplerToFifo() noexcept
    {
        if (ShouldFail(
                FfmpegAacPacketEncoderFailurePoint::FlushResampler)) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        for (;;) {
            const auto output_capacity =
                ShouldFail(
                    FfmpegAacPacketEncoderFailurePoint::
                        QueryResamplerOutput)
                ? AVERROR(EIO)
                : swr_get_out_samples(resampler, 0);
            if (output_capacity < 0) {
                return ErrorStatus(output_capacity);
            }
            if (output_capacity == 0) {
                return VRREC_STATUS_OK;
            }

            AVFrame *converted =
                ShouldFail(
                    FfmpegAacPacketEncoderFailurePoint::
                        AllocateConvertedFrame)
                ? nullptr
                : av_frame_alloc();
            if (converted == nullptr) {
                return VRREC_STATUS_OUT_OF_MEMORY;
            }
            converted->format = AV_SAMPLE_FMT_FLTP;
            converted->sample_rate = AacSampleRate;
            converted->nb_samples = output_capacity;
            const AVChannelLayout stereo = AV_CHANNEL_LAYOUT_STEREO;
            auto result =
                ShouldFail(
                    FfmpegAacPacketEncoderFailurePoint::
                        CopyConvertedChannelLayout)
                ? AVERROR(ENOMEM)
                : av_channel_layout_copy(
                    &converted->ch_layout,
                    &stereo);
            if (result >= 0) {
                result =
                    ShouldFail(
                        FfmpegAacPacketEncoderFailurePoint::
                            AllocateConvertedFrameBuffer)
                    ? AVERROR(ENOMEM)
                    : av_frame_get_buffer(converted, 0);
            }
            if (result < 0) {
                av_frame_free(&converted);
                return ErrorStatus(result);
            }

            const auto converted_count = swr_convert(
                resampler,
                converted->extended_data,
                output_capacity,
                nullptr,
                0);
            if (converted_count < 0 || converted_count > output_capacity) {
                av_frame_free(&converted);
                return converted_count < 0
                    ? ErrorStatus(converted_count)
                    : VRREC_STATUS_INTERNAL_ERROR;
            }
            if (converted_count == 0) {
                av_frame_free(&converted);
                return VRREC_STATUS_OK;
            }
            const auto written =
                ShouldFail(FfmpegAacPacketEncoderFailurePoint::WriteFifo)
                ? AVERROR(EIO)
                : av_audio_fifo_write(
                    fifo,
                    reinterpret_cast<void *const *>(
                        converted->extended_data),
                    converted_count);
            av_frame_free(&converted);
            if (written != converted_count) {
                return written < 0
                    ? ErrorStatus(written)
                    : VRREC_STATUS_INTERNAL_ERROR;
            }
        }
    }

    vrrec_status_t EncodeOneFrame(
        int sample_count,
        std::vector<EncodedMediaPacket> &packets) noexcept
    {
        if (sample_count <= 0 || sample_count > AacFrameSize ||
            frame == nullptr || fifo == nullptr ||
            av_audio_fifo_size(fifo) < sample_count ||
            next_frame_pts_48k < 0) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        auto result =
            ShouldFail(
                FfmpegAacPacketEncoderFailurePoint::MakeFrameWritable)
            ? AVERROR(ENOMEM)
            : av_frame_make_writable(frame);
        if (result < 0) {
            return ErrorStatus(result);
        }
        frame->nb_samples = sample_count;
        frame->pts = next_frame_pts_48k;
        const auto read =
            ShouldFail(FfmpegAacPacketEncoderFailurePoint::ReadFifo)
            ? AVERROR(EIO)
            : av_audio_fifo_read(
                fifo,
                reinterpret_cast<void *const *>(frame->extended_data),
                sample_count);
        if (read != sample_count) {
            return read < 0
                ? ErrorStatus(read)
                : VRREC_STATUS_INTERNAL_ERROR;
        }

        if (observer != nullptr) {
            observer->Observe(
                frame->pts,
                {
                    reinterpret_cast<const float *>(
                        frame->extended_data[0]),
                    static_cast<std::size_t>(sample_count),
                },
                {
                    reinterpret_cast<const float *>(
                        frame->extended_data[1]),
                    static_cast<std::size_t>(sample_count),
                });
        }
        result = ShouldFail(
                     FfmpegAacPacketEncoderFailurePoint::
                         PrepareFrameOutOfMemory)
            ? VRREC_STATUS_OUT_OF_MEMORY
            : port->PrepareFrame(*frame);
        if (result != VRREC_STATUS_OK) {
            return result;
        }
        auto batch = state_machine.EncodePreparedFrame();
        if (!batch.packets.empty() &&
            ShouldFail(
                FfmpegAacPacketEncoderFailurePoint::
                    AppendPacketsOutOfMemory)) {
            return VRREC_STATUS_OUT_OF_MEMORY;
        }
        const auto append_status = AppendPackets(packets, std::move(batch));
        if (append_status != VRREC_STATUS_OK) {
            return append_status;
        }
        if (next_frame_pts_48k >
            std::numeric_limits<std::int64_t>::max() - sample_count) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        next_frame_pts_48k += sample_count;
        return VRREC_STATUS_OK;
    }

    vrrec_status_t EncodeFullFrames(
        std::vector<EncodedMediaPacket> &packets) noexcept
    {
        while (av_audio_fifo_size(fifo) >= AacFrameSize) {
            const auto status = EncodeOneFrame(AacFrameSize, packets);
            if (status != VRREC_STATUS_OK) {
                return status;
            }
        }
        return VRREC_STATUS_OK;
    }

    bool ShouldFail(
        FfmpegAacPacketEncoderFailurePoint point) noexcept
    {
        if (failure_point != point) {
            return false;
        }
        ++failure_occurrence;
        return failure_occurrence == fail_on_occurrence;
    }

    std::unique_lock<std::mutex> AcquireLock(
        FfmpegAacPacketEncoderOperation operation) noexcept
    {
        std::unique_lock lock(mutex, std::defer_lock);
        if (serialization_observer == nullptr) {
            lock.lock();
            return lock;
        }
        if (!lock.try_lock()) {
            serialization_observer->ObserveContention(operation);
            lock.lock();
        }
        return lock;
    }

    std::unique_ptr<LibavcodecEncoderPort> port;
    FfmpegEncoderStateMachine state_machine;
    SwrContext *resampler = nullptr;
    AVAudioFifo *fifo = nullptr;
    AVFrame *frame = nullptr;
    FfmpegAacPacketEncoderFailurePoint failure_point =
        FfmpegAacPacketEncoderFailurePoint::None;
    FfmpegAacPreparedFrameObserver *observer = nullptr;
    std::size_t fail_on_occurrence = 1;
    std::size_t failure_occurrence = 0;
    FfmpegAacSerializationObserver *serialization_observer = nullptr;
    std::mutex mutex;
    State state = State::Active;
    std::uint64_t next_input_frame_48k = 0;
    std::int64_t next_frame_pts_48k = 0;
    bool has_input = false;
};

FfmpegAacPacketEncoder::FfmpegAacPacketEncoder(
    std::unique_ptr<Impl> impl) noexcept
    : impl_(std::move(impl))
{
}

FfmpegAacPacketEncoder::~FfmpegAacPacketEncoder()
{
    Abort();
}

FfmpegAacPacketEncoderCreateResult FfmpegAacPacketEncoder::Create(
    const AacAudioEncoderConfig &config) noexcept
{
    return CreateWithRuntimeIdentity(
        config,
        {
            avcodec_version(),
            avutil_version(),
            swresample_version(),
            av_version_info(),
        },
        FfmpegAacPacketEncoderFailurePoint::None,
        nullptr,
        1,
        nullptr);
}

#if defined(VRRECORDER_NATIVE_TESTING)
FfmpegAacPacketEncoderCreateResult
FfmpegAacPacketEncoder::CreateForTesting(
    const AacAudioEncoderConfig &config,
    unsigned int avcodec_version_value,
    unsigned int avutil_version_value,
    unsigned int swresample_version_value,
    const char *release_version,
    FfmpegAacPacketEncoderFailurePoint failure_point,
    FfmpegAacPreparedFrameObserver *observer,
    std::size_t fail_on_occurrence,
    FfmpegAacSerializationObserver *serialization_observer) noexcept
{
    return CreateWithRuntimeIdentity(
        config,
        {
            avcodec_version_value,
            avutil_version_value,
            swresample_version_value,
            release_version,
        },
        failure_point,
        observer,
        fail_on_occurrence,
        serialization_observer);
}
#endif

FfmpegAacPacketEncoderCreateResult
FfmpegAacPacketEncoder::CreateWithRuntimeIdentity(
    const AacAudioEncoderConfig &config,
    const RuntimeIdentity &runtime_identity,
    FfmpegAacPacketEncoderFailurePoint failure_point,
    FfmpegAacPreparedFrameObserver *observer,
    std::size_t fail_on_occurrence,
    FfmpegAacSerializationObserver *serialization_observer) noexcept
{
    if (!IsConfigValid(config) || fail_on_occurrence == 0) {
        return {VRREC_STATUS_INVALID_ARGUMENT, nullptr, std::nullopt};
    }
    if (runtime_identity.avcodec_version != LIBAVCODEC_VERSION_INT ||
        runtime_identity.avutil_version != LIBAVUTIL_VERSION_INT ||
        runtime_identity.swresample_version !=
            LIBSWRESAMPLE_VERSION_INT ||
        runtime_identity.release_version == nullptr ||
        std::strcmp(
            runtime_identity.release_version,
            PinnedFfmpegVersion) != 0) {
        return {VRREC_STATUS_BACKEND_UNAVAILABLE, nullptr, std::nullopt};
    }

    std::size_t failure_occurrence = 0;
    const auto should_fail = [&](FfmpegAacPacketEncoderFailurePoint point) {
        if (failure_point != point) {
            return false;
        }
        ++failure_occurrence;
        return failure_occurrence == fail_on_occurrence;
    };

    const AVCodec *codec =
        should_fail(FfmpegAacPacketEncoderFailurePoint::FindEncoder)
        ? nullptr
        : avcodec_find_encoder(AV_CODEC_ID_AAC);
    if (codec == nullptr || codec->name == nullptr ||
        std::strcmp(codec->name, "aac") != 0 ||
        codec->type != AVMEDIA_TYPE_AUDIO ||
        codec->id != AV_CODEC_ID_AAC || av_codec_is_encoder(codec) == 0 ||
        (codec->capabilities & AV_CODEC_CAP_DELAY) == 0 ||
        (codec->capabilities & AV_CODEC_CAP_SMALL_LAST_FRAME) == 0 ||
        (codec->capabilities & AV_CODEC_CAP_VARIABLE_FRAME_SIZE) != 0 ||
        !SupportsSampleFormat(*codec, AV_SAMPLE_FMT_FLTP) ||
        !SupportsSampleRate(*codec, AacSampleRate)) {
        return {VRREC_STATUS_BACKEND_UNAVAILABLE, nullptr, std::nullopt};
    }

    AVCodecContext *context =
        should_fail(FfmpegAacPacketEncoderFailurePoint::AllocateContext)
        ? nullptr
        : avcodec_alloc_context3(codec);
    if (context == nullptr) {
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr, std::nullopt};
    }
    context->bit_rate = AacBitRate;
    context->sample_rate = AacSampleRate;
    context->sample_fmt = AV_SAMPLE_FMT_FLTP;
    context->profile = AV_PROFILE_AAC_LOW;
    context->time_base = {1, AacSampleRate};
    context->flags |= AV_CODEC_FLAG_GLOBAL_HEADER;
    const AVChannelLayout stereo = AV_CHANNEL_LAYOUT_STEREO;
    const auto layout_result =
        should_fail(
            FfmpegAacPacketEncoderFailurePoint::CopyChannelLayout)
        ? AVERROR(ENOMEM)
        : av_channel_layout_copy(&context->ch_layout, &stereo);
    if (layout_result < 0) {
        avcodec_free_context(&context);
        return {ErrorStatus(layout_result), nullptr, std::nullopt};
    }

    const auto open_result =
        should_fail(
            FfmpegAacPacketEncoderFailurePoint::OpenCodecOutOfMemory)
        ? AVERROR(ENOMEM)
        : should_fail(
            FfmpegAacPacketEncoderFailurePoint::OpenCodecFailure)
        ? AVERROR(EIO)
        : avcodec_open2(context, codec, nullptr);
    if (open_result < 0) {
        avcodec_free_context(&context);
        return {ErrorStatus(open_result), nullptr, std::nullopt};
    }
    if (!IsOpenedContextValid(*context, *codec)) {
        avcodec_free_context(&context);
        return {VRREC_STATUS_BACKEND_UNAVAILABLE, nullptr, std::nullopt};
    }

    std::optional<AacStreamDescriptor> descriptor;
    try {
        if (should_fail(
                FfmpegAacPacketEncoderFailurePoint::
                    CopyDescriptorOutOfMemory)) {
            throw std::bad_alloc();
        }
        descriptor.emplace(AacStreamDescriptor {
            MicrosecondPacketTimeBase,
            static_cast<std::uint32_t>(context->sample_rate),
            static_cast<std::uint32_t>(context->ch_layout.nb_channels),
            static_cast<std::uint32_t>(context->frame_size),
            static_cast<std::uint32_t>(context->initial_padding),
            AacProfile::LowComplexity,
            AudioChannelLayout::Stereo,
            AacPacketFormat::RawAccessUnit,
            {
                reinterpret_cast<const std::byte *>(context->extradata),
                reinterpret_cast<const std::byte *>(context->extradata) +
                    context->extradata_size,
            },
        });
    } catch (const std::bad_alloc &) {
        avcodec_free_context(&context);
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr, std::nullopt};
    } catch (...) {
        avcodec_free_context(&context);
        return {VRREC_STATUS_INTERNAL_ERROR, nullptr, std::nullopt};
    }

    SwrContext *resampler = nullptr;
    auto result =
        should_fail(FfmpegAacPacketEncoderFailurePoint::AllocateResampler)
        ? AVERROR(ENOMEM)
        : swr_alloc_set_opts2(
            &resampler,
            &stereo,
            AV_SAMPLE_FMT_FLTP,
            AacSampleRate,
            &stereo,
            AV_SAMPLE_FMT_FLT,
            AacSampleRate,
            0,
            nullptr);
    if (result >= 0) {
        result =
            should_fail(
                FfmpegAacPacketEncoderFailurePoint::InitializeResampler)
            ? AVERROR(EIO)
            : swr_init(resampler);
    }
    if (result < 0 || resampler == nullptr) {
        swr_free(&resampler);
        avcodec_free_context(&context);
        return {
            result < 0 ? ErrorStatus(result) : VRREC_STATUS_OUT_OF_MEMORY,
            nullptr,
            std::nullopt,
        };
    }

    AVAudioFifo *fifo =
        should_fail(FfmpegAacPacketEncoderFailurePoint::AllocateFifo)
        ? nullptr
        : av_audio_fifo_alloc(
            AV_SAMPLE_FMT_FLTP,
            AacChannelCount,
            AacFrameSize);
    if (fifo == nullptr) {
        swr_free(&resampler);
        avcodec_free_context(&context);
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr, std::nullopt};
    }

    AVFrame *frame =
        should_fail(FfmpegAacPacketEncoderFailurePoint::AllocateFrame)
        ? nullptr
        : av_frame_alloc();
    if (frame == nullptr) {
        FreePreparationResources(resampler, fifo, frame);
        avcodec_free_context(&context);
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr, std::nullopt};
    }
    frame->format = AV_SAMPLE_FMT_FLTP;
    frame->sample_rate = AacSampleRate;
    frame->nb_samples = AacFrameSize;
    result =
        should_fail(
            FfmpegAacPacketEncoderFailurePoint::CopyFrameChannelLayout)
        ? AVERROR(ENOMEM)
        : av_channel_layout_copy(&frame->ch_layout, &stereo);
    if (result >= 0) {
        result =
            should_fail(
                FfmpegAacPacketEncoderFailurePoint::AllocateFrameBuffer)
            ? AVERROR(ENOMEM)
            : av_frame_get_buffer(frame, 0);
    }
    if (result < 0) {
        FreePreparationResources(resampler, fifo, frame);
        avcodec_free_context(&context);
        return {ErrorStatus(result), nullptr, std::nullopt};
    }

    auto port_result = LibavcodecEncoderPort::Create(context);
    context = nullptr;
    if (port_result.status != VRREC_STATUS_OK || port_result.port == nullptr) {
        FreePreparationResources(resampler, fifo, frame);
        return {
            port_result.status == VRREC_STATUS_OK
                ? VRREC_STATUS_INTERNAL_ERROR
                : port_result.status,
            nullptr,
            std::nullopt,
        };
    }

    std::unique_ptr<Impl> impl;
    if (!should_fail(
            FfmpegAacPacketEncoderFailurePoint::AllocateImplementation)) {
        impl.reset(new (std::nothrow) Impl(
            std::move(port_result.port),
            resampler,
            fifo,
            frame,
            failure_point,
            observer,
            fail_on_occurrence,
            failure_occurrence,
            serialization_observer));
    }
    if (impl == nullptr) {
        FreePreparationResources(resampler, fifo, frame);
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr, std::nullopt};
    }
    resampler = nullptr;
    fifo = nullptr;
    frame = nullptr;

    std::unique_ptr<FfmpegAacPacketEncoder> encoder;
    if (!should_fail(FfmpegAacPacketEncoderFailurePoint::AllocateEncoder)) {
        encoder.reset(
            new (std::nothrow) FfmpegAacPacketEncoder(std::move(impl)));
    }
    if (encoder == nullptr) {
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr, std::nullopt};
    }
    return {
        VRREC_STATUS_OK,
        std::move(encoder),
        std::move(descriptor),
    };
}

PacketAudioEncoderWrite FfmpegAacPacketEncoder::EncodePcm48k(
    std::uint64_t start_frame_48k,
    std::span<const float> interleaved_samples) noexcept
{
    if (impl_ == nullptr) {
        return Failure(VRREC_STATUS_INVALID_STATE);
    }
    auto lock = impl_->AcquireLock(
        FfmpegAacPacketEncoderOperation::Encode);
    if (impl_->state != Impl::State::Active) {
        return Failure(VRREC_STATUS_INVALID_STATE);
    }
    if (interleaved_samples.empty() ||
        interleaved_samples.size() % AacChannelCount != 0) {
        return impl_->FailLocked(VRREC_STATUS_INVALID_ARGUMENT);
    }

    const auto frame_count = interleaved_samples.size() /
        static_cast<std::size_t>(AacChannelCount);
    if (frame_count >
            static_cast<std::size_t>(std::numeric_limits<int>::max()) ||
        start_frame_48k >
            static_cast<std::uint64_t>(
                std::numeric_limits<std::int64_t>::max()) ||
        frame_count >
            static_cast<std::uint64_t>(
                std::numeric_limits<std::int64_t>::max()) -
                start_frame_48k ||
        !CanRescaleSampleTimestampToMicroseconds(start_frame_48k) ||
        !CanRescaleSampleTimestampToMicroseconds(
            start_frame_48k + frame_count) ||
        (impl_->has_input &&
            start_frame_48k != impl_->next_input_frame_48k)) {
        return impl_->FailLocked(VRREC_STATUS_INVALID_ARGUMENT);
    }
    if (!std::all_of(
            interleaved_samples.begin(),
            interleaved_samples.end(),
            [](float sample) { return std::isfinite(sample); })) {
        return impl_->FailLocked(VRREC_STATUS_INVALID_ARGUMENT);
    }

    if (!impl_->has_input) {
        impl_->next_frame_pts_48k =
            static_cast<std::int64_t>(start_frame_48k);
    }
    const auto queue_status = impl_->QueueConverted(interleaved_samples);
    if (queue_status != VRREC_STATUS_OK) {
        return impl_->FailLocked(queue_status);
    }
    impl_->has_input = true;
    impl_->next_input_frame_48k = start_frame_48k + frame_count;

    std::vector<EncodedMediaPacket> packets;
    const auto encode_status = impl_->EncodeFullFrames(packets);
    if (encode_status != VRREC_STATUS_OK) {
        return impl_->FailLocked(encode_status);
    }
    return {VRREC_STATUS_OK, std::move(packets)};
}

PacketAudioEncoderWrite FfmpegAacPacketEncoder::Finish() noexcept
{
    if (impl_ == nullptr) {
        return Failure(VRREC_STATUS_INVALID_STATE);
    }
    auto lock = impl_->AcquireLock(
        FfmpegAacPacketEncoderOperation::Finish);
    if (impl_->state != Impl::State::Active) {
        return Failure(VRREC_STATUS_INVALID_STATE);
    }

    std::vector<EncodedMediaPacket> packets;
    auto status = impl_->FlushResamplerToFifo();
    if (status == VRREC_STATUS_OK) {
        status = impl_->EncodeFullFrames(packets);
    }
    if (status == VRREC_STATUS_OK) {
        const auto remaining = av_audio_fifo_size(impl_->fifo);
        if (remaining < 0 || remaining >= AacFrameSize) {
            status = VRREC_STATUS_INTERNAL_ERROR;
        } else if (remaining > 0) {
            status = impl_->EncodeOneFrame(remaining, packets);
        }
    }
    if (status != VRREC_STATUS_OK) {
        return impl_->FailLocked(status);
    }

    if (impl_->ShouldFail(
            FfmpegAacPacketEncoderFailurePoint::DrainCodecFailure)) {
        return impl_->FailLocked(VRREC_STATUS_INTERNAL_ERROR);
    }
    auto final_batch = impl_->state_machine.Finish();
    if (!final_batch.packets.empty() &&
        impl_->ShouldFail(
            FfmpegAacPacketEncoderFailurePoint::
                AppendPacketsOutOfMemory)) {
        return impl_->FailLocked(VRREC_STATUS_OUT_OF_MEMORY);
    }
    const auto append_status = AppendPackets(
        packets,
        std::move(final_batch));
    if (append_status != VRREC_STATUS_OK) {
        return impl_->FailLocked(append_status);
    }
    impl_->state = Impl::State::Finished;
    return {VRREC_STATUS_OK, std::move(packets)};
}

void FfmpegAacPacketEncoder::Abort() noexcept
{
    if (impl_ == nullptr) {
        return;
    }
    auto lock = impl_->AcquireLock(
        FfmpegAacPacketEncoderOperation::Abort);
    if (impl_->state == Impl::State::Finished ||
        impl_->state == Impl::State::Aborted) {
        return;
    }
    if (impl_->state == Impl::State::Active) {
        impl_->state_machine.Abort();
    }
    if (impl_->fifo != nullptr) {
        av_audio_fifo_reset(impl_->fifo);
    }
    impl_->state = Impl::State::Aborted;
}

}
