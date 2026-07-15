#include "video_processing_encoder_sink.hpp"
#include "video_processing_layout_controller.hpp"

#include <chrono>
#include <cstddef>
#include <condition_variable>
#include <cstdlib>
#include <future>
#include <iostream>
#include <memory>
#include <mutex>
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

class FakeSurface final : public VideoSurface {
public:
    explicit FakeSurface(VideoSurfaceDescriptor descriptor) noexcept
        : descriptor_(descriptor)
    {
    }

    VideoSurfaceDescriptor Descriptor() const noexcept override
    {
        return descriptor_;
    }

    void *NativeHandle() const noexcept override
    {
        return native_handle;
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

    void *native_handle = reinterpret_cast<void *>(1);

private:
    VideoSurfaceDescriptor descriptor_;
};

class RecordingProcessor final : public VideoFrameProcessor {
public:
    vrrec_status_t Process(
        const std::shared_ptr<VideoSurface> &source,
        const VideoProcessingPlan &plan,
        std::shared_ptr<VideoSurface> &output) noexcept override
    {
        std::unique_lock lock(mutex);
        ++process_calls;
        last_source = source;
        last_plan = plan;
        process_entered = true;
        changed.notify_all();
        if (block_process) {
            changed.wait(lock, [&] { return release_process; });
        }
        output = next_output;
        return status;
    }

    void Abort() noexcept override
    {
        ++abort_calls;
        if (order != nullptr) {
            order->push_back(1);
        }
    }

    void WaitForProcess()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return process_entered; });
    }

    void ReleaseProcess()
    {
        {
            const std::lock_guard lock(mutex);
            release_process = true;
        }
        changed.notify_all();
    }

    std::shared_ptr<VideoSurface> next_output;
    std::shared_ptr<VideoSurface> last_source;
    VideoProcessingPlan last_plan {};
    vrrec_status_t status = VRREC_STATUS_OK;
    std::vector<int> *order = nullptr;
    std::size_t process_calls = 0;
    std::size_t abort_calls = 0;
    std::mutex mutex;
    std::condition_variable changed;
    bool block_process = false;
    bool process_entered = false;
    bool release_process = false;
};

class RecordingEncoder final : public VideoEncoderSink {
public:
    VideoEncoderWrite Write(
        const ScheduledVideoFrame &frame) noexcept override
    {
        frames.push_back(frame);
        return write;
    }

    VideoEncoderWrite Finish() noexcept override
    {
        ++finish_calls;
        return finish;
    }

    void Abort() noexcept override
    {
        ++abort_calls;
        if (order != nullptr) {
            order->push_back(2);
        }
    }

    VideoEncoderWrite write {VRREC_STATUS_OK, 1, 250};
    VideoEncoderWrite finish {VRREC_STATUS_OK, 2, 300};
    std::vector<ScheduledVideoFrame> frames;
    std::vector<int> *order = nullptr;
    std::size_t finish_calls = 0;
    std::size_t abort_calls = 0;
};

std::shared_ptr<FakeSurface> Surface(
    std::uint32_t width,
    std::uint32_t height,
    vrrec_source_pixel_format_t format)
{
    return std::make_shared<FakeSurface>(
        VideoSurfaceDescriptor {42, width, height, format});
}

void ConvertsToValidatedNv12BeforeCallingTheEncoder()
{
    RecordingProcessor processor;
    processor.next_output = Surface(
        1'920,
        1'080,
        VRREC_SOURCE_PIXEL_FORMAT_NV12);
    RecordingEncoder encoder;
    ProcessingVideoEncoderSink sink(
        processor,
        encoder,
        1'920,
        1'080);
    const auto source = Surface(
        1'919,
        1'079,
        VRREC_SOURCE_PIXEL_FORMAT_RGBA8);

    const auto write = sink.Write({7, 11, 1'000'000, 2, false, source});
    CHECK(write.status == VRREC_STATUS_OK);
    CHECK(processor.process_calls == 1);
    CHECK(processor.last_source == source);
    CHECK(processor.last_plan.pad_right == 1);
    CHECK(processor.last_plan.pad_bottom == 1);
    CHECK(processor.last_plan.swap_red_blue_channels);
    CHECK(encoder.frames.size() == 1);
    CHECK(encoder.frames.front().surface == processor.next_output);
    CHECK(encoder.frames.front().output_tick == 7);
    CHECK(encoder.frames.front().source_sequence == 11);
}

void PreparesAnOwnedFrameWithoutCallingTheEncoder()
{
    RecordingProcessor processor;
    processor.next_output = Surface(
        1'920,
        1'080,
        VRREC_SOURCE_PIXEL_FORMAT_NV12);
    RecordingEncoder encoder;
    ProcessingVideoEncoderSink sink(
        processor,
        encoder,
        1'920,
        1'080);
    const auto source = Surface(
        1'920,
        1'080,
        VRREC_SOURCE_PIXEL_FORMAT_BGRA8);

    const auto preparation = sink.Prepare(
        {7, 11, 1'000'000, 2, false, source});

    CHECK(preparation.status == VRREC_STATUS_OK);
    CHECK(preparation.frame.surface == processor.next_output);
    CHECK(preparation.frame.surface != source);
    CHECK(encoder.frames.empty());

    const auto write = sink.WritePrepared(preparation.frame);
    CHECK(write.status == VRREC_STATUS_OK);
    CHECK(encoder.frames.size() == 1);
    CHECK(encoder.frames.front().surface == processor.next_output);
}

void ClassifiesProcessorFailureAndSkipsTheEncoder()
{
    RecordingProcessor processor;
    processor.status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    RecordingEncoder encoder;
    ProcessingVideoEncoderSink sink(processor, encoder, 1'920, 1'080);

    const auto write = sink.Write({
        0,
        1,
        0,
        0,
        false,
        Surface(1'920, 1'080, VRREC_SOURCE_PIXEL_FORMAT_BGRA8),
    });
    CHECK(write.status == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(write.failure_stage == VideoEncoderFailureStage::Processing);
    CHECK(encoder.frames.empty());
}

void RejectsAnInvalidProcessorOutputSurface()
{
    RecordingProcessor processor;
    processor.next_output = Surface(
        1'280,
        720,
        VRREC_SOURCE_PIXEL_FORMAT_NV12);
    RecordingEncoder encoder;
    ProcessingVideoEncoderSink sink(processor, encoder, 1'920, 1'080);

    const auto write = sink.Write({
        0,
        1,
        0,
        0,
        false,
        Surface(1'920, 1'080, VRREC_SOURCE_PIXEL_FORMAT_BGRA8),
    });
    CHECK(write.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(write.failure_stage == VideoEncoderFailureStage::Processing);
    CHECK(encoder.frames.empty());
}

void RejectsAnInputSurfaceWithoutANativeHandleBeforeProcessing()
{
    RecordingProcessor processor;
    processor.next_output = Surface(
        1'920,
        1'080,
        VRREC_SOURCE_PIXEL_FORMAT_NV12);
    RecordingEncoder encoder;
    ProcessingVideoEncoderSink sink(processor, encoder, 1'920, 1'080);
    const auto source = Surface(
        1'920,
        1'080,
        VRREC_SOURCE_PIXEL_FORMAT_BGRA8);
    source->native_handle = nullptr;

    const auto write = sink.Write({0, 1, 0, 0, false, source});
    CHECK(write.status == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(write.failure_stage == VideoEncoderFailureStage::Processing);
    CHECK(processor.process_calls == 0);
    CHECK(encoder.frames.empty());
}

vrrec_video_layout_v1 PortraitLayout();

void DelegatesFinishAndAbortsProcessorBeforeEncoder()
{
    std::vector<int> order;
    RecordingProcessor processor;
    processor.order = &order;
    RecordingEncoder encoder;
    encoder.order = &order;
    ProcessingVideoEncoderSink sink(processor, encoder, 1'920, 1'080);

    CHECK(sink.Finish().muxed_packet_count == 2);
    CHECK(encoder.finish_calls == 1);
    sink.Abort();
    sink.Abort();
    CHECK(order == std::vector<int>({1, 2}));
    CHECK(sink.UpdateVideoLayout(PortraitLayout()) ==
          VRREC_STATUS_INVALID_STATE);
}

vrrec_video_layout_v1 PortraitLayout()
{
    return {
        sizeof(vrrec_video_layout_v1),
        VRREC_ABI_V1,
        1'080,
        1'920,
        1'920,
        1'080,
        656,
        0,
        608,
        1'080,
        VRREC_CANVAS_BACKGROUND_BLACK,
        VRREC_VIDEO_ROTATION_NONE,
    };
}

void AppliesValidatedLiveLayoutToTheNextFrame()
{
    RecordingProcessor processor;
    processor.next_output = Surface(1'920, 1'080, VRREC_SOURCE_PIXEL_FORMAT_NV12);
    RecordingEncoder encoder;
    ProcessingVideoEncoderSink sink(processor, encoder, 1'920, 1'080);
    ProcessingVideoLayoutController controller(sink);

    CHECK(controller.UpdateVideoLayout(PortraitLayout()) == VRREC_STATUS_OK);
    const auto write = sink.Write({0, 1, 0, 0, false,
        Surface(1'080, 1'920, VRREC_SOURCE_PIXEL_FORMAT_BGRA8)});
    CHECK(write.status == VRREC_STATUS_OK);
    CHECK(processor.last_plan.source_width == 1'080);
    CHECK(processor.last_plan.source_height == 1'920);
    CHECK(processor.last_plan.offset_x == 656);
    CHECK(processor.last_plan.offset_y == 0);
    CHECK(processor.last_plan.destination_width == 608);
    CHECK(processor.last_plan.destination_height == 1'080);
}

void FinishTerminalizesFrameProcessingAndLayoutUpdates()
{
    RecordingProcessor processor;
    processor.next_output = Surface(
        1'920,
        1'080,
        VRREC_SOURCE_PIXEL_FORMAT_NV12);
    RecordingEncoder encoder;
    ProcessingVideoEncoderSink sink(processor, encoder, 1'920, 1'080);

    CHECK(sink.Finish().status == VRREC_STATUS_OK);
    CHECK(sink.UpdateVideoLayout(PortraitLayout()) ==
          VRREC_STATUS_INVALID_STATE);
    const auto write = sink.Write({
        0,
        1,
        0,
        0,
        false,
        Surface(1'920, 1'080, VRREC_SOURCE_PIXEL_FORMAT_BGRA8),
    });
    CHECK(write.status == VRREC_STATUS_INVALID_STATE);
    CHECK(write.failure_stage == VideoEncoderFailureStage::Processing);
    CHECK(processor.process_calls == 0);
    CHECK(encoder.frames.empty());
}

void AbortPreventsAnInFlightProcessedFrameFromReachingTheEncoder()
{
    RecordingProcessor processor;
    processor.next_output = Surface(
        1'920,
        1'080,
        VRREC_SOURCE_PIXEL_FORMAT_NV12);
    processor.block_process = true;
    RecordingEncoder encoder;
    ProcessingVideoEncoderSink sink(processor, encoder, 1'920, 1'080);
    const auto source = Surface(
        1'920,
        1'080,
        VRREC_SOURCE_PIXEL_FORMAT_BGRA8);

    auto writing = std::async(std::launch::async, [&] {
        return sink.Write({0, 1, 0, 0, false, source});
    });
    processor.WaitForProcess();
    sink.Abort();
    processor.ReleaseProcess();
    const auto write = writing.get();

    CHECK(write.status == VRREC_STATUS_INVALID_STATE);
    CHECK(write.failure_stage == VideoEncoderFailureStage::Processing);
    CHECK(encoder.frames.empty());
}

void FinishPreventsAnInFlightProcessedFrameFromReachingTheEncoder()
{
    RecordingProcessor processor;
    processor.next_output = Surface(
        1'920,
        1'080,
        VRREC_SOURCE_PIXEL_FORMAT_NV12);
    processor.block_process = true;
    RecordingEncoder encoder;
    ProcessingVideoEncoderSink sink(processor, encoder, 1'920, 1'080);
    const auto source = Surface(
        1'920,
        1'080,
        VRREC_SOURCE_PIXEL_FORMAT_BGRA8);

    auto writing = std::async(std::launch::async, [&] {
        return sink.Write({0, 1, 0, 0, false, source});
    });
    processor.WaitForProcess();
    CHECK(sink.Finish().status == VRREC_STATUS_OK);
    processor.ReleaseProcess();
    const auto write = writing.get();

    CHECK(write.status == VRREC_STATUS_INVALID_STATE);
    CHECK(write.failure_stage == VideoEncoderFailureStage::Processing);
    CHECK(encoder.frames.empty());
    CHECK(encoder.finish_calls == 1);
}

void RejectsInvalidUpdatesAndMismatchedFramesWithoutLosingTheLastLayout()
{
    RecordingProcessor processor;
    processor.next_output = Surface(1'920, 1'080, VRREC_SOURCE_PIXEL_FORMAT_NV12);
    RecordingEncoder encoder;
    ProcessingVideoEncoderSink sink(processor, encoder, 1'920, 1'080);
    ProcessingVideoLayoutController controller(sink);
    CHECK(controller.UpdateVideoLayout(PortraitLayout()) == VRREC_STATUS_OK);

    auto invalid = PortraitLayout();
    invalid.canvas_width = 1'280;
    CHECK(controller.UpdateVideoLayout(invalid) == VRREC_STATUS_INVALID_ARGUMENT);
    const auto mismatch = sink.Write({0, 1, 0, 0, false,
        Surface(1'920, 1'080, VRREC_SOURCE_PIXEL_FORMAT_BGRA8)});
    CHECK(mismatch.status == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(processor.process_calls == 0);

    const auto recovered = sink.Write({1, 2, 0, 0, false,
        Surface(1'080, 1'920, VRREC_SOURCE_PIXEL_FORMAT_BGRA8)});
    CHECK(recovered.status == VRREC_STATUS_OK);
    CHECK(processor.last_plan.destination_width == 608);
}

}

int main()
{
    ConvertsToValidatedNv12BeforeCallingTheEncoder();
    PreparesAnOwnedFrameWithoutCallingTheEncoder();
    ClassifiesProcessorFailureAndSkipsTheEncoder();
    RejectsAnInvalidProcessorOutputSurface();
    RejectsAnInputSurfaceWithoutANativeHandleBeforeProcessing();
    DelegatesFinishAndAbortsProcessorBeforeEncoder();
    AppliesValidatedLiveLayoutToTheNextFrame();
    FinishTerminalizesFrameProcessingAndLayoutUpdates();
    AbortPreventsAnInFlightProcessedFrameFromReachingTheEncoder();
    FinishPreventsAnInFlightProcessedFrameFromReachingTheEncoder();
    RejectsInvalidUpdatesAndMismatchedFramesWithoutLosingTheLastLayout();
    return 0;
}
