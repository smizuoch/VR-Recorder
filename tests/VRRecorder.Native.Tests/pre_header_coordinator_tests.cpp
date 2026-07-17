#include "pre_header_coordinator.hpp"

#include "allocation_failure_test_support.hpp"

#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <future>
#include <iostream>
#include <mutex>
#include <span>
#include <thread>
#include <utility>
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

class RecordingDownstream final
    : public MediaMuxSessionPort,
      public EncodedMediaPacketSubmissionPort {
public:
    vrrec_status_t Start(
        const FragmentedMp4StreamConfiguration &configuration) noexcept override
    {
        ++start_calls;
        last_configuration = configuration;
        return start_status;
    }

    Mp4MuxResult SubmitBatch(
        MediaStreamKind producer,
        std::span<const EncodedMediaPacket> packets) noexcept override
    {
        ++submit_calls;
        for (const auto &packet : packets) {
            CHECK(packet.stream == producer);
            try {
                submitted_packets.push_back(packet);
            } catch (...) {
                return Mp4MuxResult::MuxFailed;
            }
        }
        return submit_result;
    }

    vrrec_status_t EncoderFinished(MediaStreamKind stream) noexcept override
    {
        ++encoder_finished_calls;
        try {
            finished_streams.push_back(stream);
        } catch (...) {
            return VRREC_STATUS_OUT_OF_MEMORY;
        }
        return encoder_finished_status;
    }

    void EncoderFailed(MediaStreamKind) noexcept override
    {
        ++encoder_failed_calls;
    }

    void RequestAbort() noexcept override
    {
        ++request_abort_calls;
    }

    void Abort() noexcept override
    {
        ++abort_calls;
    }

    std::int64_t AudioVideoOffsetMicroseconds() const noexcept override
    {
        return audio_video_offset_microseconds;
    }

    vrrec_status_t start_status = VRREC_STATUS_OK;
    Mp4MuxResult submit_result = Mp4MuxResult::Written;
    vrrec_status_t encoder_finished_status = VRREC_STATUS_OK;
    std::int64_t audio_video_offset_microseconds = 0;
    FragmentedMp4StreamConfiguration last_configuration {};
    std::vector<EncodedMediaPacket> submitted_packets;
    std::vector<MediaStreamKind> finished_streams;
    std::size_t start_calls = 0;
    std::size_t submit_calls = 0;
    std::size_t encoder_finished_calls = 0;
    std::size_t encoder_failed_calls = 0;
    std::size_t request_abort_calls = 0;
    std::size_t abort_calls = 0;
};

class BlockingFirstSubmissionDownstream final
    : public MediaMuxSessionPort,
      public EncodedMediaPacketSubmissionPort {
public:
    explicit BlockingFirstSubmissionDownstream(
        bool block_first_submission = true,
        bool block_encoder_finish = false,
        bool block_header_start = false) noexcept
        : block_first_submission_(block_first_submission),
          block_encoder_finish_(block_encoder_finish),
          block_header_start_(block_header_start)
    {
    }

    vrrec_status_t Start(
        const FragmentedMp4StreamConfiguration &) noexcept override
    {
        std::unique_lock lock(mutex_);
        ++start_calls_;
        header_start_entered_ = true;
        changed_.notify_all();
        if (block_header_start_) {
            changed_.wait(lock, [&] {
                return release_header_start_ || aborted_;
            });
        }
        return VRREC_STATUS_OK;
    }

    Mp4MuxResult SubmitBatch(
        MediaStreamKind producer,
        std::span<const EncodedMediaPacket> packets) noexcept override
    {
        std::unique_lock lock(mutex_);
        ++submit_calls_;
        try {
            for (const auto &packet : packets) {
                if (packet.stream != producer) {
                    return Mp4MuxResult::InvalidPacket;
                }
                submitted_packets_.push_back(packet);
            }
        } catch (...) {
            return Mp4MuxResult::MuxFailed;
        }
        if (block_first_submission_ && submit_calls_ == 1) {
            first_submit_entered_ = true;
            changed_.notify_all();
            changed_.wait(lock, [&] {
                return release_first_submit_ || aborted_;
            });
        }
        return aborted_ ? Mp4MuxResult::MuxFailed : Mp4MuxResult::Written;
    }

    vrrec_status_t EncoderFinished(MediaStreamKind) noexcept override
    {
        std::unique_lock lock(mutex_);
        encoder_finish_entered_ = true;
        changed_.notify_all();
        if (block_encoder_finish_) {
            changed_.wait(lock, [&] {
                return release_encoder_finish_ || aborted_;
            });
        }
        return VRREC_STATUS_OK;
    }

    void EncoderFailed(MediaStreamKind) noexcept override
    {
    }

    void RequestAbort() noexcept override
    {
        const std::lock_guard lock(mutex_);
        ++request_abort_calls_;
        aborted_ = true;
        changed_.notify_all();
    }

    void Abort() noexcept override
    {
        const std::lock_guard lock(mutex_);
        ++abort_calls_;
        aborted_ = true;
        changed_.notify_all();
    }

    void WaitForFirstSubmission()
    {
        std::unique_lock lock(mutex_);
        CHECK(changed_.wait_for(lock, std::chrono::seconds(2), [&] {
            return first_submit_entered_;
        }));
    }

    void WaitForHeaderStart()
    {
        std::unique_lock lock(mutex_);
        CHECK(changed_.wait_for(lock, std::chrono::seconds(2), [&] {
            return header_start_entered_;
        }));
    }

    void ReleaseFirstSubmission() noexcept
    {
        const std::lock_guard lock(mutex_);
        release_first_submit_ = true;
        changed_.notify_all();
    }

    void WaitForEncoderFinish()
    {
        std::unique_lock lock(mutex_);
        CHECK(changed_.wait_for(lock, std::chrono::seconds(2), [&] {
            return encoder_finish_entered_;
        }));
    }

    std::vector<EncodedMediaPacket> SubmittedPackets() const
    {
        const std::lock_guard lock(mutex_);
        return submitted_packets_;
    }

    std::size_t SubmitCalls() const noexcept
    {
        const std::lock_guard lock(mutex_);
        return submit_calls_;
    }

    std::size_t StartCalls() const noexcept
    {
        const std::lock_guard lock(mutex_);
        return start_calls_;
    }

    std::size_t RequestAbortCalls() const noexcept
    {
        const std::lock_guard lock(mutex_);
        return request_abort_calls_;
    }

    std::size_t AbortCalls() const noexcept
    {
        const std::lock_guard lock(mutex_);
        return abort_calls_;
    }

private:
    mutable std::mutex mutex_;
    std::condition_variable changed_;
    std::vector<EncodedMediaPacket> submitted_packets_;
    std::size_t start_calls_ = 0;
    std::size_t submit_calls_ = 0;
    std::size_t request_abort_calls_ = 0;
    std::size_t abort_calls_ = 0;
    bool first_submit_entered_ = false;
    bool release_first_submit_ = false;
    bool encoder_finish_entered_ = false;
    bool release_encoder_finish_ = false;
    bool header_start_entered_ = false;
    bool release_header_start_ = false;
    bool aborted_ = false;
    bool block_first_submission_;
    bool block_encoder_finish_;
    bool block_header_start_;
};

EncodedMediaPacket Packet(
    MediaStreamKind stream,
    std::int64_t dts_microseconds,
    std::byte payload)
{
    return {
        stream,
        dts_microseconds,
        dts_microseconds,
        10'000,
        stream == MediaStreamKind::Video,
        {payload},
    };
}

void WaitForQueuedPackets(
    const PreHeaderCoordinator &coordinator,
    std::size_t expected)
{
    const auto deadline = std::chrono::steady_clock::now() +
        std::chrono::seconds(2);
    while (coordinator.QueuedPacketCountForTesting() != expected &&
           std::chrono::steady_clock::now() < deadline) {
        std::this_thread::yield();
    }
    CHECK(coordinator.QueuedPacketCountForTesting() == expected);
}

void DoesNotStartTheHeaderBeforeDescriptorReadiness()
{
    RecordingDownstream downstream;
    int encoder_identity = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);

    CHECK(coordinator.BeginPriming(1'000'000) == VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    CHECK(downstream.start_calls == 0);
    CHECK(downstream.submit_calls == 0);
    CHECK(coordinator.State() == PreHeaderState::Priming);

    CHECK(coordinator.PublishVideoDescriptor(
              &encoder_identity,
              VideoDescriptor()) == VRREC_STATUS_OK);
    CHECK(downstream.start_calls == 1);
    CHECK(downstream.last_configuration.video.codec_extradata ==
          VideoDescriptor().codec_extradata);
    CHECK(downstream.last_configuration.audio.bitrate_bits_per_second ==
          192'000);
    CHECK(coordinator.State() == PreHeaderState::Running);
}

void QueuesOwnedPacketsAndDrainsThemInDeterministicDtsOrder()
{
    RecordingDownstream downstream;
    int encoder_identity = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(coordinator.BeginPriming(1'000'000) == VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);

    std::vector audio {Packet(MediaStreamKind::Audio, 100, std::byte {0xA1})};
    std::vector late_video {
        Packet(MediaStreamKind::Video, 100, std::byte {0xB1})};
    std::vector early_video {
        Packet(MediaStreamKind::Video, 50, std::byte {0xB0})};
    auto audio_result = std::async(std::launch::async, [&] {
        return coordinator.SubmitBatch(MediaStreamKind::Audio, audio);
    });
    auto late_video_result = std::async(std::launch::async, [&] {
        return coordinator.SubmitBatch(MediaStreamKind::Video, late_video);
    });
    auto early_video_result = std::async(std::launch::async, [&] {
        return coordinator.SubmitBatch(MediaStreamKind::Video, early_video);
    });
    WaitForQueuedPackets(coordinator, 3);
    CHECK(downstream.submit_calls == 0);
    audio.front().payload.front() = std::byte {0xEE};
    late_video.front().payload.front() = std::byte {0xEE};
    early_video.front().payload.front() = std::byte {0xEE};

    CHECK(coordinator.PublishVideoDescriptor(
              &encoder_identity,
              VideoDescriptor()) == VRREC_STATUS_OK);
    CHECK(audio_result.get() == Mp4MuxResult::Written);
    CHECK(late_video_result.get() == Mp4MuxResult::Written);
    CHECK(early_video_result.get() == Mp4MuxResult::Written);
    CHECK(downstream.start_calls == 1);
    CHECK(downstream.submit_calls == 3);
    CHECK(downstream.submitted_packets.size() == 3);
    CHECK(downstream.submitted_packets[0].stream == MediaStreamKind::Video);
    CHECK(downstream.submitted_packets[0].dts_microseconds == 50);
    CHECK(downstream.submitted_packets[0].payload.front() ==
          std::byte {0xB0});
    CHECK(downstream.submitted_packets[1].stream == MediaStreamKind::Video);
    CHECK(downstream.submitted_packets[1].dts_microseconds == 100);
    CHECK(downstream.submitted_packets[2].stream == MediaStreamKind::Audio);
    CHECK(downstream.submitted_packets[2].dts_microseconds == 100);
    CHECK(downstream.submitted_packets[2].payload.front() ==
          std::byte {0xA1});
    CHECK(coordinator.State() == PreHeaderState::Running);
}

void AbortWakesAQueuedSubmissionWithoutWritingIt()
{
    RecordingDownstream downstream;
    int encoder_identity = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    const std::vector packets {
        Packet(MediaStreamKind::Audio, 0, std::byte {0xA0})};
    auto result = std::async(std::launch::async, [&] {
        return coordinator.SubmitBatch(MediaStreamKind::Audio, packets);
    });
    WaitForQueuedPackets(coordinator, 1);

    coordinator.Abort();

    CHECK(result.get() == Mp4MuxResult::MuxFailed);
    CHECK(downstream.start_calls == 0);
    CHECK(downstream.submit_calls == 0);
    CHECK(coordinator.QueuedPacketCountForTesting() == 0);
    CHECK(coordinator.State() == PreHeaderState::Aborted);
}

void QueueOverflowIsTerminalAndBatchAtomic(
    PreHeaderQueueLimits limits,
    std::vector<EncodedMediaPacket> first_batch,
    std::vector<EncodedMediaPacket> overflowing_batch)
{
    RecordingDownstream downstream;
    int encoder_identity = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity,
        limits);
    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    const auto producer = first_batch.front().stream;
    auto first_result = std::async(std::launch::async, [&] {
        return coordinator.SubmitBatch(producer, first_batch);
    });
    WaitForQueuedPackets(coordinator, first_batch.size());

    CHECK(coordinator.SubmitBatch(producer, overflowing_batch) ==
          Mp4MuxResult::MuxFailed);

    CHECK(first_result.get() == Mp4MuxResult::MuxFailed);
    CHECK(coordinator.QueuedPacketCountForTesting() == 0);
    CHECK(downstream.start_calls == 0);
    CHECK(downstream.submit_calls == 0);
    CHECK(downstream.request_abort_calls == 1);
    CHECK(downstream.abort_calls == 1);
    CHECK(coordinator.State() == PreHeaderState::Failed);
}

void EnforcesEveryPerStreamQueueLimitBeforeMutatingTheBatch()
{
    QueueOverflowIsTerminalAndBatchAtomic(
        {2, 1'024, 1'000},
        {Packet(MediaStreamKind::Audio, 0, std::byte {0xA0})},
        {
            Packet(MediaStreamKind::Audio, 10, std::byte {0xA1}),
            Packet(MediaStreamKind::Audio, 20, std::byte {0xA2}),
        });
    QueueOverflowIsTerminalAndBatchAtomic(
        {10, 1, 1'000},
        {Packet(MediaStreamKind::Video, 0, std::byte {0xB0})},
        {Packet(MediaStreamKind::Video, 10, std::byte {0xB1})});
    QueueOverflowIsTerminalAndBatchAtomic(
        {10, 1'024, 50},
        {Packet(MediaStreamKind::Audio, 0, std::byte {0xA0})},
        {Packet(MediaStreamKind::Audio, 51, std::byte {0xA1})});

    auto audio_with_side_data =
        Packet(MediaStreamKind::Audio, 0, std::byte {0xA0});
    audio_with_side_data.side_data.push_back(
        {EncodedPacketSideDataKind::SkipSamples,
         std::vector<std::byte>(SkipSamplesSideDataSize)});
    QueueOverflowIsTerminalAndBatchAtomic(
        {10, SkipSamplesSideDataSize + 1, 1'000},
        {audio_with_side_data},
        {Packet(MediaStreamKind::Audio, 1, std::byte {0xA1})});
}

void InvalidPreHeaderBatchTerminallyWakesExistingTickets()
{
    RecordingDownstream downstream;
    int encoder_identity = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    const std::vector valid {
        Packet(MediaStreamKind::Audio, 0, std::byte {0xA0})};
    auto valid_result = std::async(std::launch::async, [&] {
        return coordinator.SubmitBatch(MediaStreamKind::Audio, valid);
    });
    WaitForQueuedPackets(coordinator, 1);
    auto invalid = Packet(MediaStreamKind::Audio, 1, std::byte {0xA1});
    invalid.payload.clear();

    const auto invalid_result = coordinator.SubmitBatch(
        MediaStreamKind::Audio,
        std::span<const EncodedMediaPacket>(&invalid, 1));
    const auto state_after_invalid = coordinator.State();
    const auto request_abort_calls_after_invalid =
        downstream.request_abort_calls;
    const auto abort_calls_after_invalid = downstream.abort_calls;
    if (state_after_invalid != PreHeaderState::Failed) {
        coordinator.Abort();
    }

    CHECK(invalid_result == Mp4MuxResult::InvalidPacket);
    CHECK(state_after_invalid == PreHeaderState::Failed);
    CHECK(valid_result.get() == Mp4MuxResult::MuxFailed);
    CHECK(request_abort_calls_after_invalid == 1);
    CHECK(abort_calls_after_invalid == 1);
    CHECK(downstream.start_calls == 0);
    CHECK(downstream.submit_calls == 0);
}

void KeepsPacketsAfterTheHeaderAdmissionCutInTheLiveBacklog()
{
    BlockingFirstSubmissionDownstream downstream;
    int encoder_identity = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    const std::vector pre_header_audio {
        Packet(MediaStreamKind::Audio, 100, std::byte {0xA0})};
    const std::vector pre_header_video {
        Packet(MediaStreamKind::Video, 200, std::byte {0xB0})};
    auto audio_result = std::async(std::launch::async, [&] {
        return coordinator.SubmitBatch(
            MediaStreamKind::Audio,
            pre_header_audio);
    });
    auto video_result = std::async(std::launch::async, [&] {
        return coordinator.SubmitBatch(
            MediaStreamKind::Video,
            pre_header_video);
    });
    WaitForQueuedPackets(coordinator, 2);

    auto descriptor_result = std::async(std::launch::async, [&] {
        return coordinator.PublishVideoDescriptor(
            &encoder_identity,
            VideoDescriptor());
    });
    downstream.WaitForFirstSubmission();
    CHECK(coordinator.AdmissionCutSequenceForTesting() == 2);
    const std::vector live_audio {
        Packet(MediaStreamKind::Audio, 150, std::byte {0xA1}),
        Packet(MediaStreamKind::Audio, 160, std::byte {0xA2}),
    };
    auto live_result = std::async(std::launch::async, [&] {
        return coordinator.SubmitBatch(MediaStreamKind::Audio, live_audio);
    });
    WaitForQueuedPackets(coordinator, 4);
    CHECK(coordinator.AdmissionCutSequenceForTesting() == 2);

    downstream.ReleaseFirstSubmission();

    CHECK(descriptor_result.get() == VRREC_STATUS_OK);
    CHECK(audio_result.get() == Mp4MuxResult::Written);
    CHECK(video_result.get() == Mp4MuxResult::Written);
    CHECK(live_result.get() == Mp4MuxResult::Written);
    CHECK(downstream.SubmitCalls() == 3);
    const auto submitted = downstream.SubmittedPackets();
    CHECK(submitted.size() == 4);
    CHECK(submitted[0].dts_microseconds == 100);
    CHECK(submitted[1].dts_microseconds == 200);
    CHECK(submitted[2].dts_microseconds == 150);
    CHECK(submitted[3].dts_microseconds == 160);
    CHECK(coordinator.State() == PreHeaderState::Running);
}

void AbortWinsAnInFlightEncoderCompletion()
{
    BlockingFirstSubmissionDownstream downstream(false, true);
    int encoder_identity = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    CHECK(coordinator.PublishVideoDescriptor(
              &encoder_identity,
              VideoDescriptor()) == VRREC_STATUS_OK);
    CHECK(coordinator.State() == PreHeaderState::Running);
    auto completion = std::async(std::launch::async, [&] {
        return coordinator.EncoderFinished(MediaStreamKind::Video);
    });
    downstream.WaitForEncoderFinish();

    coordinator.Abort();

    CHECK(completion.get() == VRREC_STATUS_INVALID_STATE);
    CHECK(coordinator.State() == PreHeaderState::Aborted);
}

void DuplicateProducerCompletionBeforeItsPeerIsTerminal()
{
    RecordingDownstream downstream;
    int encoder_identity = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    CHECK(coordinator.PublishVideoDescriptor(
              &encoder_identity,
              VideoDescriptor()) == VRREC_STATUS_OK);

    CHECK(coordinator.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(coordinator.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_INVALID_STATE);

    CHECK(downstream.encoder_finished_calls == 1);
    CHECK(downstream.request_abort_calls == 1);
    CHECK(downstream.abort_calls == 1);
    CHECK(coordinator.State() == PreHeaderState::Failed);
}

void BothProducerCompletionsEnterTerminalFinishing()
{
    RecordingDownstream downstream;
    int encoder_identity = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    CHECK(coordinator.PublishVideoDescriptor(
              &encoder_identity,
              VideoDescriptor()) == VRREC_STATUS_OK);

    CHECK(coordinator.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(coordinator.State() == PreHeaderState::Running);
    CHECK(coordinator.EncoderFinished(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    CHECK(coordinator.State() == PreHeaderState::Finishing);
    CHECK(downstream.finished_streams ==
          std::vector({MediaStreamKind::Video, MediaStreamKind::Audio}));

    coordinator.Abort();
    CHECK(coordinator.State() == PreHeaderState::Finishing);
    CHECK(downstream.request_abort_calls == 0);
    CHECK(downstream.abort_calls == 0);
}

void RejectsPacketsFromACompletedProducer()
{
    RecordingDownstream downstream;
    int encoder_identity = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    CHECK(coordinator.PublishVideoDescriptor(
              &encoder_identity,
              VideoDescriptor()) == VRREC_STATUS_OK);
    CHECK(coordinator.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    const std::vector packet {
        Packet(MediaStreamKind::Video, 0, std::byte {0xB0})};

    CHECK(coordinator.SubmitBatch(MediaStreamKind::Video, packet) ==
          Mp4MuxResult::InvalidState);

    CHECK(downstream.submit_calls == 0);
    CHECK(downstream.request_abort_calls == 1);
    CHECK(downstream.abort_calls == 1);
    CHECK(coordinator.State() == PreHeaderState::Failed);
}

void PublishesTheDescriptorWithoutLosingItsFirstVideoBatch()
{
    RecordingDownstream downstream;
    int encoder_identity = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    const std::vector audio {
        Packet(MediaStreamKind::Audio, -10, std::byte {0xA0})};
    auto audio_result = std::async(std::launch::async, [&] {
        return coordinator.SubmitBatch(MediaStreamKind::Audio, audio);
    });
    WaitForQueuedPackets(coordinator, 1);
    const std::vector first_video {
        Packet(MediaStreamKind::Video, 0, std::byte {0xB0})};

    CHECK(coordinator.SubmitVideoDescriptorBatch(
              &encoder_identity,
              VideoDescriptor(),
              first_video) == Mp4MuxResult::Written);

    CHECK(audio_result.get() == Mp4MuxResult::Written);
    CHECK(downstream.start_calls == 1);
    CHECK(downstream.submit_calls == 2);
    CHECK(downstream.submitted_packets.size() == 2);
    CHECK(downstream.submitted_packets[0].stream == MediaStreamKind::Audio);
    CHECK(downstream.submitted_packets[0].dts_microseconds == -10);
    CHECK(downstream.submitted_packets[1].stream == MediaStreamKind::Video);
    CHECK(downstream.submitted_packets[1].dts_microseconds == 0);
    CHECK(coordinator.State() == PreHeaderState::Running);
}

void RejectsEveryDescriptorBatchContractBoundary()
{
    int encoder_identity = 0;
    int wrong_identity = 0;
    const std::vector valid_packet {
        Packet(MediaStreamKind::Video, 0, std::byte {0xB0})};
    const auto rejects = [&](const void *identity,
                             H264StreamDescriptor descriptor,
                             std::vector<EncodedMediaPacket> packets) {
        RecordingDownstream downstream;
        PreHeaderCoordinator coordinator(
            downstream,
            downstream,
            AudioDescriptor(),
            DefaultFragmentedMp4FragmentPolicy,
            &encoder_identity);
        CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
        CHECK(coordinator.SubmitVideoDescriptorBatch(
                  identity, descriptor, packets) ==
              Mp4MuxResult::InvalidPacket);
        CHECK(coordinator.State() == PreHeaderState::Failed);
    };

    rejects(&encoder_identity, VideoDescriptor(), {});
    rejects(nullptr, VideoDescriptor(), valid_packet);
    rejects(&wrong_identity, VideoDescriptor(), valid_packet);
    auto invalid_descriptor = VideoDescriptor();
    invalid_descriptor.width = 0;
    rejects(&encoder_identity, invalid_descriptor, valid_packet);

    RecordingDownstream duplicate_downstream;
    PreHeaderCoordinator duplicate(
        duplicate_downstream,
        duplicate_downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(duplicate.BeginPriming(0) == VRREC_STATUS_OK);
    CHECK(duplicate.PublishVideoDescriptor(
              &encoder_identity, VideoDescriptor()) == VRREC_STATUS_OK);
    CHECK(duplicate.SubmitVideoDescriptorBatch(
              &encoder_identity,
              VideoDescriptor(),
              valid_packet) == Mp4MuxResult::InvalidState);

    RecordingDownstream created_downstream;
    PreHeaderCoordinator created(
        created_downstream,
        created_downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(created.SubmitVideoDescriptorBatch(
              &encoder_identity,
              VideoDescriptor(),
              valid_packet) == Mp4MuxResult::InvalidState);
    created.Abort();
    CHECK(created.SubmitVideoDescriptorBatch(
              &encoder_identity,
              VideoDescriptor(),
              valid_packet) == Mp4MuxResult::MuxFailed);
}

void ValidatesBatchBehaviorBeforePrimingAndWhileRunning()
{
    int encoder_identity = 0;
    const std::vector video {
        Packet(MediaStreamKind::Video, 0, std::byte {0xB0})};
    RecordingDownstream created_downstream;
    PreHeaderCoordinator created(
        created_downstream,
        created_downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(created.SubmitBatch(MediaStreamKind::Video, {}) ==
          Mp4MuxResult::InvalidPacket);
    CHECK(created.State() == PreHeaderState::Created);
    CHECK(created.SubmitBatch(MediaStreamKind::Video, video) ==
          Mp4MuxResult::InvalidState);

    RecordingDownstream running_downstream;
    PreHeaderCoordinator running(
        running_downstream,
        running_downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(running.BeginPriming(0) == VRREC_STATUS_OK);
    CHECK(running.ProducerStarted(MediaStreamKind::Video) == VRREC_STATUS_OK);
    CHECK(running.ProducerStarted(MediaStreamKind::Audio) == VRREC_STATUS_OK);
    CHECK(running.PublishVideoDescriptor(
              &encoder_identity, VideoDescriptor()) == VRREC_STATUS_OK);
    CHECK(running.SubmitBatch(MediaStreamKind::Video, {}) ==
          Mp4MuxResult::InvalidPacket);
    CHECK(running.State() == PreHeaderState::Failed);

    RecordingDownstream failing_downstream;
    failing_downstream.submit_result = Mp4MuxResult::MuxFailed;
    PreHeaderCoordinator failing(
        failing_downstream,
        failing_downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(failing.BeginPriming(0) == VRREC_STATUS_OK);
    CHECK(failing.ProducerStarted(MediaStreamKind::Video) == VRREC_STATUS_OK);
    CHECK(failing.ProducerStarted(MediaStreamKind::Audio) == VRREC_STATUS_OK);
    CHECK(failing.PublishVideoDescriptor(
              &encoder_identity, VideoDescriptor()) == VRREC_STATUS_OK);
    CHECK(failing.SubmitBatch(MediaStreamKind::Video, video) ==
          Mp4MuxResult::MuxFailed);
    CHECK(failing.State() == PreHeaderState::Failed);
}

void ValidatesAudioCompletionFailureAndMuxOffsetForwarding()
{
    RecordingDownstream downstream;
    downstream.encoder_finished_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    downstream.audio_video_offset_microseconds = -12'345;
    int encoder_identity = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    CHECK(coordinator.PublishVideoDescriptor(
              &encoder_identity, VideoDescriptor()) == VRREC_STATUS_OK);
    CHECK(coordinator.AudioVideoOffsetMicroseconds() == -12'345);
    CHECK(coordinator.EncoderFinished(MediaStreamKind::Audio) ==
          VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(coordinator.State() == PreHeaderState::Failed);

    RecordingDownstream unknown_downstream;
    PreHeaderCoordinator unknown(
        unknown_downstream,
        unknown_downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(unknown.EncoderFinished(static_cast<MediaStreamKind>(99)) ==
          VRREC_STATUS_INVALID_ARGUMENT);
}

void FinishBeforeDescriptorReadinessFailsAllQueuedPackets()
{
    RecordingDownstream downstream;
    int encoder_identity = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    const std::vector audio {
        Packet(MediaStreamKind::Audio, 0, std::byte {0xA0})};
    auto audio_result = std::async(std::launch::async, [&] {
        return coordinator.SubmitBatch(MediaStreamKind::Audio, audio);
    });
    WaitForQueuedPackets(coordinator, 1);

    CHECK(coordinator.EncoderFinished(MediaStreamKind::Audio) ==
          VRREC_STATUS_INVALID_STATE);

    CHECK(audio_result.get() == Mp4MuxResult::MuxFailed);
    CHECK(downstream.start_calls == 0);
    CHECK(downstream.submit_calls == 0);
    CHECK(downstream.request_abort_calls == 1);
    CHECK(downstream.abort_calls == 1);
    CHECK(coordinator.State() == PreHeaderState::Failed);
}

void AbortWinsAHeaderStartThatReturnsSuccessLate()
{
    BlockingFirstSubmissionDownstream downstream(false, false, true);
    int encoder_identity = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    const std::vector audio {
        Packet(MediaStreamKind::Audio, 0, std::byte {0xA0})};
    auto audio_result = std::async(std::launch::async, [&] {
        return coordinator.SubmitBatch(MediaStreamKind::Audio, audio);
    });
    WaitForQueuedPackets(coordinator, 1);
    auto descriptor_result = std::async(std::launch::async, [&] {
        return coordinator.PublishVideoDescriptor(
            &encoder_identity,
            VideoDescriptor());
    });
    downstream.WaitForHeaderStart();

    coordinator.Abort();

    CHECK(descriptor_result.get() == VRREC_STATUS_INVALID_STATE);
    CHECK(audio_result.get() == Mp4MuxResult::MuxFailed);
    CHECK(downstream.StartCalls() == 1);
    CHECK(downstream.SubmitCalls() == 0);
    CHECK(downstream.RequestAbortCalls() == 1);
    CHECK(downstream.AbortCalls() == 1);
    CHECK(coordinator.State() == PreHeaderState::Aborted);
}

void AbortWinsAnInFlightPreHeaderDrain()
{
    BlockingFirstSubmissionDownstream downstream;
    int encoder_identity = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    const std::vector audio {
        Packet(MediaStreamKind::Audio, 0, std::byte {0xA0})};
    const std::vector video {
        Packet(MediaStreamKind::Video, 10, std::byte {0xB0})};
    auto audio_result = std::async(std::launch::async, [&] {
        return coordinator.SubmitBatch(MediaStreamKind::Audio, audio);
    });
    auto video_result = std::async(std::launch::async, [&] {
        return coordinator.SubmitBatch(MediaStreamKind::Video, video);
    });
    WaitForQueuedPackets(coordinator, 2);
    auto descriptor_result = std::async(std::launch::async, [&] {
        return coordinator.PublishVideoDescriptor(
            &encoder_identity,
            VideoDescriptor());
    });
    downstream.WaitForFirstSubmission();

    coordinator.Abort();

    CHECK(descriptor_result.get() == VRREC_STATUS_INVALID_STATE);
    CHECK(audio_result.get() == Mp4MuxResult::MuxFailed);
    CHECK(video_result.get() == Mp4MuxResult::MuxFailed);
    CHECK(downstream.StartCalls() == 1);
    CHECK(downstream.SubmitCalls() == 1);
    CHECK(downstream.RequestAbortCalls() == 1);
    CHECK(downstream.AbortCalls() == 1);
    CHECK(coordinator.State() == PreHeaderState::Aborted);
}

void StartsExactlyOnceRegardlessOfReadinessOrder()
{
    RecordingDownstream downstream;
    int encoder_identity = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);

    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
    CHECK(coordinator.PublishVideoDescriptor(
              &encoder_identity,
              VideoDescriptor()) == VRREC_STATUS_OK);
    CHECK(downstream.start_calls == 0);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    CHECK(downstream.start_calls == 0);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(downstream.start_calls == 1);
}

void RejectsAThrowawayEncoderDescriptor()
{
    RecordingDownstream downstream;
    int production_encoder = 0;
    int throwaway_encoder = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &production_encoder);
    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);

    CHECK(coordinator.PublishVideoDescriptor(
              &throwaway_encoder,
              VideoDescriptor()) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(downstream.start_calls == 0);
    CHECK(downstream.request_abort_calls == 1);
    CHECK(downstream.abort_calls == 1);
    CHECK(coordinator.State() == PreHeaderState::Failed);
}

void RejectsAnIncompleteVideoDescriptorBeforeHeaderReadiness()
{
    RecordingDownstream downstream;
    int encoder_identity = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
    auto incomplete = VideoDescriptor();
    incomplete.codec_extradata.clear();

    CHECK(coordinator.PublishVideoDescriptor(
              &encoder_identity,
              incomplete) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(downstream.start_calls == 0);
    CHECK(downstream.request_abort_calls == 1);
    CHECK(downstream.abort_calls == 1);
    CHECK(coordinator.State() == PreHeaderState::Failed);
}

void HeaderFailureIsTerminalAndDoesNotRetry()
{
    RecordingDownstream downstream;
    downstream.start_status = VRREC_STATUS_INTERNAL_ERROR;
    int encoder_identity = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
    CHECK(coordinator.PublishVideoDescriptor(
              &encoder_identity,
              VideoDescriptor()) == VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);

    CHECK(coordinator.ProducerStarted(MediaStreamKind::Audio) ==
          VRREC_STATUS_INTERNAL_ERROR);
    CHECK(downstream.start_calls == 1);
    CHECK(downstream.request_abort_calls == 1);
    CHECK(downstream.abort_calls == 1);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Audio) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(downstream.start_calls == 1);
}

void AbortBeforeReadinessPreventsHeaderStart()
{
    RecordingDownstream downstream;
    int encoder_identity = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
    coordinator.Abort();
    coordinator.Abort();

    CHECK(coordinator.PublishVideoDescriptor(
              &encoder_identity,
              VideoDescriptor()) == VRREC_STATUS_INVALID_STATE);
    CHECK(downstream.start_calls == 0);
    CHECK(downstream.request_abort_calls == 1);
    CHECK(downstream.abort_calls == 1);
    CHECK(coordinator.State() == PreHeaderState::Aborted);
}

void RejectsEveryInvalidPrimingContract()
{
    int encoder_identity = 0;
    const auto rejects_audio = [&](const auto mutate) {
        RecordingDownstream downstream;
        auto audio = AudioDescriptor();
        mutate(audio);
        PreHeaderCoordinator coordinator(
            downstream,
            downstream,
            std::move(audio),
            DefaultFragmentedMp4FragmentPolicy,
            &encoder_identity);
        CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(coordinator.State() == PreHeaderState::Failed);
    };
    rejects_audio([](auto &value) { value.packet_time_base.numerator = 2; });
    rejects_audio([](auto &value) { value.sample_rate = 44'100; });
    rejects_audio([](auto &value) { value.channel_count = 1; });
    rejects_audio([](auto &value) { value.frame_size = 0; });
    rejects_audio([](auto &value) { value.frame_size = 0x8000'0000U; });
    rejects_audio([](auto &value) {
        value.initial_padding_samples = 0x8000'0000U;
    });
    rejects_audio([](auto &value) {
        value.profile = static_cast<AacProfile>(1);
    });
    rejects_audio([](auto &value) {
        value.channel_layout = static_cast<AudioChannelLayout>(1);
    });
    rejects_audio([](auto &value) {
        value.packet_format = static_cast<AacPacketFormat>(1);
    });
    rejects_audio([](auto &value) { value.codec_extradata.clear(); });
    rejects_audio([](auto &value) { value.bitrate_bits_per_second--; });

    const auto rejects_constructor = [&](std::int64_t epoch,
                                         const void *identity,
                                         FragmentedMp4FragmentPolicy policy,
                                         PreHeaderQueueLimits limits) {
        RecordingDownstream downstream;
        PreHeaderCoordinator coordinator(
            downstream,
            downstream,
            AudioDescriptor(),
            policy,
            identity,
            limits);
        CHECK(coordinator.BeginPriming(epoch) ==
              VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(coordinator.State() == PreHeaderState::Failed);
    };
    rejects_constructor(
        -1,
        &encoder_identity,
        DefaultFragmentedMp4FragmentPolicy,
        DefaultPreHeaderQueueLimits);
    rejects_constructor(
        0,
        nullptr,
        DefaultFragmentedMp4FragmentPolicy,
        DefaultPreHeaderQueueLimits);
    auto policy = DefaultFragmentedMp4FragmentPolicy;
    policy.minimum_duration_microseconds++;
    rejects_constructor(
        0,
        &encoder_identity,
        policy,
        DefaultPreHeaderQueueLimits);
    auto limits = DefaultPreHeaderQueueLimits;
    limits.maximum_packets_per_stream = 0;
    rejects_constructor(
        0,
        &encoder_identity,
        DefaultFragmentedMp4FragmentPolicy,
        limits);
    limits = DefaultPreHeaderQueueLimits;
    limits.maximum_bytes_per_stream = 0;
    rejects_constructor(
        0,
        &encoder_identity,
        DefaultFragmentedMp4FragmentPolicy,
        limits);
    limits = DefaultPreHeaderQueueLimits;
    limits.maximum_dts_span_microseconds_per_stream = -1;
    rejects_constructor(
        0,
        &encoder_identity,
        DefaultFragmentedMp4FragmentPolicy,
        limits);
}

void RejectsEveryInvalidVideoDescriptor()
{
    int encoder_identity = 0;
    const auto rejects = [&](const auto mutate) {
        RecordingDownstream downstream;
        PreHeaderCoordinator coordinator(
            downstream,
            downstream,
            AudioDescriptor(),
            DefaultFragmentedMp4FragmentPolicy,
            &encoder_identity);
        CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
        auto descriptor = VideoDescriptor();
        mutate(descriptor);
        CHECK(coordinator.PublishVideoDescriptor(
                  &encoder_identity,
                  descriptor) == VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(coordinator.State() == PreHeaderState::Failed);
    };
    rejects([](auto &value) { value.packet_time_base.denominator--; });
    rejects([](auto &value) { value.width = 0; });
    rejects([](auto &value) { value.height = 0; });
    rejects([](auto &value) { value.width = 16'386; });
    rejects([](auto &value) { value.height = 16'386; });
    rejects([](auto &value) { value.width = 1'919; });
    rejects([](auto &value) { value.height = 1'079; });
    rejects([](auto &value) {
        value.profile = static_cast<H264Profile>(2);
    });
    rejects([](auto &value) {
        value.packet_format = H264PacketFormat::AnnexB;
    });
    rejects([](auto &value) { value.codec_extradata.clear(); });

    RecordingDownstream downstream;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
    auto main_profile = VideoDescriptor();
    main_profile.profile = H264Profile::Main;
    CHECK(coordinator.PublishVideoDescriptor(
              &encoder_identity,
              main_profile) == VRREC_STATUS_OK);
}

void RejectsEveryMalformedPreHeaderPacket()
{
    int encoder_identity = 0;
    const auto rejects = [&](MediaStreamKind producer,
                             std::vector<EncodedMediaPacket> packets) {
        RecordingDownstream downstream;
        PreHeaderCoordinator coordinator(
            downstream,
            downstream,
            AudioDescriptor(),
            DefaultFragmentedMp4FragmentPolicy,
            &encoder_identity);
        CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
        CHECK(coordinator.SubmitBatch(producer, packets) ==
              Mp4MuxResult::InvalidPacket);
        CHECK(coordinator.State() == PreHeaderState::Failed);
    };

    rejects(MediaStreamKind::Video, {});
    rejects(
        static_cast<MediaStreamKind>(2),
        {Packet(MediaStreamKind::Video, 0, std::byte {1})});
    auto packet = Packet(MediaStreamKind::Video, 0, std::byte {1});
    packet.stream = MediaStreamKind::Audio;
    rejects(MediaStreamKind::Video, {packet});
    packet = Packet(MediaStreamKind::Video, 0, std::byte {1});
    packet.payload.clear();
    rejects(MediaStreamKind::Video, {packet});
    packet = Packet(MediaStreamKind::Video, 0, std::byte {1});
    packet.pts_microseconds = UnknownMediaTimestamp;
    rejects(MediaStreamKind::Video, {packet});
    packet = Packet(MediaStreamKind::Video, 0, std::byte {1});
    packet.dts_microseconds = UnknownMediaTimestamp;
    rejects(MediaStreamKind::Video, {packet});
    packet = Packet(MediaStreamKind::Video, 0, std::byte {1});
    packet.pts_microseconds = -1;
    rejects(MediaStreamKind::Video, {packet});
    packet = Packet(MediaStreamKind::Video, 0, std::byte {1});
    packet.duration_microseconds = 0;
    rejects(MediaStreamKind::Video, {packet});
}

void RejectsDuplicateAndUnknownProducerLifecycleEvents()
{
    RecordingDownstream downstream;
    int encoder_identity = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_INVALID_STATE);

    RecordingDownstream unknown_downstream;
    PreHeaderCoordinator unknown(
        unknown_downstream,
        unknown_downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(unknown.BeginPriming(0) == VRREC_STATUS_OK);
    CHECK(unknown.ProducerStarted(static_cast<MediaStreamKind>(2)) ==
          VRREC_STATUS_INVALID_ARGUMENT);

    RecordingDownstream duplicate_downstream;
    PreHeaderCoordinator duplicate(
        duplicate_downstream,
        duplicate_downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(duplicate.BeginPriming(0) == VRREC_STATUS_OK);
    CHECK(duplicate.ProducerStarted(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    CHECK(duplicate.ProducerStarted(MediaStreamKind::Audio) ==
          VRREC_STATUS_INVALID_STATE);
}

void EncoderFailureIsForwardedOnlyWhileActive()
{
    RecordingDownstream downstream;
    int encoder_identity = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
    coordinator.EncoderFailed(MediaStreamKind::Audio);
    CHECK(coordinator.State() == PreHeaderState::Failed);
    CHECK(downstream.encoder_failed_calls == 1);
    coordinator.EncoderFailed(MediaStreamKind::Video);
    CHECK(downstream.encoder_failed_calls == 1);

    RecordingDownstream aborted_downstream;
    PreHeaderCoordinator aborted(
        aborted_downstream,
        aborted_downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(aborted.BeginPriming(0) == VRREC_STATUS_OK);
    aborted.Abort();
    aborted.EncoderFailed(MediaStreamKind::Video);
    CHECK(aborted_downstream.encoder_failed_calls == 0);
}

void DescriptorPublicationAllocationFailureIsTerminal()
{
    RecordingDownstream downstream;
    int encoder_identity = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
    auto descriptor = VideoDescriptor();
    descriptor.codec_extradata.assign(256, std::byte {1});

    allocation_failure::fail_on_allocation = 1;
    const auto status = coordinator.PublishVideoDescriptor(
        &encoder_identity,
        descriptor);
    allocation_failure::fail_on_allocation = 0;

    CHECK(status == VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(coordinator.State() == PreHeaderState::Failed);
    CHECK(downstream.start_calls == 0);
    CHECK(downstream.submit_calls == 0);
    CHECK(downstream.request_abort_calls == 1);
    CHECK(downstream.abort_calls == 1);
}

void QueueOwnershipAllocationFailureIsTerminal()
{
    RecordingDownstream downstream;
    int encoder_identity = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
    const std::vector packets {
        Packet(MediaStreamKind::Audio, 0, std::byte {1})};

    allocation_failure::fail_on_allocation = 1;
    const auto result = coordinator.SubmitBatch(
        MediaStreamKind::Audio,
        packets);
    allocation_failure::fail_on_allocation = 0;

    CHECK(result == Mp4MuxResult::MuxFailed);
    CHECK(coordinator.State() == PreHeaderState::Failed);
    CHECK(coordinator.QueuedPacketCountForTesting() == 0);
    CHECK(downstream.start_calls == 0);
    CHECK(downstream.submit_calls == 0);
    CHECK(downstream.request_abort_calls == 1);
    CHECK(downstream.abort_calls == 1);
}

void HeaderConfigurationAllocationFailureIsTerminal()
{
    RecordingDownstream downstream;
    int encoder_identity = 0;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        AudioDescriptor(),
        DefaultFragmentedMp4FragmentPolicy,
        &encoder_identity);
    CHECK(coordinator.BeginPriming(0) == VRREC_STATUS_OK);
    auto descriptor = VideoDescriptor();
    descriptor.codec_extradata.assign(256, std::byte {1});
    CHECK(coordinator.PublishVideoDescriptor(
              &encoder_identity,
              descriptor) == VRREC_STATUS_OK);
    CHECK(coordinator.ProducerStarted(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);

    allocation_failure::fail_on_allocation = 1;
    const auto status = coordinator.ProducerStarted(MediaStreamKind::Audio);
    allocation_failure::fail_on_allocation = 0;

    CHECK(status == VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(coordinator.State() == PreHeaderState::Failed);
    CHECK(downstream.start_calls == 0);
    CHECK(downstream.submit_calls == 0);
    CHECK(downstream.request_abort_calls == 1);
    CHECK(downstream.abort_calls == 1);
}

}

int main()
{
    DoesNotStartTheHeaderBeforeDescriptorReadiness();
    QueuesOwnedPacketsAndDrainsThemInDeterministicDtsOrder();
    AbortWakesAQueuedSubmissionWithoutWritingIt();
    EnforcesEveryPerStreamQueueLimitBeforeMutatingTheBatch();
    InvalidPreHeaderBatchTerminallyWakesExistingTickets();
    KeepsPacketsAfterTheHeaderAdmissionCutInTheLiveBacklog();
    AbortWinsAnInFlightEncoderCompletion();
    DuplicateProducerCompletionBeforeItsPeerIsTerminal();
    BothProducerCompletionsEnterTerminalFinishing();
    RejectsPacketsFromACompletedProducer();
    PublishesTheDescriptorWithoutLosingItsFirstVideoBatch();
    RejectsEveryDescriptorBatchContractBoundary();
    ValidatesBatchBehaviorBeforePrimingAndWhileRunning();
    ValidatesAudioCompletionFailureAndMuxOffsetForwarding();
    FinishBeforeDescriptorReadinessFailsAllQueuedPackets();
    AbortWinsAHeaderStartThatReturnsSuccessLate();
    AbortWinsAnInFlightPreHeaderDrain();
    StartsExactlyOnceRegardlessOfReadinessOrder();
    RejectsAThrowawayEncoderDescriptor();
    RejectsAnIncompleteVideoDescriptorBeforeHeaderReadiness();
    HeaderFailureIsTerminalAndDoesNotRetry();
    AbortBeforeReadinessPreventsHeaderStart();
    RejectsEveryInvalidPrimingContract();
    RejectsEveryInvalidVideoDescriptor();
    RejectsEveryMalformedPreHeaderPacket();
    RejectsDuplicateAndUnknownProducerLifecycleEvents();
    EncoderFailureIsForwardedOnlyWhileActive();
    DescriptorPublicationAllocationFailureIsTerminal();
    QueueOwnershipAllocationFailureIsTerminal();
    HeaderConfigurationAllocationFailureIsTerminal();
    return 0;
}
