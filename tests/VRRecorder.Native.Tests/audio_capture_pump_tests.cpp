#include "audio_capture_pump.hpp"

#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstddef>
#include <cstdlib>
#include <future>
#include <iostream>
#include <mutex>
#include <string>
#include <utility>
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

class ScriptedAudioCaptureSource final
    : public vrrecorder::native::AudioCaptureSource {
public:
    ScriptedAudioCaptureSource(
        std::vector<vrrecorder::native::CapturedStereoPacket48k> packets,
        vrrecorder::native::AudioCaptureReadResult terminal_result)
        : packets_(std::move(packets)),
          terminal_result_(terminal_result)
    {
    }

    vrrecorder::native::AudioCaptureStartResult Start(
        const vrrecorder::native::AudioCaptureSourceConfig &config)
        noexcept override
    {
        config_ = config;
        ++start_count_;
        return vrrecorder::native::AudioCaptureStartResult::Ready;
    }

    vrrecorder::native::AudioCaptureReadResult Read(
        vrrecorder::native::CapturedStereoPacket48k &packet)
        noexcept override
    {
        if (next_packet_ == packets_.size()) {
            return terminal_result_;
        }

        packet = packets_[next_packet_++];
        return vrrecorder::native::AudioCaptureReadResult::Packet;
    }

    void Abort() noexcept override
    {
        ++abort_count_;
    }

    const vrrecorder::native::AudioCaptureSourceConfig &Config() const
        noexcept
    {
        return config_;
    }

    std::size_t StartCount() const noexcept
    {
        return start_count_;
    }

    std::size_t AbortCount() const noexcept
    {
        return abort_count_;
    }

private:
    std::vector<vrrecorder::native::CapturedStereoPacket48k> packets_;
    vrrecorder::native::AudioCaptureReadResult terminal_result_;
    vrrecorder::native::AudioCaptureSourceConfig config_ {};
    std::size_t next_packet_ = 0;
    std::size_t start_count_ = 0;
    std::size_t abort_count_ = 0;
};

class BlockingAudioCaptureSource final
    : public vrrecorder::native::AudioCaptureSource {
public:
    vrrecorder::native::AudioCaptureStartResult Start(
        const vrrecorder::native::AudioCaptureSourceConfig &) noexcept override
    {
        return vrrecorder::native::AudioCaptureStartResult::Ready;
    }

    vrrecorder::native::AudioCaptureReadResult Read(
        vrrecorder::native::CapturedStereoPacket48k &) noexcept override
    {
        std::unique_lock lock(mutex_);
        read_entered_ = true;
        changed_.notify_all();
        changed_.wait(lock, [&] { return aborted_; });
        return vrrecorder::native::AudioCaptureReadResult::Aborted;
    }

    void Abort() noexcept override
    {
        {
            const std::lock_guard lock(mutex_);
            aborted_ = true;
            ++abort_count_;
        }

        changed_.notify_all();
    }

    void WaitUntilReadEntered()
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [&] { return read_entered_; });
    }

    std::size_t AbortCount() const noexcept
    {
        const std::lock_guard lock(mutex_);
        return abort_count_;
    }

private:
    mutable std::mutex mutex_;
    std::condition_variable changed_;
    bool read_entered_ = false;
    bool aborted_ = false;
    std::size_t abort_count_ = 0;
};

void CoalescesSourcePacketsOnTheShared48KhzTimeline()
{
    const auto first = ConstantStereo(160, 0.25F);
    const auto second = ConstantStereo(320, 0.5F);
    ScriptedAudioCaptureSource source(
        {
            {0, 1'000, 1'000'000, 160, first, false, false},
            {160, 1'160, 1'033'333, 320, second, false, false},
        },
        vrrecorder::native::AudioCaptureReadResult::DeviceLost);
    vrrecorder::native::StereoCaptureTimeline timeline(960);
    const vrrecorder::native::AudioCaptureSourceConfig config {
        vrrecorder::native::AudioCaptureRole::DesktopLoopback,
        "desktop-endpoint",
        900'000,
    };
    vrrecorder::native::AudioCapturePump pump(source, config, timeline);

    CHECK(pump.Run() ==
          vrrecorder::native::AudioCapturePumpResult::DeviceLost);
    CHECK(source.StartCount() == 1);
    CHECK(source.Config().role ==
          vrrecorder::native::AudioCaptureRole::DesktopLoopback);
    CHECK(source.Config().endpoint_id_utf8 == "desktop-endpoint");
    CHECK(source.Config().session_start_qpc_100ns == 900'000);
    CHECK(timeline.FramePosition() == 0);

    std::vector<float> output(480U * 2U, -1.0F);
    vrrecorder::native::AudioTimelineRead read {};
    CHECK(timeline.WaitRead(480, output, read) ==
          vrrecorder::native::AudioTimelineResult::Ready);
    CHECK(read.start_frame_48k == 0);
    CHECK(read.input_available);
    CHECK(!read.underrun);
    for (std::size_t frame = 0; frame < 480; ++frame) {
        const auto expected = frame < 160 ? 0.25F : 0.5F;
        CHECK(NearlyEqual(output[frame * 2U], expected));
        CHECK(NearlyEqual(output[frame * 2U + 1U], expected));
    }
}

void ExpandsSilentPacketsWithoutLosingTheirClockBoundary()
{
    const auto captured = ConstantStereo(320, 0.75F);
    ScriptedAudioCaptureSource source(
        {
            {0, 50'000, 2'000'000, 160, {}, true, false},
            {160, 50'160, 2'033'333, 320, captured, false, false},
        },
        vrrecorder::native::AudioCaptureReadResult::DeviceLost);
    vrrecorder::native::StereoCaptureTimeline timeline(960);
    const vrrecorder::native::AudioCaptureSourceConfig config {
        vrrecorder::native::AudioCaptureRole::Microphone,
        "microphone-endpoint",
        1'900'000,
    };
    vrrecorder::native::AudioCapturePump pump(source, config, timeline);

    CHECK(pump.Run() ==
          vrrecorder::native::AudioCapturePumpResult::DeviceLost);
    std::vector<float> output(480U * 2U, -1.0F);
    vrrecorder::native::AudioTimelineRead read {};
    CHECK(timeline.WaitRead(480, output, read) ==
          vrrecorder::native::AudioTimelineResult::Ready);
    CHECK(!read.underrun);
    for (std::size_t frame = 0; frame < 480; ++frame) {
        const auto expected = frame < 160 ? 0.0F : 0.75F;
        CHECK(NearlyEqual(output[frame * 2U], expected));
        CHECK(NearlyEqual(output[frame * 2U + 1U], expected));
    }
}

void AbortReleasesTheSourcePumpAndTimelineReader()
{
    using namespace std::chrono_literals;

    BlockingAudioCaptureSource source;
    vrrecorder::native::StereoCaptureTimeline timeline(960);
    const vrrecorder::native::AudioCaptureSourceConfig config {
        vrrecorder::native::AudioCaptureRole::Microphone,
        "",
        1'000'000,
    };
    vrrecorder::native::AudioCapturePump pump(source, config, timeline);
    auto running = std::async(std::launch::async, [&] {
        return pump.Run();
    });
    source.WaitUntilReadEntered();

    std::vector<float> output(480U * 2U, -1.0F);
    vrrecorder::native::AudioTimelineRead read {};
    auto reading = std::async(std::launch::async, [&] {
        return timeline.WaitRead(480, output, read);
    });
    CHECK(reading.wait_for(20ms) == std::future_status::timeout);

    pump.Abort();
    CHECK(running.wait_for(1s) == std::future_status::ready);
    CHECK(running.get() ==
          vrrecorder::native::AudioCapturePumpResult::Aborted);
    CHECK(reading.wait_for(1s) == std::future_status::ready);
    CHECK(reading.get() ==
          vrrecorder::native::AudioTimelineResult::Aborted);
    CHECK(source.AbortCount() == 1);
    CHECK(timeline.FramePosition() == 0);
}

}

int main()
{
    CoalescesSourcePacketsOnTheShared48KhzTimeline();
    ExpandsSilentPacketsWithoutLosingTheirClockBoundary();
    AbortReleasesTheSourcePumpAndTimelineReader();
    return 0;
}
