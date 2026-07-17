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
    vrrec_source_pixel_format_t format,
    std::uint64_t generation_id = 0)
{
    return std::make_shared<FakeSurface>(
        VideoSurfaceDescriptor {
            42,
            width,
            height,
            format,
            generation_id,
        });
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

void RejectsAProcessorOutputFromAnotherSurfaceGeneration()
{
    RecordingProcessor processor;
    processor.next_output = Surface(
        1'920,
        1'080,
        VRREC_SOURCE_PIXEL_FORMAT_NV12,
        8);
    RecordingEncoder encoder;
    ProcessingVideoEncoderSink sink(processor, encoder, 1'920, 1'080);

    const auto preparation = sink.Prepare({
        0,
        1,
        0,
        0,
        false,
        Surface(
            1'920,
            1'080,
            VRREC_SOURCE_PIXEL_FORMAT_BGRA8,
            9),
    });

    CHECK(preparation.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(processor.last_plan.source_generation_id == 9);
    CHECK(encoder.frames.empty());
}

void RejectsEveryInvalidProcessorOutputBoundary()
{
    const auto source = Surface(
        1'920,
        1'080,
        VRREC_SOURCE_PIXEL_FORMAT_BGRA8,
        9);
    const auto rejects = [&](std::shared_ptr<VideoSurface> candidate) {
        RecordingProcessor processor;
        processor.next_output = std::move(candidate);
        RecordingEncoder encoder;
        ProcessingVideoEncoderSink sink(
            processor, encoder, 1'920, 1'080);
        const auto prepared = sink.Prepare({0, 1, 0, 0, false, source});
        CHECK(prepared.status == VRREC_STATUS_INTERNAL_ERROR);
        CHECK(!prepared.frame.surface);
        CHECK(encoder.frames.empty());
    };

    rejects(nullptr);
    auto no_handle = Surface(
        1'920, 1'080, VRREC_SOURCE_PIXEL_FORMAT_NV12, 9);
    no_handle->native_handle = nullptr;
    rejects(no_handle);
    auto wrong_adapter = Surface(
        1'920, 1'080, VRREC_SOURCE_PIXEL_FORMAT_NV12, 9);
    wrong_adapter = std::make_shared<FakeSurface>(VideoSurfaceDescriptor {
        43, 1'920, 1'080, VRREC_SOURCE_PIXEL_FORMAT_NV12, 9});
    rejects(wrong_adapter);
    rejects(Surface(1'920, 1'080, VRREC_SOURCE_PIXEL_FORMAT_NV12, 8));
    rejects(Surface(1'280, 1'080, VRREC_SOURCE_PIXEL_FORMAT_NV12, 9));
    rejects(Surface(1'920, 720, VRREC_SOURCE_PIXEL_FORMAT_NV12, 9));
    rejects(Surface(1'920, 1'080, VRREC_SOURCE_PIXEL_FORMAT_RGBA8, 9));
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

void ValidatesPreparationWriteAndFinishLifecycleBoundaries()
{
    const auto source = Surface(
        1'920, 1'080, VRREC_SOURCE_PIXEL_FORMAT_BGRA8, 1);
    RecordingProcessor processor;
    processor.next_output = Surface(
        1'920, 1'080, VRREC_SOURCE_PIXEL_FORMAT_NV12, 1);
    RecordingEncoder encoder;
    encoder.write = {
        VRREC_STATUS_INTERNAL_ERROR,
        0,
        0,
        VideoEncoderFailureStage::None,
    };
    ProcessingVideoEncoderSink sink(processor, encoder, 1'920, 1'080);

    CHECK(sink.Prepare({}).status == VRREC_STATUS_INVALID_STATE);
    const auto prepared = sink.Prepare({0, 1, 0, 0, false, source});
    CHECK(prepared.status == VRREC_STATUS_OK);
    const auto failed_write = sink.WritePrepared(prepared.frame);
    CHECK(failed_write.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(failed_write.failure_stage == VideoEncoderFailureStage::Encoding);

    encoder.write.failure_stage = VideoEncoderFailureStage::Muxing;
    const auto classified = sink.WritePrepared(prepared.frame);
    CHECK(classified.failure_stage == VideoEncoderFailureStage::Muxing);

    CHECK(sink.Finish().status == VRREC_STATUS_OK);
    CHECK(sink.Finish().status == VRREC_STATUS_INVALID_STATE);
    CHECK(sink.Prepare({0, 1, 0, 0, false, source}).status ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(sink.WritePrepared(prepared.frame).status ==
          VRREC_STATUS_INVALID_STATE);

    RecordingProcessor aborted_processor;
    RecordingEncoder aborted_encoder;
    ProcessingVideoEncoderSink aborted(
        aborted_processor, aborted_encoder, 1'920, 1'080);
    aborted.Abort();
    CHECK(aborted.Prepare({0, 1, 0, 0, false, source}).status ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(aborted.WritePrepared({}).status == VRREC_STATUS_INVALID_STATE);
    CHECK(aborted.Finish().status == VRREC_STATUS_INVALID_STATE);
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

void RejectsEveryInvalidLiveLayoutBoundary()
{
    RecordingProcessor processor;
    RecordingEncoder encoder;
    ProcessingVideoEncoderSink sink(processor, encoder, 1'920, 1'080);
    const auto valid = PortraitLayout();
    const auto rejects = [&](const auto mutate) {
        auto layout = valid;
        mutate(layout);
        CHECK(sink.UpdateVideoLayout(layout) ==
              VRREC_STATUS_INVALID_ARGUMENT);
    };

    rejects([](auto &value) { --value.struct_size; });
    rejects([](auto &value) { ++value.abi_version; });
    rejects([](auto &value) { value.source_width = 0; });
    rejects([](auto &value) { value.source_height = 0; });
    rejects([](auto &value) { --value.canvas_width; });
    rejects([](auto &value) { --value.canvas_height; });
    rejects([](auto &value) { value.destination_width = 0; });
    rejects([](auto &value) { value.destination_height = 0; });
    rejects([](auto &value) { --value.destination_width; });
    rejects([](auto &value) { --value.destination_height; });
    rejects([](auto &value) {
        value.destination_x = value.canvas_width + 1;
    });
    rejects([](auto &value) {
        value.destination_y = value.canvas_height + 1;
    });
    rejects([](auto &value) {
        value.destination_width = value.canvas_width;
    });
    rejects([](auto &value) {
        value.destination_height = value.canvas_height + 2;
    });
    rejects([](auto &value) {
        value.canvas_background =
            static_cast<vrrec_canvas_background_t>(99);
    });
    rejects([](auto &value) {
        value.rotation = static_cast<vrrec_video_rotation_t>(99);
    });
    CHECK(sink.UpdateVideoLayout(valid) == VRREC_STATUS_OK);
}

void RejectsAFrameWhenNoProcessingPlanCanBeCreated()
{
    RecordingProcessor processor;
    RecordingEncoder encoder;
    ProcessingVideoEncoderSink sink(processor, encoder, 1'919, 1'080);
    const auto preparation = sink.Prepare({
        0,
        1,
        0,
        0,
        false,
        Surface(1'920, 1'080, VRREC_SOURCE_PIXEL_FORMAT_BGRA8, 1),
    });
    CHECK(preparation.status == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(processor.process_calls == 0);
}

}

int main()
{
    ConvertsToValidatedNv12BeforeCallingTheEncoder();
    PreparesAnOwnedFrameWithoutCallingTheEncoder();
    ClassifiesProcessorFailureAndSkipsTheEncoder();
    RejectsAnInvalidProcessorOutputSurface();
    RejectsAProcessorOutputFromAnotherSurfaceGeneration();
    RejectsEveryInvalidProcessorOutputBoundary();
    RejectsAnInputSurfaceWithoutANativeHandleBeforeProcessing();
    DelegatesFinishAndAbortsProcessorBeforeEncoder();
    ValidatesPreparationWriteAndFinishLifecycleBoundaries();
    AppliesValidatedLiveLayoutToTheNextFrame();
    FinishTerminalizesFrameProcessingAndLayoutUpdates();
    AbortPreventsAnInFlightProcessedFrameFromReachingTheEncoder();
    FinishPreventsAnInFlightProcessedFrameFromReachingTheEncoder();
    RejectsInvalidUpdatesAndMismatchedFramesWithoutLosingTheLastLayout();
    RejectsEveryInvalidLiveLayoutBoundary();
    RejectsAFrameWhenNoProcessingPlanCanBeCreated();
    return 0;
}
