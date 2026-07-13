#include "ffmpeg_fragmented_mp4_muxer.hpp"

#include <limits>

namespace vrrecorder::native {

FfmpegFragmentedMp4Muxer::FfmpegFragmentedMp4Muxer(
    FfmpegMuxerPort &port) noexcept
    : port_(port)
{
}

FfmpegFragmentedMp4Muxer::~FfmpegFragmentedMp4Muxer()
{
    if (state_ != State::Finished) {
        AbortPort();
    }
}

vrrec_status_t FfmpegFragmentedMp4Muxer::WriteHeader(
    const FragmentedMp4StreamConfiguration &configuration) noexcept
{
    if (state_ != State::Created) {
        return VRREC_STATUS_INVALID_STATE;
    }

    const auto header_status = port_.WriteHeader(configuration);
    if (header_status != VRREC_STATUS_OK) {
        AbortPort();
        return header_status;
    }

    MediaTimeBase video_time_base {};
    const auto video_status = port_.GetActualStreamTimeBase(
        MediaStreamKind::Video,
        video_time_base);
    if (video_status != VRREC_STATUS_OK ||
        !IsTimeBaseValid(video_time_base)) {
        AbortPort();
        return video_status == VRREC_STATUS_OK
            ? VRREC_STATUS_INTERNAL_ERROR
            : video_status;
    }

    MediaTimeBase audio_time_base {};
    const auto audio_status = port_.GetActualStreamTimeBase(
        MediaStreamKind::Audio,
        audio_time_base);
    if (audio_status != VRREC_STATUS_OK ||
        !IsTimeBaseValid(audio_time_base)) {
        AbortPort();
        return audio_status == VRREC_STATUS_OK
            ? VRREC_STATUS_INTERNAL_ERROR
            : audio_status;
    }

    video_time_base_ = video_time_base;
    audio_time_base_ = audio_time_base;
    minimum_audio_timestamp_microseconds_ =
        AacPrimingLowerBoundMicroseconds(configuration.audio);
    state_ = State::Ready;
    return VRREC_STATUS_OK;
}

vrrec_status_t FfmpegFragmentedMp4Muxer::WritePacket(
    const EncodedMediaPacket &packet) noexcept
{
    if (state_ != State::Ready) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (!IsPacketValid(packet)) {
        AbortPort();
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    const FfmpegPacketTimestamps source {
        packet.pts_microseconds,
        packet.dts_microseconds,
        packet.duration_microseconds,
    };
    FfmpegPacketTimestamps destination {};
    const auto destination_time_base =
        packet.stream == MediaStreamKind::Video
        ? video_time_base_
        : audio_time_base_;
    port_.RescalePacketTimestamps(
        source,
        MicrosecondPacketTimeBase,
        destination_time_base,
        destination);

    if (!IsRescaledTimingValid(packet.stream, destination)) {
        AbortPort();
        return VRREC_STATUS_INTERNAL_ERROR;
    }
    if (packet.stream == MediaStreamKind::Video) {
        if (has_video_dts_ && destination.dts <= last_video_dts_) {
            AbortPort();
            return VRREC_STATUS_INTERNAL_ERROR;
        }
    } else if (has_audio_dts_ && destination.dts <= last_audio_dts_) {
        AbortPort();
        return VRREC_STATUS_INTERNAL_ERROR;
    }

    const auto write_status = port_.WriteInterleavedPacket(
        packet,
        destination);
    if (write_status != VRREC_STATUS_OK) {
        AbortPort();
        return write_status;
    }

    if (packet.stream == MediaStreamKind::Video) {
        has_video_dts_ = true;
        last_video_dts_ = destination.dts;
    } else {
        has_audio_dts_ = true;
        last_audio_dts_ = destination.dts;
    }
    return VRREC_STATUS_OK;
}

vrrec_status_t FfmpegFragmentedMp4Muxer::WriteTrailer() noexcept
{
    if (state_ != State::Ready) {
        return VRREC_STATUS_INVALID_STATE;
    }
    const auto status = port_.WriteTrailer();
    if (status != VRREC_STATUS_OK) {
        AbortPort();
        return status;
    }
    state_ = State::TrailerWritten;
    return VRREC_STATUS_OK;
}

vrrec_status_t FfmpegFragmentedMp4Muxer::FlushFile() noexcept
{
    if (state_ != State::TrailerWritten) {
        return VRREC_STATUS_INVALID_STATE;
    }
    const auto status = port_.FlushFile();
    if (status != VRREC_STATUS_OK) {
        AbortPort();
        return status;
    }
    state_ = State::Finished;
    return VRREC_STATUS_OK;
}

void FfmpegFragmentedMp4Muxer::Abort() noexcept
{
    AbortPort();
}

bool FfmpegFragmentedMp4Muxer::IsTimeBaseValid(
    MediaTimeBase time_base) noexcept
{
    return time_base.numerator > 0 && time_base.denominator > 0;
}

bool FfmpegFragmentedMp4Muxer::IsPacketValid(
    const EncodedMediaPacket &packet) const noexcept
{
    if ((packet.stream != MediaStreamKind::Video &&
            packet.stream != MediaStreamKind::Audio) ||
        packet.pts_microseconds == UnknownMediaTimestamp ||
        packet.dts_microseconds == UnknownMediaTimestamp ||
        packet.pts_microseconds < packet.dts_microseconds ||
        packet.duration_microseconds <= 0 || packet.payload.empty()) {
        return false;
    }
    const auto minimum_timestamp = packet.stream == MediaStreamKind::Video
        ? 0
        : minimum_audio_timestamp_microseconds_;
    if (packet.pts_microseconds < minimum_timestamp ||
        packet.dts_microseconds < minimum_timestamp ||
        packet.pts_microseconds >
            std::numeric_limits<std::int64_t>::max() -
                packet.duration_microseconds ||
        packet.dts_microseconds >
            std::numeric_limits<std::int64_t>::max() -
                packet.duration_microseconds) {
        return false;
    }

    bool has_skip_samples = false;
    for (const auto &side_data : packet.side_data) {
        if (side_data.kind != EncodedPacketSideDataKind::SkipSamples ||
            packet.stream != MediaStreamKind::Audio ||
            side_data.payload.size() != SkipSamplesSideDataSize ||
            has_skip_samples) {
            return false;
        }
        has_skip_samples = true;
    }
    return true;
}

bool FfmpegFragmentedMp4Muxer::IsRescaledTimingValid(
    MediaStreamKind stream,
    const FfmpegPacketTimestamps &timestamps) noexcept
{
    if ((stream != MediaStreamKind::Video &&
            stream != MediaStreamKind::Audio) ||
        timestamps.pts == UnknownMediaTimestamp ||
        timestamps.dts == UnknownMediaTimestamp ||
        (stream == MediaStreamKind::Video &&
            (timestamps.pts < 0 || timestamps.dts < 0)) ||
        timestamps.pts < timestamps.dts || timestamps.duration <= 0) {
        return false;
    }
    return timestamps.pts <=
            std::numeric_limits<std::int64_t>::max() -
                timestamps.duration &&
        timestamps.dts <=
            std::numeric_limits<std::int64_t>::max() -
                timestamps.duration;
}

void FfmpegFragmentedMp4Muxer::AbortPort() noexcept
{
    if (state_ == State::Finished || port_aborted_) {
        return;
    }
    port_aborted_ = true;
    state_ = State::Aborted;
    port_.Abort();
}

}
