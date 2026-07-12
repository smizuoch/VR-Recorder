#include "audio_capture_pump.hpp"

#include <limits>

namespace vrrecorder::native {

AudioCapturePump::AudioCapturePump(
    AudioCaptureSource &source,
    StereoCaptureTimeline &timeline,
    AudioCaptureAvailabilitySink *availability_sink) noexcept
    : source_(source),
      timeline_(timeline),
      availability_sink_(availability_sink)
{
}

vrrec_status_t AudioCapturePump::Start(
    const AudioCaptureSourceConfig &config) noexcept
{
    return StartCore(config, false);
}

vrrec_status_t AudioCapturePump::StartRecovery(
    const AudioCaptureSourceConfig &config) noexcept
{
    return StartCore(config, true);
}

vrrec_status_t AudioCapturePump::StartCore(
    const AudioCaptureSourceConfig &config,
    bool recovering) noexcept
{
    if (started_ || aborted_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }

    const auto status = source_.Start(config);
    if (status != VRREC_STATUS_OK) {
        if (!recovering) {
            timeline_.Abort();
        }

        return status;
    }

    if (aborted_.load()) {
        timeline_.Abort();
        return VRREC_STATUS_INVALID_STATE;
    }

    role_ = config.role;
    started_ = true;
    recovering_ = recovering;
    return VRREC_STATUS_OK;
}

AudioCapturePumpResult AudioCapturePump::PumpOne() noexcept
{
    if (aborted_.load()) {
        return AudioCapturePumpResult::Aborted;
    }

    if (!started_) {
        return AudioCapturePumpResult::InvalidState;
    }

    const auto read = source_.Read();
    if (aborted_.load()) {
        return AudioCapturePumpResult::Aborted;
    }

    switch (read.result) {
    case AudioCaptureReadResult::Packet:
        return AcceptPacket(read.packet);
    case AudioCaptureReadResult::DeviceLost: {
        const auto availability = MapTimelineResult(timeline_.SetAvailable(
            false,
            read.effective_frame_48k));
        if (availability != AudioCapturePumpResult::PacketAccepted) {
            return availability;
        }

        if (availability_sink_ != nullptr) {
            availability_sink_->AvailabilityChanged(
                role_,
                false,
                read.effective_frame_48k);
        }

        return AudioCapturePumpResult::DeviceLost;
    }
    case AudioCaptureReadResult::Aborted:
        timeline_.Abort();
        return AudioCapturePumpResult::Aborted;
    case AudioCaptureReadResult::Failed:
        timeline_.Abort();
        return AudioCapturePumpResult::Failed;
    }

    timeline_.Abort();
    return AudioCapturePumpResult::Failed;
}

void AudioCapturePump::Abort() noexcept
{
    if (aborted_.exchange(true)) {
        return;
    }

    source_.Abort();
    timeline_.Abort();
}

AudioCapturePumpResult AudioCapturePump::AcceptPacket(
    const CapturedStereoPacket48k &packet) noexcept
{
    constexpr auto channel_count =
        StereoAudioTimelineBuffer::ChannelCount;
    if (packet.frame_count_48k == 0 ||
        packet.frame_count_48k >
            std::numeric_limits<std::size_t>::max() / channel_count) {
        return AudioCapturePumpResult::InvalidPacket;
    }

    const auto sample_count = packet.frame_count_48k * channel_count;
    std::span<const float> samples;
    if (packet.silent) {
        try {
            silent_samples_.assign(sample_count, 0.0F);
        } catch (...) {
            timeline_.Abort();
            return AudioCapturePumpResult::Failed;
        }

        samples = silent_samples_;
    } else {
        if (packet.interleaved_samples.size() != sample_count) {
            return AudioCapturePumpResult::InvalidPacket;
        }

        samples = packet.interleaved_samples;
    }

    auto recovery_applied = false;
    if (recovering_) {
        const auto availability = MapTimelineResult(
            timeline_.SetAvailable(true, packet.start_frame_48k));
        if (availability != AudioCapturePumpResult::PacketAccepted) {
            return availability;
        }

        recovering_ = false;
        recovery_applied = true;
    }

    const auto result = MapTimelineResult(timeline_.Push({
        packet.start_frame_48k,
        {
            packet.device_position,
            packet.qpc_100ns,
            10'000'000,
        },
        samples,
        packet.discontinuity,
    }));
    if (result != AudioCapturePumpResult::PacketAccepted &&
        recovery_applied) {
        timeline_.Abort();
    } else if (result == AudioCapturePumpResult::PacketAccepted &&
               recovery_applied && availability_sink_ != nullptr) {
        availability_sink_->AvailabilityChanged(
            role_,
            true,
            packet.start_frame_48k);
    }

    return result;
}

AudioCapturePumpResult AudioCapturePump::MapTimelineResult(
    AudioTimelineResult result) noexcept
{
    switch (result) {
    case AudioTimelineResult::Ready:
        return AudioCapturePumpResult::PacketAccepted;
    case AudioTimelineResult::Aborted:
        return AudioCapturePumpResult::Aborted;
    case AudioTimelineResult::InvalidPacket:
        return AudioCapturePumpResult::InvalidPacket;
    case AudioTimelineResult::Discontinuity:
        return AudioCapturePumpResult::Discontinuity;
    case AudioTimelineResult::Overrun:
        return AudioCapturePumpResult::Overrun;
    }

    return AudioCapturePumpResult::Failed;
}

}
