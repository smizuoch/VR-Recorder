#include "muxing_video_encoder_sink.hpp"

#include <algorithm>
#include <cstdint>
#include <utility>

namespace vrrecorder::native {

MuxingVideoEncoderSink::MuxingVideoEncoderSink(
    PacketVideoEncoder &encoder,
    EncodedMediaPacketSubmissionPort &mux) noexcept
    : encoder_(encoder),
      mux_(mux)
{
}

VideoEncoderWrite MuxingVideoEncoderSink::Write(
    const ScheduledVideoFrame &frame) noexcept
{
    const std::lock_guard lock(operation_mutex_);
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
    const std::lock_guard lock(operation_mutex_);
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
        return write;
    }
    const auto finish_status = mux_.EncoderFinished(MediaStreamKind::Video);
    if (finish_status != VRREC_STATUS_OK || aborted_.load()) {
        Abort();
        return {
            finish_status != VRREC_STATUS_OK
                ? finish_status
                : VRREC_STATUS_INVALID_STATE,
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
    mux_.EncoderFailed(MediaStreamKind::Video);
    encoder_.Abort();
}

VideoEncoderWrite MuxingVideoEncoderSink::Commit(
    PacketVideoEncoderWrite encoded) noexcept
{
    if (aborted_.load()) {
        return {
            VRREC_STATUS_INVALID_STATE,
            0,
            encoded.encode_latency_microseconds,
            VideoEncoderFailureStage::Encoding,
        };
    }
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

    if (encoded.packets.empty()) {
        return {
            VRREC_STATUS_OK,
            0,
            encoded.encode_latency_microseconds,
            VideoEncoderFailureStage::None,
        };
    }
    if (aborted_.load()) {
        return {
            VRREC_STATUS_INVALID_STATE,
            0,
            encoded.encode_latency_microseconds,
            VideoEncoderFailureStage::Encoding,
        };
    }
    if (mux_.SubmitBatch(
            MediaStreamKind::Video,
            encoded.packets) != Mp4MuxResult::Written) {
        Abort();
        return {
            VRREC_STATUS_INTERNAL_ERROR,
            0,
            encoded.encode_latency_microseconds,
            VideoEncoderFailureStage::Muxing,
        };
    }
    if (aborted_.load()) {
        return {
            VRREC_STATUS_INVALID_STATE,
            0,
            encoded.encode_latency_microseconds,
            VideoEncoderFailureStage::Muxing,
        };
    }

    return {
        VRREC_STATUS_OK,
        encoded.packets.size(),
        encoded.encode_latency_microseconds,
        VideoEncoderFailureStage::None,
    };
}

}
