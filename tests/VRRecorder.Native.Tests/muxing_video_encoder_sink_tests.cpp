#include "muxing_video_encoder_sink.hpp"
#include "encoded_media_packet_submission_test_support.hpp"

#include <algorithm>
#include <chrono>
#include <cstddef>
#include <condition_variable>
#include <cstdlib>
#include <iostream>
#include <mutex>
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
using namespace vrrecorder::native::test;

EncodedMediaPacket VideoPacket(std::int64_t timestamp)
{
    return {
        MediaStreamKind::Video,
        timestamp,
        timestamp,
        33'333,
        timestamp == 0,
        std::vector<std::byte>(1'024, std::byte{0x01}),
    };
}

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

class RecordingH264DescriptorSubmissionPort final
    : public H264DescriptorPacketSubmissionPort {
public:
    Mp4MuxResult SubmitVideoDescriptorBatch(
        const void *encoder_identity,
        const H264StreamDescriptor &descriptor,
        std::span<const EncodedMediaPacket> packets) noexcept override
    {
        ++submit_calls;
        identity = encoder_identity;
        try {
            submitted_descriptor = descriptor;
            submitted_packets.assign(packets.begin(), packets.end());
        } catch (...) {
            return Mp4MuxResult::MuxFailed;
        }
        return result;
    }

    Mp4MuxResult result = Mp4MuxResult::Written;
    const void *identity = nullptr;
    H264StreamDescriptor submitted_descriptor {};
    std::vector<EncodedMediaPacket> submitted_packets;
    std::size_t submit_calls = 0;
};

class ScriptedPacketEncoder final : public PacketVideoEncoder {
public:
    PacketVideoEncoderWrite Encode(
        const ScheduledVideoFrame &frame) noexcept override
    {
        ++encode_calls;
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
    std::size_t encode_calls = 0;
    std::size_t finish_calls = 0;
    std::size_t abort_calls = 0;
};

class BlockingPacketEncoder final : public PacketVideoEncoder {
public:
    PacketVideoEncoderWrite Encode(
        const ScheduledVideoFrame &) noexcept override
    {
        std::unique_lock lock(mutex);
        encode_entered = true;
        encode_active = true;
        ++active_calls;
        maximum_active_calls = std::max(
            maximum_active_calls,
            active_calls);
        changed.notify_all();
        changed.wait(lock, [&] { return release_encode; });
        --active_calls;
        encode_active = false;
        changed.notify_all();
        return {VRREC_STATUS_OK, 150, {VideoPacket(0)}};
    }

    PacketVideoEncoderWrite Finish() noexcept override
    {
        const std::lock_guard lock(mutex);
        ++finish_calls;
        ++active_calls;
        maximum_active_calls = std::max(
            maximum_active_calls,
            active_calls);
        --active_calls;
        changed.notify_all();
        return {VRREC_STATUS_OK, 200, {}};
    }

    void Abort() noexcept override
    {
        std::unique_lock lock(mutex);
        abort_entered = true;
        changed.notify_all();
        changed.wait(lock, [&] { return !encode_active; });
        ++abort_calls;
        changed.notify_all();
    }

    std::mutex mutex;
    std::condition_variable changed;
    std::size_t active_calls = 0;
    std::size_t maximum_active_calls = 0;
    std::size_t finish_calls = 0;
    std::size_t abort_calls = 0;
    bool encode_entered = false;
    bool encode_active = false;
    bool release_encode = false;
    bool abort_entered = false;
};

void SubmitsEveryEncodedVideoPacketToTheSharedMuxTimeline()
{
    ScriptedPacketEncoder encoder;
    encoder.encode = {
        VRREC_STATUS_OK,
        250,
        {VideoPacket(0), VideoPacket(33'333)},
    };
    RecordingPacketSubmissionPort mux;
    MuxingVideoEncoderSink sink(encoder, mux);
    ScheduledVideoFrame frame {};
    frame.output_tick = 7;

    const auto write = sink.Write(frame);
    CHECK(write.status == VRREC_STATUS_OK);
    CHECK(write.muxed_packet_count == 2);
    CHECK(write.encode_latency_microseconds == 250);
    CHECK(mux.packets.size() == 2);
    CHECK(encoder.frames.size() == 1);
    CHECK(encoder.frames.front().output_tick == 7);
}

void KeepsEncoderBufferingAsAZeroPacketSuccess()
{
    ScriptedPacketEncoder encoder;
    RecordingPacketSubmissionPort mux;
    MuxingVideoEncoderSink sink(encoder, mux);

    const auto write = sink.Write({});
    CHECK(write.status == VRREC_STATUS_OK);
    CHECK(write.muxed_packet_count == 0);
    CHECK(mux.packets.empty());
    CHECK(mux.submit_calls == 0);
    CHECK(mux.batch_sizes.empty());
}

void SubmitsTheFirstDescriptorPacketThroughTheAtomicPort()
{
    ScriptedPacketEncoder encoder;
    int encoder_identity = 0;
    const auto descriptor = VideoDescriptor();
    encoder.encode = {
        VRREC_STATUS_OK,
        250,
        {VideoPacket(0)},
        true,
        &encoder_identity,
        &descriptor,
    };
    RecordingPacketSubmissionPort mux;
    RecordingH264DescriptorSubmissionPort descriptor_mux;
    MuxingVideoEncoderSink sink(encoder, mux, descriptor_mux);

    const auto write = sink.Write({});

    CHECK(write.status == VRREC_STATUS_OK);
    CHECK(write.muxed_packet_count == 1);
    CHECK(write.encode_latency_microseconds == 250);
    CHECK(mux.submit_calls == 0);
    CHECK(descriptor_mux.submit_calls == 1);
    CHECK(descriptor_mux.identity == &encoder_identity);
    CHECK(descriptor_mux.submitted_descriptor.codec_extradata ==
          descriptor.codec_extradata);
    CHECK(descriptor_mux.submitted_packets.size() == 1);
    CHECK(descriptor_mux.submitted_packets.front().dts_microseconds == 0);
}

void RejectsDescriptorMetadataWithoutTheAtomicPort()
{
    ScriptedPacketEncoder encoder;
    int encoder_identity = 0;
    const auto descriptor = VideoDescriptor();
    encoder.encode = {
        VRREC_STATUS_OK,
        250,
        {VideoPacket(0)},
        true,
        &encoder_identity,
        &descriptor,
    };
    RecordingPacketSubmissionPort mux;
    MuxingVideoEncoderSink sink(encoder, mux);

    const auto write = sink.Write({});

    CHECK(write.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(write.failure_stage == VideoEncoderFailureStage::Muxing);
    CHECK(write.muxed_packet_count == 0);
    CHECK(mux.submit_calls == 0);
    CHECK(mux.failed_streams ==
          std::vector<MediaStreamKind>({MediaStreamKind::Video}));
    CHECK(encoder.abort_calls == 1);
}

void AbortsBothSidesWhenMuxingFails()
{
    ScriptedPacketEncoder encoder;
    encoder.encode = {VRREC_STATUS_OK, 100, {VideoPacket(0)}};
    RecordingPacketSubmissionPort mux;
    mux.submit_result = Mp4MuxResult::MuxFailed;
    MuxingVideoEncoderSink sink(encoder, mux);

    const auto write = sink.Write({});
    CHECK(write.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(write.failure_stage == VideoEncoderFailureStage::Muxing);
    CHECK(write.muxed_packet_count == 0);
    CHECK(encoder.abort_calls == 1);
    CHECK(mux.failed_streams ==
          std::vector<MediaStreamKind>({MediaStreamKind::Video}));
}

void FlushesEncoderPacketsWithoutFinalizingTheSharedMuxer()
{
    ScriptedPacketEncoder encoder;
    encoder.finish = {VRREC_STATUS_OK, 300, {VideoPacket(0)}};
    RecordingPacketSubmissionPort mux;
    MuxingVideoEncoderSink sink(encoder, mux);

    const auto finish = sink.Finish();
    CHECK(finish.status == VRREC_STATUS_OK);
    CHECK(finish.muxed_packet_count == 1);
    CHECK(encoder.finish_calls == 1);
    CHECK(mux.packets.size() == 1);
    CHECK(mux.finished_streams ==
          std::vector<MediaStreamKind>({MediaStreamKind::Video}));
    CHECK(mux.failed_streams.empty());
}

void SuccessfulFinishTerminalizesTheVideoEncoderSink()
{
    ScriptedPacketEncoder encoder;
    RecordingPacketSubmissionPort mux;
    MuxingVideoEncoderSink sink(encoder, mux);

    CHECK(sink.Finish().status == VRREC_STATUS_OK);
    CHECK(sink.Write({}).status == VRREC_STATUS_INVALID_STATE);
    CHECK(sink.Finish().status == VRREC_STATUS_INVALID_STATE);
    CHECK(encoder.encode_calls == 0);
    CHECK(encoder.finish_calls == 1);
    CHECK(encoder.abort_calls == 0);
    CHECK(mux.finished_streams ==
          std::vector<MediaStreamKind>({MediaStreamKind::Video}));
    CHECK(mux.failed_streams.empty());
}

void EncoderFailureAbortsBothSidesAndRejectsFurtherFrames()
{
    ScriptedPacketEncoder encoder;
    encoder.encode = {VRREC_STATUS_INTERNAL_ERROR, 50, {}};
    RecordingPacketSubmissionPort mux;
    MuxingVideoEncoderSink sink(encoder, mux);

    const auto failed = sink.Write({});
    CHECK(failed.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(failed.failure_stage == VideoEncoderFailureStage::Encoding);
    CHECK(encoder.abort_calls == 1);
    CHECK(mux.failed_streams ==
          std::vector<MediaStreamKind>({MediaStreamKind::Video}));
    CHECK(sink.Write({}).status == VRREC_STATUS_INVALID_STATE);
    CHECK(encoder.encode_calls == 1);
}

void FinishEncodingFailureSignalsTheMuxBoundaryExactlyOnce()
{
    ScriptedPacketEncoder encoder;
    encoder.finish = {VRREC_STATUS_INTERNAL_ERROR, 75, {}};
    RecordingPacketSubmissionPort mux;
    MuxingVideoEncoderSink sink(encoder, mux);

    const auto failed = sink.Finish();
    CHECK(failed.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(failed.failure_stage == VideoEncoderFailureStage::Encoding);
    CHECK(failed.muxed_packet_count == 0);
    CHECK(failed.encode_latency_microseconds == 75);
    CHECK(encoder.finish_calls == 1);
    CHECK(encoder.abort_calls == 1);
    CHECK(mux.failed_streams ==
          std::vector<MediaStreamKind>({MediaStreamKind::Video}));
    CHECK(mux.finished_streams.empty());
}

void FinishBatchSubmissionFailureSignalsMuxBoundaryExactlyOnce()
{
    ScriptedPacketEncoder encoder;
    encoder.finish = {
        VRREC_STATUS_OK,
        225,
        {VideoPacket(0), VideoPacket(33'333)},
    };
    RecordingPacketSubmissionPort mux;
    mux.submit_result = Mp4MuxResult::MuxFailed;
    MuxingVideoEncoderSink sink(encoder, mux);

    const auto failed = sink.Finish();
    CHECK(failed.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(failed.failure_stage == VideoEncoderFailureStage::Muxing);
    CHECK(failed.muxed_packet_count == 0);
    CHECK(failed.encode_latency_microseconds == 225);
    CHECK(encoder.finish_calls == 1);
    CHECK(encoder.abort_calls == 1);
    CHECK(mux.submit_calls == 1);
    CHECK(mux.producers ==
          std::vector<MediaStreamKind>({MediaStreamKind::Video}));
    CHECK(mux.batch_sizes == std::vector<std::size_t>({2}));
    CHECK(mux.finished_streams.empty());
    CHECK(mux.failed_streams ==
          std::vector<MediaStreamKind>({MediaStreamKind::Video}));

    CHECK(sink.Finish().status == VRREC_STATUS_INVALID_STATE);
    CHECK(encoder.finish_calls == 1);
    CHECK(encoder.abort_calls == 1);
    CHECK(mux.submit_calls == 1);
    CHECK(mux.producers.size() == 1);
    CHECK(mux.batch_sizes.size() == 1);
    CHECK(mux.finished_streams.empty());
    CHECK(mux.failed_streams ==
          std::vector<MediaStreamKind>({MediaStreamKind::Video}));
}

void FinalMuxCompletionFailureAbortsAndMapsToMuxing()
{
    ScriptedPacketEncoder encoder;
    encoder.finish = {VRREC_STATUS_OK, 225, {VideoPacket(0)}};
    RecordingPacketSubmissionPort mux;
    mux.encoder_finished_status = VRREC_STATUS_INTERNAL_ERROR;
    MuxingVideoEncoderSink sink(encoder, mux);

    const auto failed = sink.Finish();
    CHECK(failed.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(failed.failure_stage == VideoEncoderFailureStage::Muxing);
    CHECK(failed.muxed_packet_count == 0);
    CHECK(failed.encode_latency_microseconds == 225);
    CHECK(encoder.finish_calls == 1);
    CHECK(encoder.abort_calls == 1);
    CHECK(mux.packets.size() == 1);
    CHECK(mux.finished_streams ==
          std::vector<MediaStreamKind>({MediaStreamKind::Video}));
    CHECK(mux.failed_streams ==
          std::vector<MediaStreamKind>({MediaStreamKind::Video}));
    CHECK(sink.Finish().status == VRREC_STATUS_INVALID_STATE);
}

void BatchSubmissionFailureReportsNoCommittedPackets()
{
    ScriptedPacketEncoder encoder;
    encoder.encode = {
        VRREC_STATUS_OK,
        125,
        {VideoPacket(0), VideoPacket(33'333), VideoPacket(66'666)},
    };
    RecordingPacketSubmissionPort mux;
    mux.submit_result = Mp4MuxResult::InvalidPacket;
    MuxingVideoEncoderSink sink(encoder, mux);

    const auto failed = sink.Write({});
    CHECK(failed.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(failed.failure_stage == VideoEncoderFailureStage::Muxing);
    CHECK(failed.muxed_packet_count == 0);
    CHECK(failed.encode_latency_microseconds == 125);
    CHECK(mux.submit_calls == 1);
    CHECK(mux.packets.size() == 3);
    CHECK(mux.batch_sizes == std::vector<std::size_t>({3}));
    CHECK(encoder.abort_calls == 1);
    CHECK(mux.failed_streams ==
          std::vector<MediaStreamKind>({MediaStreamKind::Video}));
    CHECK(sink.Write({}).status == VRREC_STATUS_INVALID_STATE);
    CHECK(encoder.encode_calls == 1);
}

void AbortTerminalizesMuxingBeforeWaitingForInFlightEncode()
{
    BlockingPacketEncoder encoder;
    CoordinatedPacketSubmissionPort mux;
    MuxingVideoEncoderSink sink(encoder, mux);
    VideoEncoderWrite write {};
    std::thread writer([&] { write = sink.Write({}); });
    {
        std::unique_lock lock(encoder.mutex);
        CHECK(encoder.changed.wait_for(
            lock,
            std::chrono::seconds(2),
            [&] { return encoder.encode_entered; }));
    }

    std::thread aborter([&] { sink.Abort(); });
    {
        std::unique_lock lock(mux.mutex);
        CHECK(mux.changed.wait_for(
            lock,
            std::chrono::seconds(2),
            [&] { return mux.failed_calls == 1; }));
        CHECK(mux.submit_calls == 0);
    }
    {
        const std::lock_guard lock(encoder.mutex);
        encoder.release_encode = true;
    }
    encoder.changed.notify_all();
    writer.join();
    aborter.join();

    CHECK(write.status == VRREC_STATUS_INVALID_STATE);
    CHECK(write.muxed_packet_count == 0);
    CHECK(write.encode_latency_microseconds == 150);
    {
        const std::lock_guard lock(encoder.mutex);
        CHECK(encoder.abort_calls == 1);
    }
    {
        const std::lock_guard lock(mux.mutex);
        CHECK(mux.failed_calls == 1);
        CHECK(mux.submit_calls == 0);
    }
}

void FinishWaitsForAnInFlightWriteBeforeDraining()
{
    BlockingPacketEncoder encoder;
    CoordinatedPacketSubmissionPort mux;
    MuxingVideoEncoderSink sink(encoder, mux);
    VideoEncoderWrite write {};
    VideoEncoderWrite finish {};
    std::thread writer([&] { write = sink.Write({}); });
    {
        std::unique_lock lock(encoder.mutex);
        CHECK(encoder.changed.wait_for(
            lock,
            std::chrono::seconds(2),
            [&] { return encoder.encode_entered; }));
    }

    std::mutex finish_mutex;
    std::condition_variable finish_changed;
    bool finish_started = false;
    std::thread finisher([&] {
        {
            const std::lock_guard lock(finish_mutex);
            finish_started = true;
        }
        finish_changed.notify_all();
        finish = sink.Finish();
    });
    {
        std::unique_lock lock(finish_mutex);
        CHECK(finish_changed.wait_for(
            lock,
            std::chrono::seconds(2),
            [&] { return finish_started; }));
    }
    {
        std::unique_lock lock(encoder.mutex);
        CHECK(!encoder.changed.wait_for(
            lock,
            std::chrono::milliseconds(100),
            [&] { return encoder.finish_calls != 0; }));
        encoder.release_encode = true;
    }
    encoder.changed.notify_all();
    writer.join();
    finisher.join();

    CHECK(write.status == VRREC_STATUS_OK);
    CHECK(write.muxed_packet_count == 1);
    CHECK(write.encode_latency_microseconds == 150);
    CHECK(finish.status == VRREC_STATUS_OK);
    CHECK(finish.encode_latency_microseconds == 200);
    {
        const std::lock_guard lock(encoder.mutex);
        CHECK(encoder.finish_calls == 1);
        CHECK(encoder.maximum_active_calls == 1);
    }
    {
        const std::lock_guard lock(mux.mutex);
        CHECK(mux.order == std::vector<int>({1, 2}));
        CHECK(mux.submit_calls == 1);
        CHECK(mux.finished_calls == 1);
        CHECK(mux.failed_calls == 0);
    }
}

void AbortDominatesAnInFlightMuxCompletion()
{
    ScriptedPacketEncoder encoder;
    encoder.finish = {VRREC_STATUS_OK, 225, {}};
    BlockingSuccessfulCompletionPort mux;
    MuxingVideoEncoderSink sink(encoder, mux);
    VideoEncoderWrite finish {};

    std::thread finisher([&] { finish = sink.Finish(); });
    {
        std::unique_lock lock(mux.mutex);
        CHECK(mux.changed.wait_for(
            lock,
            std::chrono::seconds(2),
            [&] { return mux.finish_entered; }));
    }

    std::thread aborter([&] { sink.Abort(); });
    {
        std::unique_lock lock(mux.mutex);
        CHECK(mux.changed.wait_for(
            lock,
            std::chrono::seconds(2),
            [&] { return mux.failed_calls == 1; }));
        mux.release_finish = true;
    }
    mux.changed.notify_all();
    aborter.join();
    finisher.join();

    CHECK(finish.status == VRREC_STATUS_INVALID_STATE);
    CHECK(finish.failure_stage == VideoEncoderFailureStage::Muxing);
    CHECK(finish.muxed_packet_count == 0);
    CHECK(finish.encode_latency_microseconds == 225);
    CHECK(encoder.finish_calls == 1);
    CHECK(encoder.abort_calls == 1);
    CHECK(mux.finished_calls == 1);
    CHECK(mux.failed_calls == 1);
    CHECK(mux.finished_stream == MediaStreamKind::Video);
    CHECK(mux.failed_stream == MediaStreamKind::Video);
}

void RejectsAMixedStreamBatchBeforeMutatingTheMuxer()
{
    ScriptedPacketEncoder encoder;
    auto wrong_stream = VideoPacket(33'333);
    wrong_stream.stream = MediaStreamKind::Audio;
    encoder.encode = {
        VRREC_STATUS_OK,
        100,
        {VideoPacket(0), wrong_stream},
    };
    RecordingPacketSubmissionPort mux;
    MuxingVideoEncoderSink sink(encoder, mux);

    const auto write = sink.Write({});
    CHECK(write.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(write.failure_stage == VideoEncoderFailureStage::Muxing);
    CHECK(mux.packets.empty());
    CHECK(encoder.abort_calls == 1);
    CHECK(mux.failed_streams ==
          std::vector<MediaStreamKind>({MediaStreamKind::Video}));
}

}

int main()
{
    SubmitsEveryEncodedVideoPacketToTheSharedMuxTimeline();
    KeepsEncoderBufferingAsAZeroPacketSuccess();
    SubmitsTheFirstDescriptorPacketThroughTheAtomicPort();
    RejectsDescriptorMetadataWithoutTheAtomicPort();
    AbortsBothSidesWhenMuxingFails();
    FlushesEncoderPacketsWithoutFinalizingTheSharedMuxer();
    SuccessfulFinishTerminalizesTheVideoEncoderSink();
    EncoderFailureAbortsBothSidesAndRejectsFurtherFrames();
    FinishEncodingFailureSignalsTheMuxBoundaryExactlyOnce();
    FinishBatchSubmissionFailureSignalsMuxBoundaryExactlyOnce();
    FinalMuxCompletionFailureAbortsAndMapsToMuxing();
    BatchSubmissionFailureReportsNoCommittedPackets();
    AbortTerminalizesMuxingBeforeWaitingForInFlightEncode();
    FinishWaitsForAnInFlightWriteBeforeDraining();
    AbortDominatesAnInFlightMuxCompletion();
    RejectsAMixedStreamBatchBeforeMutatingTheMuxer();
    return 0;
}
