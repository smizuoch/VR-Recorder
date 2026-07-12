#include "muxing_video_encoder_sink.hpp"

#include <cstddef>
#include <cstdlib>
#include <iostream>
#include <vector>

namespace {

#define CHECK(condition)                                                        \
    do {                                                                        \
        if (!(condition)) {                                                     \
            std::cerr << "check failed at " << __FILE__ << ':' << __LINE__      \
                      << ": " #condition << '\n';                              \
            std::abort();                                                       \
        }                                                                       \
    } while (false)

using namespace vrrecorder::native;

EncodedMediaPacket VideoPacket(std::int64_t timestamp)
{
    return {
        MediaStreamKind::Video,
        timestamp,
        timestamp,
        33'333,
        timestamp == 0,
        1'024,
    };
}

class ScriptedPacketEncoder final : public PacketVideoEncoder {
public:
    PacketVideoEncoderWrite Encode(
        const ScheduledVideoFrame &frame) noexcept override
    {
        frames.push_back(frame);
        return encode;
    }

    PacketVideoEncoderWrite Finish() noexcept override
    {
        ++finish_calls;
        return finish;
    }

    void Abort() noexcept override
    {
        ++abort_calls;
    }

    PacketVideoEncoderWrite encode {VRREC_STATUS_OK, 0, {}};
    PacketVideoEncoderWrite finish {VRREC_STATUS_OK, 0, {}};
    std::vector<ScheduledVideoFrame> frames;
    std::size_t finish_calls = 0;
    std::size_t abort_calls = 0;
};

class RecordingMuxer final : public FragmentedMp4Muxer {
public:
    vrrec_status_t WritePacket(
        const EncodedMediaPacket &packet) noexcept override
    {
        packets.push_back(packet);
        return write_status;
    }

    vrrec_status_t EndFragment() noexcept override
    {
        return VRREC_STATUS_OK;
    }

    vrrec_status_t WriteTrailer() noexcept override
    {
        return VRREC_STATUS_OK;
    }

    vrrec_status_t FlushFile() noexcept override
    {
        return VRREC_STATUS_OK;
    }

    void Abort() noexcept override
    {
        ++abort_calls;
    }

    std::vector<EncodedMediaPacket> packets;
    vrrec_status_t write_status = VRREC_STATUS_OK;
    std::size_t abort_calls = 0;
};

void SubmitsEveryEncodedVideoPacketToTheSharedMuxTimeline()
{
    ScriptedPacketEncoder encoder;
    encoder.encode = {
        VRREC_STATUS_OK,
        250,
        {VideoPacket(0), VideoPacket(33'333)},
    };
    RecordingMuxer backend;
    FragmentedMp4MuxCoordinator mux(backend);
    SharedMuxFinalizationSession session(mux);
    MuxingVideoEncoderSink sink(encoder, session);
    ScheduledVideoFrame frame {};
    frame.output_tick = 7;

    const auto write = sink.Write(frame);
    CHECK(write.status == VRREC_STATUS_OK);
    CHECK(write.muxed_packet_count == 2);
    CHECK(write.encode_latency_microseconds == 250);
    CHECK(backend.packets.size() == 2);
    CHECK(encoder.frames.size() == 1);
    CHECK(encoder.frames.front().output_tick == 7);
}

void KeepsEncoderBufferingAsAZeroPacketSuccess()
{
    ScriptedPacketEncoder encoder;
    RecordingMuxer backend;
    FragmentedMp4MuxCoordinator mux(backend);
    SharedMuxFinalizationSession session(mux);
    MuxingVideoEncoderSink sink(encoder, session);

    const auto write = sink.Write({});
    CHECK(write.status == VRREC_STATUS_OK);
    CHECK(write.muxed_packet_count == 0);
    CHECK(backend.packets.empty());
}

void AbortsBothSidesWhenMuxingFails()
{
    ScriptedPacketEncoder encoder;
    encoder.encode = {VRREC_STATUS_OK, 100, {VideoPacket(0)}};
    RecordingMuxer backend;
    backend.write_status = VRREC_STATUS_INTERNAL_ERROR;
    FragmentedMp4MuxCoordinator mux(backend);
    SharedMuxFinalizationSession session(mux);
    MuxingVideoEncoderSink sink(encoder, session);

    const auto write = sink.Write({});
    CHECK(write.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(write.failure_stage == VideoEncoderFailureStage::Muxing);
    CHECK(write.muxed_packet_count == 0);
    CHECK(encoder.abort_calls == 1);
    CHECK(backend.abort_calls == 1);
}

void FlushesEncoderPacketsWithoutFinalizingTheSharedMuxer()
{
    ScriptedPacketEncoder encoder;
    encoder.finish = {VRREC_STATUS_OK, 300, {VideoPacket(0)}};
    RecordingMuxer backend;
    FragmentedMp4MuxCoordinator mux(backend);
    SharedMuxFinalizationSession session(mux);
    MuxingVideoEncoderSink sink(encoder, session);

    const auto finish = sink.Finish();
    CHECK(finish.status == VRREC_STATUS_OK);
    CHECK(finish.muxed_packet_count == 1);
    CHECK(encoder.finish_calls == 1);
    CHECK(backend.packets.size() == 1);
    CHECK(backend.abort_calls == 0);
}

}

int main()
{
    SubmitsEveryEncodedVideoPacketToTheSharedMuxTimeline();
    KeepsEncoderBufferingAsAZeroPacketSuccess();
    AbortsBothSidesWhenMuxingFails();
    FlushesEncoderPacketsWithoutFinalizingTheSharedMuxer();
    return 0;
}
