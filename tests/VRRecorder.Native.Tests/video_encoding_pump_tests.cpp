#include "video_encoding_pump.hpp"

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <memory>
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

class ScriptedVideoSurface final : public VideoSurface {
public:
    VideoSurfaceDescriptor Descriptor() const noexcept override
    {
        return {
            42,
            1'920,
            1'080,
            VRREC_SOURCE_PIXEL_FORMAT_BGRA8,
        };
    }

    void *NativeHandle() const noexcept override
    {
        return reinterpret_cast<void *>(1);
    }

    VideoSurfaceAcquireResult AcquireForRead(
        std::chrono::milliseconds timeout) noexcept override
    {
        ++acquire_calls;
        last_timeout = timeout;
        if (order != nullptr) {
            order->push_back(1);
        }
        return acquire_result;
    }

    vrrec_status_t ReleaseFromRead() noexcept override
    {
        ++release_calls;
        if (order != nullptr) {
            order->push_back(3);
        }
        return release_status;
    }

    VideoSurfaceAcquireResult acquire_result =
        VideoSurfaceAcquireResult::Acquired;
    vrrec_status_t release_status = VRREC_STATUS_OK;
    std::chrono::milliseconds last_timeout {0};
    std::vector<int> *order = nullptr;
    std::size_t acquire_calls = 0;
    std::size_t release_calls = 0;
};

class ScriptedVideoEncoderSink final : public VideoEncoderSink {
public:
    VideoEncoderWrite Write(
        const ScheduledVideoFrame &frame) noexcept override
    {
        frames.push_back(frame);
        if (next >= writes.size()) {
            return {VRREC_STATUS_INTERNAL_ERROR, 0, 0};
        }

        return writes[next++];
    }

    VideoEncoderWrite Finish() noexcept override
    {
        ++finish_calls;
        return {VRREC_STATUS_OK, 0, 0};
    }

    void Abort() noexcept override
    {
        ++abort_calls;
    }

    std::vector<VideoEncoderWrite> writes;
    std::vector<ScheduledVideoFrame> frames;
    std::size_t next = 0;
    std::size_t finish_calls = 0;
    std::size_t abort_calls = 0;
};

class ScriptedPreparingVideoEncoderSink final
    : public VideoFramePreparingEncoderSink {
public:
    VideoFramePreparation Prepare(
        const ScheduledVideoFrame &frame) noexcept override
    {
        ++prepare_calls;
        if (order != nullptr) {
            order->push_back(2);
        }
        auto prepared = frame;
        prepared.surface = prepared_surface;
        return {prepare_status, std::move(prepared)};
    }

    VideoEncoderWrite WritePrepared(
        const ScheduledVideoFrame &frame) noexcept override
    {
        frames.push_back(frame);
        if (order != nullptr) {
            order->push_back(4);
        }
        return write;
    }

    VideoEncoderWrite Finish() noexcept override
    {
        return {VRREC_STATUS_OK, 0, 0};
    }

    void Abort() noexcept override
    {
        ++abort_calls;
        if (order != nullptr) {
            order->push_back(5);
        }
    }

    std::shared_ptr<VideoSurface> prepared_surface;
    vrrec_status_t prepare_status = VRREC_STATUS_OK;
    VideoEncoderWrite write {VRREC_STATUS_OK, 1, 100};
    std::vector<ScheduledVideoFrame> frames;
    std::vector<int> *order = nullptr;
    std::size_t prepare_calls = 0;
    std::size_t abort_calls = 0;
};

void DoesNotCallTheEncoderBeforeTheFirstSourceFrame()
{
    VideoCfrScheduler scheduler;
    ScriptedVideoEncoderSink sink;
    VideoEncodingPump pump(scheduler, sink);
    VideoEncodingRead read {};

    CHECK(pump.PumpTick(0, read) == VideoEncodingResult::NoFrame);
    CHECK(sink.frames.empty());
    const auto statistics = pump.Statistics();
    CHECK(statistics.scheduler.output_frame_count == 0);
    CHECK(statistics.muxed_packet_count == 0);
}

void DetectsTheFirstMuxedPacketAfterEncoderBuffering()
{
    VideoCfrScheduler scheduler;
    ScriptedVideoEncoderSink sink;
    sink.writes.push_back({VRREC_STATUS_OK, 0, 100});
    sink.writes.push_back({VRREC_STATUS_OK, 1, 250});
    VideoEncodingPump pump(scheduler, sink);
    CHECK(scheduler.Push({10, 1'000'000}) == VRREC_STATUS_OK);

    VideoEncodingRead read {};
    CHECK(pump.PumpTick(0, read) == VideoEncodingResult::Submitted);
    CHECK(!read.first_packet_muxed);
    CHECK(read.muxed_packet_count == 0);
    CHECK(read.encode_latency_microseconds == 100);
    CHECK(pump.PumpTick(1, read) == VideoEncodingResult::Submitted);
    CHECK(read.scheduled.duplicated);
    CHECK(read.first_packet_muxed);
    CHECK(read.muxed_packet_count == 1);
    CHECK(read.encode_latency_microseconds == 250);

    const auto statistics = pump.Statistics();
    CHECK(statistics.scheduler.source_frame_count == 1);
    CHECK(statistics.scheduler.output_frame_count == 2);
    CHECK(statistics.scheduler.duplicated_output_frame_count == 1);
    CHECK(statistics.muxed_packet_count == 1);
    CHECK(statistics.latest_encode_latency_microseconds == 250);
    CHECK(statistics.maximum_encode_latency_microseconds == 250);
}

void CountsLaterPacketsWithoutRepeatingTheFirstPacketEvent()
{
    VideoCfrScheduler scheduler;
    ScriptedVideoEncoderSink sink;
    sink.writes.push_back({VRREC_STATUS_OK, 1, 200});
    sink.writes.push_back({VRREC_STATUS_OK, 2, 150});
    VideoEncodingPump pump(scheduler, sink);
    VideoEncodingRead read {};
    CHECK(scheduler.Push({20, 2'000'000}) == VRREC_STATUS_OK);
    CHECK(pump.PumpTick(0, read) == VideoEncodingResult::Submitted);
    CHECK(read.first_packet_muxed);
    CHECK(scheduler.Push({21, 2'010'000}) == VRREC_STATUS_OK);
    CHECK(pump.PumpTick(1, read) == VideoEncodingResult::Submitted);
    CHECK(!read.first_packet_muxed);

    const auto statistics = pump.Statistics();
    CHECK(statistics.muxed_packet_count == 3);
    CHECK(statistics.latest_encode_latency_microseconds == 150);
    CHECK(statistics.maximum_encode_latency_microseconds == 200);
}

void EncoderFailureDoesNotCommitPacketOrLatencyStatistics()
{
    VideoCfrScheduler scheduler;
    ScriptedVideoEncoderSink sink;
    sink.writes.push_back({VRREC_STATUS_INTERNAL_ERROR, 5, 999});
    VideoEncodingPump pump(scheduler, sink);
    CHECK(scheduler.Push({30, 3'000'000}) == VRREC_STATUS_OK);

    VideoEncodingRead read {};
    CHECK(pump.PumpTick(0, read) == VideoEncodingResult::EncoderFailed);
    CHECK(read.encoder_status == VRREC_STATUS_INTERNAL_ERROR);
    const auto statistics = pump.Statistics();
    CHECK(statistics.muxed_packet_count == 0);
    CHECK(statistics.latest_encode_latency_microseconds == 0);
    CHECK(statistics.maximum_encode_latency_microseconds == 0);
}

void AcquiresAndReleasesTheSharedSurfaceAroundEncoderWrite()
{
    VideoCfrScheduler scheduler;
    ScriptedVideoEncoderSink sink;
    sink.writes.push_back({VRREC_STATUS_OK, 1, 100});
    VideoEncodingPump pump(
        scheduler,
        sink,
        std::chrono::milliseconds(7));
    const auto surface = std::make_shared<ScriptedVideoSurface>();
    CHECK(scheduler.Push({40, 4'000'000, surface}) == VRREC_STATUS_OK);

    VideoEncodingRead read {};
    CHECK(pump.PumpTick(0, read) == VideoEncodingResult::Submitted);
    CHECK(surface->acquire_calls == 1);
    CHECK(surface->last_timeout == std::chrono::milliseconds(7));
    CHECK(surface->release_calls == 1);
    CHECK(sink.frames.size() == 1);
    CHECK(sink.frames.front().surface == surface);
}

void KeepsSurfaceTimeoutSeparateAndDoesNotCallTheEncoder()
{
    VideoCfrScheduler scheduler;
    ScriptedVideoEncoderSink sink;
    VideoEncodingPump pump(
        scheduler,
        sink,
        std::chrono::milliseconds(3));
    const auto surface = std::make_shared<ScriptedVideoSurface>();
    surface->acquire_result = VideoSurfaceAcquireResult::Timeout;
    CHECK(scheduler.Push({50, 5'000'000, surface}) == VRREC_STATUS_OK);

    VideoEncodingRead read {};
    CHECK(pump.PumpTick(0, read) == VideoEncodingResult::SurfaceTimeout);
    CHECK(surface->acquire_calls == 1);
    CHECK(surface->release_calls == 0);
    CHECK(sink.frames.empty());
    CHECK(read.scheduled.source_sequence == 0);
    CHECK(!read.scheduled.surface);
}

void ReleasesTheSurfaceWhenTheEncoderFails()
{
    VideoCfrScheduler scheduler;
    ScriptedVideoEncoderSink sink;
    sink.writes.push_back({VRREC_STATUS_INTERNAL_ERROR, 0, 0});
    VideoEncodingPump pump(scheduler, sink);
    const auto surface = std::make_shared<ScriptedVideoSurface>();
    CHECK(scheduler.Push({60, 6'000'000, surface}) == VRREC_STATUS_OK);

    VideoEncodingRead read {};
    CHECK(pump.PumpTick(0, read) == VideoEncodingResult::EncoderFailed);
    CHECK(surface->acquire_calls == 1);
    CHECK(surface->release_calls == 1);
}

void DistinguishesAbandonedAndDeviceLostSurfaceAcquisition()
{
    for (const auto &[acquire, expected] : {
             std::pair {
                 VideoSurfaceAcquireResult::Abandoned,
                 VideoEncodingResult::SurfaceAbandoned},
             std::pair {
                 VideoSurfaceAcquireResult::DeviceLost,
                 VideoEncodingResult::SurfaceDeviceLost},
         }) {
        VideoCfrScheduler scheduler;
        ScriptedVideoEncoderSink sink;
        VideoEncodingPump pump(scheduler, sink);
        const auto surface = std::make_shared<ScriptedVideoSurface>();
        surface->acquire_result = acquire;
        CHECK(scheduler.Push({70, 7'000'000, surface}) ==
              VRREC_STATUS_OK);
        VideoEncodingRead read {};

        CHECK(pump.PumpTick(0, read) == expected);
        CHECK(surface->acquire_calls == 1);
        CHECK(surface->release_calls == 0);
        CHECK(sink.frames.empty());
    }
}

void ReleaseFailureAbortsWithoutCommittingSuccessfulWriteStatistics()
{
    VideoCfrScheduler scheduler;
    ScriptedVideoEncoderSink sink;
    sink.writes.push_back({VRREC_STATUS_OK, 1, 200});
    VideoEncodingPump pump(scheduler, sink);
    const auto surface = std::make_shared<ScriptedVideoSurface>();
    surface->release_status = VRREC_STATUS_INTERNAL_ERROR;
    CHECK(scheduler.Push({80, 8'000'000, surface}) == VRREC_STATUS_OK);
    VideoEncodingRead read {};

    CHECK(pump.PumpTick(0, read) == VideoEncodingResult::SurfaceFailed);

    CHECK(surface->release_calls == 1);
    CHECK(sink.frames.size() == 1);
    CHECK(sink.abort_calls == 1);
    CHECK(!read.first_packet_muxed);
    CHECK(read.muxed_packet_count == 0);
    CHECK(read.encode_latency_microseconds == 0);
    CHECK(read.encoder_status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(pump.Statistics().muxed_packet_count == 0);
}

void ReleasesTheSourceBeforeEncodingThePreparedOwnedSurface()
{
    std::vector<int> order;
    VideoCfrScheduler scheduler;
    ScriptedPreparingVideoEncoderSink sink;
    sink.order = &order;
    sink.prepared_surface = std::make_shared<ScriptedVideoSurface>();
    VideoEncodingPump pump(scheduler, sink);
    const auto source = std::make_shared<ScriptedVideoSurface>();
    source->order = &order;
    CHECK(scheduler.Push({90, 9'000'000, source}) == VRREC_STATUS_OK);
    VideoEncodingRead read {};

    CHECK(pump.PumpTick(0, read) == VideoEncodingResult::Submitted);

    CHECK(order == std::vector<int>({1, 2, 3, 4}));
    CHECK(sink.prepare_calls == 1);
    CHECK(sink.frames.size() == 1);
    CHECK(sink.frames.front().surface == sink.prepared_surface);
    CHECK(sink.frames.front().surface != source);
    CHECK(read.muxed_packet_count == 1);
}

void ReleaseFailureRejectsThePreparedFrameBeforeEncoding()
{
    std::vector<int> order;
    VideoCfrScheduler scheduler;
    ScriptedPreparingVideoEncoderSink sink;
    sink.order = &order;
    sink.prepared_surface = std::make_shared<ScriptedVideoSurface>();
    VideoEncodingPump pump(scheduler, sink);
    const auto source = std::make_shared<ScriptedVideoSurface>();
    source->order = &order;
    source->release_status = VRREC_STATUS_INTERNAL_ERROR;
    CHECK(scheduler.Push({100, 10'000'000, source}) == VRREC_STATUS_OK);
    VideoEncodingRead read {};

    CHECK(pump.PumpTick(0, read) == VideoEncodingResult::SurfaceFailed);

    CHECK(order == std::vector<int>({1, 2, 3, 5}));
    CHECK(sink.prepare_calls == 1);
    CHECK(sink.frames.empty());
    CHECK(sink.abort_calls == 1);
    CHECK(read.muxed_packet_count == 0);
    CHECK(read.encode_latency_microseconds == 0);
    CHECK(pump.Statistics().muxed_packet_count == 0);
}

void ProcessingFailureStillReleasesTheSourceExactlyOnce()
{
    std::vector<int> order;
    VideoCfrScheduler scheduler;
    ScriptedPreparingVideoEncoderSink sink;
    sink.order = &order;
    sink.prepare_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    VideoEncodingPump pump(scheduler, sink);
    const auto source = std::make_shared<ScriptedVideoSurface>();
    source->order = &order;
    CHECK(scheduler.Push({110, 11'000'000, source}) == VRREC_STATUS_OK);
    VideoEncodingRead read {};

    CHECK(pump.PumpTick(0, read) == VideoEncodingResult::ProcessorFailed);

    CHECK(order == std::vector<int>({1, 2, 3}));
    CHECK(source->release_calls == 1);
    CHECK(sink.frames.empty());
    CHECK(read.encoder_status == VRREC_STATUS_BACKEND_UNAVAILABLE);
}

}

int main()
{
    DoesNotCallTheEncoderBeforeTheFirstSourceFrame();
    DetectsTheFirstMuxedPacketAfterEncoderBuffering();
    CountsLaterPacketsWithoutRepeatingTheFirstPacketEvent();
    EncoderFailureDoesNotCommitPacketOrLatencyStatistics();
    AcquiresAndReleasesTheSharedSurfaceAroundEncoderWrite();
    KeepsSurfaceTimeoutSeparateAndDoesNotCallTheEncoder();
    ReleasesTheSurfaceWhenTheEncoderFails();
    DistinguishesAbandonedAndDeviceLostSurfaceAcquisition();
    ReleaseFailureAbortsWithoutCommittingSuccessfulWriteStatistics();
    ReleasesTheSourceBeforeEncodingThePreparedOwnedSurface();
    ReleaseFailureRejectsThePreparedFrameBeforeEncoding();
    ProcessingFailureStillReleasesTheSourceExactlyOnce();
    return 0;
}
