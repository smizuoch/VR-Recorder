#include "shared_mux_finalization_session.hpp"
#include "fragmented_mp4_test_support.hpp"

#include <cstddef>
#include <chrono>
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
        order.push_back(1);
        return write_status;
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

    std::vector<int> order;
    vrrec_status_t write_status = VRREC_STATUS_OK;
    std::size_t abort_calls = 0;
};

class AbortSharedSessionObserver final : public EncodedMediaPacketObserver {
public:
    vrrec_status_t Observe(const EncodedMediaPacket &) noexcept override
    {
        ++observe_calls;
        CHECK(session != nullptr);
        session->Abort();
        abort_returned = true;
        return VRREC_STATUS_OK;
    }

    SharedMuxFinalizationSession *session = nullptr;
    std::size_t observe_calls = 0;
    bool abort_returned = false;
};

EncodedMediaPacket VideoPacket()
{
    return {
        MediaStreamKind::Video,
        0,
        0,
        33'333,
        true,
        std::vector<std::byte>(100, std::byte{0x01}),
    };
}

EncodedMediaPacket AudioPacket()
{
    return {
        MediaStreamKind::Audio,
        0,
        0,
        21'333,
        false,
        std::vector<std::byte>(100, std::byte{0x02}),
    };
}

void FinalizesOnlyAfterBothEncodersFlushSuccessfully()
{
    RecordingMuxer backend;
    FragmentedMp4MuxCoordinator mux(backend);
    CHECK(mux.Begin(TestMp4Streams()) == VRREC_STATUS_OK);
    SharedMuxFinalizationSession session(mux);
    CHECK(session.Submit(VideoPacket()) == Mp4MuxResult::Written);

    CHECK(session.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(backend.order == std::vector<int>({1}));
    CHECK(session.EncoderFinished(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    CHECK(backend.order == std::vector<int>({1, 3, 4}));
}

void SupportsAudioFinishingBeforeVideo()
{
    RecordingMuxer backend;
    FragmentedMp4MuxCoordinator mux(backend);
    CHECK(mux.Begin(TestMp4Streams()) == VRREC_STATUS_OK);
    SharedMuxFinalizationSession session(mux);

    CHECK(session.EncoderFinished(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    CHECK(backend.order.empty());
    CHECK(session.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(backend.order == std::vector<int>({3, 4}));
}

void EmptyBatchChecksStateWithoutFinishingItsProducer()
{
    RecordingMuxer backend;
    FragmentedMp4MuxCoordinator mux(backend);
    CHECK(mux.Begin(TestMp4Streams()) == VRREC_STATUS_OK);
    SharedMuxFinalizationSession session(mux);
    const std::span<const EncodedMediaPacket> empty;

    CHECK(session.SubmitBatch(MediaStreamKind::Video, empty) ==
          Mp4MuxResult::Written);
    CHECK(backend.order.empty());
    CHECK(session.Submit(VideoPacket()) == Mp4MuxResult::Written);
    CHECK(backend.order == std::vector<int>({1}));
}

void RejectsPacketsAfterTheirEncoderHasFinished()
{
    RecordingMuxer backend;
    FragmentedMp4MuxCoordinator mux(backend);
    CHECK(mux.Begin(TestMp4Streams()) == VRREC_STATUS_OK);
    SharedMuxFinalizationSession session(mux);

    CHECK(session.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(session.Submit(VideoPacket()) == Mp4MuxResult::InvalidState);
    CHECK(backend.abort_calls == 1);
    CHECK(backend.order == std::vector<int>({5}));
    CHECK(session.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_INVALID_STATE);
}

void DuplicateEncoderCompletionAbortsImmediately()
{
    RecordingMuxer backend;
    FragmentedMp4MuxCoordinator mux(backend);
    CHECK(mux.Begin(TestMp4Streams()) == VRREC_STATUS_OK);
    SharedMuxFinalizationSession session(mux);

    CHECK(session.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(session.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(backend.abort_calls == 1);
    CHECK(backend.order == std::vector<int>({5}));
    CHECK(session.EncoderFinished(MediaStreamKind::Audio) ==
          VRREC_STATUS_INVALID_STATE);
}

void InvalidCompletionStreamAbortsImmediately()
{
    RecordingMuxer backend;
    FragmentedMp4MuxCoordinator mux(backend);
    CHECK(mux.Begin(TestMp4Streams()) == VRREC_STATUS_OK);
    SharedMuxFinalizationSession session(mux);

    CHECK(session.EncoderFinished(static_cast<MediaStreamKind>(99)) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(backend.abort_calls == 1);
    CHECK(backend.order == std::vector<int>({5}));
}

void FinalizationAgainstAnUnstartedMuxAbortsImmediately()
{
    RecordingMuxer backend;
    FragmentedMp4MuxCoordinator mux(backend);
    SharedMuxFinalizationSession session(mux);

    CHECK(session.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(session.EncoderFinished(MediaStreamKind::Audio) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(backend.abort_calls == 1);
    CHECK(backend.order == std::vector<int>({5}));
}

void ObserverCanAbortTheSharedSessionWithoutDeadlocking()
{
    RecordingMuxer backend;
    AbortSharedSessionObserver observer;
    FragmentedMp4MuxCoordinator mux(backend, &observer);
    CHECK(mux.Begin(TestMp4Streams()) == VRREC_STATUS_OK);
    SharedMuxFinalizationSession session(mux);
    observer.session = &session;

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

    const auto result = session.Submit(VideoPacket());
    {
        const std::lock_guard lock(watchdog_mutex);
        submit_completed = true;
    }
    watchdog_changed.notify_all();
    watchdog.join();

    CHECK(result == Mp4MuxResult::MuxFailed);
    CHECK(observer.observe_calls == 1);
    CHECK(observer.abort_returned);
    CHECK(backend.abort_calls == 1);
    CHECK(session.Submit(AudioPacket()) == Mp4MuxResult::InvalidState);
}

void EncoderFailureAbortsWithoutWritingATrailer()
{
    RecordingMuxer backend;
    FragmentedMp4MuxCoordinator mux(backend);
    CHECK(mux.Begin(TestMp4Streams()) == VRREC_STATUS_OK);
    SharedMuxFinalizationSession session(mux);
    CHECK(session.Submit(VideoPacket()) == Mp4MuxResult::Written);

    session.EncoderFailed(MediaStreamKind::Audio);
    session.EncoderFailed(MediaStreamKind::Audio);
    CHECK(backend.order == std::vector<int>({1, 5}));
    CHECK(backend.abort_calls == 1);
    CHECK(session.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_INVALID_STATE);
}

void PacketMuxFailureImmediatelyTerminalizesTheSharedSession()
{
    RecordingMuxer backend;
    backend.write_status = VRREC_STATUS_INTERNAL_ERROR;
    FragmentedMp4MuxCoordinator mux(backend);
    CHECK(mux.Begin(TestMp4Streams()) == VRREC_STATUS_OK);
    SharedMuxFinalizationSession session(mux);

    CHECK(session.Submit(VideoPacket()) == Mp4MuxResult::MuxFailed);
    CHECK(backend.abort_calls == 1);
    CHECK(session.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(session.EncoderFinished(MediaStreamKind::Audio) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(session.Submit(VideoPacket()) == Mp4MuxResult::InvalidState);
}

void InvalidBatchTerminalizesBeforeWritingItsValidPrefix()
{
    RecordingMuxer backend;
    FragmentedMp4MuxCoordinator mux(backend);
    CHECK(mux.Begin(TestMp4Streams()) == VRREC_STATUS_OK);
    SharedMuxFinalizationSession session(mux);
    auto invalid = VideoPacket();
    invalid.dts_microseconds = 33'333;
    invalid.pts_microseconds = 33'333;
    invalid.duration_microseconds = 0;
    const std::vector<EncodedMediaPacket> batch {
        VideoPacket(),
        invalid,
    };

    CHECK(session.SubmitBatch(MediaStreamKind::Video, batch) ==
          Mp4MuxResult::InvalidPacket);
    CHECK(backend.order == std::vector<int>({5}));
    CHECK(backend.abort_calls == 1);
    CHECK(session.SubmitBatch(
              MediaStreamKind::Audio,
              std::span<const EncodedMediaPacket> {}) ==
          Mp4MuxResult::InvalidState);
    CHECK(session.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(session.EncoderFinished(MediaStreamKind::Audio) ==
          VRREC_STATUS_INVALID_STATE);
}

void RejectsAProducerStreamMismatchWithoutMuxMutation()
{
    RecordingMuxer backend;
    FragmentedMp4MuxCoordinator mux(backend);
    CHECK(mux.Begin(TestMp4Streams()) == VRREC_STATUS_OK);
    SharedMuxFinalizationSession session(mux);
    const std::vector<EncodedMediaPacket> batch {AudioPacket()};

    CHECK(session.SubmitBatch(MediaStreamKind::Video, batch) ==
          Mp4MuxResult::InvalidPacket);
    CHECK(backend.order == std::vector<int>({5}));
    CHECK(backend.abort_calls == 1);
}

void RejectsUnknownProducersAndPacketsFromAFinishedAudioEncoder()
{
    {
        RecordingMuxer backend;
        FragmentedMp4MuxCoordinator mux(backend);
        CHECK(mux.Begin(TestMp4Streams()) == VRREC_STATUS_OK);
        SharedMuxFinalizationSession session(mux);

        CHECK(session.SubmitBatch(
                  static_cast<MediaStreamKind>(99),
                  std::span<const EncodedMediaPacket> {}) ==
              Mp4MuxResult::InvalidPacket);
        CHECK(backend.abort_calls == 1);
    }

    RecordingMuxer backend;
    FragmentedMp4MuxCoordinator mux(backend);
    CHECK(mux.Begin(TestMp4Streams()) == VRREC_STATUS_OK);
    SharedMuxFinalizationSession session(mux);
    CHECK(session.EncoderFinished(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    CHECK(session.Submit(AudioPacket()) == Mp4MuxResult::InvalidState);
    CHECK(backend.abort_calls == 1);
}

}

int main()
{
    FinalizesOnlyAfterBothEncodersFlushSuccessfully();
    SupportsAudioFinishingBeforeVideo();
    EmptyBatchChecksStateWithoutFinishingItsProducer();
    RejectsPacketsAfterTheirEncoderHasFinished();
    DuplicateEncoderCompletionAbortsImmediately();
    InvalidCompletionStreamAbortsImmediately();
    FinalizationAgainstAnUnstartedMuxAbortsImmediately();
    ObserverCanAbortTheSharedSessionWithoutDeadlocking();
    EncoderFailureAbortsWithoutWritingATrailer();
    PacketMuxFailureImmediatelyTerminalizesTheSharedSession();
    InvalidBatchTerminalizesBeforeWritingItsValidPrefix();
    RejectsAProducerStreamMismatchWithoutMuxMutation();
    RejectsUnknownProducersAndPacketsFromAFinishedAudioEncoder();
    return 0;
}
