#include "audio_pipeline_session.hpp"
#include "fragmented_mp4_test_support.hpp"
#include "media_mux_pipeline.hpp"
#include "media_recording_pipeline.hpp"
#include "muxing_audio_encoder_sink.hpp"
#include "muxing_video_encoder_sink.hpp"
#include "pipeline_media_backend.hpp"
#include "video_cfr_scheduler.hpp"
#include "video_encoding_worker.hpp"
#include "video_pipeline_session.hpp"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <mutex>
#include <span>
#include <string>
#include <thread>

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

EncodedMediaPacket Packet(
    MediaStreamKind stream,
    std::int64_t pts,
    std::int64_t duration)
{
    return {
        stream,
        pts,
        pts,
        duration,
        stream == MediaStreamKind::Video,
        {std::byte {0x01}},
        {},
    };
}

class RecordingMuxer final : public FragmentedMp4Muxer {
public:
    vrrec_status_t WriteHeader(
        const FragmentedMp4StreamConfiguration &) noexcept override
    {
        return VRREC_STATUS_OK;
    }

    vrrec_status_t WritePacket(
        const EncodedMediaPacket &) noexcept override
    {
        const std::lock_guard lock(mutex_);
        ++packet_calls_;
        return VRREC_STATUS_OK;
    }

    vrrec_status_t WriteTrailer() noexcept override
    {
        const std::lock_guard lock(mutex_);
        ++trailer_calls_;
        return VRREC_STATUS_OK;
    }

    vrrec_status_t FlushFile() noexcept override
    {
        const std::lock_guard lock(mutex_);
        ++flush_calls_;
        return VRREC_STATUS_OK;
    }

    void Abort() noexcept override
    {
        const std::lock_guard lock(mutex_);
        ++abort_calls_;
    }

    std::size_t PacketCalls() const noexcept
    {
        const std::lock_guard lock(mutex_);
        return packet_calls_;
    }

    std::size_t AbortCalls() const noexcept
    {
        const std::lock_guard lock(mutex_);
        return abort_calls_;
    }

    std::size_t TrailerCalls() const noexcept
    {
        const std::lock_guard lock(mutex_);
        return trailer_calls_;
    }

    std::size_t FlushCalls() const noexcept
    {
        const std::lock_guard lock(mutex_);
        return flush_calls_;
    }

private:
    mutable std::mutex mutex_;
    std::size_t packet_calls_ = 0;
    std::size_t abort_calls_ = 0;
    std::size_t trailer_calls_ = 0;
    std::size_t flush_calls_ = 0;
};

class ManualVideoClock final : public VideoCfrClock {
public:
    VideoCfrClockResult WaitNext(
        std::uint64_t &tick) noexcept override
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [this] {
            return pending_ticks_ != 0 || aborted_;
        });
        if (aborted_) {
            return VideoCfrClockResult::Aborted;
        }

        --pending_ticks_;
        tick = next_tick_++;
        return VideoCfrClockResult::Tick;
    }

    void Abort() noexcept override
    {
        {
            const std::lock_guard lock(mutex_);
            aborted_ = true;
        }
        changed_.notify_all();
    }

    void ReleaseTick()
    {
        {
            const std::lock_guard lock(mutex_);
            ++pending_ticks_;
        }
        changed_.notify_all();
    }

private:
    std::mutex mutex_;
    std::condition_variable changed_;
    std::uint64_t next_tick_ = 0;
    std::size_t pending_ticks_ = 0;
    bool aborted_ = false;
};

class ImmediateCaptureWorker final : public SpoutCaptureWorkerPort {
public:
    vrrec_status_t Start(
        std::chrono::milliseconds) noexcept override
    {
        return VRREC_STATUS_OK;
    }

    void Abort() noexcept override
    {
        aborted_.store(true);
    }

    SpoutCaptureWorkerResult Join() noexcept override
    {
        return aborted_.load()
            ? SpoutCaptureWorkerResult::Aborted
            : SpoutCaptureWorkerResult::InvalidState;
    }

private:
    std::atomic_bool aborted_ = false;
};

class ManualAudioCapture final : public StereoAudioCaptureSessionPort {
public:
    vrrec_status_t Start(
        const StereoAudioCaptureSessionConfig &) noexcept override
    {
        return VRREC_STATUS_OK;
    }

    StereoAudioMixResult MixNext(
        std::size_t frame_count_48k,
        std::span<float> output,
        StereoAudioMixRead &read) noexcept override
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [this] {
            return pending_windows_ != 0 || aborted_;
        });
        if (aborted_) {
            return StereoAudioMixResult::Aborted;
        }

        --pending_windows_;
        const auto start = next_start_frame_48k_;
        next_start_frame_48k_ += frame_count_48k;
        lock.unlock();

        std::fill(output.begin(), output.end(), 0.25F);
        read = {
            start,
            frame_count_48k,
            true,
            true,
            false,
            false,
        };
        return StereoAudioMixResult::Mixed;
    }

    vrrec_status_t SetRouting(
        vrrec_audio_routing_t) noexcept override
    {
        return VRREC_STATUS_OK;
    }

    void Abort() noexcept override
    {
        {
            const std::lock_guard lock(mutex_);
            aborted_ = true;
        }
        changed_.notify_all();
    }

    void ReleaseWindow()
    {
        {
            const std::lock_guard lock(mutex_);
            ++pending_windows_;
        }
        changed_.notify_all();
    }

private:
    std::mutex mutex_;
    std::condition_variable changed_;
    std::uint64_t next_start_frame_48k_ = 0;
    std::size_t pending_windows_ = 0;
    bool aborted_ = false;
};

class ScriptedVideoEncoder final : public PacketVideoEncoder {
public:
    ScriptedVideoEncoder(
        std::int64_t first_pts,
        std::int64_t second_pts) noexcept
        : first_pts_(first_pts), second_pts_(second_pts)
    {
    }

    PacketVideoEncoderWrite Encode(
        const ScheduledVideoFrame &) noexcept override
    {
        const auto index = encode_calls_.fetch_add(1);
        const auto pts = index == 0 ? first_pts_ : second_pts_;
        return {
            VRREC_STATUS_OK,
            7,
            {Packet(MediaStreamKind::Video, pts, 33'333)},
        };
    }

    PacketVideoEncoderWrite Finish() noexcept override
    {
        return {VRREC_STATUS_OK, 0, {}};
    }

    void Abort() noexcept override
    {
    }

private:
    std::int64_t first_pts_;
    std::int64_t second_pts_;
    std::atomic<std::size_t> encode_calls_ {0};
};

class ScriptedAudioEncoder final : public PacketAudioEncoder {
public:
    ScriptedAudioEncoder(
        std::int64_t first_pts,
        std::int64_t second_pts) noexcept
        : first_pts_(first_pts), second_pts_(second_pts)
    {
    }

    PacketAudioEncoderWrite EncodePcm48k(
        std::uint64_t,
        std::span<const float>) noexcept override
    {
        const auto index = encode_calls_.fetch_add(1);
        const auto pts = index == 0 ? first_pts_ : second_pts_;
        return {
            VRREC_STATUS_OK,
            {Packet(MediaStreamKind::Audio, pts, 21'333)},
        };
    }

    PacketAudioEncoderWrite Finish() noexcept override
    {
        return {VRREC_STATUS_OK, {}};
    }

    void Abort() noexcept override
    {
    }

private:
    std::int64_t first_pts_;
    std::int64_t second_pts_;
    std::atomic<std::size_t> encode_calls_ {0};
};

class ForwardingMuxGate final : public EncodedMediaPacketSubmissionPort {
public:
    explicit ForwardingMuxGate(
        MediaMuxPipeline &delegate,
        MediaStreamKind peer_stream) noexcept
        : delegate_(delegate), peer_stream_(peer_stream)
    {
    }

    Mp4MuxResult SubmitBatch(
        MediaStreamKind producer,
        std::span<const EncodedMediaPacket> packets)
        noexcept override
    {
        std::size_t peer_index = 0;
        if (producer == peer_stream_) {
            {
                const std::lock_guard lock(mutex_);
                peer_index = ++peer_submissions_;
                if (peer_index == 2) {
                    second_peer_entered_ = true;
                }
            }
            changed_.notify_all();
        }

        const auto result = delegate_.SubmitBatch(producer, packets);
        {
            const std::lock_guard lock(mutex_);
            if (peer_index == 1 &&
                result == Mp4MuxResult::Written) {
                first_peer_committed_ = true;
            }
            if (peer_index == 2) {
                second_peer_result_ = result;
                second_peer_returned_ = true;
            }
        }
        changed_.notify_all();
        return result;
    }

    vrrec_status_t EncoderFinished(
        MediaStreamKind stream) noexcept override
    {
        return delegate_.EncoderFinished(stream);
    }

    void EncoderFailed(
        MediaStreamKind stream) noexcept override
    {
        delegate_.EncoderFailed(stream);
    }

    void WaitForFirstPeerCommit()
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [this] { return first_peer_committed_; });
    }

    void WaitForSecondPeerEntry()
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [this] { return second_peer_entered_; });
    }

    void WaitForSecondPeerReturn()
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [this] { return second_peer_returned_; });
    }

    Mp4MuxResult SecondPeerResult() const noexcept
    {
        const std::lock_guard lock(mutex_);
        return second_peer_result_;
    }

private:
    MediaMuxPipeline &delegate_;
    MediaStreamKind peer_stream_;
    mutable std::mutex mutex_;
    std::condition_variable changed_;
    std::size_t peer_submissions_ = 0;
    Mp4MuxResult second_peer_result_ = Mp4MuxResult::Written;
    bool first_peer_committed_ = false;
    bool second_peer_entered_ = false;
    bool second_peer_returned_ = false;
};

class NoopLayout final : public VideoLayoutUpdatePort {
public:
    vrrec_status_t UpdateVideoLayout(
        const vrrec_video_layout_v1 &) noexcept override
    {
        return VRREC_STATUS_OK;
    }
};

class AbortOnDriftEvents final : public MediaEventSink {
public:
    void Bind(
        PipelineMediaBackend &backend,
        ManualVideoClock &clock,
        ManualAudioCapture &audio_capture,
        ForwardingMuxGate &gate,
        bool callback_from_audio) noexcept
    {
        backend_ = &backend;
        clock_ = &clock;
        audio_capture_ = &audio_capture;
        gate_ = &gate;
        callback_from_audio_ = callback_from_audio;
    }

    void FirstVideoPacketMuxed() noexcept override
    {
        ++first_video_calls_;
    }

    void Stopped(
        std::uint64_t,
        std::uint64_t) noexcept override
    {
        ++stopped_calls_;
    }

    void Faulted(
        vrrec_status_t,
        const char *) noexcept override
    {
        ++faulted_calls_;
    }

    void AudioEndpointAvailabilityChanged(
        AudioEndpointRole,
        bool,
        std::uint64_t) noexcept override
    {
    }

    void AvSyncDriftExceeded(
        std::uint64_t video_pts,
        std::uint64_t audio_pts,
        std::uint64_t drift) noexcept override
    {
        observed_video_pts_.store(video_pts);
        observed_audio_pts_.store(audio_pts);
        observed_drift_.store(drift);
        ++drift_calls_;

        CHECK(backend_ != nullptr);
        CHECK(clock_ != nullptr);
        CHECK(audio_capture_ != nullptr);
        CHECK(gate_ != nullptr);

        if (callback_from_audio_) {
            clock_->ReleaseTick();
        } else {
            audio_capture_->ReleaseWindow();
        }
        gate_->WaitForSecondPeerEntry();
        backend_->RequestAbort();

        {
            const std::lock_guard lock(mutex_);
            callback_returned_ = true;
        }
        changed_.notify_all();
    }

    void WaitForCallbackReturn()
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [this] { return callback_returned_; });
    }

    std::size_t DriftCalls() const noexcept
    {
        return drift_calls_.load();
    }

    std::size_t FirstVideoCalls() const noexcept
    {
        return first_video_calls_.load();
    }

    std::size_t StoppedCalls() const noexcept
    {
        return stopped_calls_.load();
    }

    std::size_t FaultedCalls() const noexcept
    {
        return faulted_calls_.load();
    }

    std::uint64_t VideoPts() const noexcept
    {
        return observed_video_pts_.load();
    }

    std::uint64_t AudioPts() const noexcept
    {
        return observed_audio_pts_.load();
    }

    std::uint64_t Drift() const noexcept
    {
        return observed_drift_.load();
    }

private:
    PipelineMediaBackend *backend_ = nullptr;
    ManualVideoClock *clock_ = nullptr;
    ManualAudioCapture *audio_capture_ = nullptr;
    ForwardingMuxGate *gate_ = nullptr;
    bool callback_from_audio_ = false;
    std::mutex mutex_;
    std::condition_variable changed_;
    bool callback_returned_ = false;
    std::atomic<std::size_t> first_video_calls_ {0};
    std::atomic<std::size_t> stopped_calls_ {0};
    std::atomic<std::size_t> faulted_calls_ {0};
    std::atomic<std::size_t> drift_calls_ {0};
    std::atomic<std::uint64_t> observed_video_pts_ {0};
    std::atomic<std::uint64_t> observed_audio_pts_ {0};
    std::atomic<std::uint64_t> observed_drift_ {0};
};

void ExerciseFullGraph(bool callback_from_audio)
{
    AbortOnDriftEvents events;

    RecordingMuxer muxer;
    MediaMuxPipeline mux(muxer, events);
    ForwardingMuxGate mux_gate(
        mux,
        callback_from_audio
            ? MediaStreamKind::Video
            : MediaStreamKind::Audio);

    ScriptedVideoEncoder video_encoder(
        callback_from_audio ? 0 : 100'000,
        callback_from_audio ? 33'333 : 133'333);
    MuxingVideoEncoderSink video_sink(video_encoder, mux_gate);
    VideoCfrScheduler scheduler;
    CHECK(scheduler.Push({1, 0, {}}) == VRREC_STATUS_OK);
    ManualVideoClock video_clock;
    VideoEncodingWorker video_worker(
        scheduler,
        video_clock,
        video_sink,
        events);
    ImmediateCaptureWorker capture_worker;
    VideoPipelineSession video_session(
        capture_worker,
        video_worker,
        events);

    ScriptedAudioEncoder audio_encoder(
        callback_from_audio ? 100'000 : 0,
        callback_from_audio ? 121'333 : 21'333);
    MuxingAudioEncoderSink audio_sink(audio_encoder, mux_gate);
    ManualAudioCapture audio_capture;
    StereoAudioPipelineSession audio_session(audio_capture, audio_sink);

    MediaRecordingPipeline recording(
        video_session,
        std::chrono::milliseconds {80},
        audio_session,
        StereoAudioCaptureSessionConfig {
            "desktop-id",
            "microphone-id",
            0,
        },
        1'024,
        mux,
        TestMp4Streams(),
        events);

    NoopLayout layout;
    PipelineMediaBackend backend(recording, layout);
    events.Bind(
        backend,
        video_clock,
        audio_capture,
        mux_gate,
        callback_from_audio);

    CHECK(backend.Start() == VRREC_STATUS_OK);
    if (callback_from_audio) {
        video_clock.ReleaseTick();
        mux_gate.WaitForFirstPeerCommit();
        audio_capture.ReleaseWindow();
    } else {
        audio_capture.ReleaseWindow();
        mux_gate.WaitForFirstPeerCommit();
        video_clock.ReleaseTick();
    }
    events.WaitForCallbackReturn();

    backend.JoinAfterAbort();
    mux_gate.WaitForSecondPeerReturn();

    CHECK(events.DriftCalls() == 1);
    CHECK(events.FirstVideoCalls() == (callback_from_audio ? 1 : 0));
    CHECK(events.StoppedCalls() == 0);
    CHECK(events.FaultedCalls() == 0);
    CHECK(events.VideoPts() == (callback_from_audio ? 0 : 100'000));
    CHECK(events.AudioPts() == (callback_from_audio ? 100'000 : 0));
    CHECK(events.Drift() == 100'000);
    CHECK(mux.IsMuxAbortRequestedForTesting());
    CHECK(mux_gate.SecondPeerResult() != Mp4MuxResult::Written);
    CHECK(muxer.PacketCalls() == 2);
    CHECK(muxer.AbortCalls() == 1);
    CHECK(muxer.TrailerCalls() == 0);
    CHECK(muxer.FlushCalls() == 0);

    const auto statistics = recording.Statistics();
    CHECK(statistics.video.muxed_packet_count ==
          (callback_from_audio ? 1 : 0));
    CHECK(statistics.audio.muxed_packet_count ==
          (callback_from_audio ? 0 : 1));
}

int Child()
{
    std::mutex mutex;
    std::condition_variable changed;
    bool completed = false;
    std::thread watchdog([&] {
        std::unique_lock lock(mutex);
        if (!changed.wait_for(
                lock,
                std::chrono::seconds {2},
                [&] { return completed; })) {
            std::cerr << "full media graph Abort timed out\n" << std::flush;
            std::_Exit(86);
        }
    });

    ExerciseFullGraph(true);
    ExerciseFullGraph(false);
    {
        const std::lock_guard lock(mutex);
        completed = true;
    }
    changed.notify_all();
    watchdog.join();
    return 0;
}

void FullSessionAbortFromEitherSynchronousDriftCallbackDoesNotJoinMuxPeer(
    const char *executable)
{
    const auto command = std::string("\"") + executable +
        "\" --full-media-graph-abort-child";
    CHECK(std::system(command.c_str()) == 0);
}

}

int main(int argc, char **argv)
{
    if (argc == 2 &&
        std::string(argv[1]) == "--full-media-graph-abort-child") {
        return Child();
    }

    FullSessionAbortFromEitherSynchronousDriftCallbackDoesNotJoinMuxPeer(
        argv[0]);
    return 0;
}
