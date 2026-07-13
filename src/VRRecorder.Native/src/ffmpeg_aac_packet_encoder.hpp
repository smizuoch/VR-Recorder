#ifndef VRRECORDER_NATIVE_FFMPEG_AAC_PACKET_ENCODER_HPP
#define VRRECORDER_NATIVE_FFMPEG_AAC_PACKET_ENCODER_HPP

#include <cstddef>
#include <cstdint>
#include <memory>
#include <optional>
#include <span>

#include "audio_encoder_config.hpp"
#include "muxing_audio_encoder_sink.hpp"

namespace vrrecorder::native {

enum class FfmpegAacPacketEncoderFailurePoint {
    None,
    FindEncoder,
    AllocateContext,
    CopyChannelLayout,
    OpenCodecOutOfMemory,
    OpenCodecFailure,
    CopyDescriptorOutOfMemory,
    AllocateResampler,
    InitializeResampler,
    AllocateFifo,
    AllocateFrame,
    CopyFrameChannelLayout,
    AllocateFrameBuffer,
    AllocateImplementation,
    AllocateEncoder,
    MakeFrameWritable,
    QueryResamplerOutput,
    ConvertSamples,
    WriteFifo,
    ReadFifo,
    FlushResampler,
    AllocateConvertedFrame,
    CopyConvertedChannelLayout,
    AllocateConvertedFrameBuffer,
    PrepareFrameOutOfMemory,
    AppendPacketsOutOfMemory,
    DrainCodecFailure,
};

class FfmpegAacPreparedFrameObserver {
public:
    virtual ~FfmpegAacPreparedFrameObserver() = default;

    // Test-only hook invoked while the encoder operation mutex is held.
    // The spans are valid only for this call. Implementations must not re-enter
    // the encoder and must outlive it.
    virtual void Observe(
        std::int64_t pts_48k,
        std::span<const float> left,
        std::span<const float> right) noexcept = 0;
};

enum class FfmpegAacPacketEncoderOperation {
    Encode,
    Finish,
    Abort,
};

class FfmpegAacSerializationObserver {
public:
    virtual ~FfmpegAacSerializationObserver() = default;

    // Test-only hook invoked after try_lock proves that another encoder
    // operation owns the mutex. Implementations must not re-enter the encoder
    // and must outlive it.
    virtual void ObserveContention(
        FfmpegAacPacketEncoderOperation operation) noexcept = 0;
};

struct FfmpegAacPacketEncoderCreateResult;

class FfmpegAacPacketEncoder final : public PacketAudioEncoder {
public:
    ~FfmpegAacPacketEncoder() override;

    FfmpegAacPacketEncoder(const FfmpegAacPacketEncoder &) = delete;
    FfmpegAacPacketEncoder &operator=(
        const FfmpegAacPacketEncoder &) = delete;
    FfmpegAacPacketEncoder(FfmpegAacPacketEncoder &&) = delete;
    FfmpegAacPacketEncoder &operator=(FfmpegAacPacketEncoder &&) = delete;

    static FfmpegAacPacketEncoderCreateResult Create(
        const AacAudioEncoderConfig &config) noexcept;

#if defined(VRRECORDER_NATIVE_TESTING)
    static FfmpegAacPacketEncoderCreateResult CreateForTesting(
        const AacAudioEncoderConfig &config,
        unsigned int avcodec_version,
        unsigned int avutil_version,
        unsigned int swresample_version,
        const char *release_version,
        FfmpegAacPacketEncoderFailurePoint failure_point =
            FfmpegAacPacketEncoderFailurePoint::None,
        FfmpegAacPreparedFrameObserver *observer = nullptr,
        std::size_t fail_on_occurrence = 1,
        FfmpegAacSerializationObserver *serialization_observer =
            nullptr) noexcept;
#endif

    PacketAudioEncoderWrite EncodePcm48k(
        std::uint64_t start_frame_48k,
        std::span<const float> interleaved_samples) noexcept override;
    PacketAudioEncoderWrite Finish() noexcept override;
    void Abort() noexcept override;

private:
    class Impl;
    struct RuntimeIdentity;

    explicit FfmpegAacPacketEncoder(std::unique_ptr<Impl> impl) noexcept;
    static FfmpegAacPacketEncoderCreateResult CreateWithRuntimeIdentity(
        const AacAudioEncoderConfig &config,
        const RuntimeIdentity &runtime_identity,
        FfmpegAacPacketEncoderFailurePoint failure_point,
        FfmpegAacPreparedFrameObserver *observer,
        std::size_t fail_on_occurrence,
        FfmpegAacSerializationObserver *serialization_observer) noexcept;

    std::unique_ptr<Impl> impl_;
};

struct FfmpegAacPacketEncoderCreateResult final {
    vrrec_status_t status = VRREC_STATUS_INTERNAL_ERROR;
    std::unique_ptr<FfmpegAacPacketEncoder> encoder;
    std::optional<AacStreamDescriptor> descriptor;
};

}

#endif
