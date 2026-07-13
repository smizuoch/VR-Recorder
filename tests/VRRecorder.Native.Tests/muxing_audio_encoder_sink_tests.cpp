#include "muxing_audio_encoder_sink.hpp"
#include "encoded_media_packet_submission_test_support.hpp"

#include <algorithm>
#include <chrono>
#include <cstddef>
#include <condition_variable>
#include <cstdlib>
#include <iostream>
#include <mutex>
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

class BlockingPacketEncoder final : public PacketAudioEncoder {
public:
    PacketAudioEncoderWrite EncodePcm48k(
        std::uint64_t,
        std::span<const float>) noexcept override
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
        return {VRREC_STATUS_OK, {AudioPacket(0)}};
    }

    PacketAudioEncoderWrite Finish() noexcept override
    {
        const std::lock_guard lock(mutex);
        ++finish_calls;
        ++active_calls;
        maximum_active_calls = std::max(
            maximum_active_calls,
            active_calls);
        --active_calls;
        changed.notify_all();
        return {VRREC_STATUS_OK, {}};
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

void SubmitsEveryEncodedAacPacketToTheSharedMuxTimeline()
{
    ScriptedPacketEncoder encoder;
    encoder.encode = {
        VRREC_STATUS_OK,
        {AudioPacket(0), AudioPacket(21'333)},
    };
    RecordingPacketSubmissionPort mux;
    MuxingAudioEncoderSink sink(encoder, mux);
    const std::vector<float> samples {0.25F, -0.25F, 0.5F, -0.5F};

    const auto write = sink.WritePcm48k(480, samples);
    CHECK(write.status == VRREC_STATUS_OK);
    CHECK(write.muxed_packet_count == 2);
    CHECK(encoder.last_start_frame == 480);
    CHECK(encoder.last_samples == samples);
    CHECK(mux.packets.size() == 2);
}

void KeepsEncoderBufferingAsAZeroPacketSuccess()
{
    ScriptedPacketEncoder encoder;
    RecordingPacketSubmissionPort mux;
    MuxingAudioEncoderSink sink(encoder, mux);

    const auto write = sink.WritePcm48k(0, std::vector<float> {0.0F, 0.0F});
    CHECK(write.status == VRREC_STATUS_OK);
    CHECK(write.muxed_packet_count == 0);
    CHECK(mux.packets.empty());
    CHECK(mux.submit_calls == 0);
    CHECK(mux.batch_sizes.empty());
}

void AbortsBothSidesWhenMuxingFails()
{
    ScriptedPacketEncoder encoder;
    encoder.encode = {VRREC_STATUS_OK, {AudioPacket(0)}};
    RecordingPacketSubmissionPort mux;
    mux.submit_result = Mp4MuxResult::MuxFailed;
    MuxingAudioEncoderSink sink(encoder, mux);

    const auto write = sink.WritePcm48k(0, std::vector<float> {0.0F, 0.0F});
    CHECK(write.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(write.failure_stage == AudioEncoderFailureStage::Muxing);
    CHECK(write.muxed_packet_count == 0);
    CHECK(encoder.abort_calls == 1);
    CHECK(mux.failed_streams ==
          std::vector<MediaStreamKind>({MediaStreamKind::Audio}));
}

void FlushesAacPacketsWithoutFinalizingTheSharedMuxer()
{
    ScriptedPacketEncoder encoder;
    encoder.finish = {VRREC_STATUS_OK, {AudioPacket(0)}};
    RecordingPacketSubmissionPort mux;
    MuxingAudioEncoderSink sink(encoder, mux);

    const auto finish = sink.Finish();
    CHECK(finish.status == VRREC_STATUS_OK);
    CHECK(finish.muxed_packet_count == 1);
    CHECK(encoder.finish_calls == 1);
    CHECK(mux.packets.size() == 1);
    CHECK(mux.finished_streams ==
          std::vector<MediaStreamKind>({MediaStreamKind::Audio}));
    CHECK(mux.failed_streams.empty());
}

void SuccessfulFinishTerminalizesTheAudioEncoderSink()
{
    ScriptedPacketEncoder encoder;
    RecordingPacketSubmissionPort mux;
    MuxingAudioEncoderSink sink(encoder, mux);

    CHECK(sink.Finish().status == VRREC_STATUS_OK);
    CHECK(sink.WritePcm48k(0, std::vector<float> {0.0F, 0.0F}).status ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(sink.Finish().status == VRREC_STATUS_INVALID_STATE);
    CHECK(encoder.encode_calls == 0);
    CHECK(encoder.finish_calls == 1);
    CHECK(encoder.abort_calls == 0);
    CHECK(mux.finished_streams ==
          std::vector<MediaStreamKind>({MediaStreamKind::Audio}));
    CHECK(mux.failed_streams.empty());
}

void EncoderFailureAbortsBothSidesAndRejectsFurtherWrites()
{
    ScriptedPacketEncoder encoder;
    encoder.encode = {VRREC_STATUS_INTERNAL_ERROR, {}};
    RecordingPacketSubmissionPort mux;
    MuxingAudioEncoderSink sink(encoder, mux);
    const std::vector<float> samples {0.0F, 0.0F};

    const auto failed = sink.WritePcm48k(0, samples);
    CHECK(failed.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(failed.failure_stage == AudioEncoderFailureStage::Encoding);
    CHECK(encoder.abort_calls == 1);
    CHECK(mux.failed_streams ==
          std::vector<MediaStreamKind>({MediaStreamKind::Audio}));
    CHECK(sink.WritePcm48k(1, samples).status ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(encoder.encode_calls == 1);
}

void FinishEncodingFailureSignalsTheMuxBoundaryExactlyOnce()
{
    ScriptedPacketEncoder encoder;
    encoder.finish = {VRREC_STATUS_INTERNAL_ERROR, {}};
    RecordingPacketSubmissionPort mux;
    MuxingAudioEncoderSink sink(encoder, mux);

    const auto failed = sink.Finish();
    CHECK(failed.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(failed.failure_stage == AudioEncoderFailureStage::Encoding);
    CHECK(failed.muxed_packet_count == 0);
    CHECK(encoder.finish_calls == 1);
    CHECK(encoder.abort_calls == 1);
    CHECK(mux.failed_streams ==
          std::vector<MediaStreamKind>({MediaStreamKind::Audio}));
    CHECK(mux.finished_streams.empty());
}

void FinishBatchSubmissionFailureSignalsMuxBoundaryExactlyOnce()
{
    ScriptedPacketEncoder encoder;
    encoder.finish = {
        VRREC_STATUS_OK,
        {AudioPacket(0), AudioPacket(21'333)},
    };
    RecordingPacketSubmissionPort mux;
    mux.submit_result = Mp4MuxResult::MuxFailed;
    MuxingAudioEncoderSink sink(encoder, mux);

    const auto failed = sink.Finish();
    CHECK(failed.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(failed.failure_stage == AudioEncoderFailureStage::Muxing);
    CHECK(failed.muxed_packet_count == 0);
    CHECK(encoder.finish_calls == 1);
    CHECK(encoder.abort_calls == 1);
    CHECK(mux.submit_calls == 1);
    CHECK(mux.producers ==
          std::vector<MediaStreamKind>({MediaStreamKind::Audio}));
    CHECK(mux.batch_sizes == std::vector<std::size_t>({2}));
    CHECK(mux.finished_streams.empty());
    CHECK(mux.failed_streams ==
          std::vector<MediaStreamKind>({MediaStreamKind::Audio}));

    CHECK(sink.Finish().status == VRREC_STATUS_INVALID_STATE);
    CHECK(encoder.finish_calls == 1);
    CHECK(encoder.abort_calls == 1);
    CHECK(mux.submit_calls == 1);
    CHECK(mux.producers.size() == 1);
    CHECK(mux.batch_sizes.size() == 1);
    CHECK(mux.finished_streams.empty());
    CHECK(mux.failed_streams ==
          std::vector<MediaStreamKind>({MediaStreamKind::Audio}));
}

void FinalMuxCompletionFailureAbortsAndMapsToMuxing()
{
    ScriptedPacketEncoder encoder;
    encoder.finish = {VRREC_STATUS_OK, {AudioPacket(0)}};
    RecordingPacketSubmissionPort mux;
    mux.encoder_finished_status = VRREC_STATUS_INTERNAL_ERROR;
    MuxingAudioEncoderSink sink(encoder, mux);

    const auto failed = sink.Finish();
    CHECK(failed.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(failed.failure_stage == AudioEncoderFailureStage::Muxing);
    CHECK(failed.muxed_packet_count == 0);
    CHECK(encoder.finish_calls == 1);
    CHECK(encoder.abort_calls == 1);
    CHECK(mux.packets.size() == 1);
    CHECK(mux.finished_streams ==
          std::vector<MediaStreamKind>({MediaStreamKind::Audio}));
    CHECK(mux.failed_streams ==
          std::vector<MediaStreamKind>({MediaStreamKind::Audio}));
    CHECK(sink.Finish().status == VRREC_STATUS_INVALID_STATE);
}

void BatchSubmissionFailureReportsNoCommittedPackets()
{
    ScriptedPacketEncoder encoder;
    encoder.encode = {
        VRREC_STATUS_OK,
        {AudioPacket(0), AudioPacket(21'333), AudioPacket(42'666)},
    };
    RecordingPacketSubmissionPort mux;
    mux.submit_result = Mp4MuxResult::InvalidPacket;
    MuxingAudioEncoderSink sink(encoder, mux);

    const auto failed = sink.WritePcm48k(
        0,
        std::vector<float> {0.0F, 0.0F});
    CHECK(failed.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(failed.failure_stage == AudioEncoderFailureStage::Muxing);
    CHECK(failed.muxed_packet_count == 0);
    CHECK(mux.submit_calls == 1);
    CHECK(mux.packets.size() == 3);
    CHECK(mux.batch_sizes == std::vector<std::size_t>({3}));
    CHECK(encoder.abort_calls == 1);
    CHECK(mux.failed_streams ==
          std::vector<MediaStreamKind>({MediaStreamKind::Audio}));
    CHECK(sink.WritePcm48k(
              1,
              std::vector<float> {0.0F, 0.0F}).status ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(encoder.encode_calls == 1);
}

void AbortTerminalizesMuxingBeforeWaitingForInFlightEncode()
{
    BlockingPacketEncoder encoder;
    CoordinatedPacketSubmissionPort mux;
    MuxingAudioEncoderSink sink(encoder, mux);
    StereoAudioEncoderWrite write {};
    std::thread writer([&] {
        write = sink.WritePcm48k(
            0,
            std::vector<float> {0.0F, 0.0F});
    });
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
    MuxingAudioEncoderSink sink(encoder, mux);
    StereoAudioEncoderWrite write {};
    StereoAudioEncoderWrite finish {};
    std::thread writer([&] {
        write = sink.WritePcm48k(
            0,
            std::vector<float> {0.0F, 0.0F});
    });
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
    CHECK(finish.status == VRREC_STATUS_OK);
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
    BlockingSuccessfulCompletionPort mux;
    MuxingAudioEncoderSink sink(encoder, mux);
    StereoAudioEncoderWrite finish {};

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
    CHECK(finish.failure_stage == AudioEncoderFailureStage::Muxing);
    CHECK(finish.muxed_packet_count == 0);
    CHECK(encoder.finish_calls == 1);
    CHECK(encoder.abort_calls == 1);
    CHECK(mux.finished_calls == 1);
    CHECK(mux.failed_calls == 1);
    CHECK(mux.finished_stream == MediaStreamKind::Audio);
    CHECK(mux.failed_stream == MediaStreamKind::Audio);
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
    RecordingPacketSubmissionPort mux;
    MuxingAudioEncoderSink sink(encoder, mux);

    const auto write = sink.WritePcm48k(
        0,
        std::vector<float> {0.0F, 0.0F});
    CHECK(write.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(write.failure_stage == AudioEncoderFailureStage::Muxing);
    CHECK(mux.packets.empty());
    CHECK(encoder.abort_calls == 1);
    CHECK(mux.failed_streams ==
          std::vector<MediaStreamKind>({MediaStreamKind::Audio}));
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
