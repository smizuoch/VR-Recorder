#include "fragmented_mp4_mux_coordinator.hpp"

#include <limits>

namespace vrrecorder::native {

FragmentedMp4MuxCoordinator::FragmentedMp4MuxCoordinator(
    FragmentedMp4Muxer &muxer,
    EncodedMediaPacketObserver *observer) noexcept
    : muxer_(muxer),
      observer_(observer)
{
}

FragmentedMp4MuxCoordinator::~FragmentedMp4MuxCoordinator()
{
    Abort();
}

vrrec_status_t FragmentedMp4MuxCoordinator::Begin(
    const FragmentedMp4StreamConfiguration &configuration) noexcept
{
    const std::lock_guard submit_lock(submit_mutex_);
    const std::lock_guard lock(mutex_);
    if (terminal_ || started_) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (abort_requested_.load()) {
        AbortLocked();
        return VRREC_STATUS_INVALID_STATE;
    }
    if (!IsConfigurationValid(configuration)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    const auto status = muxer_.WriteHeader(configuration);
    if (status != VRREC_STATUS_OK) {
        AbortLocked();
        return status;
    }
    if (abort_requested_.load()) {
        AbortLocked();
        return VRREC_STATUS_INVALID_STATE;
    }
    minimum_audio_timestamp_microseconds_ =
        AacPrimingLowerBoundMicroseconds(configuration.audio);
    started_ = true;
    return VRREC_STATUS_OK;
}

Mp4MuxResult FragmentedMp4MuxCoordinator::Submit(
    const EncodedMediaPacket &packet) noexcept
{
    return SubmitBatch(std::span<const EncodedMediaPacket>(&packet, 1));
}

Mp4MuxResult FragmentedMp4MuxCoordinator::SubmitBatch(
    std::span<const EncodedMediaPacket> packets) noexcept
{
    const std::lock_guard submit_lock(submit_mutex_);
    {
        const std::lock_guard lock(mutex_);
        if (terminal_ || !started_) {
            return Mp4MuxResult::InvalidState;
        }
        if (abort_requested_.load()) {
            AbortLocked();
            return Mp4MuxResult::MuxFailed;
        }

        auto next_has_video_dts = has_video_dts_;
        auto next_video_dts = last_video_dts_;
        auto next_has_audio_dts = has_audio_dts_;
        auto next_audio_dts = last_audio_dts_;
        for (const auto &packet : packets) {
            if (!IsPacketValid(
                    packet,
                    next_has_video_dts,
                    next_video_dts,
                    next_has_audio_dts,
                    next_audio_dts)) {
                return Mp4MuxResult::InvalidPacket;
            }
            if (packet.stream == MediaStreamKind::Video) {
                next_has_video_dts = true;
                next_video_dts = packet.dts_microseconds;
            } else {
                next_has_audio_dts = true;
                next_audio_dts = packet.dts_microseconds;
            }
        }

        for (const auto &packet : packets) {
            if (abort_requested_.load()) {
                AbortLocked();
                return Mp4MuxResult::MuxFailed;
            }
            if (muxer_.WritePacket(packet) != VRREC_STATUS_OK) {
                AbortLocked();
                return Mp4MuxResult::MuxFailed;
            }
            if (abort_requested_.load()) {
                AbortLocked();
                return Mp4MuxResult::MuxFailed;
            }
        }
        has_video_dts_ = next_has_video_dts;
        last_video_dts_ = next_video_dts;
        has_audio_dts_ = next_has_audio_dts;
        last_audio_dts_ = next_audio_dts;
    }

    if (observer_ != nullptr) {
        for (const auto &packet : packets) {
            {
                const std::lock_guard lock(mutex_);
                if (abort_requested_.load()) {
                    AbortLocked();
                    return Mp4MuxResult::MuxFailed;
                }
                if (terminal_) {
                    return Mp4MuxResult::MuxFailed;
                }
            }
            if (observer_->Observe(packet) != VRREC_STATUS_OK) {
                const std::lock_guard lock(mutex_);
                AbortLocked();
                return Mp4MuxResult::MuxFailed;
            }
            {
                const std::lock_guard lock(mutex_);
                if (abort_requested_.load()) {
                    AbortLocked();
                    return Mp4MuxResult::MuxFailed;
                }
                if (terminal_) {
                    return Mp4MuxResult::MuxFailed;
                }
            }
        }
    }
    return Mp4MuxResult::Written;
}

vrrec_status_t FragmentedMp4MuxCoordinator::Finish() noexcept
{
    const std::lock_guard submit_lock(submit_mutex_);
    const std::lock_guard lock(mutex_);
    if (terminal_ || !started_) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (abort_requested_.load()) {
        AbortLocked();
        return VRREC_STATUS_INVALID_STATE;
    }

    if (muxer_.WriteTrailer() != VRREC_STATUS_OK) {
        AbortLocked();
        return VRREC_STATUS_INTERNAL_ERROR;
    }
    if (abort_requested_.load()) {
        AbortLocked();
        return VRREC_STATUS_INVALID_STATE;
    }
    if (muxer_.FlushFile() != VRREC_STATUS_OK) {
        AbortLocked();
        return VRREC_STATUS_INTERNAL_ERROR;
    }
    if (abort_requested_.load()) {
        AbortLocked();
        return VRREC_STATUS_INVALID_STATE;
    }

    terminal_ = true;
    return VRREC_STATUS_OK;
}

void FragmentedMp4MuxCoordinator::Abort() noexcept
{
    abort_requested_.store(true);
    const std::lock_guard lock(mutex_);
    AbortLocked();
}

#if defined(VRRECORDER_NATIVE_TESTING)
bool FragmentedMp4MuxCoordinator::IsAbortRequestedForTesting()
    const noexcept
{
    return abort_requested_.load();
}
#endif

bool FragmentedMp4MuxCoordinator::IsConfigurationValid(
    const FragmentedMp4StreamConfiguration &configuration) noexcept
{
    constexpr std::uint32_t maximum_dimension = 16'384;
    const auto video_profile_valid =
        configuration.video.profile == H264Profile::Main ||
        configuration.video.profile == H264Profile::High;
    const auto video_packet_format_valid =
        configuration.video.packet_format == H264PacketFormat::AnnexB ||
        configuration.video.packet_format ==
            H264PacketFormat::AvccLengthPrefixed;
    return configuration.video.packet_time_base ==
               MicrosecondPacketTimeBase &&
        configuration.audio.packet_time_base ==
               MicrosecondPacketTimeBase &&
        configuration.video.width != 0 &&
        configuration.video.height != 0 &&
        configuration.video.width <= maximum_dimension &&
        configuration.video.height <= maximum_dimension &&
        configuration.video.width % 2 == 0 &&
        configuration.video.height % 2 == 0 && video_profile_valid &&
        video_packet_format_valid &&
        !configuration.video.codec_extradata.empty() &&
        configuration.audio.sample_rate == 48'000 &&
        configuration.audio.channel_count == 2 &&
        configuration.audio.frame_size != 0 &&
        configuration.audio.frame_size <=
            static_cast<std::uint32_t>(
                std::numeric_limits<std::int32_t>::max()) &&
        configuration.audio.initial_padding_samples <=
            static_cast<std::uint32_t>(
                std::numeric_limits<std::int32_t>::max()) &&
        configuration.audio.profile == AacProfile::LowComplexity &&
        configuration.audio.channel_layout == AudioChannelLayout::Stereo &&
        configuration.audio.packet_format ==
            AacPacketFormat::RawAccessUnit &&
        !configuration.audio.codec_extradata.empty() &&
        configuration.fragment_policy ==
            DefaultFragmentedMp4FragmentPolicy;
}

bool FragmentedMp4MuxCoordinator::IsPacketValid(
    const EncodedMediaPacket &packet,
    bool has_video_dts,
    std::int64_t last_video_dts,
    bool has_audio_dts,
    std::int64_t last_audio_dts) const noexcept
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

    if (packet.stream == MediaStreamKind::Video) {
        return !has_video_dts || packet.dts_microseconds > last_video_dts;
    }
    return !has_audio_dts || packet.dts_microseconds > last_audio_dts;
}

void FragmentedMp4MuxCoordinator::AbortLocked() noexcept
{
    abort_requested_.store(true);
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
