#include "ffmpeg_aac_audio_pipeline.hpp"

#include <array>
#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <mutex>
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

constexpr auto WaitTimeout = std::chrono::seconds(2);
constexpr std::array<std::byte, 5> ExpectedAudioSpecificConfig {
    std::byte {0x11},
    std::byte {0x90},
    std::byte {0x56},
    std::byte {0xe5},
    std::byte {0x00},
};

class OneWindowCapture final : public StereoAudioCaptureSessionPort {
public:
    vrrec_status_t Start(
        const StereoAudioCaptureSessionConfig &config) noexcept override
    {
        const std::lock_guard lock(mutex_);
        ++start_calls_;
        last_config_ = config;
        return VRREC_STATUS_OK;
    }

    StereoAudioMixResult MixNext(
        std::size_t frame_count_48k,
        std::span<float> output_interleaved,
        StereoAudioMixRead &read) noexcept override
    {
        std::unique_lock lock(mutex_);
        ++mix_calls_;
        requested_frame_counts_.push_back(frame_count_48k);
        changed_.notify_all();

        if (mix_calls_ == 1) {
            if (output_interleaved.size() != frame_count_48k * 2U) {
                return StereoAudioMixResult::InvalidArgument;
            }
            for (std::size_t index = 0; index < frame_count_48k; ++index) {
                output_interleaved[index * 2U] = 0.125F;
                output_interleaved[index * 2U + 1U] = -0.125F;
            }
            read = {0, frame_count_48k, true, true, false, false};
            return StereoAudioMixResult::Mixed;
        }

        changed_.wait(lock, [this] { return aborted_; });
        return StereoAudioMixResult::Aborted;
    }

    vrrec_status_t SetRouting(
        vrrec_audio_routing_t) noexcept override
    {
        const std::lock_guard lock(mutex_);
        ++routing_calls_;
        return VRREC_STATUS_OK;
    }

    void Abort() noexcept override
    {
        {
            const std::lock_guard lock(mutex_);
            if (aborted_) {
                return;
            }
            aborted_ = true;
            ++abort_calls_;
        }
        changed_.notify_all();
    }

    bool WaitForBlockedSecondMix()
    {
        std::unique_lock lock(mutex_);
        return changed_.wait_for(
            lock,
            WaitTimeout,
            [this] { return mix_calls_ >= 2; });
    }

    struct Snapshot final {
        StereoAudioCaptureSessionConfig last_config;
        std::vector<std::size_t> requested_frame_counts;
        std::size_t start_calls;
        std::size_t routing_calls;
        std::size_t abort_calls;
        std::size_t mix_calls;
    };

    Snapshot Read() const
    {
        const std::lock_guard lock(mutex_);
        return {
            last_config_,
            requested_frame_counts_,
            start_calls_,
            routing_calls_,
            abort_calls_,
            mix_calls_,
        };
    }

private:
    mutable std::mutex mutex_;
    std::condition_variable changed_;
    StereoAudioCaptureSessionConfig last_config_ {};
    std::vector<std::size_t> requested_frame_counts_;
    std::size_t start_calls_ = 0;
    std::size_t routing_calls_ = 0;
    std::size_t abort_calls_ = 0;
    std::size_t mix_calls_ = 0;
    bool aborted_ = false;
};

class RecordingSubmission final : public EncodedMediaPacketSubmissionPort {
public:
    Mp4MuxResult SubmitBatch(
        MediaStreamKind producer,
        std::span<const EncodedMediaPacket> packets) noexcept override
    {
        const std::lock_guard lock(mutex_);
        ++submit_calls_;
        all_audio_ = all_audio_ && producer == MediaStreamKind::Audio;
        for (const auto &packet : packets) {
            all_audio_ = all_audio_ &&
                packet.stream == MediaStreamKind::Audio;
            all_payloads_nonempty_ =
                all_payloads_nonempty_ && !packet.payload.empty();
        }
        packet_count_ += packets.size();
        return Mp4MuxResult::Written;
    }

    vrrec_status_t EncoderFinished(
        MediaStreamKind stream) noexcept override
    {
        const std::lock_guard lock(mutex_);
        ++finished_calls_;
        all_audio_ = all_audio_ && stream == MediaStreamKind::Audio;
        return VRREC_STATUS_OK;
    }

    void EncoderFailed(MediaStreamKind stream) noexcept override
    {
        const std::lock_guard lock(mutex_);
        ++failed_calls_;
        all_audio_ = all_audio_ && stream == MediaStreamKind::Audio;
    }

    struct Snapshot final {
        std::size_t submit_calls;
        std::size_t packet_count;
        std::size_t finished_calls;
        std::size_t failed_calls;
        bool all_audio;
        bool all_payloads_nonempty;
    };

    Snapshot Read() const
    {
        const std::lock_guard lock(mutex_);
        return {
            submit_calls_,
            packet_count_,
            finished_calls_,
            failed_calls_,
            all_audio_,
            all_payloads_nonempty_,
        };
    }

private:
    mutable std::mutex mutex_;
    std::size_t submit_calls_ = 0;
    std::size_t packet_count_ = 0;
    std::size_t finished_calls_ = 0;
    std::size_t failed_calls_ = 0;
    bool all_audio_ = true;
    bool all_payloads_nonempty_ = true;
};

StereoAudioCaptureSessionConfig CaptureConfig()
{
    return {"desktop-id", "microphone-id", 12'345'678};
}

void CheckExactDescriptor(const AacStreamDescriptor &descriptor)
{
    CHECK(descriptor.packet_time_base == MicrosecondPacketTimeBase);
    CHECK(descriptor.sample_rate == 48'000);
    CHECK(descriptor.channel_count == 2);
    CHECK(descriptor.frame_size == 1'024);
    CHECK(descriptor.initial_padding_samples == 1'024);
    CHECK(descriptor.profile == AacProfile::LowComplexity);
    CHECK(descriptor.channel_layout == AudioChannelLayout::Stereo);
    CHECK(descriptor.packet_format == AacPacketFormat::RawAccessUnit);
    CHECK(descriptor.bitrate_bits_per_second == 192'000);
    CHECK(
        descriptor.codec_extradata ==
        std::vector<std::byte>(
            ExpectedAudioSpecificConfig.begin(),
            ExpectedAudioSpecificConfig.end()));
}

void CreatesOwnedPipelineWithoutStartingAdjacentPorts()
{
    OneWindowCapture capture;
    RecordingSubmission submission;

    auto creation = CreateFfmpegAacAudioPipeline(capture, submission);
    CHECK(creation.status == VRREC_STATUS_OK);
    CHECK(creation.pipeline != nullptr);
    CHECK(creation.descriptor.has_value());
    CheckExactDescriptor(*creation.descriptor);

    auto descriptor = std::move(*creation.descriptor);
    creation.descriptor.reset();
    creation.pipeline.reset();
    CheckExactDescriptor(descriptor);

    const auto capture_snapshot = capture.Read();
    CHECK(capture_snapshot.start_calls == 0);
    CHECK(capture_snapshot.mix_calls == 0);
    CHECK(capture_snapshot.routing_calls == 0);
    CHECK(capture_snapshot.abort_calls == 0);
    const auto submission_snapshot = submission.Read();
    CHECK(submission_snapshot.submit_calls == 0);
    CHECK(submission_snapshot.finished_calls == 0);
    CHECK(submission_snapshot.failed_calls == 0);
}

void FailsClosedWhenCompositionAllocationFails()
{
    OneWindowCapture capture;
    RecordingSubmission submission;

    auto creation = CreateFfmpegAacAudioPipelineForTesting(
        capture,
        submission,
        FfmpegAacAudioPipelineFailurePoint::AllocatePipeline);
    CHECK(creation.status == VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(creation.pipeline == nullptr);
    CHECK(!creation.descriptor.has_value());

    const auto capture_snapshot = capture.Read();
    CHECK(capture_snapshot.start_calls == 0);
    CHECK(capture_snapshot.mix_calls == 0);
    CHECK(capture_snapshot.abort_calls == 0);
    const auto submission_snapshot = submission.Read();
    CHECK(submission_snapshot.submit_calls == 0);
    CHECK(submission_snapshot.finished_calls == 0);
    CHECK(submission_snapshot.failed_calls == 0);
}

void RejectsAnUnknownCompositionFailurePointWithoutSideEffects()
{
    OneWindowCapture capture;
    RecordingSubmission submission;

    auto creation = CreateFfmpegAacAudioPipelineForTesting(
        capture,
        submission,
        static_cast<FfmpegAacAudioPipelineFailurePoint>(255));
    CHECK(creation.status == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(creation.pipeline == nullptr);
    CHECK(!creation.descriptor.has_value());

    const auto capture_snapshot = capture.Read();
    CHECK(capture_snapshot.start_calls == 0);
    CHECK(capture_snapshot.mix_calls == 0);
    CHECK(capture_snapshot.abort_calls == 0);
    const auto submission_snapshot = submission.Read();
    CHECK(submission_snapshot.submit_calls == 0);
    CHECK(submission_snapshot.finished_calls == 0);
    CHECK(submission_snapshot.failed_calls == 0);
}

void FlushesRealAacPacketsWhenTheSessionStops()
{
    OneWindowCapture capture;
    RecordingSubmission submission;
    auto creation = CreateFfmpegAacAudioPipeline(capture, submission);
    CHECK(creation.status == VRREC_STATUS_OK);
    CHECK(creation.pipeline != nullptr);
    CHECK(creation.descriptor.has_value());

    auto &session = creation.pipeline->Session();
    CHECK(
        session.Start(CaptureConfig(), creation.descriptor->frame_size) ==
        VRREC_STATUS_OK);
    CHECK(capture.WaitForBlockedSecondMix());

    const auto before_stop = submission.Read();
    CHECK(before_stop.submit_calls == 0);
    CHECK(before_stop.packet_count == 0);

    CHECK(session.RequestStop() == VRREC_STATUS_OK);
    CHECK(session.Join() == StereoAudioEncodingWorkerResult::Stopped);

    const auto statistics = session.Statistics();
    const auto capture_snapshot = capture.Read();
    const auto submission_snapshot = submission.Read();
    CHECK(capture_snapshot.last_config.desktop_endpoint_id_utf8 == "desktop-id");
    CHECK(
        capture_snapshot.last_config.microphone_endpoint_id_utf8 ==
        "microphone-id");
    CHECK(capture_snapshot.last_config.session_start_qpc_100ns == 12'345'678);
    CHECK(
        capture_snapshot.requested_frame_counts ==
        std::vector<std::size_t>({1'024, 1'024}));
    CHECK(capture_snapshot.abort_calls == 1);
    CHECK(statistics.submitted_frame_count == 1'024);
    CHECK(submission_snapshot.submit_calls == 1);
    CHECK(submission_snapshot.packet_count > 0);
    CHECK(
        statistics.muxed_packet_count ==
        submission_snapshot.packet_count);
    CHECK(submission_snapshot.finished_calls == 1);
    CHECK(submission_snapshot.failed_calls == 0);
    CHECK(submission_snapshot.all_audio);
    CHECK(submission_snapshot.all_payloads_nonempty);
}

void AbortsAndJoinsBeforeDestroyingAnActiveEncoder()
{
    OneWindowCapture capture;
    RecordingSubmission submission;
    auto creation = CreateFfmpegAacAudioPipeline(capture, submission);
    CHECK(creation.status == VRREC_STATUS_OK);
    CHECK(creation.pipeline != nullptr);
    CHECK(creation.descriptor.has_value());

    CHECK(
        creation.pipeline->Session().Start(
            CaptureConfig(),
            creation.descriptor->frame_size) == VRREC_STATUS_OK);
    CHECK(capture.WaitForBlockedSecondMix());
    creation.pipeline.reset();

    const auto capture_snapshot = capture.Read();
    const auto submission_snapshot = submission.Read();
    CHECK(capture_snapshot.abort_calls == 1);
    CHECK(submission_snapshot.finished_calls == 0);
    CHECK(submission_snapshot.failed_calls == 1);
    CHECK(submission_snapshot.all_audio);
}

} // namespace

int main()
{
    CreatesOwnedPipelineWithoutStartingAdjacentPorts();
    FailsClosedWhenCompositionAllocationFails();
    RejectsAnUnknownCompositionFailurePointWithoutSideEffects();
    FlushesRealAacPacketsWhenTheSessionStops();
    AbortsAndJoinsBeforeDestroyingAnActiveEncoder();
    return 0;
}
