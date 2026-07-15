#include "ffmpeg_libavcodec_encoder_port.hpp"

#include <cstddef>
#include <cstdint>
#include <cstring>
#include <cstdlib>
#include <iostream>
#include <limits>
#include <memory>
#include <utility>
#include <vector>

extern "C" {
#include <libavcodec/avcodec.h>
#include <libavcodec/version.h>
#include <libavutil/avutil.h>
#include <libavutil/channel_layout.h>
#include <libavutil/frame.h>
#include <libavutil/log.h>
#include <libavutil/mem.h>
#include <libavutil/samplefmt.h>
#include <libavutil/version.h>
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

constexpr int SampleRate = 48'000;
constexpr int ChannelCount = 2;

struct FrameDeleter final {
    void operator()(AVFrame *frame) const noexcept
    {
        av_frame_free(&frame);
    }
};

using FrameHandle = std::unique_ptr<AVFrame, FrameDeleter>;

AVCodecContext *AllocateAacContext()
{
    const auto *codec = avcodec_find_encoder(AV_CODEC_ID_AAC);
    CHECK(codec != nullptr);
    auto *context = avcodec_alloc_context3(codec);
    CHECK(context != nullptr);
    context->bit_rate = 128'000;
    context->sample_rate = SampleRate;
    context->sample_fmt = AV_SAMPLE_FMT_FLTP;
    context->time_base = {1, SampleRate};
    av_channel_layout_default(&context->ch_layout, ChannelCount);
    return context;
}

AVCodecContext *OpenAacContext()
{
    auto *context = AllocateAacContext();
    CHECK(avcodec_open2(context, context->codec, nullptr) == 0);
    CHECK(context->frame_size > 0);
    return context;
}

FrameHandle SilenceFrame(int sample_count, std::int64_t pts)
{
    FrameHandle frame(av_frame_alloc());
    CHECK(frame != nullptr);
    frame->format = AV_SAMPLE_FMT_FLTP;
    frame->sample_rate = SampleRate;
    frame->nb_samples = sample_count;
    frame->pts = pts;
    av_channel_layout_default(&frame->ch_layout, ChannelCount);
    CHECK(av_frame_get_buffer(frame.get(), 0) == 0);
    CHECK(av_samples_set_silence(
              frame->extended_data,
              0,
              sample_count,
              ChannelCount,
              AV_SAMPLE_FMT_FLTP) == 0);
    return frame;
}

void RejectsMissingOrUnopenedEncoderContext()
{
    auto missing = LibavcodecEncoderPort::Create(nullptr);
    CHECK(missing.status == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(missing.port == nullptr);

    auto unopened = LibavcodecEncoderPort::Create(AllocateAacContext());
    CHECK(unopened.status == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(unopened.port == nullptr);
}

void AcceptsOnlyThePinnedRuntimeVersion()
{
    CHECK(avcodec_version() == LIBAVCODEC_VERSION_INT);
    CHECK(avutil_version() == LIBAVUTIL_VERSION_INT);
    CHECK(std::strcmp(av_version_info(), "8.1.2") == 0);

    auto creation = LibavcodecEncoderPort::Create(OpenAacContext());
    CHECK(creation.status == VRREC_STATUS_OK);
    CHECK(creation.port != nullptr);
}

void CopiesOpenedContextExtradataWithoutExposingItsLifetime()
{
    auto *context = OpenAacContext();
    CHECK(context->extradata != nullptr);
    CHECK(context->extradata_size > 0);
    const std::vector<std::byte> expected(
        reinterpret_cast<const std::byte *>(context->extradata),
        reinterpret_cast<const std::byte *>(context->extradata) +
            context->extradata_size);
    auto creation = LibavcodecEncoderPort::Create(context);
    CHECK(creation.status == VRREC_STATUS_OK);

    std::vector<std::byte> copied;
    CHECK(creation.port->CopyCodecExtradata(copied) == VRREC_STATUS_OK);
    CHECK(copied == expected);
    copied[0] ^= std::byte {0xff};
    CHECK(creation.port->CopyCodecExtradata(copied) == VRREC_STATUS_OK);
    CHECK(copied == expected);

    creation.port->Abort();
    copied = {std::byte {0x55}};
    CHECK(creation.port->CopyCodecExtradata(copied) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(copied == std::vector<std::byte> {std::byte {0x55}});
}

void RejectsEveryMismatchedRuntimeIdentity()
{
    const auto reject = [](
                            unsigned int avcodec_version_value,
                            unsigned int avutil_version_value,
                            const char *release_version) {
        auto creation = LibavcodecEncoderPort::CreateForTesting(
            OpenAacContext(),
            avcodec_version_value,
            avutil_version_value,
            release_version);
        CHECK(creation.status == VRREC_STATUS_BACKEND_UNAVAILABLE);
        CHECK(creation.port == nullptr);
    };

    reject(
        LIBAVCODEC_VERSION_INT - 1U,
        LIBAVUTIL_VERSION_INT,
        "8.1.2");
    reject(
        LIBAVCODEC_VERSION_INT,
        LIBAVUTIL_VERSION_INT - 1U,
        "8.1.2");
    reject(
        LIBAVCODEC_VERSION_INT,
        LIBAVUTIL_VERSION_INT,
        "8.1.1");
    reject(
        LIBAVCODEC_VERSION_INT,
        LIBAVUTIL_VERSION_INT,
        nullptr);
}

void MapsLibavAllocationFailures()
{
    auto *factory_context = OpenAacContext();
    av_max_alloc(1);
    auto factory_failure =
        LibavcodecEncoderPort::Create(factory_context);
    av_max_alloc(static_cast<std::size_t>(
        std::numeric_limits<int>::max()));
    CHECK(factory_failure.status == VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(factory_failure.port == nullptr);

    auto *prepare_context = OpenAacContext();
    const auto sample_count = prepare_context->frame_size;
    auto creation = LibavcodecEncoderPort::Create(prepare_context);
    CHECK(creation.status == VRREC_STATUS_OK);
    auto frame = SilenceFrame(sample_count, 0);
    av_max_alloc(1);
    const auto prepare_status = creation.port->PrepareFrame(*frame);
    av_max_alloc(static_cast<std::size_t>(
        std::numeric_limits<int>::max()));
    CHECK(prepare_status == VRREC_STATUS_OUT_OF_MEMORY);
}

void RetainsFramesAcrossBackpressureAndExposesBorrowedPackets()
{
    auto *context = OpenAacContext();
    const auto sample_count = context->frame_size;
    auto creation = LibavcodecEncoderPort::Create(context);
    CHECK(creation.status == VRREC_STATUS_OK);
    CHECK(creation.port != nullptr);
    auto &port = *creation.port;

    std::int64_t next_pts = 0;
    const auto expected_duration_microseconds =
        (static_cast<std::int64_t>(sample_count) * 1'000'000 +
            SampleRate / 2) /
        SampleRate;
    bool observed_again = false;
    bool observed_packet = false;
    std::vector<std::int64_t> packet_pts;
    auto record_packet = [&](const FfmpegReceivedPacketView &packet) {
        CHECK(packet.data != nullptr);
        CHECK(packet.size > 0);
        CHECK(
            packet.duration_microseconds ==
            expected_duration_microseconds);
        CHECK(packet.pts_microseconds == packet.dts_microseconds);
        CHECK(packet.key_frame);
        CHECK(!packet.corrupt);
        for (std::size_t side_index = 0;
             side_index < packet.side_data_count;
             ++side_index) {
            const auto &side_data = packet.side_data[side_index];
            CHECK(
                side_data.kind ==
                FfmpegReceivedPacketSideDataKind::SkipSamples);
            CHECK(side_data.data != nullptr);
            CHECK(side_data.size == 10);
        }
        observed_packet = true;
        packet_pts.push_back(packet.pts_microseconds);
    };

    const auto missing_frame = port.SendPreparedFrame();
    CHECK(missing_frame.state == FfmpegCodecIoState::Failed);
    CHECK(missing_frame.failure_status == VRREC_STATUS_INVALID_STATE);

    for (int index = 0; index < 32 && !observed_again; ++index) {
        auto frame = SilenceFrame(sample_count, next_pts);
        CHECK(port.PrepareFrame(*frame) == VRREC_STATUS_OK);
        CHECK(port.PrepareFrame(*frame) == VRREC_STATUS_INVALID_STATE);
        const auto drain_with_pending_frame = port.SendDrain();
        CHECK(drain_with_pending_frame.state == FfmpegCodecIoState::Failed);
        CHECK(
            drain_with_pending_frame.failure_status ==
            VRREC_STATUS_INVALID_STATE);
        frame.reset();

        const auto send = port.SendPreparedFrame();
        if (send.state == FfmpegCodecIoState::Again) {
            observed_again = true;
            auto replacement = SilenceFrame(sample_count, next_pts);
            CHECK(
                port.PrepareFrame(*replacement) ==
                VRREC_STATUS_INVALID_STATE);

            FfmpegReceivedPacketView packet {};
            const auto receive = port.ReceivePacket(packet);
            CHECK(receive.state == FfmpegCodecIoState::Ok);
            record_packet(packet);

            FfmpegReceivedPacketView second_view {};
            const auto receive_while_borrowed =
                port.ReceivePacket(second_view);
            CHECK(
                receive_while_borrowed.state ==
                FfmpegCodecIoState::Failed);
            CHECK(
                receive_while_borrowed.failure_status ==
                VRREC_STATUS_INVALID_STATE);
            port.UnrefReceivedPacket();
            port.UnrefReceivedPacket();

            for (;;) {
                FfmpegReceivedPacketView pending_packet {};
                const auto pending_receive =
                    port.ReceivePacket(pending_packet);
                if (pending_receive.state == FfmpegCodecIoState::Again) {
                    break;
                }
                CHECK(pending_receive.state == FfmpegCodecIoState::Ok);
                record_packet(pending_packet);
                port.UnrefReceivedPacket();
            }

            CHECK(port.SendPreparedFrame().state == FfmpegCodecIoState::Ok);
        } else {
            CHECK(send.state == FfmpegCodecIoState::Ok);
        }
        next_pts += sample_count;
    }
    CHECK(observed_again);

    for (;;) {
        const auto drain = port.SendDrain();
        if (drain.state == FfmpegCodecIoState::Ok) {
            break;
        }
        CHECK(drain.state == FfmpegCodecIoState::Again);
        for (;;) {
            FfmpegReceivedPacketView packet {};
            const auto receive = port.ReceivePacket(packet);
            if (receive.state == FfmpegCodecIoState::Again) {
                break;
            }
            CHECK(receive.state == FfmpegCodecIoState::Ok);
            record_packet(packet);
            port.UnrefReceivedPacket();
        }
    }

    for (;;) {
        FfmpegReceivedPacketView packet {};
        const auto receive = port.ReceivePacket(packet);
        if (receive.state == FfmpegCodecIoState::EndOfStream) {
            break;
        }
        CHECK(receive.state == FfmpegCodecIoState::Ok);
        record_packet(packet);
        port.UnrefReceivedPacket();
    }

    CHECK(observed_packet);
    CHECK(!packet_pts.empty());
    CHECK(packet_pts.front() < 0);
    for (std::size_t index = 1; index < packet_pts.size(); ++index) {
        const auto delta = packet_pts[index] - packet_pts[index - 1];
        CHECK(
            delta == expected_duration_microseconds ||
            delta == expected_duration_microseconds + 1);
    }
    FfmpegReceivedPacketView finished_packet {};
    CHECK(
        port.ReceivePacket(finished_packet).state ==
        FfmpegCodecIoState::EndOfStream);
    const auto drain_after_finish = port.SendDrain();
    CHECK(drain_after_finish.state == FfmpegCodecIoState::Failed);
    CHECK(drain_after_finish.failure_status == VRREC_STATUS_INVALID_STATE);
    CHECK(port.PrepareFrame(*SilenceFrame(sample_count, next_pts)) ==
          VRREC_STATUS_INVALID_STATE);
}

void PreservesUnknownPacketTimestamps()
{
    auto *context = OpenAacContext();
    const auto sample_count = context->frame_size;
    auto creation = LibavcodecEncoderPort::Create(context);
    CHECK(creation.status == VRREC_STATUS_OK);
    auto &port = *creation.port;

    auto frame = SilenceFrame(sample_count, AV_NOPTS_VALUE);
    CHECK(port.PrepareFrame(*frame) == VRREC_STATUS_OK);
    frame.reset();
    CHECK(port.SendPreparedFrame().state == FfmpegCodecIoState::Ok);
    CHECK(port.SendDrain().state == FfmpegCodecIoState::Ok);

    bool observed_packet = false;
    for (;;) {
        FfmpegReceivedPacketView packet {};
        const auto receive = port.ReceivePacket(packet);
        if (receive.state == FfmpegCodecIoState::EndOfStream) {
            break;
        }
        CHECK(receive.state == FfmpegCodecIoState::Ok);
        CHECK(packet.pts_microseconds == UnknownMediaTimestamp);
        CHECK(packet.dts_microseconds == UnknownMediaTimestamp);
        observed_packet = true;
        port.UnrefReceivedPacket();
    }
    CHECK(observed_packet);
}

void EncodesOwnedPacketsThroughTheStateMachine()
{
    auto *context = OpenAacContext();
    const auto sample_count = context->frame_size;
    auto creation = LibavcodecEncoderPort::Create(context);
    CHECK(creation.status == VRREC_STATUS_OK);
    auto &port = *creation.port;
    FfmpegEncoderStateMachine state_machine(
        port,
        MediaStreamKind::Audio);

    std::vector<EncodedMediaPacket> owned_packets;
    std::int64_t next_pts = 0;
    for (int frame_index = 0; frame_index < 8; ++frame_index) {
        auto frame = SilenceFrame(sample_count, next_pts);
        CHECK(port.PrepareFrame(*frame) == VRREC_STATUS_OK);
        frame.reset();
        auto batch = state_machine.EncodePreparedFrame();
        CHECK(batch.status == VRREC_STATUS_OK);
        for (auto &packet : batch.packets) {
            CHECK(packet.stream == MediaStreamKind::Audio);
            CHECK(!packet.payload.empty());
            owned_packets.push_back(std::move(packet));
        }
        next_pts += sample_count;
    }

    auto final_batch = state_machine.Finish();
    CHECK(final_batch.status == VRREC_STATUS_OK);
    for (auto &packet : final_batch.packets) {
        CHECK(packet.stream == MediaStreamKind::Audio);
        CHECK(!packet.payload.empty());
        owned_packets.push_back(std::move(packet));
    }
    CHECK(state_machine.Finish().status == VRREC_STATUS_INVALID_STATE);
    CHECK(!owned_packets.empty());
    CHECK(owned_packets.front().pts_microseconds < 0);
    CHECK(
        owned_packets.front().dts_microseconds ==
        owned_packets.front().pts_microseconds);

    const auto first_payload = owned_packets.front().payload;
    port.Abort();
    CHECK(owned_packets.front().payload == first_payload);
}

void AbortIsIdempotentAndTerminal()
{
    auto *context = OpenAacContext();
    const auto sample_count = context->frame_size;
    auto creation = LibavcodecEncoderPort::Create(context);
    CHECK(creation.status == VRREC_STATUS_OK);
    auto &port = *creation.port;

    auto frame = SilenceFrame(sample_count, 0);
    CHECK(port.PrepareFrame(*frame) == VRREC_STATUS_OK);
    frame.reset();
    port.Abort();
    port.Abort();

    auto rejected = SilenceFrame(sample_count, sample_count);
    CHECK(port.PrepareFrame(*rejected) == VRREC_STATUS_INVALID_STATE);
    const auto send = port.SendPreparedFrame();
    CHECK(send.state == FfmpegCodecIoState::Failed);
    CHECK(send.failure_status == VRREC_STATUS_INVALID_STATE);
    CHECK(port.SendDrain().state == FfmpegCodecIoState::Failed);
    FfmpegReceivedPacketView packet {};
    CHECK(port.ReceivePacket(packet).state == FfmpegCodecIoState::Failed);
    port.UnrefReceivedPacket();
}

}

int main()
{
    av_log_set_level(AV_LOG_QUIET);
    RejectsMissingOrUnopenedEncoderContext();
    AcceptsOnlyThePinnedRuntimeVersion();
    CopiesOpenedContextExtradataWithoutExposingItsLifetime();
    RejectsEveryMismatchedRuntimeIdentity();
    MapsLibavAllocationFailures();
    RetainsFramesAcrossBackpressureAndExposesBorrowedPackets();
    PreservesUnknownPacketTimestamps();
    EncodesOwnedPacketsThroughTheStateMachine();
    AbortIsIdempotentAndTerminal();
    return 0;
}
