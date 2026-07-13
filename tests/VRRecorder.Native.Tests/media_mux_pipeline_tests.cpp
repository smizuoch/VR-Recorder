#include "media_mux_pipeline.hpp"
#include "fragmented_mp4_test_support.hpp"
#include "muxing_audio_encoder_sink.hpp"
#include "muxing_video_encoder_sink.hpp"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <condition_variable>
#include <cstdlib>
#include <iostream>
#include <mutex>
#include <span>
#include <thread>
#include <type_traits>
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

static_assert(std::is_constructible_v<
    MuxingAudioEncoderSink,
    PacketAudioEncoder &,
    MediaMuxPipeline &>);
static_assert(std::is_constructible_v<
    MuxingVideoEncoderSink,
    PacketVideoEncoder &,
    MediaMuxPipeline &>);
static_assert(!std::is_constructible_v<
    MuxingAudioEncoderSink,
    PacketAudioEncoder &,
    SharedMuxFinalizationSession &>);
static_assert(!std::is_constructible_v<
    MuxingVideoEncoderSink,
    PacketVideoEncoder &,
    SharedMuxFinalizationSession &>);

class RecordingMuxer final : public FragmentedMp4Muxer {
public:
    vrrec_status_t WriteHeader(
        const FragmentedMp4StreamConfiguration &) noexcept override
    {
        order.push_back(0);
        return VRREC_STATUS_OK;
    }

    vrrec_status_t WritePacket(
        const EncodedMediaPacket &packet) noexcept override
    {
        ++write_calls;
        packets.push_back(packet);
        order.push_back(1);
        if (fail_write_call != 0 && write_calls == fail_write_call) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        return VRREC_STATUS_OK;
    }

    vrrec_status_t WriteTrailer() noexcept override
    {
        order.push_back(3);
        return VRREC_STATUS_OK;
    }

    vrrec_status_t FlushFile() noexcept override
    {
        order.push_back(4);
        return VRREC_STATUS_OK;
    }

    void Abort() noexcept override
    {
        order.push_back(5);
        ++abort_calls;
    }

    std::vector<EncodedMediaPacket> packets;
    std::vector<int> order;
    std::size_t write_calls = 0;
    std::size_t fail_write_call = 0;
    std::size_t abort_calls = 0;
};

class BlockingFailureMuxer final : public FragmentedMp4Muxer {
public:
    vrrec_status_t WriteHeader(
        const FragmentedMp4StreamConfiguration &) noexcept override
    {
        return VRREC_STATUS_OK;
    }

    vrrec_status_t WritePacket(
        const EncodedMediaPacket &packet) noexcept override
    {
        std::unique_lock lock(mutex);
        packets.push_back(packet);
        ++write_calls;
        if (write_calls != fail_write_call) {
            return VRREC_STATUS_OK;
        }
        failed_write_entered = true;
        changed.notify_all();
        changed.wait(lock, [&] { return release_failed_write; });
        return VRREC_STATUS_INTERNAL_ERROR;
    }

    vrrec_status_t WriteTrailer() noexcept override
    {
        ++trailer_calls;
        return VRREC_STATUS_OK;
    }

    vrrec_status_t FlushFile() noexcept override
    {
        ++flush_calls;
        return VRREC_STATUS_OK;
    }

    void Abort() noexcept override
    {
        ++abort_calls;
    }

    std::mutex mutex;
    std::condition_variable changed;
    std::vector<EncodedMediaPacket> packets;
    std::size_t write_calls = 0;
    std::size_t fail_write_call = 2;
    std::size_t trailer_calls = 0;
    std::size_t flush_calls = 0;
    std::size_t abort_calls = 0;
    bool failed_write_entered = false;
    bool release_failed_write = false;
};

class BlockingHeaderMuxer final : public FragmentedMp4Muxer {
public:
    vrrec_status_t WriteHeader(
        const FragmentedMp4StreamConfiguration &) noexcept override
    {
        std::unique_lock lock(mutex);
        header_entered = true;
        changed.notify_all();
        changed.wait(lock, [&] { return release_header; });
        return VRREC_STATUS_OK;
    }

    vrrec_status_t WritePacket(
        const EncodedMediaPacket &) noexcept override
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
        const std::lock_guard lock(mutex);
        ++abort_calls;
        changed.notify_all();
    }

    std::mutex mutex;
    std::condition_variable changed;
    std::size_t abort_calls = 0;
    bool header_entered = false;
    bool release_header = false;
};

class BlockingTrailerMuxer final : public FragmentedMp4Muxer {
public:
    vrrec_status_t WriteHeader(
        const FragmentedMp4StreamConfiguration &) noexcept override
    {
        return VRREC_STATUS_OK;
    }

    vrrec_status_t WritePacket(
        const EncodedMediaPacket &) noexcept override
    {
        return VRREC_STATUS_OK;
    }

    vrrec_status_t WriteTrailer() noexcept override
    {
        std::unique_lock lock(mutex);
        ++trailer_calls;
        trailer_entered = true;
        changed.notify_all();
        changed.wait(lock, [&] { return release_trailer; });
        return VRREC_STATUS_OK;
    }

    vrrec_status_t FlushFile() noexcept override
    {
        const std::lock_guard lock(mutex);
        ++flush_calls;
        return VRREC_STATUS_OK;
    }

    void Abort() noexcept override
    {
        const std::lock_guard lock(mutex);
        ++abort_calls;
        changed.notify_all();
    }

    std::mutex mutex;
    std::condition_variable changed;
    std::size_t trailer_calls = 0;
    std::size_t flush_calls = 0;
    std::size_t abort_calls = 0;
    bool trailer_entered = false;
    bool release_trailer = false;
};

class RecordingMediaEvents : public MediaEventSink {
public:
    void FirstVideoPacketMuxed() noexcept override
    {
    }

    void Stopped(std::uint64_t, std::uint64_t) noexcept override
    {
    }

    void Faulted(vrrec_status_t, const char *) noexcept override
    {
    }

    void AudioEndpointAvailabilityChanged(
        AudioEndpointRole,
        bool,
        std::uint64_t) noexcept override
    {
    }

    void AvSyncDriftExceeded(
        std::uint64_t video_pts_microseconds,
        std::uint64_t audio_pts_microseconds,
        std::uint64_t absolute_drift_microseconds) noexcept override
    {
        ++drift_calls;
        video_pts = video_pts_microseconds;
        audio_pts = audio_pts_microseconds;
        absolute_drift = absolute_drift_microseconds;
    }

    std::size_t drift_calls = 0;
    std::uint64_t video_pts = 0;
    std::uint64_t audio_pts = 0;
    std::uint64_t absolute_drift = 0;
};

class AbortOnDriftMediaEvents final : public RecordingMediaEvents {
public:
    void AvSyncDriftExceeded(
        std::uint64_t,
        std::uint64_t,
        std::uint64_t) noexcept override
    {
        ++drift_calls;
        CHECK(pipeline != nullptr);
        const auto snapshot = pipeline->AvSyncStatistics();
        snapshot_read = snapshot.threshold_event_count == 1;
        pipeline->Abort();
        abort_returned = true;
    }

    MediaMuxPipeline *pipeline = nullptr;
    bool abort_returned = false;
    bool snapshot_read = false;
};

class BlockingDriftMediaEvents final : public RecordingMediaEvents {
public:
    void AvSyncDriftExceeded(
        std::uint64_t video_pts_microseconds,
        std::uint64_t audio_pts_microseconds,
        std::uint64_t absolute_drift_microseconds) noexcept override
    {
        RecordingMediaEvents::AvSyncDriftExceeded(
            video_pts_microseconds,
            audio_pts_microseconds,
            absolute_drift_microseconds);
        std::unique_lock lock(mutex);
        callback_entered = true;
        changed.notify_all();
        changed.wait(lock, [&] { return release_callback; });
        callback_returned = true;
        changed.notify_all();
    }

    std::mutex mutex;
    std::condition_variable changed;
    bool callback_entered = false;
    bool release_callback = false;
    bool callback_returned = false;
};

class BlockingAbortOnDriftMediaEvents final : public RecordingMediaEvents {
public:
    void AvSyncDriftExceeded(
        std::uint64_t video_pts_microseconds,
        std::uint64_t audio_pts_microseconds,
        std::uint64_t absolute_drift_microseconds) noexcept override
    {
        RecordingMediaEvents::AvSyncDriftExceeded(
            video_pts_microseconds,
            audio_pts_microseconds,
            absolute_drift_microseconds);
        std::unique_lock lock(mutex);
        callback_entered = true;
        changed.notify_all();
        changed.wait(lock, [&] { return release_callback; });
        lock.unlock();
        CHECK(pipeline != nullptr);
        pipeline->Abort();
        abort_returned = true;
        changed.notify_all();
    }

    MediaMuxPipeline *pipeline = nullptr;
    std::mutex mutex;
    std::condition_variable changed;
    bool callback_entered = false;
    bool release_callback = false;
    bool abort_returned = false;
};

class AbortAudioSinkOnDriftMediaEvents final
    : public RecordingMediaEvents {
public:
    void AvSyncDriftExceeded(
        std::uint64_t video_pts_microseconds,
        std::uint64_t audio_pts_microseconds,
        std::uint64_t absolute_drift_microseconds) noexcept override
    {
        RecordingMediaEvents::AvSyncDriftExceeded(
            video_pts_microseconds,
            audio_pts_microseconds,
            absolute_drift_microseconds);
        CHECK(audio != nullptr);
        audio->Abort();
        abort_returned = true;
    }

    MuxingAudioEncoderSink *audio = nullptr;
    bool abort_returned = false;
};

EncodedMediaPacket Packet(MediaStreamKind stream, std::int64_t pts);

class OnePacketAudioEncoder final : public PacketAudioEncoder {
public:
    PacketAudioEncoderWrite EncodePcm48k(
        std::uint64_t,
        std::span<const float>) noexcept override
    {
        return {VRREC_STATUS_OK, {Packet(MediaStreamKind::Audio, 100'000)}};
    }

    PacketAudioEncoderWrite Finish() noexcept override
    {
        return {VRREC_STATUS_OK, {}};
    }

    void Abort() noexcept override
    {
        ++abort_calls;
    }

    std::size_t abort_calls = 0;
};

class TwoPacketAudioEncoder final : public PacketAudioEncoder {
public:
    PacketAudioEncoderWrite EncodePcm48k(
        std::uint64_t,
        std::span<const float>) noexcept override
    {
        ++encode_calls;
        return {
            VRREC_STATUS_OK,
            {
                Packet(MediaStreamKind::Audio, 100'000),
                Packet(MediaStreamKind::Audio, 200'000),
            },
        };
    }

    PacketAudioEncoderWrite Finish() noexcept override
    {
        return {VRREC_STATUS_OK, {}};
    }

    void Abort() noexcept override
    {
        ++abort_calls;
    }

    std::size_t encode_calls = 0;
    std::size_t abort_calls = 0;
};

class OnePacketVideoEncoder final : public PacketVideoEncoder {
public:
    PacketVideoEncoderWrite Encode(
        const ScheduledVideoFrame &) noexcept override
    {
        return {
            VRREC_STATUS_OK,
            7,
            {Packet(MediaStreamKind::Video, 0)},
        };
    }

    PacketVideoEncoderWrite Finish() noexcept override
    {
        return {VRREC_STATUS_OK, 11, {}};
    }

    void Abort() noexcept override
    {
        ++abort_calls;
    }

    std::size_t abort_calls = 0;
};

EncodedMediaPacket Packet(MediaStreamKind stream, std::int64_t pts)
{
    return {
        stream,
        pts,
        pts,
        1,
        stream == MediaStreamKind::Video,
        std::vector<std::byte>(1, std::byte{0x01}),
    };
}

void WaitForMuxAbortRequest(MediaMuxPipeline &pipeline)
{
    const auto deadline =
        std::chrono::steady_clock::now() + std::chrono::seconds(2);
    while (!pipeline.IsMuxAbortRequestedForTesting()) {
        CHECK(std::chrono::steady_clock::now() < deadline);
        std::this_thread::yield();
    }
}

void ConnectsMuxedPacketsToDriftEventsAndStatistics()
{
    RecordingMuxer muxer;
    RecordingMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    CHECK(pipeline.Start(TestMp4Streams()) == VRREC_STATUS_OK);

    CHECK(pipeline.Submit(Packet(MediaStreamKind::Video, 0)) ==
          Mp4MuxResult::Written);
    CHECK(pipeline.Submit(Packet(MediaStreamKind::Audio, 100'000)) ==
          Mp4MuxResult::Written);
    CHECK(events.drift_calls == 1);
    CHECK(events.video_pts == 0);
    CHECK(events.audio_pts == 100'000);
    CHECK(events.absolute_drift == 100'000);
    const auto snapshot = pipeline.AvSyncStatistics();
    CHECK(snapshot.latest_absolute_drift_microseconds == 100'000);
    CHECK(snapshot.maximum_absolute_drift_microseconds == 100'000);
    CHECK(snapshot.threshold_event_count == 1);
    CHECK(pipeline.AudioVideoOffsetMicroseconds() == 100'000);
}

void EncoderSinksCannotBypassTheMuxPipelineSubmissionBoundary()
{
    RecordingMuxer muxer;
    RecordingMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    CHECK(pipeline.Start(TestMp4Streams()) == VRREC_STATUS_OK);
    OnePacketVideoEncoder video_encoder;
    OnePacketAudioEncoder audio_encoder;
    MuxingVideoEncoderSink video(video_encoder, pipeline);
    MuxingAudioEncoderSink audio(audio_encoder, pipeline);

    CHECK(video.Write({}).status == VRREC_STATUS_OK);
    const std::vector<float> samples {0.0F, 0.0F};
    CHECK(audio.WritePcm48k(0, samples).status == VRREC_STATUS_OK);

    CHECK(muxer.packets.size() == 2);
    CHECK(events.drift_calls == 1);
    CHECK(events.video_pts == 0);
    CHECK(events.audio_pts == 100'000);
    CHECK(events.absolute_drift == 100'000);
    CHECK(pipeline.AvSyncStatistics().has_both_streams);

    CHECK(video.Finish().status == VRREC_STATUS_OK);
    CHECK(audio.Finish().status == VRREC_STATUS_OK);
    CHECK(muxer.order == std::vector<int>({0, 1, 1, 3, 4}));
    CHECK(video_encoder.abort_calls == 0);
    CHECK(audio_encoder.abort_calls == 0);
}

void RejectsAnInvalidBatchBeforeMuxingOrObservingItsPrefix()
{
    RecordingMuxer muxer;
    RecordingMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    CHECK(pipeline.Start(TestMp4Streams()) == VRREC_STATUS_OK);
    auto invalid = Packet(MediaStreamKind::Video, 33'333);
    invalid.duration_microseconds = 0;
    const std::vector<EncodedMediaPacket> batch {
        Packet(MediaStreamKind::Video, 0),
        invalid,
    };

    CHECK(pipeline.SubmitBatch(MediaStreamKind::Video, batch) ==
          Mp4MuxResult::InvalidPacket);
    CHECK(muxer.packets.empty());
    CHECK(muxer.abort_calls == 1);
    CHECK(events.drift_calls == 0);
    CHECK(!pipeline.AvSyncStatistics().has_both_streams);
    CHECK(pipeline.SubmitBatch(
              MediaStreamKind::Audio,
              std::span<const EncodedMediaPacket> {}) ==
          Mp4MuxResult::InvalidState);
}

void FailedBatchDoesNotCommitItsWrittenPrefixToDriftStatistics()
{
    RecordingMuxer muxer;
    muxer.fail_write_call = 3;
    RecordingMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    CHECK(pipeline.Start(TestMp4Streams()) == VRREC_STATUS_OK);
    CHECK(pipeline.Submit(Packet(MediaStreamKind::Audio, 0)) ==
          Mp4MuxResult::Written);
    const std::vector<EncodedMediaPacket> batch {
        Packet(MediaStreamKind::Video, 0),
        Packet(MediaStreamKind::Video, 33'333),
        Packet(MediaStreamKind::Video, 66'666),
    };

    CHECK(pipeline.SubmitBatch(MediaStreamKind::Video, batch) ==
          Mp4MuxResult::MuxFailed);
    CHECK(muxer.write_calls == 3);
    CHECK(muxer.packets.size() == 3);
    CHECK(muxer.abort_calls == 1);
    CHECK(events.drift_calls == 0);
    CHECK(!pipeline.AvSyncStatistics().has_both_streams);
    CHECK(pipeline.Submit(Packet(MediaStreamKind::Audio, 0)) ==
          Mp4MuxResult::InvalidState);
}

void PreservesCanonicalPtsAndDtsWhileDriftUsesPresentationTime()
{
    RecordingMuxer muxer;
    RecordingMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    CHECK(pipeline.Start(TestMp4Streams()) == VRREC_STATUS_OK);
    auto reordered_video = Packet(MediaStreamKind::Video, 100);
    reordered_video.dts_microseconds = 0;

    CHECK(pipeline.Submit(reordered_video) == Mp4MuxResult::Written);
    CHECK(pipeline.Submit(Packet(MediaStreamKind::Audio, 100'100)) ==
          Mp4MuxResult::Written);
    CHECK(muxer.packets[0].pts_microseconds == 100);
    CHECK(muxer.packets[0].dts_microseconds == 0);
    CHECK(events.video_pts == 100);
    CHECK(events.audio_pts == 100'100);
    CHECK(events.absolute_drift == 100'000);
}

void ConcurrentBatchesNeverInterleaveTheirPackets()
{
    RecordingMuxer muxer;
    RecordingMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    CHECK(pipeline.Start(TestMp4Streams()) == VRREC_STATUS_OK);
    const std::vector<EncodedMediaPacket> video {
        Packet(MediaStreamKind::Video, 0),
        Packet(MediaStreamKind::Video, 33'333),
    };
    const std::vector<EncodedMediaPacket> audio {
        Packet(MediaStreamKind::Audio, 0),
        Packet(MediaStreamKind::Audio, 21'333),
    };

    std::mutex start_mutex;
    std::condition_variable start_changed;
    std::size_t ready = 0;
    bool release = false;
    Mp4MuxResult video_result = Mp4MuxResult::InvalidState;
    Mp4MuxResult audio_result = Mp4MuxResult::InvalidState;
    const auto wait_for_start = [&] {
        std::unique_lock lock(start_mutex);
        ++ready;
        start_changed.notify_all();
        start_changed.wait(lock, [&] { return release; });
    };
    std::thread video_thread([&] {
        wait_for_start();
        video_result = pipeline.SubmitBatch(MediaStreamKind::Video, video);
    });
    std::thread audio_thread([&] {
        wait_for_start();
        audio_result = pipeline.SubmitBatch(MediaStreamKind::Audio, audio);
    });
    {
        std::unique_lock lock(start_mutex);
        CHECK(start_changed.wait_for(
            lock,
            std::chrono::seconds(2),
            [&] { return ready == 2; }));
        release = true;
    }
    start_changed.notify_all();
    video_thread.join();
    audio_thread.join();

    CHECK(video_result == Mp4MuxResult::Written);
    CHECK(audio_result == Mp4MuxResult::Written);
    CHECK(muxer.packets.size() == 4);
    const std::vector<MediaStreamKind> order {
        muxer.packets[0].stream,
        muxer.packets[1].stream,
        muxer.packets[2].stream,
        muxer.packets[3].stream,
    };
    CHECK(order == std::vector<MediaStreamKind>({
              MediaStreamKind::Video,
              MediaStreamKind::Video,
              MediaStreamKind::Audio,
              MediaStreamKind::Audio,
          }) ||
          order == std::vector<MediaStreamKind>({
              MediaStreamKind::Audio,
              MediaStreamKind::Audio,
              MediaStreamKind::Video,
              MediaStreamKind::Video,
          }));
}

void PeerBatchCannotWriteThroughAnInFlightBatchFailure()
{
    BlockingFailureMuxer muxer;
    RecordingMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    CHECK(pipeline.Start(TestMp4Streams()) == VRREC_STATUS_OK);
    const std::vector<EncodedMediaPacket> video {
        Packet(MediaStreamKind::Video, 0),
        Packet(MediaStreamKind::Video, 33'333),
        Packet(MediaStreamKind::Video, 66'666),
    };
    const std::vector<EncodedMediaPacket> audio {
        Packet(MediaStreamKind::Audio, 0),
        Packet(MediaStreamKind::Audio, 21'333),
    };
    Mp4MuxResult video_result = Mp4MuxResult::InvalidState;
    Mp4MuxResult audio_result = Mp4MuxResult::Written;
    std::thread video_thread([&] {
        video_result = pipeline.SubmitBatch(MediaStreamKind::Video, video);
    });
    {
        std::unique_lock lock(muxer.mutex);
        CHECK(muxer.changed.wait_for(
            lock,
            std::chrono::seconds(2),
            [&] { return muxer.failed_write_entered; }));
    }

    std::mutex peer_mutex;
    std::condition_variable peer_changed;
    bool peer_started = false;
    std::thread audio_thread([&] {
        {
            const std::lock_guard lock(peer_mutex);
            peer_started = true;
        }
        peer_changed.notify_all();
        audio_result = pipeline.SubmitBatch(MediaStreamKind::Audio, audio);
    });
    {
        std::unique_lock lock(peer_mutex);
        CHECK(peer_changed.wait_for(
            lock,
            std::chrono::seconds(2),
            [&] { return peer_started; }));
    }
    {
        const std::lock_guard lock(muxer.mutex);
        muxer.release_failed_write = true;
    }
    muxer.changed.notify_all();
    video_thread.join();
    audio_thread.join();

    CHECK(video_result == Mp4MuxResult::MuxFailed);
    CHECK(audio_result == Mp4MuxResult::InvalidState);
    {
        const std::lock_guard lock(muxer.mutex);
        CHECK(muxer.write_calls == 2);
        CHECK(muxer.packets.size() == 2);
        CHECK(std::all_of(
            muxer.packets.begin(),
            muxer.packets.end(),
            [](const EncodedMediaPacket &packet) {
                return packet.stream == MediaStreamKind::Video;
            }));
        CHECK(muxer.abort_calls == 1);
        CHECK(muxer.trailer_calls == 0);
        CHECK(muxer.flush_calls == 0);
    }
}

void SinkAbortFromDriftCallbackStopsTheRemainingBatchObservation()
{
    RecordingMuxer muxer;
    AbortAudioSinkOnDriftMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    CHECK(pipeline.Start(TestMp4Streams()) == VRREC_STATUS_OK);
    CHECK(pipeline.Submit(Packet(MediaStreamKind::Video, 0)) ==
          Mp4MuxResult::Written);
    TwoPacketAudioEncoder encoder;
    MuxingAudioEncoderSink audio(encoder, pipeline);
    events.audio = &audio;

    const auto write = audio.WritePcm48k(
        0,
        std::vector<float> {0.0F, 0.0F});
    CHECK(write.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(write.failure_stage == AudioEncoderFailureStage::Muxing);
    CHECK(write.muxed_packet_count == 0);
    CHECK(events.abort_returned);
    CHECK(events.drift_calls == 1);
    CHECK(encoder.encode_calls == 1);
    CHECK(encoder.abort_calls == 1);
    CHECK(muxer.packets.size() == 3);
    CHECK(muxer.abort_calls == 1);
    const auto snapshot = pipeline.AvSyncStatistics();
    CHECK(snapshot.latest_audio_video_offset_microseconds == 100'000);
    CHECK(snapshot.maximum_absolute_drift_microseconds == 100'000);
    CHECK(audio.WritePcm48k(
              1,
              std::vector<float> {0.0F, 0.0F}).status ==
          VRREC_STATUS_INVALID_STATE);
}

void MuxesNegativeAacPrimingButStartsDriftObservationAtPresentationZero()
{
    RecordingMuxer muxer;
    RecordingMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    CHECK(pipeline.Start(TestMp4Streams()) == VRREC_STATUS_OK);

    CHECK(pipeline.Submit(Packet(MediaStreamKind::Audio, -21'333)) ==
        Mp4MuxResult::Written);
    CHECK(pipeline.Submit(Packet(MediaStreamKind::Video, 0)) ==
        Mp4MuxResult::Written);
    CHECK(muxer.packets.size() == 2);
    CHECK(muxer.packets[0].pts_microseconds == -21'333);
    CHECK(muxer.abort_calls == 0);
    CHECK(!pipeline.AvSyncStatistics().has_both_streams);

    CHECK(pipeline.Submit(Packet(MediaStreamKind::Audio, 0)) ==
        Mp4MuxResult::Written);
    const auto snapshot = pipeline.AvSyncStatistics();
    CHECK(snapshot.has_both_streams);
    CHECK(snapshot.latest_absolute_drift_microseconds == 0);
    CHECK(snapshot.latest_audio_video_offset_microseconds == 0);
    CHECK(events.drift_calls == 0);
    CHECK(muxer.abort_calls == 0);
}

void DriftCallbackCanAbortTheMuxPipelineWithoutDeadlocking()
{
    RecordingMuxer muxer;
    AbortOnDriftMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    events.pipeline = &pipeline;
    CHECK(pipeline.Start(TestMp4Streams()) == VRREC_STATUS_OK);
    CHECK(pipeline.Submit(Packet(MediaStreamKind::Video, 0)) ==
        Mp4MuxResult::Written);

    std::mutex watchdog_mutex;
    std::condition_variable watchdog_changed;
    bool submit_completed = false;
    std::thread watchdog([&] {
        std::unique_lock lock(watchdog_mutex);
        if (!watchdog_changed.wait_for(
                lock,
                std::chrono::seconds(2),
                [&] { return submit_completed; })) {
            std::cerr << __func__
                      << " timed out waiting for reentrant Abort\n";
            std::abort();
        }
    });

    const auto result =
        pipeline.Submit(Packet(MediaStreamKind::Audio, 100'000));
    {
        const std::lock_guard lock(watchdog_mutex);
        submit_completed = true;
    }
    watchdog_changed.notify_all();
    watchdog.join();

    CHECK(result == Mp4MuxResult::MuxFailed);
    CHECK(events.drift_calls == 1);
    CHECK(events.abort_returned);
    CHECK(events.snapshot_read);
    CHECK(muxer.abort_calls == 1);
    CHECK(pipeline.Submit(Packet(MediaStreamKind::Video, 1)) ==
        Mp4MuxResult::InvalidState);
}

void FinalCompletionWaitsForInFlightDriftObservation()
{
    RecordingMuxer muxer;
    BlockingDriftMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    CHECK(pipeline.Start(TestMp4Streams()) == VRREC_STATUS_OK);
    CHECK(pipeline.Submit(Packet(MediaStreamKind::Video, 0)) ==
          Mp4MuxResult::Written);
    CHECK(pipeline.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);

    std::atomic<Mp4MuxResult> submit_result {Mp4MuxResult::InvalidState};
    std::thread submitter([&] {
        submit_result.store(
            pipeline.Submit(Packet(MediaStreamKind::Audio, 100'000)));
    });
    {
        std::unique_lock lock(events.mutex);
        CHECK(events.changed.wait_for(
            lock,
            std::chrono::seconds(2),
            [&] { return events.callback_entered; }));
    }

    std::mutex finish_mutex;
    std::condition_variable finish_changed;
    bool finish_started = false;
    bool finish_returned = false;
    vrrec_status_t finish_status = VRREC_STATUS_INVALID_STATE;
    std::thread finisher([&] {
        {
            const std::lock_guard lock(finish_mutex);
            finish_started = true;
        }
        finish_changed.notify_all();
        const auto status =
            pipeline.EncoderFinished(MediaStreamKind::Audio);
        {
            const std::lock_guard lock(finish_mutex);
            finish_status = status;
            finish_returned = true;
        }
        finish_changed.notify_all();
    });
    {
        std::unique_lock lock(finish_mutex);
        CHECK(finish_changed.wait_for(
            lock,
            std::chrono::seconds(2),
            [&] { return finish_started; }));
        CHECK(!finish_changed.wait_for(
            lock,
            std::chrono::milliseconds(100),
            [&] { return finish_returned; }));
    }

    {
        const std::lock_guard lock(events.mutex);
        events.release_callback = true;
    }
    events.changed.notify_all();
    submitter.join();
    finisher.join();

    CHECK(events.callback_returned);
    CHECK(submit_result.load() == Mp4MuxResult::Written);
    CHECK(finish_status == VRREC_STATUS_OK);
    CHECK(muxer.order == std::vector<int>({0, 1, 1, 3, 4}));
}

void CallbackAbortWinsAgainstTheLastEncoderCompletion()
{
    RecordingMuxer muxer;
    BlockingAbortOnDriftMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    events.pipeline = &pipeline;
    CHECK(pipeline.Start(TestMp4Streams()) == VRREC_STATUS_OK);
    CHECK(pipeline.Submit(Packet(MediaStreamKind::Video, 0)) ==
          Mp4MuxResult::Written);
    CHECK(pipeline.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);

    std::atomic<Mp4MuxResult> submit_result {Mp4MuxResult::InvalidState};
    std::thread submitter([&] {
        submit_result.store(
            pipeline.Submit(Packet(MediaStreamKind::Audio, 100'000)));
    });
    {
        std::unique_lock lock(events.mutex);
        CHECK(events.changed.wait_for(
            lock,
            std::chrono::seconds(2),
            [&] { return events.callback_entered; }));
    }

    std::mutex finish_mutex;
    std::condition_variable finish_changed;
    bool finish_started = false;
    bool finish_returned = false;
    vrrec_status_t finish_status = VRREC_STATUS_OK;
    std::thread finisher([&] {
        {
            const std::lock_guard lock(finish_mutex);
            finish_started = true;
        }
        finish_changed.notify_all();
        const auto status =
            pipeline.EncoderFinished(MediaStreamKind::Audio);
        {
            const std::lock_guard lock(finish_mutex);
            finish_status = status;
            finish_returned = true;
        }
        finish_changed.notify_all();
    });
    {
        std::unique_lock lock(finish_mutex);
        CHECK(finish_changed.wait_for(
            lock,
            std::chrono::seconds(2),
            [&] { return finish_started; }));
        CHECK(!finish_changed.wait_for(
            lock,
            std::chrono::milliseconds(100),
            [&] { return finish_returned; }));
    }

    {
        const std::lock_guard lock(events.mutex);
        events.release_callback = true;
    }
    events.changed.notify_all();
    submitter.join();
    finisher.join();

    CHECK(events.abort_returned);
    CHECK(submit_result.load() == Mp4MuxResult::MuxFailed);
    CHECK(finish_status == VRREC_STATUS_INVALID_STATE);
    CHECK(muxer.order == std::vector<int>({0, 1, 1, 5}));
    CHECK(muxer.abort_calls == 1);
    CHECK(pipeline.Submit(Packet(MediaStreamKind::Video, 1)) ==
          Mp4MuxResult::InvalidState);
}

void PostFinishSubmissionAbortsTheActiveMuxImmediately()
{
    RecordingMuxer muxer;
    RecordingMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    CHECK(pipeline.Start(TestMp4Streams()) == VRREC_STATUS_OK);
    CHECK(pipeline.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);

    CHECK(pipeline.Submit(Packet(MediaStreamKind::Video, 0)) ==
          Mp4MuxResult::InvalidState);
    CHECK(muxer.abort_calls == 1);
    CHECK(muxer.order == std::vector<int>({0, 5}));
    CHECK(pipeline.EncoderFinished(MediaStreamKind::Audio) ==
          VRREC_STATUS_INVALID_STATE);
}

void DuplicateCompletionAbortsTheActiveMuxImmediately()
{
    RecordingMuxer muxer;
    RecordingMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    CHECK(pipeline.Start(TestMp4Streams()) == VRREC_STATUS_OK);
    CHECK(pipeline.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);

    CHECK(pipeline.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(muxer.abort_calls == 1);
    CHECK(muxer.order == std::vector<int>({0, 5}));
    CHECK(pipeline.Submit(Packet(MediaStreamKind::Audio, 0)) ==
          Mp4MuxResult::InvalidState);
}

void StartAndPreStartProtocolFailuresAbortImmediately()
{
    {
        RecordingMuxer muxer;
        RecordingMediaEvents events;
        MediaMuxPipeline pipeline(muxer, events);
        auto invalid = TestMp4Streams();
        invalid.video.width = 0;

        CHECK(pipeline.Start(invalid) == VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(muxer.abort_calls == 1);
        CHECK(muxer.order == std::vector<int>({5}));
        CHECK(pipeline.Start(TestMp4Streams()) ==
              VRREC_STATUS_INVALID_STATE);
    }

    {
        RecordingMuxer muxer;
        RecordingMediaEvents events;
        MediaMuxPipeline pipeline(muxer, events);
        CHECK(pipeline.Start(TestMp4Streams()) == VRREC_STATUS_OK);
        CHECK(pipeline.Start(TestMp4Streams()) ==
              VRREC_STATUS_INVALID_STATE);
        CHECK(muxer.abort_calls == 1);
        CHECK(muxer.order == std::vector<int>({0, 5}));
    }

    {
        RecordingMuxer muxer;
        RecordingMediaEvents events;
        MediaMuxPipeline pipeline(muxer, events);
        CHECK(pipeline.EncoderFinished(MediaStreamKind::Video) ==
              VRREC_STATUS_INVALID_STATE);
        CHECK(muxer.abort_calls == 1);
        CHECK(muxer.order == std::vector<int>({5}));
        CHECK(pipeline.Start(TestMp4Streams()) ==
              VRREC_STATUS_INVALID_STATE);
    }
}

void AbortDuringHeaderPreventsStartFromRevivingThePipeline()
{
    BlockingHeaderMuxer muxer;
    RecordingMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    std::atomic<vrrec_status_t> start_status {VRREC_STATUS_OK};
    std::thread starter([&] {
        start_status.store(pipeline.Start(TestMp4Streams()));
    });
    {
        std::unique_lock lock(muxer.mutex);
        CHECK(muxer.changed.wait_for(
            lock,
            std::chrono::seconds(2),
            [&] { return muxer.header_entered; }));
    }

    std::atomic_bool abort_returned = false;
    std::thread aborter([&] {
        pipeline.Abort();
        abort_returned.store(true);
    });
    WaitForMuxAbortRequest(pipeline);
    CHECK(!abort_returned.load());
    {
        const std::lock_guard lock(muxer.mutex);
        muxer.release_header = true;
    }
    muxer.changed.notify_all();
    starter.join();
    aborter.join();

    CHECK(start_status.load() == VRREC_STATUS_INVALID_STATE);
    {
        const std::lock_guard lock(muxer.mutex);
        CHECK(muxer.abort_calls == 1);
    }
    CHECK(pipeline.Submit(Packet(MediaStreamKind::Video, 0)) ==
          Mp4MuxResult::InvalidState);
}

void AbortDuringTrailerStopsBeforeFileFlush()
{
    BlockingTrailerMuxer muxer;
    RecordingMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    CHECK(pipeline.Start(TestMp4Streams()) == VRREC_STATUS_OK);
    CHECK(pipeline.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);

    std::atomic<vrrec_status_t> finish_status {VRREC_STATUS_OK};
    std::thread finisher([&] {
        finish_status.store(
            pipeline.EncoderFinished(MediaStreamKind::Audio));
    });
    {
        std::unique_lock lock(muxer.mutex);
        CHECK(muxer.changed.wait_for(
            lock,
            std::chrono::seconds(2),
            [&] { return muxer.trailer_entered; }));
    }

    std::atomic_bool abort_returned = false;
    std::thread aborter([&] {
        pipeline.Abort();
        abort_returned.store(true);
    });
    WaitForMuxAbortRequest(pipeline);
    CHECK(!abort_returned.load());
    {
        const std::lock_guard lock(muxer.mutex);
        muxer.release_trailer = true;
    }
    muxer.changed.notify_all();
    finisher.join();
    aborter.join();

    CHECK(finish_status.load() == VRREC_STATUS_INVALID_STATE);
    {
        const std::lock_guard lock(muxer.mutex);
        CHECK(muxer.trailer_calls == 1);
        CHECK(muxer.flush_calls == 0);
        CHECK(muxer.abort_calls == 1);
    }
}

void FinalizesOnlyAfterBothStreamsFinish()
{
    RecordingMuxer muxer;
    RecordingMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    CHECK(pipeline.Start(TestMp4Streams()) == VRREC_STATUS_OK);
    CHECK(pipeline.Submit(Packet(MediaStreamKind::Video, 0)) ==
          Mp4MuxResult::Written);

    CHECK(pipeline.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(muxer.order == std::vector<int>({0, 1}));
    CHECK(pipeline.EncoderFinished(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    CHECK(muxer.order == std::vector<int>({0, 1, 3, 4}));
}

void AbortStopsTheWholeMuxGraphWithoutATrailer()
{
    RecordingMuxer muxer;
    RecordingMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    pipeline.Abort();
    pipeline.Abort();
    CHECK(muxer.order == std::vector<int>({5}));
    CHECK(muxer.abort_calls == 1);
}

void LogicalAbortReachesTheMuxCoordinatorWithoutPhysicalCleanup()
{
    RecordingMuxer muxer;
    RecordingMediaEvents events;
    MediaMuxPipeline pipeline(muxer, events);
    CHECK(pipeline.Start(TestMp4Streams()) == VRREC_STATUS_OK);

    pipeline.RequestAbort();

    CHECK(pipeline.IsMuxAbortRequestedForTesting());
    CHECK(muxer.abort_calls == 0);
    CHECK(muxer.order == std::vector<int>({0}));
    CHECK(pipeline.Submit(Packet(MediaStreamKind::Video, 0)) ==
          Mp4MuxResult::InvalidState);

    pipeline.Abort();
    CHECK(muxer.abort_calls == 1);
    CHECK(muxer.order == std::vector<int>({0, 5}));
}

}

int main()
{
    ConnectsMuxedPacketsToDriftEventsAndStatistics();
    EncoderSinksCannotBypassTheMuxPipelineSubmissionBoundary();
    RejectsAnInvalidBatchBeforeMuxingOrObservingItsPrefix();
    FailedBatchDoesNotCommitItsWrittenPrefixToDriftStatistics();
    PreservesCanonicalPtsAndDtsWhileDriftUsesPresentationTime();
    ConcurrentBatchesNeverInterleaveTheirPackets();
    PeerBatchCannotWriteThroughAnInFlightBatchFailure();
    SinkAbortFromDriftCallbackStopsTheRemainingBatchObservation();
    MuxesNegativeAacPrimingButStartsDriftObservationAtPresentationZero();
    DriftCallbackCanAbortTheMuxPipelineWithoutDeadlocking();
    FinalCompletionWaitsForInFlightDriftObservation();
    CallbackAbortWinsAgainstTheLastEncoderCompletion();
    PostFinishSubmissionAbortsTheActiveMuxImmediately();
    DuplicateCompletionAbortsTheActiveMuxImmediately();
    StartAndPreStartProtocolFailuresAbortImmediately();
    AbortDuringHeaderPreventsStartFromRevivingThePipeline();
    AbortDuringTrailerStopsBeforeFileFlush();
    FinalizesOnlyAfterBothStreamsFinish();
    AbortStopsTheWholeMuxGraphWithoutATrailer();
    LogicalAbortReachesTheMuxCoordinatorWithoutPhysicalCleanup();
    return 0;
}
