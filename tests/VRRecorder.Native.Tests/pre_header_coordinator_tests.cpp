#include "pre_header_coordinator.hpp"

#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <iostream>
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
        MediaStreamKind,
        std::span<const EncodedMediaPacket>) noexcept override
    {
        ++submit_calls;
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
        ++request_abort_calls;
    }

    void Abort() noexcept override
    {
        ++abort_calls;
    }

    vrrec_status_t start_status = VRREC_STATUS_OK;
    FragmentedMp4StreamConfiguration last_configuration {};
    std::size_t start_calls = 0;
    std::size_t submit_calls = 0;
    std::size_t request_abort_calls = 0;
    std::size_t abort_calls = 0;
};

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
    CHECK(coordinator.State() == PreHeaderState::DrainingPreHeader);
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

}

int main()
{
    DoesNotStartTheHeaderBeforeDescriptorReadiness();
    StartsExactlyOnceRegardlessOfReadinessOrder();
    RejectsAThrowawayEncoderDescriptor();
    RejectsAnIncompleteVideoDescriptorBeforeHeaderReadiness();
    HeaderFailureIsTerminalAndDoesNotRetry();
    AbortBeforeReadinessPreventsHeaderStart();
    return 0;
}
