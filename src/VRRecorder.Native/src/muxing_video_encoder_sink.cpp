#include "muxing_video_encoder_sink.hpp"

#include <cstdint>
#include <utility>

namespace vrrecorder::native {

MuxingVideoEncoderSink::MuxingVideoEncoderSink(
    PacketVideoEncoder &encoder,
    FragmentedMp4MuxCoordinator &mux) noexcept
    : encoder_(encoder),
      mux_(mux)
{
}

VideoEncoderWrite MuxingVideoEncoderSink::Write(
    const ScheduledVideoFrame &frame) noexcept
{
    if (aborted_.load()) {
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
    if (aborted_.load()) {
        return {
            VRREC_STATUS_INVALID_STATE,
            0,
            0,
            VideoEncoderFailureStage::Encoding,
        };
    }
    return Commit(encoder_.Finish());
}

void MuxingVideoEncoderSink::Abort() noexcept
{
    if (aborted_.exchange(true)) {
        return;
    }
    encoder_.Abort();
    mux_.Abort();
}

VideoEncoderWrite MuxingVideoEncoderSink::Commit(
    PacketVideoEncoderWrite encoded) noexcept
{
    if (encoded.status != VRREC_STATUS_OK) {
        return {
            encoded.status,
            0,
            encoded.encode_latency_microseconds,
            VideoEncoderFailureStage::Encoding,
        };
    }

    std::uint64_t committed = 0;
    for (const auto &packet : encoded.packets) {
        if (packet.stream != MediaStreamKind::Video ||
            mux_.Submit(packet) != Mp4MuxResult::Written) {
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
