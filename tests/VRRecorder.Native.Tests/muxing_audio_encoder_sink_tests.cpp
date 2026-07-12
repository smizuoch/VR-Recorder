#include "muxing_audio_encoder_sink.hpp"

#include <cstddef>
#include <cstdlib>
#include <iostream>
#include <span>
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

EncodedMediaPacket AudioPacket(std::int64_t timestamp)
{
    return {
        MediaStreamKind::Audio,
        timestamp,
        timestamp,
        21'333,
        false,
        512,
    };
}

class ScriptedPacketEncoder final : public PacketAudioEncoder {
public:
    PacketAudioEncoderWrite EncodePcm48k(
        std::uint64_t start_frame_48k,
        std::span<const float> samples) noexcept override
    {
        last_start_frame = start_frame_48k;
        last_samples.assign(samples.begin(), samples.end());
        ++encode_calls;
        return encode;
    }

    PacketAudioEncoderWrite Finish() noexcept override
    {
        ++finish_calls;
        return finish;
    }

    void Abort() noexcept override
    {
        ++abort_calls;
    }

    PacketAudioEncoderWrite encode {VRREC_STATUS_OK, {}};
    PacketAudioEncoderWrite finish {VRREC_STATUS_OK, {}};
    std::vector<float> last_samples;
    std::uint64_t last_start_frame = 0;
    std::size_t encode_calls = 0;
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

void SubmitsEveryEncodedAacPacketToTheSharedMuxTimeline()
{
    ScriptedPacketEncoder encoder;
    encoder.encode = {
        VRREC_STATUS_OK,
        {AudioPacket(0), AudioPacket(21'333)},
    };
    RecordingMuxer backend;
    FragmentedMp4MuxCoordinator mux(backend);
    SharedMuxFinalizationSession session(mux);
    MuxingAudioEncoderSink sink(encoder, session);
    const std::vector<float> samples {0.25F, -0.25F, 0.5F, -0.5F};

    const auto write = sink.WritePcm48k(480, samples);
    CHECK(write.status == VRREC_STATUS_OK);
    CHECK(write.muxed_packet_count == 2);
    CHECK(encoder.last_start_frame == 480);
    CHECK(encoder.last_samples == samples);
    CHECK(backend.packets.size() == 2);
}

void KeepsEncoderBufferingAsAZeroPacketSuccess()
{
    ScriptedPacketEncoder encoder;
    RecordingMuxer backend;
    FragmentedMp4MuxCoordinator mux(backend);
    SharedMuxFinalizationSession session(mux);
    MuxingAudioEncoderSink sink(encoder, session);

    const auto write = sink.WritePcm48k(0, std::vector<float> {0.0F, 0.0F});
    CHECK(write.status == VRREC_STATUS_OK);
    CHECK(write.muxed_packet_count == 0);
    CHECK(backend.packets.empty());
}

void AbortsBothSidesWhenMuxingFails()
{
    ScriptedPacketEncoder encoder;
    encoder.encode = {VRREC_STATUS_OK, {AudioPacket(0)}};
    RecordingMuxer backend;
    backend.write_status = VRREC_STATUS_INTERNAL_ERROR;
    FragmentedMp4MuxCoordinator mux(backend);
    SharedMuxFinalizationSession session(mux);
    MuxingAudioEncoderSink sink(encoder, session);

    const auto write = sink.WritePcm48k(0, std::vector<float> {0.0F, 0.0F});
    CHECK(write.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(write.failure_stage == AudioEncoderFailureStage::Muxing);
    CHECK(write.muxed_packet_count == 0);
    CHECK(encoder.abort_calls == 1);
    CHECK(backend.abort_calls == 1);
}

void FlushesAacPacketsWithoutFinalizingTheSharedMuxer()
{
    ScriptedPacketEncoder encoder;
    encoder.finish = {VRREC_STATUS_OK, {AudioPacket(0)}};
    RecordingMuxer backend;
    FragmentedMp4MuxCoordinator mux(backend);
    SharedMuxFinalizationSession session(mux);
    MuxingAudioEncoderSink sink(encoder, session);

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
    SubmitsEveryEncodedAacPacketToTheSharedMuxTimeline();
    KeepsEncoderBufferingAsAZeroPacketSuccess();
    AbortsBothSidesWhenMuxingFails();
    FlushesAacPacketsWithoutFinalizingTheSharedMuxer();
    return 0;
}
