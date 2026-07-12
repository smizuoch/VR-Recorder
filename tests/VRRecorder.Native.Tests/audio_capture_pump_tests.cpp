#include "audio_capture_input_runner.hpp"
#include "audio_capture_pump.hpp"

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <memory>
#include <span>
#include <string>
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

struct ScriptedRead final {
    AudioCaptureReadResult result = AudioCaptureReadResult::Failed;
    std::uint64_t start_frame_48k = 0;
    std::uint64_t device_position = 0;
    std::int64_t qpc_100ns = 0;
    std::size_t frame_count_48k = 0;
    std::vector<float> samples;
    bool silent = false;
    bool discontinuity = false;
    std::uint64_t effective_frame_48k = 0;
};

class FakeAudioCaptureSource final : public AudioCaptureSource {
public:
    vrrec_status_t Start(
        const AudioCaptureSourceConfig &config) noexcept override
    {
        ++start_calls;
        started_role = config.role;
        endpoint = config.endpoint_id_utf8;
        session_start_qpc_100ns = config.session_start_qpc_100ns;
        return start_status;
    }

    AudioCaptureRead Read() noexcept override
    {
        if (next_read >= reads.size()) {
            return {};
        }

        auto &script = reads[next_read++];
        return AudioCaptureRead {
            script.result,
            {
                script.start_frame_48k,
                script.device_position,
                script.qpc_100ns,
                script.frame_count_48k,
                std::span<const float>(script.samples),
                script.silent,
                script.discontinuity,
            },
            script.effective_frame_48k,
        };
    }

    void Abort() noexcept override
    {
        ++abort_calls;
    }

    vrrec_status_t start_status = VRREC_STATUS_OK;
    std::vector<ScriptedRead> reads;
    std::size_t next_read = 0;
    int start_calls = 0;
    int abort_calls = 0;
    AudioCaptureRole started_role = AudioCaptureRole::Microphone;
    std::string endpoint;
    std::int64_t session_start_qpc_100ns = 0;
};

class FailingRecoverySourceProvider final
    : public AudioCaptureSourceProvider {
public:
    explicit FailingRecoverySourceProvider(
        std::unique_ptr<AudioCaptureSource> initial)
        : initial_(std::move(initial))
    {
    }

    vrrec_status_t Create(
        std::unique_ptr<AudioCaptureSource> &source) noexcept override
    {
        ++create_calls;
        if (initial_ != nullptr) {
            source = std::move(initial_);
            return VRREC_STATUS_OK;
        }

        source.reset();
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    }

    std::size_t create_calls = 0;

private:
    std::unique_ptr<AudioCaptureSource> initial_;
};

class RecordingRecoveryWaiter final : public AudioCaptureRecoveryWaiter {
public:
    bool Wait(std::chrono::milliseconds duration) noexcept override
    {
        ++wait_calls;
        total_wait += duration;
        return true;
    }

    std::size_t wait_calls = 0;
    std::chrono::milliseconds total_wait {0};
};

AudioCaptureSourceConfig Config()
{
    return {
        AudioCaptureRole::DesktopLoopback,
        "{render-endpoint}",
        900'000,
    };
}

void StartsAndForwardsAClockedStereoPacket()
{
    FakeAudioCaptureSource source;
    source.reads.push_back({
        AudioCaptureReadResult::Packet,
        0,
        100,
        1'000'000,
        2,
        {0.25F, -0.25F, 0.5F, -0.5F},
        false,
        false,
        0,
    });
    StereoCaptureTimeline timeline(8);
    AudioCapturePump pump(source, timeline);

    CHECK(pump.Start(Config()) == VRREC_STATUS_OK);
    CHECK(source.start_calls == 1);
    CHECK(source.started_role == AudioCaptureRole::DesktopLoopback);
    CHECK(source.endpoint == "{render-endpoint}");
    CHECK(source.session_start_qpc_100ns == 900'000);
    CHECK(pump.PumpOne() == AudioCapturePumpResult::PacketAccepted);

    std::vector<float> output(4, -1.0F);
    AudioTimelineRead read {};
    CHECK(timeline.WaitRead(2, output, read) == AudioTimelineResult::Ready);
    CHECK(!read.underrun);
    CHECK(output == source.reads[0].samples);
}

void ExpandsSilentPacketsUsingTheirExplicitFrameCount()
{
    FakeAudioCaptureSource source;
    source.reads.push_back({
        AudioCaptureReadResult::Packet,
        0,
        200,
        2'000'000,
        4,
        std::vector<float>(8, 0.75F),
        true,
        false,
        0,
    });
    StereoCaptureTimeline timeline(8);
    AudioCapturePump pump(source, timeline);

    CHECK(pump.Start(Config()) == VRREC_STATUS_OK);
    CHECK(pump.PumpOne() == AudioCapturePumpResult::PacketAccepted);

    std::vector<float> output(8, -1.0F);
    AudioTimelineRead read {};
    CHECK(timeline.WaitRead(4, output, read) == AudioTimelineResult::Ready);
    CHECK(!read.underrun);
    for (const auto sample : output) {
        CHECK(sample == 0.0F);
    }
}

void AppliesLossAtTheExactFrameAndAbortsIdempotently()
{
    FakeAudioCaptureSource source;
    source.reads.push_back({
        AudioCaptureReadResult::Packet,
        0,
        300,
        3'000'000,
        2,
        {0.25F, -0.25F, 0.5F, -0.5F},
        false,
        false,
        0,
    });
    source.reads.push_back({
        AudioCaptureReadResult::DeviceLost,
        0,
        0,
        0,
        0,
        {},
        false,
        false,
        2,
    });
    StereoCaptureTimeline timeline(8);
    AudioCapturePump pump(source, timeline);

    CHECK(pump.Start(Config()) == VRREC_STATUS_OK);
    CHECK(pump.PumpOne() == AudioCapturePumpResult::PacketAccepted);
    CHECK(pump.PumpOne() == AudioCapturePumpResult::DeviceLost);

    std::vector<float> output(8, -1.0F);
    AudioTimelineRead read {};
    CHECK(timeline.WaitRead(4, output, read) == AudioTimelineResult::Ready);
    CHECK(!read.input_available);
    CHECK(read.underrun);
    CHECK(output[0] == 0.25F);
    CHECK(output[1] == -0.25F);
    CHECK(output[2] == 0.5F);
    CHECK(output[3] == -0.5F);
    for (std::size_t index = 4; index < output.size(); ++index) {
        CHECK(output[index] == 0.0F);
    }

    pump.Abort();
    pump.Abort();
    CHECK(source.abort_calls == 1);
    CHECK(timeline.WaitRead(4, output, read) == AudioTimelineResult::Aborted);
    CHECK(timeline.FramePosition() == 4);
}

void RetainsAFlaggedPacketAfterItsExplicitGap()
{
    FakeAudioCaptureSource source;
    source.reads.push_back({
        AudioCaptureReadResult::Packet,
        0,
        100,
        1'000'000,
        2,
        {0.25F, 0.25F, 0.25F, 0.25F},
        false,
        false,
        0,
    });
    source.reads.push_back({
        AudioCaptureReadResult::Packet,
        4,
        0,
        1'010'000,
        2,
        {0.75F, 0.75F, 0.75F, 0.75F},
        false,
        true,
        0,
    });
    source.reads.push_back({
        AudioCaptureReadResult::DeviceLost,
        0,
        0,
        0,
        0,
        {},
        false,
        false,
        6,
    });
    StereoCaptureTimeline timeline(12);
    AudioCapturePump pump(source, timeline);

    CHECK(pump.Start(Config()) == VRREC_STATUS_OK);
    CHECK(pump.PumpOne() == AudioCapturePumpResult::PacketAccepted);
    CHECK(pump.PumpOne() == AudioCapturePumpResult::PacketAccepted);
    CHECK(pump.PumpOne() == AudioCapturePumpResult::DeviceLost);

    std::vector<float> output(12, -1.0F);
    AudioTimelineRead read {};
    CHECK(timeline.WaitRead(6, output, read) == AudioTimelineResult::Ready);
    CHECK(read.underrun);
    CHECK(read.input_available);
    for (std::size_t frame = 0; frame < 6; ++frame) {
        const auto expected = frame < 2
            ? 0.25F
            : (frame < 4 ? 0.0F : 0.75F);
        CHECK(output[frame * 2U] == expected);
        CHECK(output[frame * 2U + 1U] == expected);
    }
}

void RecoversAReplacementSourceAtItsExactFirstFrame()
{
    FakeAudioCaptureSource lost_source;
    lost_source.reads.push_back({
        AudioCaptureReadResult::Packet,
        0,
        500,
        2'000'000,
        2,
        {0.25F, 0.25F, 0.25F, 0.25F},
        false,
        false,
        0,
    });
    lost_source.reads.push_back({
        AudioCaptureReadResult::DeviceLost,
        0,
        0,
        0,
        0,
        {},
        false,
        false,
        2,
    });
    StereoCaptureTimeline timeline(12);
    AudioCapturePump initial(lost_source, timeline);
    CHECK(initial.Start(Config()) == VRREC_STATUS_OK);
    CHECK(initial.PumpOne() == AudioCapturePumpResult::PacketAccepted);
    CHECK(initial.PumpOne() == AudioCapturePumpResult::DeviceLost);

    FakeAudioCaptureSource recovered_source;
    recovered_source.reads.push_back({
        AudioCaptureReadResult::Packet,
        4,
        0,
        2'010'000,
        2,
        {0.75F, 0.75F, 0.75F, 0.75F},
        false,
        false,
        0,
    });
    AudioCapturePump recovered(recovered_source, timeline);
    CHECK(recovered.StartRecovery(Config()) == VRREC_STATUS_OK);
    CHECK(recovered.PumpOne() == AudioCapturePumpResult::PacketAccepted);

    std::vector<float> output(12, -1.0F);
    AudioTimelineRead read {};
    CHECK(timeline.WaitRead(6, output, read) == AudioTimelineResult::Ready);
    CHECK(read.input_available);
    CHECK(read.underrun);
    for (std::size_t frame = 0; frame < 6; ++frame) {
        const auto expected = frame < 2
            ? 0.25F
            : (frame < 4 ? 0.0F : 0.75F);
        CHECK(output[frame * 2U] == expected);
        CHECK(output[frame * 2U + 1U] == expected);
    }
}

void StopsDefaultEndpointRediscoveryAfterFiveSeconds()
{
    auto initial = std::make_unique<FakeAudioCaptureSource>();
    initial->reads.push_back({
        AudioCaptureReadResult::Packet,
        0,
        100,
        3'000'000,
        2,
        {0.25F, 0.25F, 0.25F, 0.25F},
        false,
        false,
        0,
    });
    initial->reads.push_back({
        AudioCaptureReadResult::DeviceLost,
        0,
        0,
        0,
        0,
        {},
        false,
        false,
        2,
    });
    FailingRecoverySourceProvider provider(std::move(initial));
    RecordingRecoveryWaiter waiter;
    StereoCaptureTimeline timeline(12);
    AudioCaptureInputRunner runner(provider, waiter, timeline);

    CHECK(runner.Run(Config()) ==
          AudioCaptureInputResult::RecoveryTimedOut);
    CHECK(provider.create_calls == 51);
    CHECK(waiter.wait_calls == 50);
    CHECK(waiter.total_wait == std::chrono::seconds(5));

    std::vector<float> output(8, -1.0F);
    AudioTimelineRead read {};
    CHECK(timeline.WaitRead(4, output, read) == AudioTimelineResult::Ready);
    CHECK(!read.input_available);
    CHECK(read.underrun);
    for (std::size_t frame = 0; frame < 4; ++frame) {
        const auto expected = frame < 2 ? 0.25F : 0.0F;
        CHECK(output[frame * 2U] == expected);
        CHECK(output[frame * 2U + 1U] == expected);
    }
}

}

int main()
{
    StartsAndForwardsAClockedStereoPacket();
    ExpandsSilentPacketsUsingTheirExplicitFrameCount();
    AppliesLossAtTheExactFrameAndAbortsIdempotently();
    RetainsAFlaggedPacketAfterItsExplicitGap();
    RecoversAReplacementSourceAtItsExactFirstFrame();
    StopsDefaultEndpointRediscoveryAfterFiveSeconds();
    return 0;
}
