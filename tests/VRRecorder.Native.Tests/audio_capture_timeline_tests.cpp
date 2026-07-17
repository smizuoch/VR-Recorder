#include "audio_capture_timeline.hpp"

#include <chrono>
#include <cmath>
#include <cstddef>
#include <cstdlib>
#include <future>
#include <iostream>
#include <limits>
#include <vector>

namespace {

constexpr float kTolerance = 0.00001F;

#define CHECK(condition)                                                        \
    do {                                                                        \
        if (!(condition)) {                                                     \
            std::cerr << "check failed at " << __FILE__ << ':' << __LINE__      \
                      << ": " #condition << '\n';                              \
            std::abort();                                                       \
        }                                                                       \
    } while (false)

bool NearlyEqual(float actual, float expected)
{
    return std::fabs(actual - expected) <= kTolerance;
}

std::vector<float> ConstantStereo(
    std::size_t frame_count,
    float sample)
{
    return std::vector<float>(frame_count * 2U, sample);
}

void JoinsArbitraryPacketBoundariesOnOne48KhzTimeline()
{
    vrrecorder::native::StereoCaptureTimeline timeline(960);
    const auto first = ConstantStereo(160, 0.25F);
    const auto second = ConstantStereo(320, 0.5F);
    const vrrecorder::native::NormalizedStereoPacket first_packet {
        0,
        {0, 1'000'000, 10'000'000},
        first,
        false,
    };
    const vrrecorder::native::NormalizedStereoPacket second_packet {
        160,
        {160, 1'033'333, 10'000'000},
        second,
        false,
    };

    CHECK(timeline.Push(first_packet) ==
          vrrecorder::native::AudioTimelineResult::Ready);
    CHECK(timeline.Push(second_packet) ==
          vrrecorder::native::AudioTimelineResult::Ready);

    std::vector<float> output(480U * 2U, -1.0F);
    vrrecorder::native::AudioTimelineRead read {};
    CHECK(timeline.WaitRead(480, output, read) ==
          vrrecorder::native::AudioTimelineResult::Ready);
    CHECK(read.start_frame_48k == 0);
    CHECK(read.input_available);
    CHECK(!read.underrun);
    CHECK(timeline.FramePosition() == 480);
    CHECK(timeline.BufferedFrames() == 0);
    for (std::size_t frame = 0; frame < 480; ++frame) {
        const auto expected = frame < 160 ? 0.25F : 0.5F;
        CHECK(NearlyEqual(output[frame * 2U], expected));
        CHECK(NearlyEqual(output[frame * 2U + 1U], expected));
    }
}

void DeviceLossWakesAReaderAndCompletesWithSilence()
{
    using namespace std::chrono_literals;

    vrrecorder::native::StereoCaptureTimeline timeline(960);
    const auto captured = ConstantStereo(160, 0.25F);
    const vrrecorder::native::NormalizedStereoPacket packet {
        0,
        {0, 1'000'000, 10'000'000},
        captured,
        false,
    };
    CHECK(timeline.Push(packet) ==
          vrrecorder::native::AudioTimelineResult::Ready);
    std::vector<float> output(480U * 2U, -1.0F);
    vrrecorder::native::AudioTimelineRead read {};
    std::promise<void> reader_started;
    auto reader_started_future = reader_started.get_future();
    auto waiting = std::async(std::launch::async, [&] {
        reader_started.set_value();
        return timeline.WaitRead(480, output, read);
    });
    reader_started_future.wait();

    CHECK(waiting.wait_for(20ms) == std::future_status::timeout);
    CHECK(timeline.SetAvailable(false, 160) ==
          vrrecorder::native::AudioTimelineResult::Ready);
    CHECK(waiting.wait_for(1s) == std::future_status::ready);
    CHECK(waiting.get() ==
          vrrecorder::native::AudioTimelineResult::Ready);

    CHECK(read.start_frame_48k == 0);
    CHECK(!read.input_available);
    CHECK(read.underrun);
    CHECK(timeline.FramePosition() == 480);
    for (std::size_t frame = 0; frame < 480; ++frame) {
        const auto expected = frame < 160 ? 0.25F : 0.0F;
        CHECK(NearlyEqual(output[frame * 2U], expected));
        CHECK(NearlyEqual(output[frame * 2U + 1U], expected));
    }
}

void AbortWakesAReaderWithoutAdvancingTheTimeline()
{
    using namespace std::chrono_literals;

    vrrecorder::native::StereoCaptureTimeline timeline(960);
    std::vector<float> output(480U * 2U, -1.0F);
    vrrecorder::native::AudioTimelineRead read {99, true, true};
    std::promise<void> reader_started;
    auto reader_started_future = reader_started.get_future();
    auto waiting = std::async(std::launch::async, [&] {
        reader_started.set_value();
        return timeline.WaitRead(480, output, read);
    });
    reader_started_future.wait();

    CHECK(waiting.wait_for(20ms) == std::future_status::timeout);
    timeline.Abort();
    timeline.Abort();
    CHECK(waiting.wait_for(1s) == std::future_status::ready);
    CHECK(waiting.get() ==
          vrrecorder::native::AudioTimelineResult::Aborted);
    CHECK(read.start_frame_48k == 0);
    CHECK(!read.input_available);
    CHECK(!read.underrun);
    CHECK(timeline.FramePosition() == 0);
    CHECK(timeline.BufferedFrames() == 0);
    for (const auto sample : output) {
        CHECK(NearlyEqual(sample, -1.0F));
    }

    CHECK(timeline.WaitRead(480, output, read) ==
          vrrecorder::native::AudioTimelineResult::Aborted);
    CHECK(timeline.FramePosition() == 0);
}

void ReportsOnlyGapsInsideTheCurrentReadWindow()
{
    vrrecorder::native::StereoCaptureTimeline timeline(1'200);
    const auto first = ConstantStereo(480, 0.25F);
    const auto second = ConstantStereo(320, 0.5F);
    CHECK(timeline.Push({
              0,
              {0, 1'000'000, 10'000'000},
              first,
              false,
          }) == vrrecorder::native::AudioTimelineResult::Ready);
    CHECK(timeline.Push({
              640,
              {640, 1'100'000, 10'000'000},
              second,
              false,
          }) == vrrecorder::native::AudioTimelineResult::Ready);

    std::vector<float> output(480U * 2U, -1.0F);
    vrrecorder::native::AudioTimelineRead first_read {};
    CHECK(timeline.WaitRead(480, output, first_read) ==
          vrrecorder::native::AudioTimelineResult::Ready);
    CHECK(first_read.start_frame_48k == 0);
    CHECK(!first_read.underrun);
    for (const auto sample : output) {
        CHECK(NearlyEqual(sample, 0.25F));
    }

    vrrecorder::native::AudioTimelineRead second_read {};
    CHECK(timeline.WaitRead(480, output, second_read) ==
          vrrecorder::native::AudioTimelineResult::Ready);
    CHECK(second_read.start_frame_48k == 480);
    CHECK(second_read.underrun);
    for (std::size_t frame = 0; frame < 480; ++frame) {
        const auto expected = frame < 160 ? 0.0F : 0.5F;
        CHECK(NearlyEqual(output[frame * 2U], expected));
        CHECK(NearlyEqual(output[frame * 2U + 1U], expected));
    }
}

void RecoveryKeepsSilenceUntilItsExactEffectiveFrame()
{
    vrrecorder::native::StereoCaptureTimeline timeline(960);
    const auto initial = ConstantStereo(160, 0.25F);
    CHECK(timeline.Push({
              0,
              {10'000, 1'000'000, 10'000'000},
              initial,
              false,
          }) == vrrecorder::native::AudioTimelineResult::Ready);
    CHECK(timeline.SetAvailable(false, 160) ==
          vrrecorder::native::AudioTimelineResult::Ready);
    std::vector<float> output(480U * 2U, -1.0F);
    vrrecorder::native::AudioTimelineRead loss_read {};
    CHECK(timeline.WaitRead(480, output, loss_read) ==
          vrrecorder::native::AudioTimelineResult::Ready);

    CHECK(timeline.SetAvailable(true, 640) ==
          vrrecorder::native::AudioTimelineResult::Ready);
    const auto too_early = ConstantStereo(160, 0.9F);
    CHECK(timeline.Push({
              480,
              {0, 1'050'000, 10'000'000},
              too_early,
              false,
          }) == vrrecorder::native::AudioTimelineResult::InvalidPacket);
    const auto recovered = ConstantStereo(320, 0.75F);
    CHECK(timeline.Push({
              640,
              {0, 1'100'000, 10'000'000},
              recovered,
              false,
          }) == vrrecorder::native::AudioTimelineResult::Ready);

    vrrecorder::native::AudioTimelineRead recovery_read {};
    CHECK(timeline.WaitRead(480, output, recovery_read) ==
          vrrecorder::native::AudioTimelineResult::Ready);
    CHECK(recovery_read.start_frame_48k == 480);
    CHECK(recovery_read.input_available);
    CHECK(recovery_read.underrun);
    CHECK(timeline.FramePosition() == 960);
    for (std::size_t frame = 0; frame < 480; ++frame) {
        const auto expected = frame < 160 ? 0.0F : 0.75F;
        CHECK(NearlyEqual(output[frame * 2U], expected));
        CHECK(NearlyEqual(output[frame * 2U + 1U], expected));
    }
}

void RejectsDuplicateDeviceClockAnchorsWithoutMutation()
{
    vrrecorder::native::StereoCaptureTimeline timeline(480);
    const auto first = ConstantStereo(160, 0.25F);
    CHECK(timeline.Push({
              0,
              {100, 1'000'000, 10'000'000},
              first,
              false,
          }) == vrrecorder::native::AudioTimelineResult::Ready);
    const auto duplicate = ConstantStereo(160, 0.9F);
    CHECK(timeline.Push({
              160,
              {100, 1'010'000, 10'000'000},
              duplicate,
              false,
          }) == vrrecorder::native::AudioTimelineResult::InvalidPacket);
    const auto second = ConstantStereo(160, 0.5F);
    CHECK(timeline.Push({
              160,
              {260, 1'020'000, 10'000'000},
              second,
              false,
          }) == vrrecorder::native::AudioTimelineResult::Ready);

    std::vector<float> output(320U * 2U, -1.0F);
    vrrecorder::native::AudioTimelineRead read {};
    CHECK(timeline.WaitRead(320, output, read) ==
          vrrecorder::native::AudioTimelineResult::Ready);
    CHECK(!read.underrun);
    for (std::size_t frame = 0; frame < 320; ++frame) {
        const auto expected = frame < 160 ? 0.25F : 0.5F;
        CHECK(NearlyEqual(output[frame * 2U], expected));
        CHECK(NearlyEqual(output[frame * 2U + 1U], expected));
    }
}

void RejectsMalformedPacketsClockRegressionAndCapacityFailures()
{
    using vrrecorder::native::AudioTimelineResult;
    using vrrecorder::native::NormalizedStereoPacket;
    using vrrecorder::native::StereoCaptureTimeline;

    const auto stereo = ConstantStereo(2, 0.25F);
    const std::vector<float> odd_samples {0.25F, 0.25F, 0.25F};
    StereoCaptureTimeline validation(8);
    CHECK(validation.Push(NormalizedStereoPacket {
              0, {0, 0, 10'000'000}, {}, false}) ==
          AudioTimelineResult::InvalidPacket);
    CHECK(validation.Push(NormalizedStereoPacket {
              0, {0, 0, 10'000'000}, odd_samples, false}) ==
          AudioTimelineResult::InvalidPacket);
    CHECK(validation.Push(NormalizedStereoPacket {
              std::numeric_limits<std::uint64_t>::max(),
              {0, 0, 10'000'000},
              stereo,
              false}) == AudioTimelineResult::InvalidPacket);
    CHECK(validation.Push(NormalizedStereoPacket {
              0, {0, -1, 10'000'000}, stereo, false}) ==
          AudioTimelineResult::InvalidPacket);
    CHECK(validation.Push(NormalizedStereoPacket {
              0, {0, 0, 0}, stereo, false}) ==
          AudioTimelineResult::InvalidPacket);

    StereoCaptureTimeline ordering(16);
    CHECK(ordering.Push({
              0, {100, 1'000, 10'000'000}, stereo, false}) ==
          AudioTimelineResult::Ready);
    CHECK(ordering.Push({
              1, {102, 1'100, 10'000'000}, stereo, false}) ==
          AudioTimelineResult::InvalidPacket);
    CHECK(ordering.Push({
              2, {101, 999, 10'000'000}, stereo, false}) ==
          AudioTimelineResult::InvalidPacket);
    CHECK(ordering.Push({
              2, {101, 1'100, 9'999'999}, stereo, false}) ==
          AudioTimelineResult::InvalidPacket);
    CHECK(ordering.Push({
              2, {100, 1'100, 10'000'000}, stereo, true}) ==
          AudioTimelineResult::Ready);
    CHECK(ordering.BufferedFrames() == 4);

    StereoCaptureTimeline past_read(16);
    CHECK(past_read.SetAvailable(false, 0) == AudioTimelineResult::Ready);
    std::vector<float> silence(4U, -1.0F);
    vrrecorder::native::AudioTimelineRead read {};
    CHECK(past_read.WaitRead(2, silence, read) == AudioTimelineResult::Ready);
    CHECK(past_read.Push({
              0, {1, 1, 10'000'000}, stereo, false}) ==
          AudioTimelineResult::InvalidPacket);

    StereoCaptureTimeline unavailable(16);
    CHECK(unavailable.SetAvailable(false, 1) == AudioTimelineResult::Ready);
    CHECK(unavailable.Push({
              0, {1, 1, 10'000'000}, stereo, false}) ==
          AudioTimelineResult::InvalidPacket);

    StereoCaptureTimeline recovering(16);
    CHECK(recovering.SetAvailable(false, 0) == AudioTimelineResult::Ready);
    CHECK(recovering.SetAvailable(true, 2) == AudioTimelineResult::Ready);
    CHECK(recovering.Push({
              1, {1, 1, 10'000'000}, stereo, false}) ==
          AudioTimelineResult::InvalidPacket);

    StereoCaptureTimeline overrun(1);
    CHECK(overrun.Push({
              0, {1, 1, 10'000'000}, stereo, false}) ==
          AudioTimelineResult::Overrun);

    StereoCaptureTimeline no_capacity(0);
    const auto one_frame = ConstantStereo(1, 0.25F);
    CHECK(no_capacity.Push({
              0, {1, 1, 10'000'000}, one_frame, false}) ==
          AudioTimelineResult::Overrun);

    validation.Abort();
    CHECK(validation.Push({
              0, {1, 1, 10'000'000}, one_frame, false}) ==
          AudioTimelineResult::Aborted);
}

void ValidatesReadAndAvailabilityTransitionBoundaries()
{
    using vrrecorder::native::AudioTimelineRead;
    using vrrecorder::native::AudioTimelineResult;
    using vrrecorder::native::StereoCaptureTimeline;

    StereoCaptureTimeline timeline(8);
    AudioTimelineRead read {99, true, true};
    std::vector<float> stereo(2U, -1.0F);
    std::vector<float> wrong_size(1U, -1.0F);
    CHECK(timeline.WaitRead(0, {}, read) == AudioTimelineResult::InvalidPacket);
    CHECK(timeline.WaitRead(1, wrong_size, read) ==
          AudioTimelineResult::InvalidPacket);
    CHECK(read.start_frame_48k == 0);
    CHECK(!read.input_available);
    CHECK(!read.underrun);

    CHECK(timeline.SetAvailable(true, 0) ==
          AudioTimelineResult::InvalidPacket);
    CHECK(timeline.SetAvailable(false, 2) == AudioTimelineResult::Ready);
    CHECK(timeline.SetAvailable(false, 2) == AudioTimelineResult::Ready);
    CHECK(timeline.SetAvailable(false, 3) ==
          AudioTimelineResult::InvalidPacket);
    CHECK(timeline.SetAvailable(true, 1) ==
          AudioTimelineResult::InvalidPacket);
    CHECK(timeline.SetAvailable(true, 4) == AudioTimelineResult::Ready);
    CHECK(timeline.SetAvailable(true, 5) ==
          AudioTimelineResult::InvalidPacket);
    CHECK(timeline.SetAvailable(false, 5) ==
          AudioTimelineResult::InvalidPacket);

    const auto recovered = ConstantStereo(1, 0.5F);
    CHECK(timeline.Push({
              4, {0, 1, 10'000'000}, recovered, false}) ==
          AudioTimelineResult::Ready);
    CHECK(timeline.WaitRead(1, stereo, read) == AudioTimelineResult::Ready);
    CHECK(read.input_available);
    CHECK(read.underrun);
    CHECK(timeline.FramePosition() == 1);
    CHECK(timeline.SetAvailable(false, 0) ==
          AudioTimelineResult::InvalidPacket);

    StereoCaptureTimeline invalid_buffer(0);
    CHECK(invalid_buffer.SetAvailable(false, 0) == AudioTimelineResult::Ready);
    CHECK(invalid_buffer.WaitRead(1, stereo, read) ==
          AudioTimelineResult::InvalidPacket);

    timeline.Abort();
    CHECK(timeline.SetAvailable(false, 1) == AudioTimelineResult::Aborted);
}

}

int main()
{
    JoinsArbitraryPacketBoundariesOnOne48KhzTimeline();
    DeviceLossWakesAReaderAndCompletesWithSilence();
    AbortWakesAReaderWithoutAdvancingTheTimeline();
    ReportsOnlyGapsInsideTheCurrentReadWindow();
    RecoveryKeepsSilenceUntilItsExactEffectiveFrame();
    RejectsDuplicateDeviceClockAnchorsWithoutMutation();
    RejectsMalformedPacketsClockRegressionAndCapacityFailures();
    ValidatesReadAndAvailabilityTransitionBoundaries();
    return 0;
}
