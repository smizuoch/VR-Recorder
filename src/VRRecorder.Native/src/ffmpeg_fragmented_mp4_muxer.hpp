#ifndef VRRECORDER_NATIVE_FFMPEG_FRAGMENTED_MP4_MUXER_HPP
#define VRRECORDER_NATIVE_FFMPEG_FRAGMENTED_MP4_MUXER_HPP

#include <cstdint>

#include "fragmented_mp4_mux_coordinator.hpp"

namespace vrrecorder::native {

struct FfmpegPacketTimestamps final {
    std::int64_t pts;
    std::int64_t dts;
    std::int64_t duration;

    bool operator==(const FfmpegPacketTimestamps &) const = default;
};

class FfmpegMuxerPort {
public:
    virtual ~FfmpegMuxerPort() = default;

    virtual vrrec_status_t WriteHeader(
        const FragmentedMp4StreamConfiguration &configuration)
        noexcept = 0;
    virtual vrrec_status_t GetActualStreamTimeBase(
        MediaStreamKind stream,
        MediaTimeBase &time_base) noexcept = 0;
    virtual void RescalePacketTimestamps(
        const FfmpegPacketTimestamps &source,
        MediaTimeBase source_time_base,
        MediaTimeBase destination_time_base,
        FfmpegPacketTimestamps &destination) noexcept = 0;
    virtual vrrec_status_t WriteInterleavedPacket(
        const EncodedMediaPacket &canonical_packet,
        const FfmpegPacketTimestamps &stream_timestamps) noexcept = 0;
    virtual vrrec_status_t WriteTrailer() noexcept = 0;
    virtual vrrec_status_t FlushFile() noexcept = 0;
    virtual void Abort() noexcept = 0;
};

class FfmpegFragmentedMp4Muxer final : public FragmentedMp4Muxer {
public:
    explicit FfmpegFragmentedMp4Muxer(FfmpegMuxerPort &port) noexcept;
    ~FfmpegFragmentedMp4Muxer() override;

    FfmpegFragmentedMp4Muxer(
        const FfmpegFragmentedMp4Muxer &) = delete;
    FfmpegFragmentedMp4Muxer &operator=(
        const FfmpegFragmentedMp4Muxer &) = delete;

    vrrec_status_t WriteHeader(
        const FragmentedMp4StreamConfiguration &configuration)
        noexcept override;
    vrrec_status_t WritePacket(
        const EncodedMediaPacket &packet) noexcept override;
    vrrec_status_t WriteTrailer() noexcept override;
    vrrec_status_t FlushFile() noexcept override;
    void Abort() noexcept override;

private:
    enum class State {
        Created,
        Ready,
        TrailerWritten,
        Finished,
        Aborted,
    };

    static bool IsTimeBaseValid(MediaTimeBase time_base) noexcept;
    bool IsPacketValid(const EncodedMediaPacket &packet) const noexcept;
    static bool IsRescaledTimingValid(
        MediaStreamKind stream,
        const FfmpegPacketTimestamps &timestamps) noexcept;
    void AbortPort() noexcept;

    FfmpegMuxerPort &port_;
    MediaTimeBase video_time_base_ {};
    MediaTimeBase audio_time_base_ {};
    std::int64_t last_video_dts_ = 0;
    std::int64_t last_audio_dts_ = 0;
    std::int64_t minimum_audio_timestamp_microseconds_ = 0;
    State state_ = State::Created;
    bool has_video_dts_ = false;
    bool has_audio_dts_ = false;
    bool port_aborted_ = false;
};

}

#endif
