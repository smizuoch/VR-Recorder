#include "muxing_video_encoder_sink.hpp"

#include <algorithm>
#include <cstdint>
#include <limits>
#include <utility>

namespace vrrecorder::native {

MuxingVideoEncoderSink::MuxingVideoEncoderSink(
    PacketVideoEncoder &encoder,
    EncodedMediaPacketSubmissionPort &mux) noexcept
    : encoder_(encoder),
      mux_(mux),
      descriptor_mux_(nullptr)
{
}

MuxingVideoEncoderSink::MuxingVideoEncoderSink(
    PacketVideoEncoder &encoder,
    EncodedMediaPacketSubmissionPort &mux,
    H264DescriptorPacketSubmissionPort &descriptor_mux) noexcept
    : encoder_(encoder),
      mux_(mux),
      descriptor_mux_(&descriptor_mux)
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
    AbortEncoderOnce();
}

void MuxingVideoEncoderSink::AbortEncoderOnce() noexcept
{
    if (!encoder_aborted_.exchange(true)) {
        encoder_.Abort();
    }
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
        if (committed_muxed_packet_count_ > 0) {
            finished_.store(true);
            AbortEncoderOnce();
            const auto finish_status =
                mux_.EncoderFinished(MediaStreamKind::Video);
            if (finish_status == VRREC_STATUS_OK && !aborted_.load()) {
                return {
                    encoded.status,
                    0,
                    encoded.encode_latency_microseconds,
                    VideoEncoderFailureStage::Encoding,
                    true,
                };
            }
            Abort();
            return {
                finish_status != VRREC_STATUS_OK
                    ? finish_status
                    : VRREC_STATUS_INVALID_STATE,
                0,
                encoded.encode_latency_microseconds,
                VideoEncoderFailureStage::Muxing,
            };
        }
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

    const auto descriptor_metadata_valid =
        encoded.descriptor_became_ready
        ? descriptor_mux_ != nullptr &&
            encoded.encoder_identity != nullptr &&
            encoded.descriptor != nullptr &&
            !encoded.packets.empty()
        : encoded.encoder_identity == nullptr && encoded.descriptor == nullptr;
    if (!descriptor_metadata_valid) {
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
    const auto submit_result = encoded.descriptor_became_ready
        ? descriptor_mux_->SubmitVideoDescriptorBatch(
            encoded.encoder_identity,
            *encoded.descriptor,
            encoded.packets)
        : mux_.SubmitBatch(MediaStreamKind::Video, encoded.packets);
    if (submit_result != Mp4MuxResult::Written) {
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

    if (committed_muxed_packet_count_ >
        std::numeric_limits<std::uint64_t>::max() -
            encoded.packets.size()) {
        Abort();
        return {
            VRREC_STATUS_INTERNAL_ERROR,
            0,
            encoded.encode_latency_microseconds,
            VideoEncoderFailureStage::Muxing,
        };
    }
    committed_muxed_packet_count_ += encoded.packets.size();

    return {
        VRREC_STATUS_OK,
        encoded.packets.size(),
        encoded.encode_latency_microseconds,
        VideoEncoderFailureStage::None,
    };
}

}
