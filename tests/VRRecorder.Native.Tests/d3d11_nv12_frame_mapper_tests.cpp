#include "d3d11_nv12_frame_mapper.hpp"

#include <chrono>
#include <array>
#include <condition_variable>
#include <cstddef>
#include <cstdlib>
#include <iostream>
#include <memory>
#include <mutex>
#include <thread>
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
using namespace std::chrono_literals;

std::array<std::byte, 64> view_storage {};

SystemMemoryNv12FrameView TestView(
    std::uint32_t width = 4,
    std::uint32_t height = 4,
    std::uint32_t y_stride = 4,
    std::uint32_t uv_stride = 4,
    std::size_t y_size = 16,
    std::size_t uv_size = 8)
{
    return {
        width,
        height,
        y_stride,
        uv_stride,
        std::span<const std::byte>(view_storage.data(), y_size),
        std::span<const std::byte>(view_storage.data(), uv_size),
        -1,
    };
}

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

class FakeMapping final : public SystemMemoryNv12FrameMapping {
public:
    explicit FakeMapping(
        SystemMemoryNv12FrameView configured,
        std::size_t &destruction_count)
        : configured_(configured), destruction_count_(destruction_count)
    {
        y.resize(configured_.y_plane.size());
        uv.resize(configured_.uv_plane.size());
    }

    ~FakeMapping() override
    {
        ++destruction_count_;
    }

    SystemMemoryNv12FrameView View() const noexcept override
    {
        auto result = configured_;
        result.y_plane = y;
        result.uv_plane = uv;
        return result;
    }

private:
    SystemMemoryNv12FrameView configured_;
    std::size_t &destruction_count_;
    std::vector<std::byte> y;
    std::vector<std::byte> uv;
};

class FakeReadbackPort final : public D3d11Nv12ReadbackPort {
public:
    SystemMemoryNv12FrameMapResult Read(
        const std::shared_ptr<VideoSurface> &surface) noexcept override
    {
        std::unique_lock lock(mutex);
        ++read_calls;
        observed_surface = surface;
        entered = true;
        changed.notify_all();
        if (block) {
            changed.wait(lock, [this] { return released; });
        }
        if (!return_mapping) {
            return {status, {}};
        }
        auto created = std::unique_ptr<SystemMemoryNv12FrameMapping>(
            new (std::nothrow) FakeMapping(view, destruction_count));
        return {
            created ? status : VRREC_STATUS_OUT_OF_MEMORY,
            std::move(created),
        };
    }

    void Abort() noexcept override
    {
        const std::lock_guard lock(mutex);
        ++abort_calls;
        released = true;
        changed.notify_all();
    }

    void WaitUntilEntered()
    {
        std::unique_lock lock(mutex);
        CHECK(changed.wait_for(lock, 1s, [this] { return entered; }));
    }

    std::mutex mutex;
    std::condition_variable changed;
    vrrec_status_t status = VRREC_STATUS_OK;
    SystemMemoryNv12FrameView view = TestView();
    std::shared_ptr<VideoSurface> observed_surface;
    std::size_t read_calls = 0;
    std::size_t abort_calls = 0;
    std::size_t destruction_count = 0;
    bool return_mapping = true;
    bool block = false;
    bool entered = false;
    bool released = false;
};

std::shared_ptr<FakeSurface> Surface(
    VideoSurfaceDescriptor descriptor = {
        42,
        4,
        4,
        VRREC_SOURCE_PIXEL_FORMAT_NV12,
        7,
    })
{
    return std::make_shared<FakeSurface>(descriptor);
}

ScheduledVideoFrame Frame(const std::shared_ptr<VideoSurface> &surface)
{
    ScheduledVideoFrame result {};
    result.output_tick = 3;
    result.surface = surface;
    return result;
}

void ReturnsAValidatedReadbackMapping()
{
    FakeReadbackPort port;
    D3d11SystemMemoryNv12FrameMapper mapper(port);
    const auto surface = Surface();

    auto result = mapper.Map(Frame(surface));

    CHECK(result.status == VRREC_STATUS_OK);
    CHECK(result.mapping != nullptr);
    CHECK(port.read_calls == 1);
    CHECK(port.observed_surface == surface);
    const auto view = result.mapping->View();
    CHECK(view.width == 4);
    CHECK(view.height == 4);
    CHECK(view.y_plane.size() == 16);
    CHECK(view.uv_plane.size() == 8);
}

void RejectsInvalidSurfacesBeforeCallingThePort()
{
    const VideoSurfaceDescriptor invalid_descriptors[] = {
        {0, 4, 4, VRREC_SOURCE_PIXEL_FORMAT_NV12, 7},
        {42, 0, 4, VRREC_SOURCE_PIXEL_FORMAT_NV12, 7},
        {42, 3, 4, VRREC_SOURCE_PIXEL_FORMAT_NV12, 7},
        {42, 4, 3, VRREC_SOURCE_PIXEL_FORMAT_NV12, 7},
        {42, 16'386, 4, VRREC_SOURCE_PIXEL_FORMAT_NV12, 7},
        {42, 4, 4, VRREC_SOURCE_PIXEL_FORMAT_BGRA8, 7},
        {42, 4, 4, VRREC_SOURCE_PIXEL_FORMAT_NV12, 0},
    };

    for (const auto descriptor : invalid_descriptors) {
        FakeReadbackPort port;
        D3d11SystemMemoryNv12FrameMapper mapper(port);
        CHECK(mapper.Map(Frame(Surface(descriptor))).status ==
              VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(port.read_calls == 0);
    }

    FakeReadbackPort port;
    D3d11SystemMemoryNv12FrameMapper mapper(port);
    CHECK(mapper.Map(Frame({})).status == VRREC_STATUS_INVALID_ARGUMENT);
    const auto without_handle = Surface();
    without_handle->native_handle = nullptr;
    CHECK(mapper.Map(Frame(without_handle)).status ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(port.read_calls == 0);
}

void RejectsInconsistentPortResultsAndInvalidPlanes()
{
    {
        FakeReadbackPort port;
        port.return_mapping = false;
        D3d11SystemMemoryNv12FrameMapper mapper(port);
        CHECK(mapper.Map(Frame(Surface())).status ==
              VRREC_STATUS_INTERNAL_ERROR);
    }
    {
        FakeReadbackPort port;
        port.status = VRREC_STATUS_BACKEND_UNAVAILABLE;
        D3d11SystemMemoryNv12FrameMapper mapper(port);
        CHECK(mapper.Map(Frame(Surface())).status ==
              VRREC_STATUS_INTERNAL_ERROR);
        CHECK(port.destruction_count == 1);
    }

    const SystemMemoryNv12FrameView invalid_views[] = {
        TestView(2, 4),
        TestView(4, 2),
        TestView(4, 4, 3, 4),
        TestView(4, 4, 4, 3),
        TestView(4, 4, 4, 4, 15, 8),
        TestView(4, 4, 4, 4, 16, 7),
    };
    for (const auto view : invalid_views) {
        FakeReadbackPort port;
        port.view = view;
        D3d11SystemMemoryNv12FrameMapper mapper(port);
        CHECK(mapper.Map(Frame(Surface())).status ==
              VRREC_STATUS_INTERNAL_ERROR);
        CHECK(port.destruction_count == 1);
    }
}

void PropagatesAConsistentReadbackFailure()
{
    FakeReadbackPort port;
    port.status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    port.return_mapping = false;
    D3d11SystemMemoryNv12FrameMapper mapper(port);

    CHECK(mapper.Map(Frame(Surface())).status ==
          VRREC_STATUS_BACKEND_UNAVAILABLE);
}

void AbortWinsAgainstAnInFlightReadAndIsForwardedOnce()
{
    FakeReadbackPort port;
    port.block = true;
    D3d11SystemMemoryNv12FrameMapper mapper(port);
    SystemMemoryNv12FrameMapResult result;
    std::thread reading([&] { result = mapper.Map(Frame(Surface())); });

    port.WaitUntilEntered();
    mapper.Abort();
    mapper.Abort();
    reading.join();

    CHECK(result.status == VRREC_STATUS_INVALID_STATE);
    CHECK(result.mapping == nullptr);
    CHECK(port.abort_calls == 1);
    CHECK(port.destruction_count == 1);
    CHECK(mapper.Map(Frame(Surface())).status ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(port.read_calls == 1);
}

}

int main()
{
    ReturnsAValidatedReadbackMapping();
    RejectsInvalidSurfacesBeforeCallingThePort();
    RejectsInconsistentPortResultsAndInvalidPlanes();
    PropagatesAConsistentReadbackFailure();
    AbortWinsAgainstAnInFlightReadAndIsForwardedOnce();
    return 0;
}
