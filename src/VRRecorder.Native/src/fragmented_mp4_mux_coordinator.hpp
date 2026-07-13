#ifndef VRRECORDER_NATIVE_FRAGMENTED_MP4_MUX_COORDINATOR_HPP
#define VRRECORDER_NATIVE_FRAGMENTED_MP4_MUX_COORDINATOR_HPP

#include <atomic>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <mutex>
#include <span>
#include <vector>

#include "audio_encoder_config.hpp"
#include "video_encoder_config.hpp"
#include "vrrecorder_native.h"

namespace vrrecorder::native {

enum class MediaStreamKind {
    Video,
    Audio,
};

struct MediaTimeBase final {
    std::int32_t numerator;
    std::int32_t denominator;

    bool operator==(const MediaTimeBase &) const = default;
};

inline constexpr MediaTimeBase MicrosecondPacketTimeBase {1, 1'000'000};
inline constexpr std::int64_t UnknownMediaTimestamp =
    std::numeric_limits<std::int64_t>::min();

enum class H264PacketFormat {
    AnnexB,
    AvccLengthPrefixed,
};

enum class AacPacketFormat {
    RawAccessUnit,
};

struct H264StreamDescriptor final {
    MediaTimeBase packet_time_base;
    std::uint32_t width;
    std::uint32_t height;
    H264Profile profile;
    H264PacketFormat packet_format;
    std::vector<std::byte> codec_extradata;
};

struct AacStreamDescriptor final {
    MediaTimeBase packet_time_base;
    std::uint32_t sample_rate;
    std::uint32_t channel_count;
    std::uint32_t frame_size;
    std::uint32_t initial_padding_samples;
    AacProfile profile;
    AudioChannelLayout channel_layout;
    AacPacketFormat packet_format;
    std::vector<std::byte> codec_extradata;
    std::uint32_t bitrate_bits_per_second;
};

inline std::int64_t AacPrimingLowerBoundMicroseconds(
    const AacStreamDescriptor &audio) noexcept
{
    if (audio.sample_rate == 0 || audio.initial_padding_samples == 0) {
        return 0;
    }
    const auto scaled_samples =
        static_cast<std::uint64_t>(audio.initial_padding_samples) *
        static_cast<std::uint64_t>(MicrosecondPacketTimeBase.denominator);
    const auto magnitude =
        (scaled_samples + audio.sample_rate - 1U) / audio.sample_rate;
    return -static_cast<std::int64_t>(magnitude);
}

struct FragmentedMp4FragmentPolicy final {
    std::int64_t minimum_duration_microseconds;
    std::int64_t maximum_duration_microseconds;
    bool prefer_video_key_frames;

    bool operator==(const FragmentedMp4FragmentPolicy &) const = default;
};

inline constexpr FragmentedMp4FragmentPolicy
    DefaultFragmentedMp4FragmentPolicy {
        1'000'000,
        2'000'000,
        true,
    };

struct FragmentedMp4StreamConfiguration final {
    H264StreamDescriptor video;
    AacStreamDescriptor audio;
    FragmentedMp4FragmentPolicy fragment_policy;
};

enum class EncodedPacketSideDataKind {
    SkipSamples,
};

inline constexpr std::size_t SkipSamplesSideDataSize = 10;

struct EncodedPacketSideData final {
    EncodedPacketSideDataKind kind;
    std::vector<std::byte> payload;

    bool operator==(const EncodedPacketSideData &) const = default;
};

struct EncodedMediaPacket final {
    MediaStreamKind stream;
    std::int64_t pts_microseconds;
    std::int64_t dts_microseconds;
    std::int64_t duration_microseconds;
    bool key_frame;
    std::vector<std::byte> payload;
    std::vector<EncodedPacketSideData> side_data {};
};

class EncodedMediaPacketObserver {
public:
    virtual ~EncodedMediaPacketObserver() = default;

    virtual vrrec_status_t Observe(
        const EncodedMediaPacket &packet) noexcept = 0;
};

class FragmentedMp4Muxer {
public:
    virtual ~FragmentedMp4Muxer() = default;

    virtual vrrec_status_t WriteHeader(
        const FragmentedMp4StreamConfiguration &configuration)
        noexcept = 0;
    virtual vrrec_status_t WritePacket(
        const EncodedMediaPacket &packet) noexcept = 0;
    virtual vrrec_status_t WriteTrailer() noexcept = 0;
    virtual vrrec_status_t FlushFile() noexcept = 0;
    virtual void Abort() noexcept = 0;
};

enum class Mp4MuxResult {
    Written,
    InvalidPacket,
    InvalidState,
    MuxFailed,
};

class FragmentedMp4MuxCoordinator final {
public:
    explicit FragmentedMp4MuxCoordinator(
        FragmentedMp4Muxer &muxer,
        EncodedMediaPacketObserver *observer = nullptr) noexcept;
    ~FragmentedMp4MuxCoordinator();

    FragmentedMp4MuxCoordinator(
        const FragmentedMp4MuxCoordinator &) = delete;
    FragmentedMp4MuxCoordinator &operator=(
        const FragmentedMp4MuxCoordinator &) = delete;

    vrrec_status_t Begin(
        const FragmentedMp4StreamConfiguration &configuration) noexcept;
    Mp4MuxResult Submit(const EncodedMediaPacket &packet) noexcept;
    Mp4MuxResult SubmitBatch(
        std::span<const EncodedMediaPacket> packets) noexcept;
    vrrec_status_t Finish() noexcept;
    void RequestAbort() noexcept;
    void Abort() noexcept;
#if defined(VRRECORDER_NATIVE_TESTING)
    bool IsAbortRequestedForTesting() const noexcept;
#endif

private:
    static bool IsConfigurationValid(
        const FragmentedMp4StreamConfiguration &configuration) noexcept;
    bool IsPacketValid(
        const EncodedMediaPacket &packet,
        bool has_video_dts,
        std::int64_t last_video_dts,
        bool has_audio_dts,
        std::int64_t last_audio_dts) const noexcept;
    void AbortLocked() noexcept;

    FragmentedMp4Muxer &muxer_;
    EncodedMediaPacketObserver *observer_;
    std::mutex submit_mutex_;
    std::mutex mutex_;
    std::int64_t last_video_dts_ = 0;
    std::int64_t last_audio_dts_ = 0;
    std::int64_t minimum_audio_timestamp_microseconds_ = 0;
    bool has_video_dts_ = false;
    bool has_audio_dts_ = false;
    bool started_ = false;
    bool terminal_ = false;
    bool aborted_ = false;
    std::atomic_bool abort_requested_ = false;
};

}

#endif
