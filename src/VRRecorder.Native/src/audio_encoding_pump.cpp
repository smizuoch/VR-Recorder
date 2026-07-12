#include "audio_encoding_pump.hpp"

#include <limits>

namespace vrrecorder::native {

StereoAudioEncodingPump::StereoAudioEncodingPump(
    StereoAudioMixSource &source,
    StereoAudioEncoderSink &sink) noexcept
    : source_(source),
      sink_(sink)
{
}

StereoAudioEncodingResult StereoAudioEncodingPump::PumpNext(
    std::size_t frame_count_48k,
    StereoAudioEncodingRead &read) noexcept
{
    constexpr auto channel_count = StereoAudioMixer::ChannelCount;
    if (frame_count_48k == 0 ||
        frame_count_48k >
            std::numeric_limits<std::size_t>::max() / channel_count ||
        frame_count_48k >
            std::numeric_limits<std::uint64_t>::max()) {
        return StereoAudioEncodingResult::InvalidArgument;
    }

    try {
        mixed_samples_.assign(
            frame_count_48k * channel_count,
            0.0F);
    } catch (...) {
        return StereoAudioEncodingResult::Failed;
    }

    StereoAudioMixRead mix_read {};
    const auto mix_result = source_.MixNext(
        frame_count_48k,
        mixed_samples_,
        mix_read);
    if (mix_result != StereoAudioMixResult::Mixed) {
        return MapMixResult(mix_result);
    }

    if (mix_read.frame_count_48k != frame_count_48k) {
        return StereoAudioEncodingResult::CaptureFailed;
    }

    const auto write = sink_.WritePcm48k(
        mix_read.start_frame_48k,
        mixed_samples_);
    read = StereoAudioEncodingRead {
        mix_read,
        write.muxed_packet_count,
        write.status,
    };
    if (write.status != VRREC_STATUS_OK) {
        return write.failure_stage == AudioEncoderFailureStage::Muxing
            ? StereoAudioEncodingResult::MuxFailed
            : StereoAudioEncodingResult::EncoderFailed;
    }

    const auto submitted = submitted_frame_count_.load();
    const auto muxed = muxed_packet_count_.load();
    const auto frame_count = static_cast<std::uint64_t>(frame_count_48k);
    if (submitted >
            std::numeric_limits<std::uint64_t>::max() - frame_count ||
        muxed > std::numeric_limits<std::uint64_t>::max() -
            write.muxed_packet_count) {
        return StereoAudioEncodingResult::Failed;
    }

    submitted_frame_count_.store(submitted + frame_count);
    muxed_packet_count_.store(muxed + write.muxed_packet_count);
    return StereoAudioEncodingResult::Submitted;
}

std::uint64_t StereoAudioEncodingPump::SubmittedFrameCount() const noexcept
{
    return submitted_frame_count_.load();
}

std::uint64_t StereoAudioEncodingPump::MuxedPacketCount() const noexcept
{
    return muxed_packet_count_.load();
}

StereoAudioEncodingResult StereoAudioEncodingPump::MapMixResult(
    StereoAudioMixResult result) noexcept
{
    switch (result) {
    case StereoAudioMixResult::Aborted:
        return StereoAudioEncodingResult::Aborted;
    case StereoAudioMixResult::InvalidArgument:
        return StereoAudioEncodingResult::InvalidArgument;
    case StereoAudioMixResult::InvalidState:
        return StereoAudioEncodingResult::InvalidState;
    case StereoAudioMixResult::Failed:
        return StereoAudioEncodingResult::CaptureFailed;
    case StereoAudioMixResult::Mixed:
        break;
    }

    return StereoAudioEncodingResult::Failed;
}

}
