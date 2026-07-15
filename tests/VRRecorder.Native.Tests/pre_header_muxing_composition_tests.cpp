#include "muxing_audio_encoder_sink.hpp"
#include "muxing_video_encoder_sink.hpp"
#include "pre_header_coordinator.hpp"

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <future>
#include <iostream>
#include <span>
#include <thread>
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

H264StreamDescriptor VideoDescriptor()
{
    return {
        MicrosecondPacketTimeBase,
        1'920,
        1'080,
        H264Profile::High,
        H264PacketFormat::AvccLengthPrefixed,
        {std::byte {1}, std::byte {100}},
    };
}

AacStreamDescriptor AudioDescriptor()
{
    return {
        MicrosecondPacketTimeBase,
        48'000,
        2,
        1'024,
        1'024,
        AacProfile::LowComplexity,
        AudioChannelLayout::Stereo,
        AacPacketFormat::RawAccessUnit,
        {std::byte {0x11}, std::byte {0x90}},
        192'000,
    };
}

EncodedMediaPacket Packet(
    MediaStreamKind stream,
    std::int64_t timestamp,
    std::byte payload)
{
    return {
        stream,
        timestamp,
        timestamp,
        21'333,
        stream == MediaStreamKind::Video,
        {payload},
    };
}

class DescriptorReadyVideoEncoder final : public PacketVideoEncoder {
public:
    PacketVideoEncoderWrite Encode(
        const ScheduledVideoFrame &) noexcept override
    {
        ++encode_calls;
        return {
            VRREC_STATUS_OK,
            125,
            {Packet(MediaStreamKind::Video, 0, std::byte {0xB0})},
            true,
            this,
            &descriptor,
        };
    }

    PacketVideoEncoderWrite Finish() noexcept override
    {
        ++finish_calls;
        return {VRREC_STATUS_OK, 25, {}};
    }

    void Abort() noexcept override
    {
        ++abort_calls;
    }

    H264StreamDescriptor descriptor = VideoDescriptor();
    std::size_t encode_calls = 0;
    std::size_t finish_calls = 0;
    std::size_t abort_calls = 0;
};

class PrimingAudioEncoder final : public PacketAudioEncoder {
public:
    PacketAudioEncoderWrite EncodePcm48k(
        std::uint64_t,
        std::span<const float>) noexcept override
    {
        ++encode_calls;
        return {
            VRREC_STATUS_OK,
            {Packet(MediaStreamKind::Audio, -21'333, std::byte {0xA0})},
        };
    }

    PacketAudioEncoderWrite Finish() noexcept override
    {
        ++finish_calls;
        return {VRREC_STATUS_OK, {}};
    }

    void Abort() noexcept override
    {
        ++abort_calls;
    }

    std::size_t encode_calls = 0;
    std::size_t finish_calls = 0;
    std::size_t abort_calls = 0;
};

class RecordingMuxPipeline final
    : public MediaMuxSessionPort,
      public EncodedMediaPacketSubmissionPort {
public:
    vrrec_status_t Start(
        const FragmentedMp4StreamConfiguration &configuration) noexcept override
    {
        ++start_calls;
        try {
            started_configuration = configuration;
            operations.push_back(1);
        } catch (...) {
            return VRREC_STATUS_OUT_OF_MEMORY;
        }
        return VRREC_STATUS_OK;
    }

    Mp4MuxResult SubmitBatch(
        MediaStreamKind producer,
        std::span<const EncodedMediaPacket> packets) noexcept override
    {
        try {
            for (const auto &packet : packets) {
                if (packet.stream != producer) {
                    return Mp4MuxResult::InvalidPacket;
                }
                submitted_packets.push_back(packet);
                operations.push_back(2);
            }
        } catch (...) {
            return Mp4MuxResult::MuxFailed;
        }
        return Mp4MuxResult::Written;
    }

    vrrec_status_t EncoderFinished(MediaStreamKind stream) noexcept override
    {
        try {
            finished_streams.push_back(stream);
            operations.push_back(3);
        } catch (...) {
            return VRREC_STATUS_OUT_OF_MEMORY;
        }
        return VRREC_STATUS_OK;
    }

    void EncoderFailed(MediaStreamKind stream) noexcept override
    {
        failed_streams.push_back(stream);
    }

    void RequestAbort() noexcept override
    {
        ++request_abort_calls;
    }

    void Abort() noexcept override
    {
        ++abort_calls;
    }

    FragmentedMp4StreamConfiguration started_configuration {};
    std::vector<EncodedMediaPacket> submitted_packets;
    std::vector<MediaStreamKind> finished_streams;
    std::vector<MediaStreamKind> failed_streams;
    std::vector<int> operations;
    std::size_t start_calls = 0;
    std::size_t request_abort_calls = 0;
    std::size_t abort_calls = 0;
};

void WaitForQueuedAudio(const PreHeaderCoordinator &coordinator)
{
    const auto deadline = std::chrono::steady_clock::now() +
        std::chrono::seconds(2);
    while (coordinator.QueuedPacketCountForTesting() != 1 &&
           std::chrono::steady_clock::now() < deadline) {
        std::this_thread::yield();
    }
    CHECK(coordinator.QueuedPacketCountForTesting() == 1);
}

void ConnectsBothMuxingSinksThroughOnePreHeaderOwner()
{
    RecordingMuxPipeline mux;
    DescriptorReadyVideoEncoder video_encoder;
    PrimingAudioEncoder audio_encoder;
    PreHeaderCoordinator coordinator(
        mux,
        mux,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &video_encoder);
    MuxingVideoEncoderSink video_sink(
        video_encoder,
        coordinator,
        coordinator);
    MuxingAudioEncoderSink audio_sink(audio_encoder, coordinator);
    CHECK(coordinator.BeginPriming(1'000'000) == VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    const std::vector<float> silence(2'048, 0.0F);
    auto audio_write = std::async(std::launch::async, [&] {
        return audio_sink.WritePcm48k(0, silence);
    });
    WaitForQueuedAudio(coordinator);

    const auto video_write = video_sink.Write({});

    CHECK(video_write.status == VRREC_STATUS_OK);
    CHECK(video_write.muxed_packet_count == 1);
    const auto completed_audio_write = audio_write.get();
    CHECK(completed_audio_write.status == VRREC_STATUS_OK);
    CHECK(completed_audio_write.muxed_packet_count == 1);
    CHECK(mux.start_calls == 1);
    CHECK(mux.started_configuration.video.codec_extradata ==
          video_encoder.descriptor.codec_extradata);
    CHECK(mux.submitted_packets.size() == 2);
    CHECK(mux.submitted_packets[0].stream == MediaStreamKind::Audio);
    CHECK(mux.submitted_packets[1].stream == MediaStreamKind::Video);
    CHECK(mux.operations == std::vector<int>({1, 2, 2}));
    CHECK(coordinator.State() == PreHeaderState::Running);

    CHECK(video_sink.Finish().status == VRREC_STATUS_OK);
    CHECK(audio_sink.Finish().status == VRREC_STATUS_OK);
    CHECK(mux.finished_streams ==
          std::vector({MediaStreamKind::Video, MediaStreamKind::Audio}));
    CHECK(coordinator.State() == PreHeaderState::Finishing);
    CHECK(mux.request_abort_calls == 0);
    CHECK(mux.abort_calls == 0);
}

}

int main()
{
    ConnectsBothMuxingSinksThroughOnePreHeaderOwner();
    return 0;
}
