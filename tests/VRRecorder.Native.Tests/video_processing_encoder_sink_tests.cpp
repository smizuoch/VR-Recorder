#include "video_processing_encoder_sink.hpp"

#include <chrono>
#include <cstddef>
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

    void ReleaseFromRead() noexcept override
    {
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
        ++process_calls;
        last_source = source;
        last_plan = plan;
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

    std::shared_ptr<VideoSurface> next_output;
    std::shared_ptr<VideoSurface> last_source;
    VideoProcessingPlan last_plan {};
    vrrec_status_t status = VRREC_STATUS_OK;
    std::vector<int> *order = nullptr;
    std::size_t process_calls = 0;
    std::size_t abort_calls = 0;
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
}

}

int main()
{
    ConvertsToValidatedNv12BeforeCallingTheEncoder();
    ClassifiesProcessorFailureAndSkipsTheEncoder();
    RejectsAnInvalidProcessorOutputSurface();
    DelegatesFinishAndAbortsProcessorBeforeEncoder();
    return 0;
}
