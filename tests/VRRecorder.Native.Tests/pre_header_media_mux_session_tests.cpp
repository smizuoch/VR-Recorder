#include "pre_header_media_mux_session.hpp"

#include <atomic>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
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

FragmentedMp4StreamConfiguration Configuration()
{
    return {
        {
            MicrosecondPacketTimeBase,
            1'920,
            1'080,
            H264Profile::High,
            H264PacketFormat::AvccLengthPrefixed,
            {std::byte {1}, std::byte {100}},
        },
        {
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
        },
        DefaultFragmentedMp4FragmentPolicy,
    };
}

class RecordingDownstream final
    : public MediaMuxSessionPort,
      public EncodedMediaPacketSubmissionPort {
public:
    vrrec_status_t Start(
        const FragmentedMp4StreamConfiguration &configuration)
        noexcept override
    {
        {
            const std::lock_guard lock(mutex_);
            ++start_calls_;
            last_configuration_ = configuration;
            start_entered_ = true;
        }
        changed_.notify_all();

        std::unique_lock lock(mutex_);
        if (block_start_) {
            changed_.wait(lock, [this] {
                return abort_requested_;
            });
        }
        return start_status_;
    }

    Mp4MuxResult SubmitBatch(
        MediaStreamKind,
        std::span<const EncodedMediaPacket>) noexcept override
    {
        return Mp4MuxResult::Written;
    }

    vrrec_status_t EncoderFinished(MediaStreamKind) noexcept override
    {
        return VRREC_STATUS_OK;
    }

    void EncoderFailed(MediaStreamKind) noexcept override
    {
    }

    void RequestAbort() noexcept override
    {
        {
            const std::lock_guard lock(mutex_);
            ++request_abort_calls_;
            abort_requested_ = true;
        }
        changed_.notify_all();
    }

    void Abort() noexcept override
    {
        const std::lock_guard lock(mutex_);
        ++abort_calls_;
    }

    std::int64_t AudioVideoOffsetMicroseconds() const noexcept override
    {
        return -4'321;
    }

    void SetStartStatus(vrrec_status_t status) noexcept
    {
        start_status_ = status;
    }

    void BlockStart() noexcept
    {
        block_start_ = true;
    }

    void WaitForStart()
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [this] {
            return start_entered_;
        });
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

    FragmentedMp4StreamConfiguration LastConfiguration() const
    {
        const std::lock_guard lock(mutex_);
        return last_configuration_;
    }

private:
    mutable std::mutex mutex_;
    std::condition_variable changed_;
    FragmentedMp4StreamConfiguration last_configuration_ {};
    vrrec_status_t start_status_ = VRREC_STATUS_OK;
    std::size_t start_calls_ = 0;
    std::size_t request_abort_calls_ = 0;
    std::size_t abort_calls_ = 0;
    bool block_start_ = false;
    bool start_entered_ = false;
    bool abort_requested_ = false;
};

void CheckSameConfiguration(
    const FragmentedMp4StreamConfiguration &actual,
    const FragmentedMp4StreamConfiguration &expected)
{
    CHECK(actual.video.packet_time_base == expected.video.packet_time_base);
    CHECK(actual.video.width == expected.video.width);
    CHECK(actual.video.height == expected.video.height);
    CHECK(actual.video.profile == expected.video.profile);
    CHECK(actual.video.packet_format == expected.video.packet_format);
    CHECK(actual.video.codec_extradata == expected.video.codec_extradata);
    CHECK(actual.audio.packet_time_base == expected.audio.packet_time_base);
    CHECK(actual.audio.sample_rate == expected.audio.sample_rate);
    CHECK(actual.audio.channel_count == expected.audio.channel_count);
    CHECK(actual.audio.frame_size == expected.audio.frame_size);
    CHECK(actual.audio.initial_padding_samples ==
          expected.audio.initial_padding_samples);
    CHECK(actual.audio.profile == expected.audio.profile);
    CHECK(actual.audio.channel_layout == expected.audio.channel_layout);
    CHECK(actual.audio.packet_format == expected.audio.packet_format);
    CHECK(actual.audio.codec_extradata == expected.audio.codec_extradata);
    CHECK(actual.audio.bitrate_bits_per_second ==
          expected.audio.bitrate_bits_per_second);
    CHECK(actual.fragment_policy == expected.fragment_policy);
}

void StartsCoordinatorWithTheExactCodecDescriptors()
{
    RecordingDownstream downstream;
    const auto configuration = Configuration();
    const int encoder_identity = 1;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        configuration.audio,
        configuration.fragment_policy,
        &encoder_identity);
    PreHeaderMediaMuxSession session(
        coordinator,
        99,
        &encoder_identity,
        configuration);

    CHECK(session.Start(configuration) == VRREC_STATUS_OK);
    CHECK(coordinator.State() == PreHeaderState::Running);
    CHECK(downstream.StartCalls() == 1);
    CheckSameConfiguration(downstream.LastConfiguration(), configuration);
    CHECK(session.AudioVideoOffsetMicroseconds() == -4'321);
    CHECK(session.Start(configuration) == VRREC_STATUS_INVALID_STATE);
    CHECK(downstream.StartCalls() == 1);
}

void DefersTheVideoDescriptorUntilTheHardwareEncoderProducesAPacket()
{
    RecordingDownstream downstream;
    const auto descriptor = Configuration().video;
    auto priming_configuration = Configuration();
    priming_configuration.video.codec_extradata.clear();
    const int encoder_identity = 8;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        priming_configuration.audio,
        priming_configuration.fragment_policy,
        &encoder_identity);
    PreHeaderMediaMuxSession session(
        coordinator,
        104,
        &encoder_identity,
        priming_configuration,
        false);

    CHECK(session.Start(priming_configuration) == VRREC_STATUS_OK);
    CHECK(coordinator.State() == PreHeaderState::Priming);
    CHECK(downstream.StartCalls() == 0);
    CHECK(coordinator.PublishVideoDescriptor(
              &encoder_identity,
              descriptor) == VRREC_STATUS_OK);
    CHECK(coordinator.State() == PreHeaderState::Running);
    CHECK(downstream.StartCalls() == 1);
    auto expected = priming_configuration;
    expected.video = descriptor;
    CheckSameConfiguration(downstream.LastConfiguration(), expected);
}

void RejectsAConfigurationThatDiffersFromTheWiredGraph()
{
    RecordingDownstream downstream;
    const auto configuration = Configuration();
    auto mismatched = configuration;
    mismatched.video.width += 2;
    const int encoder_identity = 2;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        configuration.audio,
        configuration.fragment_policy,
        &encoder_identity);
    PreHeaderMediaMuxSession session(
        coordinator,
        100,
        &encoder_identity,
        configuration);

    CHECK(session.Start(mismatched) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(downstream.StartCalls() == 0);
    CHECK(downstream.RequestAbortCalls() == 1);
    CHECK(downstream.AbortCalls() == 1);
}

void RejectsInvalidPrimingEpochAndEncoderIdentity()
{
    {
        RecordingDownstream downstream;
        const auto configuration = Configuration();
        const int encoder_identity = 5;
        PreHeaderCoordinator coordinator(
            downstream,
            downstream,
            configuration.audio,
            configuration.fragment_policy,
            &encoder_identity);
        PreHeaderMediaMuxSession session(
            coordinator,
            -1,
            &encoder_identity,
            configuration);

        CHECK(session.Start(configuration) ==
              VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(downstream.StartCalls() == 0);
        CHECK(downstream.AbortCalls() == 1);
    }

    RecordingDownstream downstream;
    const auto configuration = Configuration();
    const int expected_identity = 6;
    const int actual_identity = 7;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        configuration.audio,
        configuration.fragment_policy,
        &expected_identity);
    PreHeaderMediaMuxSession session(
        coordinator,
        103,
        &actual_identity,
        configuration);

    CHECK(session.Start(configuration) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(downstream.StartCalls() == 0);
    CHECK(downstream.AbortCalls() == 1);
}

void PropagatesHeaderFailureAndAbortsExactlyOnce()
{
    RecordingDownstream downstream;
    downstream.SetStartStatus(VRREC_STATUS_BACKEND_UNAVAILABLE);
    const auto configuration = Configuration();
    const int encoder_identity = 3;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        configuration.audio,
        configuration.fragment_policy,
        &encoder_identity);
    PreHeaderMediaMuxSession session(
        coordinator,
        101,
        &encoder_identity,
        configuration);

    CHECK(session.Start(configuration) == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(downstream.StartCalls() == 1);
    CHECK(downstream.RequestAbortCalls() == 1);
    CHECK(downstream.AbortCalls() == 1);
    session.Abort();
    CHECK(downstream.RequestAbortCalls() == 1);
    CHECK(downstream.AbortCalls() == 1);
}

void RequestAbortInterruptsAConcurrentHeaderStart()
{
    RecordingDownstream downstream;
    downstream.BlockStart();
    const auto configuration = Configuration();
    const int encoder_identity = 4;
    PreHeaderCoordinator coordinator(
        downstream,
        downstream,
        configuration.audio,
        configuration.fragment_policy,
        &encoder_identity);
    PreHeaderMediaMuxSession session(
        coordinator,
        102,
        &encoder_identity,
        configuration);

    std::atomic<vrrec_status_t> start_status {VRREC_STATUS_OK};
    std::thread starter([&] {
        start_status.store(session.Start(configuration));
    });
    downstream.WaitForStart();
    session.RequestAbort();
    starter.join();
    session.Abort();

    CHECK(start_status.load() == VRREC_STATUS_INVALID_STATE);
    CHECK(downstream.RequestAbortCalls() == 1);
    CHECK(downstream.AbortCalls() == 1);
}

}

int main()
{
    StartsCoordinatorWithTheExactCodecDescriptors();
    DefersTheVideoDescriptorUntilTheHardwareEncoderProducesAPacket();
    RejectsAConfigurationThatDiffersFromTheWiredGraph();
    RejectsInvalidPrimingEpochAndEncoderIdentity();
    PropagatesHeaderFailureAndAbortsExactlyOnce();
    RequestAbortInterruptsAConcurrentHeaderStart();
    return 0;
}
