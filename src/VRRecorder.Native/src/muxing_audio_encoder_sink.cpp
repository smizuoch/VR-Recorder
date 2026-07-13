#include "muxing_audio_encoder_sink.hpp"

#include <algorithm>
#include <cstdint>
#include <utility>

namespace vrrecorder::native {

MuxingAudioEncoderSink::MuxingAudioEncoderSink(
    PacketAudioEncoder &encoder,
    EncodedMediaPacketSubmissionPort &mux) noexcept
    : encoder_(encoder),
      mux_(mux)
{
}

StereoAudioEncoderWrite MuxingAudioEncoderSink::WritePcm48k(
    std::uint64_t start_frame_48k,
    std::span<const float> interleaved_samples) noexcept
{
    const std::lock_guard lock(operation_mutex_);
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
    const std::lock_guard lock(operation_mutex_);
    if (aborted_.load() || finished_.exchange(true)) {
        return {
            VRREC_STATUS_INVALID_STATE,
            0,
            AudioEncoderFailureStage::Encoding,
        };
    }
    auto write = Commit(encoder_.Finish());
    if (write.status != VRREC_STATUS_OK) {
        return write;
    }
    const auto finish_status = mux_.EncoderFinished(MediaStreamKind::Audio);
    if (finish_status != VRREC_STATUS_OK || aborted_.load()) {
        Abort();
        return {
            finish_status != VRREC_STATUS_OK
                ? finish_status
                : VRREC_STATUS_INVALID_STATE,
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
    mux_.EncoderFailed(MediaStreamKind::Audio);
    encoder_.Abort();
}

StereoAudioEncoderWrite MuxingAudioEncoderSink::Commit(
    PacketAudioEncoderWrite encoded) noexcept
{
    if (aborted_.load()) {
        return {
            VRREC_STATUS_INVALID_STATE,
            0,
            AudioEncoderFailureStage::Encoding,
        };
    }
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

    if (encoded.packets.empty()) {
        return {
            VRREC_STATUS_OK,
            0,
            AudioEncoderFailureStage::None,
        };
    }
    if (aborted_.load()) {
        return {
            VRREC_STATUS_INVALID_STATE,
            0,
            AudioEncoderFailureStage::Encoding,
        };
    }
    if (mux_.SubmitBatch(
            MediaStreamKind::Audio,
            encoded.packets) != Mp4MuxResult::Written) {
        Abort();
        return {
            VRREC_STATUS_INTERNAL_ERROR,
            0,
            AudioEncoderFailureStage::Muxing,
        };
    }
    if (aborted_.load()) {
        return {
            VRREC_STATUS_INVALID_STATE,
            0,
            AudioEncoderFailureStage::Muxing,
        };
    }
    return {
        VRREC_STATUS_OK,
        encoded.packets.size(),
        AudioEncoderFailureStage::None,
    };
}

}
