#include "muxing_audio_encoder_sink.hpp"

#include <cstdint>
#include <utility>

namespace vrrecorder::native {

MuxingAudioEncoderSink::MuxingAudioEncoderSink(
    PacketAudioEncoder &encoder,
    FragmentedMp4MuxCoordinator &mux) noexcept
    : encoder_(encoder),
      mux_(mux)
{
}

StereoAudioEncoderWrite MuxingAudioEncoderSink::WritePcm48k(
    std::uint64_t start_frame_48k,
    std::span<const float> interleaved_samples) noexcept
{
    if (aborted_.load()) {
        return {
            VRREC_STATUS_INVALID_STATE,
            0,
            AudioEncoderFailureStage::Encoding,
        };
    }
    return Commit(encoder_.EncodePcm48k(
        start_frame_48k,
        interleaved_samples));
}

StereoAudioEncoderWrite MuxingAudioEncoderSink::Finish() noexcept
{
    if (aborted_.load()) {
        return {
            VRREC_STATUS_INVALID_STATE,
            0,
            AudioEncoderFailureStage::Encoding,
        };
    }
    return Commit(encoder_.Finish());
}

void MuxingAudioEncoderSink::Abort() noexcept
{
    if (aborted_.exchange(true)) {
        return;
    }
    encoder_.Abort();
    mux_.Abort();
}

StereoAudioEncoderWrite MuxingAudioEncoderSink::Commit(
    PacketAudioEncoderWrite encoded) noexcept
{
    if (encoded.status != VRREC_STATUS_OK) {
        return {
            encoded.status,
            0,
            AudioEncoderFailureStage::Encoding,
        };
    }

    std::uint64_t committed = 0;
    for (const auto &packet : encoded.packets) {
        if (packet.stream != MediaStreamKind::Audio ||
            mux_.Submit(packet) != Mp4MuxResult::Written) {
            Abort();
            return {
                VRREC_STATUS_INTERNAL_ERROR,
                0,
                AudioEncoderFailureStage::Muxing,
            };
        }
        ++committed;
    }
    return {
        VRREC_STATUS_OK,
        committed,
        AudioEncoderFailureStage::None,
    };
}

}
