#include "muxing_audio_encoder_sink.hpp"

#include <algorithm>
#include <cstdint>
#include <utility>

namespace vrrecorder::native {

MuxingAudioEncoderSink::MuxingAudioEncoderSink(
    PacketAudioEncoder &encoder,
    SharedMuxFinalizationSession &mux) noexcept
    : encoder_(encoder),
      mux_(mux)
{
}

StereoAudioEncoderWrite MuxingAudioEncoderSink::WritePcm48k(
    std::uint64_t start_frame_48k,
    std::span<const float> interleaved_samples) noexcept
{
    if (aborted_.load() || finished_.load()) {
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
    if (aborted_.load() || finished_.exchange(true)) {
        return {
            VRREC_STATUS_INVALID_STATE,
            0,
            AudioEncoderFailureStage::Encoding,
        };
    }
    auto write = Commit(encoder_.Finish());
    if (write.status != VRREC_STATUS_OK) {
        mux_.EncoderFailed(MediaStreamKind::Audio);
        return write;
    }
    const auto finish_status = mux_.EncoderFinished(MediaStreamKind::Audio);
    if (finish_status != VRREC_STATUS_OK) {
        Abort();
        return {
            finish_status,
            0,
            AudioEncoderFailureStage::Muxing,
        };
    }
    return write;
}

void MuxingAudioEncoderSink::Abort() noexcept
{
    if (aborted_.exchange(true)) {
        return;
    }
    encoder_.Abort();
    mux_.EncoderFailed(MediaStreamKind::Audio);
}

StereoAudioEncoderWrite MuxingAudioEncoderSink::Commit(
    PacketAudioEncoderWrite encoded) noexcept
{
    if (encoded.status != VRREC_STATUS_OK) {
        Abort();
        return {
            encoded.status,
            0,
            AudioEncoderFailureStage::Encoding,
        };
    }

    if (!std::all_of(
            encoded.packets.begin(),
            encoded.packets.end(),
            [](const EncodedMediaPacket &packet) {
                return packet.stream == MediaStreamKind::Audio;
            })) {
        Abort();
        return {
            VRREC_STATUS_INTERNAL_ERROR,
            0,
            AudioEncoderFailureStage::Muxing,
        };
    }

    std::uint64_t committed = 0;
    for (const auto &packet : encoded.packets) {
        if (mux_.Submit(packet) != Mp4MuxResult::Written) {
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
