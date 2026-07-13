#include "muxing_video_encoder_sink.hpp"

#include <algorithm>
#include <cstdint>
#include <utility>

namespace vrrecorder::native {

MuxingVideoEncoderSink::MuxingVideoEncoderSink(
    PacketVideoEncoder &encoder,
    SharedMuxFinalizationSession &mux) noexcept
    : encoder_(encoder),
      mux_(mux)
{
}

VideoEncoderWrite MuxingVideoEncoderSink::Write(
    const ScheduledVideoFrame &frame) noexcept
{
    if (aborted_.load() || finished_.load()) {
        return {
            VRREC_STATUS_INVALID_STATE,
            0,
            0,
            VideoEncoderFailureStage::Encoding,
        };
    }
    return Commit(encoder_.Encode(frame));
}

VideoEncoderWrite MuxingVideoEncoderSink::Finish() noexcept
{
    if (aborted_.load() || finished_.exchange(true)) {
        return {
            VRREC_STATUS_INVALID_STATE,
            0,
            0,
            VideoEncoderFailureStage::Encoding,
        };
    }
    auto write = Commit(encoder_.Finish());
    if (write.status != VRREC_STATUS_OK) {
        mux_.EncoderFailed(MediaStreamKind::Video);
        return write;
    }
    const auto finish_status = mux_.EncoderFinished(MediaStreamKind::Video);
    if (finish_status != VRREC_STATUS_OK) {
        Abort();
        return {
            finish_status,
            0,
            write.encode_latency_microseconds,
            VideoEncoderFailureStage::Muxing,
        };
    }
    return write;
}

void MuxingVideoEncoderSink::Abort() noexcept
{
    if (aborted_.exchange(true)) {
        return;
    }
    encoder_.Abort();
    mux_.EncoderFailed(MediaStreamKind::Video);
}

VideoEncoderWrite MuxingVideoEncoderSink::Commit(
    PacketVideoEncoderWrite encoded) noexcept
{
    if (encoded.status != VRREC_STATUS_OK) {
        Abort();
        return {
            encoded.status,
            0,
            encoded.encode_latency_microseconds,
            VideoEncoderFailureStage::Encoding,
        };
    }

    if (!std::all_of(
            encoded.packets.begin(),
            encoded.packets.end(),
            [](const EncodedMediaPacket &packet) {
                return packet.stream == MediaStreamKind::Video;
            })) {
        Abort();
        return {
            VRREC_STATUS_INTERNAL_ERROR,
            0,
            encoded.encode_latency_microseconds,
            VideoEncoderFailureStage::Muxing,
        };
    }

    std::uint64_t committed = 0;
    for (const auto &packet : encoded.packets) {
        if (mux_.Submit(packet) != Mp4MuxResult::Written) {
            Abort();
            return {
                VRREC_STATUS_INTERNAL_ERROR,
                0,
                encoded.encode_latency_microseconds,
                VideoEncoderFailureStage::Muxing,
            };
        }
        ++committed;
    }

    return {
        VRREC_STATUS_OK,
        committed,
        encoded.encode_latency_microseconds,
        VideoEncoderFailureStage::None,
    };
}

}
