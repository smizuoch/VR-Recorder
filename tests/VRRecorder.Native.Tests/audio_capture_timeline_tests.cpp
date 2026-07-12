#include "audio_capture_timeline.hpp"

#include <chrono>
#include <cmath>
#include <cstddef>
#include <cstdlib>
#include <future>
#include <iostream>
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
    vrrecorder::native::AudioTimelineRead read {};
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
    CHECK(timeline.FramePosition() == 0);
    CHECK(timeline.BufferedFrames() == 0);
    for (const auto sample : output) {
        CHECK(NearlyEqual(sample, -1.0F));
    }

    CHECK(timeline.WaitRead(480, output, read) ==
          vrrecorder::native::AudioTimelineResult::Aborted);
    CHECK(timeline.FramePosition() == 0);
}

}

int main()
{
    JoinsArbitraryPacketBoundariesOnOne48KhzTimeline();
    DeviceLossWakesAReaderAndCompletesWithSilence();
    AbortWakesAReaderWithoutAdvancingTheTimeline();
    return 0;
}
