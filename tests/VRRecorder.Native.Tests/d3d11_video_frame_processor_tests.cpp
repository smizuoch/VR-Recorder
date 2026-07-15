#include "d3d11_video_frame_processor.hpp"

#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdlib>
#include <future>
#include <iostream>
#include <memory>
#include <mutex>
#include <utility>

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
    explicit FakeSurface(
        VideoSurfaceDescriptor descriptor,
        std::size_t *destruction_count = nullptr) noexcept
        : descriptor_(descriptor),
          destruction_count_(destruction_count)
    {
    }

    ~FakeSurface()
    {
        if (destruction_count_ != nullptr) {
            ++*destruction_count_;
        }
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
    std::size_t *destruction_count_;
};

class FakeD3d11VideoProcessorPort final : public D3d11VideoProcessorPort {
public:
    D3d11VideoProcessorResult Convert(
        const std::shared_ptr<VideoSurface> &source,
        const VideoProcessingPlan &plan,
        std::shared_ptr<VideoSurface> &output) noexcept override
    {
        std::unique_lock lock(mutex);
        ++convert_calls;
        last_source = source;
        last_plan = plan;
        convert_entered = true;
        changed.notify_all();
        if (block_convert) {
            changed.wait(lock, [&] { return release_convert; });
        }
        output = std::move(next_output);
        return result;
    }

    void Abort() noexcept override
    {
        {
            const std::lock_guard lock(mutex);
            ++abort_calls;
            release_convert = true;
        }
        changed.notify_all();
    }

    void WaitForConvert()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return convert_entered; });
    }

    std::mutex mutex;
    std::condition_variable changed;
    D3d11VideoProcessorResult result =
        D3d11VideoProcessorResult::Converted;
    std::shared_ptr<VideoSurface> next_output;
    std::shared_ptr<VideoSurface> last_source;
    VideoProcessingPlan last_plan {};
    std::size_t convert_calls = 0;
    std::size_t abort_calls = 0;
    bool block_convert = false;
    bool convert_entered = false;
    bool release_convert = false;
};

std::shared_ptr<FakeSurface> Surface(
    vrrec_source_pixel_format_t format,
    std::uint64_t generation_id = 7,
    std::uint64_t adapter_luid = 42,
    std::uint32_t width = 1'920,
    std::uint32_t height = 1'080,
    std::size_t *destruction_count = nullptr)
{
    return std::make_shared<FakeSurface>(
        VideoSurfaceDescriptor {
            adapter_luid,
            width,
            height,
            format,
            generation_id,
        },
        destruction_count);
}

VideoProcessingPlan Plan(const std::shared_ptr<VideoSurface> &source)
{
    VideoProcessingPlan plan {};
    CHECK(CreateSingleFileVideoProcessingPlan(
              source->Descriptor(),
              1'920,
              1'080,
              plan) == VRREC_STATUS_OK);
    return plan;
}

void ConvertsThroughTheInjectedPortAndReturnsOwnedNv12()
{
    FakeD3d11VideoProcessorPort port;
    const auto source = Surface(VRREC_SOURCE_PIXEL_FORMAT_BGRA8);
    const auto expected = Surface(VRREC_SOURCE_PIXEL_FORMAT_NV12);
    port.next_output = expected;
    D3d11VideoFrameProcessor processor(port);
    std::shared_ptr<VideoSurface> output;

    CHECK(processor.Process(source, Plan(source), output) ==
          VRREC_STATUS_OK);

    CHECK(output == expected);
    CHECK(output != source);
    CHECK(port.convert_calls == 1);
    CHECK(port.last_source == source);
    CHECK(port.last_plan.adapter_luid == 42);
    CHECK(port.last_plan.source_generation_id == 7);
    CHECK(processor.LastResult() ==
          D3d11VideoProcessorResult::Converted);
}

void DistinguishesDeviceRemovedAndResetAsTerminalFailures()
{
    for (const auto result : {
             D3d11VideoProcessorResult::DeviceRemoved,
             D3d11VideoProcessorResult::DeviceReset,
         }) {
        FakeD3d11VideoProcessorPort port;
        port.result = result;
        const auto source = Surface(VRREC_SOURCE_PIXEL_FORMAT_BGRA8);
        D3d11VideoFrameProcessor processor(port);
        std::shared_ptr<VideoSurface> output;

        CHECK(processor.Process(source, Plan(source), output) ==
              VRREC_STATUS_BACKEND_UNAVAILABLE);
        CHECK(processor.LastResult() == result);
        CHECK(!output);
        CHECK(port.abort_calls == 1);
        CHECK(processor.Process(source, Plan(source), output) ==
              VRREC_STATUS_INVALID_STATE);
        CHECK(port.convert_calls == 1);
        CHECK(port.abort_calls == 1);
    }
}

void RejectsAndReleasesAnOutputFromTheWrongGeneration()
{
    std::size_t destructions = 0;
    FakeD3d11VideoProcessorPort port;
    port.next_output = Surface(
        VRREC_SOURCE_PIXEL_FORMAT_NV12,
        6,
        42,
        1'920,
        1'080,
        &destructions);
    std::weak_ptr<VideoSurface> rejected = port.next_output;
    const auto source = Surface(VRREC_SOURCE_PIXEL_FORMAT_BGRA8, 7);
    D3d11VideoFrameProcessor processor(port);
    std::shared_ptr<VideoSurface> output;

    CHECK(processor.Process(source, Plan(source), output) ==
          VRREC_STATUS_INTERNAL_ERROR);

    CHECK(!output);
    CHECK(rejected.expired());
    CHECK(destructions == 1);
    CHECK(processor.LastResult() == D3d11VideoProcessorResult::Failed);
    CHECK(port.abort_calls == 1);
}

void RejectsAMismatchedPlanBeforeCallingThePort()
{
    FakeD3d11VideoProcessorPort port;
    const auto source = Surface(VRREC_SOURCE_PIXEL_FORMAT_RGBA8);
    auto plan = Plan(source);
    ++plan.source_generation_id;
    D3d11VideoFrameProcessor processor(port);
    std::shared_ptr<VideoSurface> output;

    CHECK(processor.Process(source, plan, output) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(port.convert_calls == 0);
    CHECK(port.abort_calls == 0);
}

void AbortDuringConvertReleasesTheLateOutputAndThePortOnce()
{
    std::size_t destructions = 0;
    FakeD3d11VideoProcessorPort port;
    port.block_convert = true;
    port.next_output = Surface(
        VRREC_SOURCE_PIXEL_FORMAT_NV12,
        7,
        42,
        1'920,
        1'080,
        &destructions);
    std::weak_ptr<VideoSurface> rejected = port.next_output;
    const auto source = Surface(VRREC_SOURCE_PIXEL_FORMAT_BGRA8);
    D3d11VideoFrameProcessor processor(port);
    std::shared_ptr<VideoSurface> output;

    auto processing = std::async(std::launch::async, [&] {
        return processor.Process(source, Plan(source), output);
    });
    port.WaitForConvert();
    processor.Abort();
    processor.Abort();

    CHECK(processing.get() == VRREC_STATUS_INVALID_STATE);
    CHECK(!output);
    CHECK(rejected.expired());
    CHECK(destructions == 1);
    CHECK(port.abort_calls == 1);
    CHECK(processor.LastResult() == D3d11VideoProcessorResult::Aborted);
}

void MapsPortOutOfMemoryWithoutLeakingAnOutput()
{
    FakeD3d11VideoProcessorPort port;
    port.result = D3d11VideoProcessorResult::OutOfMemory;
    const auto source = Surface(VRREC_SOURCE_PIXEL_FORMAT_BGRA8);
    D3d11VideoFrameProcessor processor(port);
    std::shared_ptr<VideoSurface> output;

    CHECK(processor.Process(source, Plan(source), output) ==
          VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(!output);
    CHECK(port.abort_calls == 1);
}

}

int main()
{
    ConvertsThroughTheInjectedPortAndReturnsOwnedNv12();
    DistinguishesDeviceRemovedAndResetAsTerminalFailures();
    RejectsAndReleasesAnOutputFromTheWrongGeneration();
    RejectsAMismatchedPlanBeforeCallingThePort();
    AbortDuringConvertReleasesTheLateOutputAndThePortOnce();
    MapsPortOutOfMemoryWithoutLeakingAnOutput();
    return 0;
}
