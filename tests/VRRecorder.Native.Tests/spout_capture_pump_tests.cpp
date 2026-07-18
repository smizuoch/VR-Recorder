#include "spout_capture_pump.hpp"

#include <chrono>
#include <array>
#include <cmath>
#include <cstddef>
#include <condition_variable>
#include <cstdlib>
#include <deque>
#include <future>
#include <iostream>
#include <limits>
#include <memory>
#include <mutex>
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

class FakeVideoSurface final : public VideoSurface {
public:
    explicit FakeVideoSurface(
        VideoSurfaceDescriptor descriptor,
        bool has_native_handle = true) noexcept
        : descriptor_(descriptor), has_native_handle_(has_native_handle)
    {
    }

    VideoSurfaceDescriptor Descriptor() const noexcept override
    {
        return descriptor_;
    }

    void *NativeHandle() const noexcept override
    {
        return has_native_handle_ ? reinterpret_cast<void *>(1) : nullptr;
    }

    VideoSurfaceAcquireResult AcquireForRead(
        std::chrono::milliseconds) noexcept override
    {
        return VideoSurfaceAcquireResult::Acquired;
    }

    vrrec_status_t ReleaseFromRead() noexcept override
    {
        return VRREC_STATUS_OK;
    }

private:
    VideoSurfaceDescriptor descriptor_;
    bool has_native_handle_;
};

class BlockingDescriptorSurface final : public VideoSurface {
public:
    VideoSurfaceDescriptor Descriptor() const noexcept override
    {
        std::unique_lock lock(mutex_);
        descriptor_entered_ = true;
        changed_.notify_all();
        changed_.wait(lock, [this] { return release_descriptor_; });
        return {
            42,
            1'920,
            1'080,
            VRREC_SOURCE_PIXEL_FORMAT_BGRA8,
            1,
        };
    }

    void *NativeHandle() const noexcept override
    {
        return reinterpret_cast<void *>(1);
    }

    VideoSurfaceAcquireResult AcquireForRead(
        std::chrono::milliseconds) noexcept override
    {
        return VideoSurfaceAcquireResult::Acquired;
    }

    vrrec_status_t ReleaseFromRead() noexcept override
    {
        return VRREC_STATUS_OK;
    }

    void WaitForDescriptor() const
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [this] { return descriptor_entered_; });
    }

    void ReleaseDescriptor()
    {
        {
            const std::lock_guard lock(mutex_);
            release_descriptor_ = true;
        }
        changed_.notify_all();
    }

private:
    mutable std::mutex mutex_;
    mutable std::condition_variable changed_;
    mutable bool descriptor_entered_ = false;
    bool release_descriptor_ = false;
};

struct PollResult final {
    vrrec_status_t status;
    SpoutFrame frame;
};

class ScriptedSpoutBackend final : public SpoutSourceBackend {
public:
    vrrec_status_t Snapshot(
        std::vector<SpoutSenderSnapshot> &senders) override
    {
        senders.clear();
        return VRREC_STATUS_OK;
    }

    vrrec_status_t Poll(
        std::chrono::milliseconds timeout,
        SpoutFrame &frame) override
    {
        if (block_poll) {
            std::unique_lock lock(mutex);
            poll_entered = true;
            changed.notify_all();
            changed.wait(lock, [this] { return release_poll; });
        }
        if (throw_on_poll) {
            throw 1;
        }
        last_timeout = timeout;
        ++poll_calls;
        if (polls.empty()) {
            return VRREC_STATUS_TIMEOUT;
        }

        auto result = std::move(polls.front());
        polls.pop_front();
        frame = std::move(result.frame);
        return result.status;
    }

    void Abort() noexcept override
    {
        {
            const std::lock_guard lock(mutex);
            ++abort_calls;
            release_poll = true;
        }
        changed.notify_all();
    }

    void WaitForPoll()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [this] { return poll_entered; });
    }

    std::mutex mutex;
    std::condition_variable changed;
    std::deque<PollResult> polls;
    std::chrono::milliseconds last_timeout {0};
    std::size_t poll_calls = 0;
    std::size_t abort_calls = 0;
    bool throw_on_poll = false;
    bool block_poll = false;
    bool poll_entered = false;
    bool release_poll = false;
};

class RecordingGeometryEvents final : public SpoutCaptureEventSink {
public:
    void StableVideoGeometryChanged(
        std::uint32_t width,
        std::uint32_t height,
        vrrec_source_pixel_format_t pixel_format) noexcept override
    {
        ++calls;
        last_width = width;
        last_height = height;
        last_pixel_format = pixel_format;
    }

    std::size_t calls = 0;
    std::uint32_t last_width = 0;
    std::uint32_t last_height = 0;
    vrrec_source_pixel_format_t last_pixel_format =
        VRREC_SOURCE_PIXEL_FORMAT_BGRA8;
};

SpoutFrame Frame(
    const char *sender,
    std::uint64_t sequence,
    std::int64_t timestamp,
    std::uint64_t generation = 1,
    std::uint64_t adapter_luid = 42)
{
    auto frame = SpoutFrame {
        sender,
        adapter_luid,
        "gpu",
        VRREC_GPU_VENDOR_NVIDIA,
        1'920,
        1'080,
        VRREC_SOURCE_PIXEL_FORMAT_BGRA8,
        60.0,
        sequence,
        timestamp,
    };
    frame.surface = std::make_shared<FakeVideoSurface>(
        VideoSurfaceDescriptor {
            adapter_luid,
            1'920,
            1'080,
            VRREC_SOURCE_PIXEL_FORMAT_BGRA8,
            generation,
        });
    return frame;
}

SpoutFrame FrameWithGeometry(
    std::uint64_t sequence,
    std::int64_t timestamp,
    std::uint64_t generation,
    std::uint32_t width,
    std::uint32_t height,
    vrrec_source_pixel_format_t pixel_format =
        VRREC_SOURCE_PIXEL_FORMAT_BGRA8)
{
    auto frame = Frame(
        "selected",
        sequence,
        timestamp,
        generation);
    frame.width = width;
    frame.height = height;
    frame.pixel_format = pixel_format;
    frame.surface = std::make_shared<FakeVideoSurface>(
        VideoSurfaceDescriptor {
            frame.adapter_luid,
            width,
            height,
            pixel_format,
            generation,
        });
    return frame;
}

void AcceptsOnlyTheSelectedSenderIntoTheScheduler()
{
    ScriptedSpoutBackend backend;
    auto frame = Frame("selected", 10, 1'000'000);
    const auto surface = frame.surface;
    backend.polls.push_back({VRREC_STATUS_OK, std::move(frame)});
    VideoCfrScheduler scheduler;
    SpoutCapturePump pump(backend, scheduler, "selected");

    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::FrameAccepted);
    CHECK(backend.last_timeout == std::chrono::milliseconds(100));
    ScheduledVideoFrame output {};
    CHECK(scheduler.Schedule(0, output) == VideoScheduleResult::Ready);
    CHECK(output.source_sequence == 10);
    CHECK(output.source_timestamp_microseconds == 1'000'000);
    CHECK(output.surface == surface);
}

void KeepsTimeoutSeparateFromSenderLoss()
{
    ScriptedSpoutBackend backend;
    backend.polls.push_back({VRREC_STATUS_TIMEOUT, {}});
    backend.polls.push_back({VRREC_STATUS_BACKEND_UNAVAILABLE, {}});
    VideoCfrScheduler scheduler;
    SpoutCapturePump pump(backend, scheduler, "selected");

    CHECK(pump.PollOne(std::chrono::milliseconds(50)) ==
          SpoutCaptureResult::Timeout);
    CHECK(pump.PollOne(std::chrono::milliseconds(50)) ==
          SpoutCaptureResult::SenderLost);
    CHECK(scheduler.Statistics().source_frame_count == 0);
}

void ValidatesPollArgumentsAndMapsBackendFailures()
{
    ScriptedSpoutBackend backend;
    VideoCfrScheduler scheduler;
    SpoutCapturePump missing_sender(backend, scheduler, "");
    CHECK(missing_sender.PollOne(std::chrono::milliseconds(0)) ==
          SpoutCaptureResult::InvalidFrame);
    CHECK(backend.poll_calls == 0);

    SpoutCapturePump pump(backend, scheduler, "selected");
    CHECK(pump.PollOne(std::chrono::milliseconds(-1)) ==
          SpoutCaptureResult::InvalidFrame);
    CHECK(pump.PollOne(std::chrono::milliseconds(
              VRREC_SPOUT_MAX_POLL_TIMEOUT_MILLISECONDS + 1)) ==
          SpoutCaptureResult::InvalidFrame);
    CHECK(backend.poll_calls == 0);

    backend.polls.push_back({VRREC_STATUS_OUT_OF_MEMORY, {}});
    CHECK(pump.PollOne(std::chrono::milliseconds(0)) ==
          SpoutCaptureResult::Failed);
    CHECK(backend.poll_calls == 1);

    backend.throw_on_poll = true;
    CHECK(pump.PollOne(std::chrono::milliseconds(
              VRREC_SPOUT_MAX_POLL_TIMEOUT_MILLISECONDS)) ==
          SpoutCaptureResult::Failed);
}

void RejectsFramesFromAnotherSender()
{
    ScriptedSpoutBackend backend;
    backend.polls.push_back({
        VRREC_STATUS_OK,
        Frame("another", 20, 2'000'000),
    });
    VideoCfrScheduler scheduler;
    SpoutCapturePump pump(backend, scheduler, "selected");

    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::InvalidFrame);
    CHECK(scheduler.Statistics().source_frame_count == 0);
}

void RejectsSurfaceMetadataThatDoesNotMatchTheTexture()
{
    ScriptedSpoutBackend backend;
    auto frame = Frame("selected", 21, 2'100'000);
    frame.surface = std::make_shared<FakeVideoSurface>(
        VideoSurfaceDescriptor {
            42,
            1'280,
            720,
            VRREC_SOURCE_PIXEL_FORMAT_BGRA8,
            1,
        });
    backend.polls.push_back({VRREC_STATUS_OK, std::move(frame)});
    VideoCfrScheduler scheduler;
    SpoutCapturePump pump(backend, scheduler, "selected");

    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::InvalidFrame);
    CHECK(scheduler.Statistics().source_frame_count == 0);
}

void RejectsEveryMalformedFrameBoundary()
{
    const auto rejects = [](SpoutFrame frame) {
        ScriptedSpoutBackend backend;
        backend.polls.push_back({VRREC_STATUS_OK, std::move(frame)});
        VideoCfrScheduler scheduler;
        SpoutCapturePump pump(backend, scheduler, "selected");
        CHECK(pump.PollOne(std::chrono::milliseconds(10)) ==
              SpoutCaptureResult::InvalidFrame);
        CHECK(scheduler.Statistics().source_frame_count == 0);
    };

    auto invalid = Frame("selected", 1, 1);
    invalid.surface.reset();
    rejects(std::move(invalid));
    invalid = Frame("selected", 1, 1);
    invalid.surface = std::make_shared<FakeVideoSurface>(
        invalid.surface->Descriptor(), false);
    rejects(std::move(invalid));
    invalid = Frame("selected", 1, 1);
    invalid.gpu_identity.clear();
    rejects(std::move(invalid));
    invalid = Frame("selected", 1, 1);
    invalid.width = 0;
    rejects(std::move(invalid));
    invalid = Frame("selected", 1, 1);
    invalid.height = 0;
    rejects(std::move(invalid));
    invalid = Frame("selected", 1, 1);
    auto descriptor = invalid.surface->Descriptor();
    descriptor.generation_id = 0;
    invalid.surface = std::make_shared<FakeVideoSurface>(descriptor);
    rejects(std::move(invalid));
    invalid = Frame("selected", 1, 1);
    descriptor = invalid.surface->Descriptor();
    ++descriptor.adapter_luid;
    invalid.surface = std::make_shared<FakeVideoSurface>(descriptor);
    rejects(std::move(invalid));
    invalid = Frame("selected", 1, 1);
    descriptor = invalid.surface->Descriptor();
    ++descriptor.width;
    invalid.surface = std::make_shared<FakeVideoSurface>(descriptor);
    rejects(std::move(invalid));
    invalid = Frame("selected", 1, 1);
    descriptor = invalid.surface->Descriptor();
    ++descriptor.height;
    invalid.surface = std::make_shared<FakeVideoSurface>(descriptor);
    rejects(std::move(invalid));
    invalid = Frame("selected", 1, 1);
    descriptor = invalid.surface->Descriptor();
    descriptor.pixel_format = VRREC_SOURCE_PIXEL_FORMAT_RGBA8;
    invalid.surface = std::make_shared<FakeVideoSurface>(descriptor);
    rejects(std::move(invalid));
    invalid = Frame("selected", 1, 1);
    invalid.gpu_vendor = static_cast<vrrec_gpu_vendor_t>(99);
    rejects(std::move(invalid));
    invalid = Frame("selected", 1, 1);
    invalid.pixel_format = static_cast<vrrec_source_pixel_format_t>(99);
    rejects(std::move(invalid));
    invalid = Frame("selected", 1, 1);
    invalid.estimated_source_fps =
        std::numeric_limits<double>::quiet_NaN();
    rejects(std::move(invalid));
    invalid = Frame("selected", 1, 1);
    invalid.estimated_source_fps = 0.0;
    rejects(std::move(invalid));
    invalid = Frame("selected", 1, -1);
    rejects(std::move(invalid));
    invalid = Frame("selected", 0, 1);
    rejects(std::move(invalid));
}

void AcceptsEverySupportedVendorAndPixelFormat()
{
    constexpr std::array vendors {
        VRREC_GPU_VENDOR_UNKNOWN,
        VRREC_GPU_VENDOR_NVIDIA,
        VRREC_GPU_VENDOR_AMD,
        VRREC_GPU_VENDOR_INTEL,
    };
    constexpr std::array pixel_formats {
        VRREC_SOURCE_PIXEL_FORMAT_BGRA8,
        VRREC_SOURCE_PIXEL_FORMAT_RGBA8,
        VRREC_SOURCE_PIXEL_FORMAT_NV12,
    };
    std::uint64_t sequence = 1;
    for (const auto vendor : vendors) {
        for (const auto pixel_format : pixel_formats) {
            ScriptedSpoutBackend backend;
            auto frame = Frame("selected", sequence++, 1);
            frame.gpu_vendor = vendor;
            frame.pixel_format = pixel_format;
            auto descriptor = frame.surface->Descriptor();
            descriptor.pixel_format = pixel_format;
            frame.surface = std::make_shared<FakeVideoSurface>(descriptor);
            backend.polls.push_back({VRREC_STATUS_OK, std::move(frame)});
            VideoCfrScheduler scheduler;
            SpoutCapturePump pump(backend, scheduler, "selected");
            CHECK(pump.PollOne(std::chrono::milliseconds(10)) ==
                  SpoutCaptureResult::FrameAccepted);
        }
    }
}

void AbortStopsPollingAndReleasesTheBackend()
{
    ScriptedSpoutBackend backend;
    VideoCfrScheduler scheduler;
    SpoutCapturePump pump(backend, scheduler, "selected");

    pump.Abort();
    pump.Abort();
    CHECK(backend.abort_calls == 1);
    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::Aborted);
    CHECK(backend.poll_calls == 0);
}

void AbortPreventsAValidatedInFlightFrameFromReachingTheScheduler()
{
    ScriptedSpoutBackend backend;
    auto frame = Frame("selected", 30, 3'000'000);
    const auto surface = std::make_shared<BlockingDescriptorSurface>();
    frame.surface = surface;
    backend.polls.push_back({VRREC_STATUS_OK, std::move(frame)});
    VideoCfrScheduler scheduler;
    SpoutCapturePump pump(backend, scheduler, "selected");

    auto polling = std::async(std::launch::async, [&] {
        return pump.PollOne(std::chrono::milliseconds(100));
    });
    surface->WaitForDescriptor();
    pump.Abort();
    surface->ReleaseDescriptor();

    CHECK(polling.get() == SpoutCaptureResult::Aborted);
    CHECK(scheduler.Statistics().source_frame_count == 0);
    CHECK(backend.abort_calls == 1);
}

void AbortWakesAnInFlightBackendPollBeforeFrameValidation()
{
    ScriptedSpoutBackend backend;
    backend.block_poll = true;
    backend.polls.push_back({
        VRREC_STATUS_OK,
        Frame("selected", 31, 3'100'000),
    });
    VideoCfrScheduler scheduler;
    SpoutCapturePump pump(backend, scheduler, "selected");

    auto polling = std::async(std::launch::async, [&] {
        return pump.PollOne(std::chrono::milliseconds(100));
    });
    backend.WaitForPoll();
    pump.Abort();

    CHECK(polling.get() == SpoutCaptureResult::Aborted);
    CHECK(backend.abort_calls == 1);
    CHECK(scheduler.Statistics().source_frame_count == 0);
}

void MapsSchedulerRejectionToAnInvalidFrame()
{
    ScriptedSpoutBackend backend;
    backend.polls.push_back({
        VRREC_STATUS_OK,
        Frame("selected", 32, 3'200'000),
    });
    backend.polls.push_back({
        VRREC_STATUS_OK,
        Frame("selected", 32, 3'200'001),
    });
    VideoCfrScheduler scheduler;
    SpoutCapturePump pump(backend, scheduler, "selected");

    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::FrameAccepted);
    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::InvalidFrame);
    CHECK(scheduler.Statistics().source_frame_count == 1);
}

void DropsAnOlderSurfaceGenerationWithoutReplacingTheNewFrame()
{
    ScriptedSpoutBackend backend;
    auto newest = Frame("selected", 40, 4'000'000, 2);
    const auto newest_surface = newest.surface;
    backend.polls.push_back({VRREC_STATUS_OK, std::move(newest)});
    backend.polls.push_back({
        VRREC_STATUS_OK,
        Frame("selected", 41, 4'010'000, 1),
    });
    VideoCfrScheduler scheduler;
    SpoutCapturePump pump(backend, scheduler, "selected");

    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::FrameAccepted);
    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::StaleFrame);

    ScheduledVideoFrame output {};
    CHECK(scheduler.Schedule(0, output) == VideoScheduleResult::Ready);
    CHECK(output.surface == newest_surface);
    CHECK(output.surface->Descriptor().generation_id == 2);
    CHECK(scheduler.Statistics().source_frame_count == 1);
}

void ReplacesTheSurfaceOnlyWithANewerGeneration()
{
    ScriptedSpoutBackend backend;
    auto initial = Frame("selected", 50, 5'000'000, 1);
    const auto initial_surface = initial.surface;
    auto replacement = Frame("selected", 51, 5'010'000, 2);
    const auto replacement_surface = replacement.surface;
    backend.polls.push_back({VRREC_STATUS_OK, std::move(initial)});
    backend.polls.push_back({VRREC_STATUS_OK, std::move(replacement)});
    VideoCfrScheduler scheduler;
    SpoutCapturePump pump(backend, scheduler, "selected");
    ScheduledVideoFrame output {};

    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::FrameAccepted);
    CHECK(scheduler.Schedule(0, output) == VideoScheduleResult::Ready);
    CHECK(output.surface == initial_surface);
    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::FrameAccepted);
    CHECK(scheduler.Schedule(1, output) == VideoScheduleResult::Ready);
    CHECK(output.surface == replacement_surface);
    CHECK(scheduler.Schedule(2, output) == VideoScheduleResult::Ready);
    CHECK(output.duplicated);
    CHECK(output.surface == replacement_surface);
}

void DistinguishesAnAdapterChangeWithoutPushingItsSurface()
{
    ScriptedSpoutBackend backend;
    backend.polls.push_back({
        VRREC_STATUS_OK,
        Frame("selected", 60, 6'000'000, 1, 42),
    });
    backend.polls.push_back({
        VRREC_STATUS_OK,
        Frame("selected", 61, 6'010'000, 2, 84),
    });
    VideoCfrScheduler scheduler;
    SpoutCapturePump pump(backend, scheduler, "selected");

    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::FrameAccepted);
    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::AdapterChanged);
    CHECK(scheduler.Statistics().source_frame_count == 1);
}

void RejectsResourceMutationWithoutAGenerationAdvance()
{
    ScriptedSpoutBackend backend;
    backend.polls.push_back({
        VRREC_STATUS_OK,
        Frame("selected", 70, 7'000'000, 3),
    });
    auto changed = Frame("selected", 71, 7'010'000, 3);
    changed.width = 1'280;
    changed.height = 720;
    changed.surface = std::make_shared<FakeVideoSurface>(
        VideoSurfaceDescriptor {
            42,
            1'280,
            720,
            VRREC_SOURCE_PIXEL_FORMAT_BGRA8,
            3,
        });
    backend.polls.push_back({VRREC_STATUS_OK, std::move(changed)});
    auto height_changed = Frame("selected", 72, 7'020'000, 3);
    height_changed.height = 720;
    height_changed.surface = std::make_shared<FakeVideoSurface>(
        VideoSurfaceDescriptor {
            42,
            1'920,
            720,
            VRREC_SOURCE_PIXEL_FORMAT_BGRA8,
            3,
        });
    backend.polls.push_back({VRREC_STATUS_OK, std::move(height_changed)});
    auto format_changed = Frame("selected", 73, 7'030'000, 3);
    format_changed.pixel_format = VRREC_SOURCE_PIXEL_FORMAT_RGBA8;
    format_changed.surface = std::make_shared<FakeVideoSurface>(
        VideoSurfaceDescriptor {
            42,
            1'920,
            1'080,
            VRREC_SOURCE_PIXEL_FORMAT_RGBA8,
            3,
        });
    backend.polls.push_back({VRREC_STATUS_OK, std::move(format_changed)});
    VideoCfrScheduler scheduler;
    SpoutCapturePump pump(backend, scheduler, "selected");

    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::FrameAccepted);
    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::InvalidFrame);
    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::InvalidFrame);
    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::InvalidFrame);
    CHECK(scheduler.Statistics().source_frame_count == 1);
}

void QuarantinesChangedGeometryUntilStableForFiveHundredMilliseconds()
{
    ScriptedSpoutBackend backend;
    backend.polls.push_back({
        VRREC_STATUS_OK,
        Frame("selected", 80, 8'000'000, 1),
    });
    backend.polls.push_back({
        VRREC_STATUS_OK,
        FrameWithGeometry(81, 8'100'000, 2, 1'280, 720),
    });
    backend.polls.push_back({
        VRREC_STATUS_OK,
        FrameWithGeometry(82, 8'599'999, 2, 1'280, 720),
    });
    backend.polls.push_back({
        VRREC_STATUS_OK,
        FrameWithGeometry(
            83,
            8'600'000,
            3,
            1'280,
            720,
            VRREC_SOURCE_PIXEL_FORMAT_RGBA8),
    });
    backend.polls.push_back({
        VRREC_STATUS_OK,
        FrameWithGeometry(
            84,
            9'100'000,
            3,
            1'280,
            720,
            VRREC_SOURCE_PIXEL_FORMAT_RGBA8),
    });
    backend.polls.push_back({
        VRREC_STATUS_OK,
        FrameWithGeometry(
            85,
            9'200'000,
            3,
            1'280,
            720,
            VRREC_SOURCE_PIXEL_FORMAT_RGBA8),
    });
    RecordingGeometryEvents events;
    VideoCfrScheduler scheduler;
    SpoutCapturePump pump(backend, scheduler, "selected", events);

    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::FrameAccepted);
    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::GeometryChangePending);
    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::GeometryChangePending);
    CHECK(events.calls == 0);

    // Pixel format is part of the source signature, so this restarts the
    // stability window instead of completing the BGRA candidate.
    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::GeometryChangePending);
    CHECK(events.calls == 0);
    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::GeometryChangePending);
    CHECK(events.calls == 1);
    CHECK(events.last_width == 1'280);
    CHECK(events.last_height == 720);
    CHECK(events.last_pixel_format == VRREC_SOURCE_PIXEL_FORMAT_RGBA8);
    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::GeometryChangePending);
    CHECK(events.calls == 1);
    CHECK(scheduler.Statistics().source_frame_count == 1);
}

void CancelsAnUnstableGeometryChangeWhenTheOriginalGeometryReturns()
{
    ScriptedSpoutBackend backend;
    backend.polls.push_back({
        VRREC_STATUS_OK,
        Frame("selected", 90, 9'000'000, 1),
    });
    backend.polls.push_back({
        VRREC_STATUS_OK,
        FrameWithGeometry(91, 9'100'000, 2, 1'280, 720),
    });
    backend.polls.push_back({
        VRREC_STATUS_OK,
        Frame("selected", 92, 9'200'000, 3),
    });
    RecordingGeometryEvents events;
    VideoCfrScheduler scheduler;
    SpoutCapturePump pump(backend, scheduler, "selected", events);

    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::FrameAccepted);
    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::GeometryChangePending);
    CHECK(pump.PollOne(std::chrono::milliseconds(100)) ==
          SpoutCaptureResult::FrameAccepted);
    CHECK(events.calls == 0);
    CHECK(scheduler.Statistics().source_frame_count == 2);
}

}

int main()
{
    AcceptsOnlyTheSelectedSenderIntoTheScheduler();
    KeepsTimeoutSeparateFromSenderLoss();
    ValidatesPollArgumentsAndMapsBackendFailures();
    RejectsFramesFromAnotherSender();
    RejectsSurfaceMetadataThatDoesNotMatchTheTexture();
    RejectsEveryMalformedFrameBoundary();
    AcceptsEverySupportedVendorAndPixelFormat();
    AbortStopsPollingAndReleasesTheBackend();
    AbortPreventsAValidatedInFlightFrameFromReachingTheScheduler();
    AbortWakesAnInFlightBackendPollBeforeFrameValidation();
    MapsSchedulerRejectionToAnInvalidFrame();
    DropsAnOlderSurfaceGenerationWithoutReplacingTheNewFrame();
    ReplacesTheSurfaceOnlyWithANewerGeneration();
    DistinguishesAnAdapterChangeWithoutPushingItsSurface();
    RejectsResourceMutationWithoutAGenerationAdvance();
    QuarantinesChangedGeometryUntilStableForFiveHundredMilliseconds();
    CancelsAnUnstableGeometryChangeWhenTheOriginalGeometryReturns();
    return 0;
}
