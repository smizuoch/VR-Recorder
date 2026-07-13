#include "muxing_audio_encoder_sink.hpp"
#include "fragmented_mp4_test_support.hpp"

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
using namespace vrrecorder::native::test;

EncodedMediaPacket AudioPacket(std::int64_t timestamp)
{
    return {
        MediaStreamKind::Audio,
        timestamp,
        timestamp,
        21'333,
        false,
        std::vector<std::byte>(512, std::byte{0x02}),
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
    vrrec_status_t WriteHeader(
        const FragmentedMp4StreamConfiguration &) noexcept override
    {
        return VRREC_STATUS_OK;
    }

    vrrec_status_t WritePacket(
        const EncodedMediaPacket &packet) noexcept override
    {
        packets.push_back(packet);
        return write_status;
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
    CHECK(mux.Begin(TestMp4Streams()) == VRREC_STATUS_OK);
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
    CHECK(mux.Begin(TestMp4Streams()) == VRREC_STATUS_OK);
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
    CHECK(mux.Begin(TestMp4Streams()) == VRREC_STATUS_OK);
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
    CHECK(mux.Begin(TestMp4Streams()) == VRREC_STATUS_OK);
    SharedMuxFinalizationSession session(mux);
    MuxingAudioEncoderSink sink(encoder, session);

    const auto finish = sink.Finish();
    CHECK(finish.status == VRREC_STATUS_OK);
    CHECK(finish.muxed_packet_count == 1);
    CHECK(encoder.finish_calls == 1);
    CHECK(backend.packets.size() == 1);
    CHECK(backend.abort_calls == 0);
}

void SuccessfulFinishTerminalizesTheAudioEncoderSink()
{
    ScriptedPacketEncoder encoder;
    RecordingMuxer backend;
    FragmentedMp4MuxCoordinator mux(backend);
    CHECK(mux.Begin(TestMp4Streams()) == VRREC_STATUS_OK);
    SharedMuxFinalizationSession session(mux);
    MuxingAudioEncoderSink sink(encoder, session);

    CHECK(sink.Finish().status == VRREC_STATUS_OK);
    CHECK(sink.WritePcm48k(0, std::vector<float> {0.0F, 0.0F}).status ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(sink.Finish().status == VRREC_STATUS_INVALID_STATE);
    CHECK(encoder.encode_calls == 0);
    CHECK(encoder.finish_calls == 1);
    CHECK(encoder.abort_calls == 0);
    CHECK(backend.abort_calls == 0);
}

void EncoderFailureAbortsBothSidesAndRejectsFurtherWrites()
{
    ScriptedPacketEncoder encoder;
    encoder.encode = {VRREC_STATUS_INTERNAL_ERROR, {}};
    RecordingMuxer backend;
    FragmentedMp4MuxCoordinator mux(backend);
    CHECK(mux.Begin(TestMp4Streams()) == VRREC_STATUS_OK);
    SharedMuxFinalizationSession session(mux);
    MuxingAudioEncoderSink sink(encoder, session);
    const std::vector<float> samples {0.0F, 0.0F};

    const auto failed = sink.WritePcm48k(0, samples);
    CHECK(failed.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(failed.failure_stage == AudioEncoderFailureStage::Encoding);
    CHECK(encoder.abort_calls == 1);
    CHECK(backend.abort_calls == 1);
    CHECK(sink.WritePcm48k(1, samples).status ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(encoder.encode_calls == 1);
}

void RejectsAMixedStreamBatchBeforeMutatingTheMuxer()
{
    ScriptedPacketEncoder encoder;
    auto wrong_stream = AudioPacket(21'333);
    wrong_stream.stream = MediaStreamKind::Video;
    encoder.encode = {
        VRREC_STATUS_OK,
        {AudioPacket(0), wrong_stream},
    };
    RecordingMuxer backend;
    FragmentedMp4MuxCoordinator mux(backend);
    CHECK(mux.Begin(TestMp4Streams()) == VRREC_STATUS_OK);
    SharedMuxFinalizationSession session(mux);
    MuxingAudioEncoderSink sink(encoder, session);

    const auto write = sink.WritePcm48k(
        0,
        std::vector<float> {0.0F, 0.0F});
    CHECK(write.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(write.failure_stage == AudioEncoderFailureStage::Muxing);
    CHECK(backend.packets.empty());
    CHECK(encoder.abort_calls == 1);
    CHECK(backend.abort_calls == 1);
}

}

int main()
{
    SubmitsEveryEncodedAacPacketToTheSharedMuxTimeline();
    KeepsEncoderBufferingAsAZeroPacketSuccess();
    AbortsBothSidesWhenMuxingFails();
    FlushesAacPacketsWithoutFinalizingTheSharedMuxer();
    SuccessfulFinishTerminalizesTheAudioEncoderSink();
    EncoderFailureAbortsBothSidesAndRejectsFurtherWrites();
    RejectsAMixedStreamBatchBeforeMutatingTheMuxer();
    return 0;
}
