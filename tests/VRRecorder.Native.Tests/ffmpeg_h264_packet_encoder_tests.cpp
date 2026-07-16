#include "ffmpeg_h264_packet_encoder.hpp"
#include "ffmpeg_h264_software_codec_session.hpp"
#include "ffmpeg_h264_system_memory_packet_encoder_adapter.hpp"
#include "h264_test_vectors.hpp"
#include "muxing_video_encoder_sink.hpp"

#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <iostream>
#include <memory>
#include <new>
#include <span>
#include <utility>
#include <vector>

extern "C" {
#include <libavutil/frame.h>
#include <libavutil/pixfmt.h>
}

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

H264VideoEncoderConfig ExactConfig()
{
    H264VideoEncoderConfig config {};
    CHECK(CreateH264VideoEncoderConfig(
              32,
              16,
              30,
              true,
              config) == VRREC_STATUS_OK);
    return config;
}

std::vector<std::byte> AnnexB(
    std::initializer_list<std::span<const std::byte>> nals)
{
    std::vector<std::byte> bytes;
    for (const auto nal : nals) {
        bytes.insert(
            bytes.end(),
            {std::byte {0}, std::byte {0}, std::byte {0}, std::byte {1}});
        bytes.insert(bytes.end(), nal.begin(), nal.end());
    }
    return bytes;
}

std::vector<std::byte> Sps()
{
    SpsSettings settings;
    settings.pic_width_in_mbs_minus1 = 1;
    return MakeSps(settings);
}

EncodedMediaPacket RawPacket(
    std::vector<std::byte> payload,
    bool key_frame,
    std::int64_t pts = 0)
{
    return {
        MediaStreamKind::Video,
        pts,
        pts,
        33'333,
        key_frame,
        std::move(payload),
        {},
    };
}

AVFrame *AllocateFrame()
{
    AVFrame *frame = av_frame_alloc();
    CHECK(frame != nullptr);
    frame->format = AV_PIX_FMT_NV12;
    frame->width = 32;
    frame->height = 16;
    CHECK(av_frame_get_buffer(frame, 32) == 0);
    return frame;
}

class FakeCodecSession final : public FfmpegH264CodecSession {
public:
    vrrec_status_t PrepareFrame(const AVFrame &frame) noexcept override
    {
        ++prepare_calls;
        observed_pts = frame.pts;
        observed_duration = frame.duration;
        observed_y = frame.data[0][0];
        return prepare_status;
    }

    FfmpegEncodeBatch EncodePreparedFrame() noexcept override
    {
        ++encode_calls;
        return std::move(encode_batch);
    }

    FfmpegEncodeBatch Finish() noexcept override
    {
        ++finish_calls;
        return std::move(finish_batch);
    }

    vrrec_status_t CopyCodecExtradata(
        std::vector<std::byte> &output) const noexcept override
    {
        if (extradata_status == VRREC_STATUS_OK) {
            output = extradata;
        }
        return extradata_status;
    }

    void Abort() noexcept override
    {
        ++abort_calls;
    }

    std::vector<std::byte> extradata;
    FfmpegEncodeBatch encode_batch;
    FfmpegEncodeBatch finish_batch;
    vrrec_status_t prepare_status = VRREC_STATUS_OK;
    vrrec_status_t extradata_status = VRREC_STATUS_OK;
    std::size_t prepare_calls = 0;
    std::size_t encode_calls = 0;
    std::size_t finish_calls = 0;
    std::size_t abort_calls = 0;
    std::int64_t observed_pts = -1;
    std::int64_t observed_duration = -1;
    std::uint8_t observed_y = 0;
};

class OwnedSystemMemoryMapping final
    : public SystemMemoryNv12FrameMapping {
public:
    explicit OwnedSystemMemoryMapping(std::size_t &destruction_count)
        : y(32U * 16U, std::byte {0x31}),
          uv(32U * 8U, std::byte {0x80}),
          destruction_count_(destruction_count)
    {
    }

    ~OwnedSystemMemoryMapping() override
    {
        ++destruction_count_;
    }

    SystemMemoryNv12FrameView View() const noexcept override
    {
        return {32, 16, 32, 32, y, uv, -1};
    }

    std::vector<std::byte> y;
    std::vector<std::byte> uv;

private:
    std::size_t &destruction_count_;
};

class FakeSystemMemoryMapper final : public SystemMemoryNv12FrameMapper {
public:
    SystemMemoryNv12FrameMapResult Map(
        const ScheduledVideoFrame &frame) noexcept override
    {
        ++map_calls;
        observed_tick = frame.output_tick;
        auto mapping = std::unique_ptr<SystemMemoryNv12FrameMapping>(
            new (std::nothrow) OwnedSystemMemoryMapping(
                mapping_destruction_count));
        return {
            mapping != nullptr
                ? VRREC_STATUS_OK
                : VRREC_STATUS_OUT_OF_MEMORY,
            std::move(mapping),
        };
    }

    void Abort() noexcept override
    {
        ++abort_calls;
    }

    std::size_t map_calls = 0;
    std::size_t mapping_destruction_count = 0;
    std::size_t abort_calls = 0;
    std::uint64_t observed_tick = 0;
};

SystemMemoryNv12FrameView FrameView(
    std::vector<std::byte> &y,
    std::vector<std::byte> &uv,
    std::int64_t pts)
{
    return {32, 16, 32, 32, y, uv, pts};
}

void UsesOpenTimeExtradataAndEncodesOwnedNv12()
{
    const auto sps = Sps();
    const auto pps = MakePps({});
    const std::vector<std::byte> idr {std::byte {0x65}, std::byte {0x88}};
    auto session = std::make_unique<FakeCodecSession>();
    auto *observed = session.get();
    session->extradata = AnnexB({sps, pps});
    session->encode_batch = {
        VRREC_STATUS_OK,
        {RawPacket(AnnexB({idr}), true)},
    };

    auto creation = FfmpegH264PacketEncoder::CreateForTesting(
        ExactConfig(),
        std::move(session),
        AllocateFrame());
    CHECK(creation.status == VRREC_STATUS_OK);
    CHECK(creation.encoder != nullptr);
    CHECK(creation.encoder->Descriptor() != nullptr);
    std::vector<std::byte> y(32U * 16U, std::byte {0x23});
    std::vector<std::byte> uv(32U * 8U, std::byte {0x80});

    const auto write = creation.encoder->EncodeNv12(FrameView(y, uv, 7));
    CHECK(write.status == VRREC_STATUS_OK);
    CHECK(!write.descriptor_became_ready);
    CHECK(write.packets.size() == 1);
    CHECK(write.packets[0].payload.size() == idr.size() + 4U);
    CHECK(observed->prepare_calls == 1);
    CHECK(observed->encode_calls == 1);
    CHECK(observed->observed_pts == 7);
    CHECK(observed->observed_duration == 1);
    CHECK(observed->observed_y == 0x23);

    auto next_session = std::make_unique<FakeCodecSession>();
    next_session->extradata = AnnexB({sps, pps});
    next_session->encode_batch = {
        VRREC_STATUS_OK,
        {RawPacket(AnnexB({idr}), true)},
    };
    auto next_creation = FfmpegH264PacketEncoder::CreateForTesting(
        ExactConfig(),
        std::move(next_session),
        AllocateFrame());
    auto open_time_write = next_creation.encoder->EncodeNv12(
        FrameView(y, uv, 8));
    const auto adapted = MakeMuxingVideoEncoderWrite(
        *next_creation.encoder,
        std::move(open_time_write),
        25);
    CHECK(!adapted.descriptor_became_ready);
    CHECK(adapted.encoder_identity == nullptr);
    CHECK(adapted.descriptor == nullptr);
}

void DerivesLateExtradataFromTheFirstRealPacket()
{
    const auto sps = Sps();
    const auto pps = MakePps({});
    const std::vector<std::byte> idr {std::byte {0x65}, std::byte {0x88}};
    auto session = std::make_unique<FakeCodecSession>();
    session->encode_batch = {
        VRREC_STATUS_OK,
        {RawPacket(AnnexB({sps, pps, idr}), true)},
    };
    auto creation = FfmpegH264PacketEncoder::CreateForTesting(
        ExactConfig(),
        std::move(session),
        AllocateFrame());
    CHECK(creation.status == VRREC_STATUS_OK);
    CHECK(creation.encoder->Descriptor() == nullptr);
    std::vector<std::byte> y(32U * 16U);
    std::vector<std::byte> uv(32U * 8U);

    const auto write = creation.encoder->EncodeNv12(FrameView(y, uv, 0));
    CHECK(write.status == VRREC_STATUS_OK);
    CHECK(write.descriptor_became_ready);
    CHECK(write.packets.size() == 1);
    CHECK(creation.encoder->Descriptor() != nullptr);
}

void AdaptsLateDescriptorMetadataForTheMuxingVideoSink()
{
    const auto sps = Sps();
    const auto pps = MakePps({});
    const std::vector<std::byte> idr {std::byte {0x65}, std::byte {0x88}};
    auto session = std::make_unique<FakeCodecSession>();
    session->encode_batch = {
        VRREC_STATUS_OK,
        {RawPacket(AnnexB({sps, pps, idr}), true)},
    };
    auto creation = FfmpegH264PacketEncoder::CreateForTesting(
        ExactConfig(),
        std::move(session),
        AllocateFrame());
    std::vector<std::byte> y(32U * 16U);
    std::vector<std::byte> uv(32U * 8U);
    auto encoded = creation.encoder->EncodeNv12(FrameView(y, uv, 0));

    const auto adapted = MakeMuxingVideoEncoderWrite(
        *creation.encoder,
        std::move(encoded),
        175);

    CHECK(adapted.status == VRREC_STATUS_OK);
    CHECK(adapted.encode_latency_microseconds == 175);
    CHECK(adapted.packets.size() == 1);
    CHECK(adapted.descriptor_became_ready);
    CHECK(adapted.encoder_identity == creation.encoder.get());
    CHECK(adapted.descriptor == creation.encoder->Descriptor());
}

void MapsScheduledFramesIntoTheSystemMemoryH264Encoder()
{
    const auto sps = Sps();
    const auto pps = MakePps({});
    const std::vector<std::byte> idr {std::byte {0x65}, std::byte {0x88}};
    auto session = std::make_unique<FakeCodecSession>();
    auto *observed = session.get();
    session->encode_batch = {
        VRREC_STATUS_OK,
        {RawPacket(AnnexB({sps, pps, idr}), true, 100'000)},
    };
    auto creation = FfmpegH264PacketEncoder::CreateForTesting(
        ExactConfig(),
        std::move(session),
        AllocateFrame());
    FakeSystemMemoryMapper mapper;
    FfmpegH264SystemMemoryPacketEncoderAdapter adapter(
        *creation.encoder,
        mapper,
        30);
    ScheduledVideoFrame scheduled {};
    scheduled.output_tick = 3;

    const auto write = adapter.Encode(scheduled);

    CHECK(write.status == VRREC_STATUS_OK);
    CHECK(write.packets.size() == 1);
    CHECK(write.descriptor_became_ready);
    CHECK(write.encoder_identity == creation.encoder.get());
    CHECK(write.descriptor == creation.encoder->Descriptor());
    CHECK(mapper.map_calls == 1);
    CHECK(mapper.observed_tick == 3);
    CHECK(mapper.mapping_destruction_count == 1);
    CHECK(observed->observed_pts == 3);
    CHECK(observed->observed_y == 0x31);

    adapter.Abort();
    adapter.Abort();
    CHECK(mapper.abort_calls == 1);
    CHECK(observed->abort_calls == 1);
}

void PreservesZeroPacketBatchesAndNormalizesFinishPackets()
{
    const auto sps = Sps();
    const auto pps = MakePps({});
    const std::vector<std::byte> idr {std::byte {0x65}, std::byte {0x88}};
    auto session = std::make_unique<FakeCodecSession>();
    auto *observed = session.get();
    session->encode_batch = {VRREC_STATUS_OK, {}};
    session->finish_batch = {
        VRREC_STATUS_OK,
        {RawPacket(AnnexB({sps, pps, idr}), true)},
    };
    auto creation = FfmpegH264PacketEncoder::CreateForTesting(
        ExactConfig(),
        std::move(session),
        AllocateFrame());
    std::vector<std::byte> y(32U * 16U);
    std::vector<std::byte> uv(32U * 8U);

    CHECK(creation.encoder->EncodeNv12(FrameView(y, uv, 0)).packets.empty());
    const auto finish = creation.encoder->Finish();
    CHECK(finish.status == VRREC_STATUS_OK);
    CHECK(finish.descriptor_became_ready);
    CHECK(finish.packets.size() == 1);
    CHECK(observed->finish_calls == 1);
    CHECK(creation.encoder->Finish().status == VRREC_STATUS_INVALID_STATE);
}

void FailureAbortsTheSessionAndMakesTheEncoderTerminal()
{
    auto session = std::make_unique<FakeCodecSession>();
    auto *observed = session.get();
    session->prepare_status = VRREC_STATUS_OUT_OF_MEMORY;
    auto creation = FfmpegH264PacketEncoder::CreateForTesting(
        ExactConfig(),
        std::move(session),
        AllocateFrame());
    std::vector<std::byte> y(32U * 16U);
    std::vector<std::byte> uv(32U * 8U);

    CHECK(creation.encoder->EncodeNv12(FrameView(y, uv, 0)).status ==
          VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(observed->abort_calls == 1);
    CHECK(creation.encoder->EncodeNv12(FrameView(y, uv, 1)).status ==
          VRREC_STATUS_INVALID_STATE);
    creation.encoder->Abort();
    CHECK(observed->abort_calls == 1);
}

void ProductionFactoryFailsClosedWhenH264MfIsUnavailable()
{
#if !defined(_WIN32)
    const auto creation = FfmpegH264PacketEncoder::Create(ExactConfig());
    CHECK(creation.status == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(creation.encoder == nullptr);
#endif
}

void SoftwareCodecSessionFactoryRejectsInvalidConfigurationAndFailsClosed()
{
    auto invalid = ExactConfig();
    invalid.width = 0;
    auto rejected = CreateFfmpegH264SoftwareCodecSession(invalid);
    CHECK(rejected.status == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(rejected.session == nullptr);
    CHECK(rejected.owned_frame == nullptr);

#if !defined(_WIN32)
    auto unavailable = CreateFfmpegH264SoftwareCodecSession(ExactConfig());
    CHECK(unavailable.status == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(unavailable.session == nullptr);
    CHECK(unavailable.owned_frame == nullptr);
#endif
}

}

int main()
{
    UsesOpenTimeExtradataAndEncodesOwnedNv12();
    DerivesLateExtradataFromTheFirstRealPacket();
    AdaptsLateDescriptorMetadataForTheMuxingVideoSink();
    MapsScheduledFramesIntoTheSystemMemoryH264Encoder();
    PreservesZeroPacketBatchesAndNormalizesFinishPackets();
    FailureAbortsTheSessionAndMakesTheEncoderTerminal();
    ProductionFactoryFailsClosedWhenH264MfIsUnavailable();
    SoftwareCodecSessionFactoryRejectsInvalidConfigurationAndFailsClosed();
    return 0;
}
