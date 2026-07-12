#include "fragmented_mp4_mux_coordinator.hpp"

namespace vrrecorder::native {

FragmentedMp4MuxCoordinator::FragmentedMp4MuxCoordinator(
    FragmentedMp4Muxer &muxer) noexcept
    : muxer_(muxer)
{
}

FragmentedMp4MuxCoordinator::~FragmentedMp4MuxCoordinator()
{
    Abort();
}

Mp4MuxResult FragmentedMp4MuxCoordinator::Submit(
    const EncodedMediaPacket &packet) noexcept
{
    const std::lock_guard lock(mutex_);
    if (terminal_) {
        return Mp4MuxResult::InvalidState;
    }
    if (!IsPacketValid(packet)) {
        return Mp4MuxResult::InvalidPacket;
    }

    constexpr std::int64_t minimum_fragment_microseconds = 1'000'000;
    constexpr std::int64_t maximum_fragment_microseconds = 2'000'000;
    const auto fragment_duration = has_fragment_
        ? packet.dts_microseconds - fragment_start_dts_
        : 0;
    const auto preferred_key_frame_boundary =
        packet.stream == MediaStreamKind::Video && packet.key_frame &&
        fragment_duration >= minimum_fragment_microseconds;
    const auto forced_maximum_boundary =
        fragment_duration >= maximum_fragment_microseconds;
    if (has_fragment_ &&
        (preferred_key_frame_boundary || forced_maximum_boundary)) {
        if (muxer_.EndFragment() != VRREC_STATUS_OK) {
            AbortLocked();
            return Mp4MuxResult::MuxFailed;
        }
        fragment_start_dts_ = packet.dts_microseconds;
    }

    if (muxer_.WritePacket(packet) != VRREC_STATUS_OK) {
        AbortLocked();
        return Mp4MuxResult::MuxFailed;
    }

    if (!has_fragment_) {
        has_fragment_ = true;
        fragment_start_dts_ = packet.dts_microseconds;
    }
    if (packet.stream == MediaStreamKind::Video) {
        has_video_dts_ = true;
        last_video_dts_ = packet.dts_microseconds;
    } else {
        has_audio_dts_ = true;
        last_audio_dts_ = packet.dts_microseconds;
    }
    return Mp4MuxResult::Written;
}

vrrec_status_t FragmentedMp4MuxCoordinator::Finish() noexcept
{
    const std::lock_guard lock(mutex_);
    if (terminal_) {
        return VRREC_STATUS_INVALID_STATE;
    }

    if (has_fragment_ && muxer_.EndFragment() != VRREC_STATUS_OK) {
        AbortLocked();
        return VRREC_STATUS_INTERNAL_ERROR;
    }
    if (muxer_.WriteTrailer() != VRREC_STATUS_OK) {
        AbortLocked();
        return VRREC_STATUS_INTERNAL_ERROR;
    }
    if (muxer_.FlushFile() != VRREC_STATUS_OK) {
        AbortLocked();
        return VRREC_STATUS_INTERNAL_ERROR;
    }

    terminal_ = true;
    return VRREC_STATUS_OK;
}

void FragmentedMp4MuxCoordinator::Abort() noexcept
{
    const std::lock_guard lock(mutex_);
    AbortLocked();
}

bool FragmentedMp4MuxCoordinator::IsPacketValid(
    const EncodedMediaPacket &packet) const noexcept
{
    if ((packet.stream != MediaStreamKind::Video &&
         packet.stream != MediaStreamKind::Audio) ||
        packet.pts_microseconds < 0 || packet.dts_microseconds < 0 ||
        packet.duration_microseconds <= 0 || packet.payload_size == 0) {
        return false;
    }

    if (packet.stream == MediaStreamKind::Video) {
        return !has_video_dts_ || packet.dts_microseconds > last_video_dts_;
    }
    return !has_audio_dts_ || packet.dts_microseconds > last_audio_dts_;
}

void FragmentedMp4MuxCoordinator::AbortLocked() noexcept
{
    if (terminal_) {
        return;
    }
    terminal_ = true;
    if (!aborted_) {
        aborted_ = true;
        muxer_.Abort();
    }
}

}
