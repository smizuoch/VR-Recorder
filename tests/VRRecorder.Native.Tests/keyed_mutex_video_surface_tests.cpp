#include "keyed_mutex_video_surface.hpp"

#include <chrono>
#include <cstddef>
#include <cstdlib>
#include <deque>
#include <iostream>
#include <memory>
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

class ScriptedKeyedMutexPort final : public KeyedMutexPort {
public:
    void *NativeHandle() const noexcept override
    {
        return native_handle;
    }

    KeyedMutexOperationResult Acquire(
        std::chrono::milliseconds timeout) noexcept override
    {
        ++acquire_calls;
        last_timeout = timeout;
        if (acquire_results.empty()) {
            return KeyedMutexOperationResult::Failed;
        }
        const auto result = acquire_results.front();
        acquire_results.pop_front();
        return result;
    }

    KeyedMutexOperationResult Release() noexcept override
    {
        ++release_calls;
        if (release_observer != nullptr) {
            ++*release_observer;
        }
        return release_result;
    }

    void *native_handle = reinterpret_cast<void *>(1);
    std::deque<KeyedMutexOperationResult> acquire_results;
    KeyedMutexOperationResult release_result =
        KeyedMutexOperationResult::Succeeded;
    std::chrono::milliseconds last_timeout {0};
    std::size_t acquire_calls = 0;
    std::size_t release_calls = 0;
    std::size_t *release_observer = nullptr;
};

VideoSurfaceDescriptor Descriptor()
{
    return {
        42,
        1'920,
        1'080,
        VRREC_SOURCE_PIXEL_FORMAT_BGRA8,
        7,
    };
}

struct SurfaceAndPort final {
    std::shared_ptr<KeyedMutexVideoSurface> surface;
    ScriptedKeyedMutexPort *port = nullptr;
};

SurfaceAndPort CreateSurface()
{
    auto port = std::make_unique<ScriptedKeyedMutexPort>();
    auto *observed = port.get();
    vrrec_status_t status = VRREC_STATUS_INTERNAL_ERROR;
    auto surface = CreateKeyedMutexVideoSurface(
        std::move(port),
        Descriptor(),
        status);
    CHECK(status == VRREC_STATUS_OK);
    CHECK(surface != nullptr);
    return {std::move(surface), observed};
}

void TimeoutIsRetryableAndSuccessReleasesOnce()
{
    auto owner = CreateSurface();
    owner.port->acquire_results = {
        KeyedMutexOperationResult::Timeout,
        KeyedMutexOperationResult::Succeeded,
    };

    CHECK(owner.surface->AcquireForRead(std::chrono::milliseconds(3)) ==
          VideoSurfaceAcquireResult::Timeout);
    CHECK(owner.surface->AcquireForRead(std::chrono::milliseconds(5)) ==
          VideoSurfaceAcquireResult::Acquired);
    CHECK(owner.surface->ReleaseFromRead() == VRREC_STATUS_OK);

    CHECK(owner.port->acquire_calls == 2);
    CHECK(owner.port->last_timeout == std::chrono::milliseconds(5));
    CHECK(owner.port->release_calls == 1);
    CHECK(owner.surface->LastResult() ==
          KeyedMutexOperationResult::Succeeded);
}

void RejectsDuplicateAcquireAndReleaseWithoutCallingThePort()
{
    auto owner = CreateSurface();
    owner.port->acquire_results = {
        KeyedMutexOperationResult::Succeeded,
    };

    CHECK(owner.surface->AcquireForRead(std::chrono::milliseconds(5)) ==
          VideoSurfaceAcquireResult::Acquired);
    CHECK(owner.surface->AcquireForRead(std::chrono::milliseconds(5)) ==
          VideoSurfaceAcquireResult::Failed);
    CHECK(owner.port->acquire_calls == 1);
    CHECK(owner.surface->ReleaseFromRead() == VRREC_STATUS_OK);
    CHECK(owner.surface->ReleaseFromRead() == VRREC_STATUS_INVALID_STATE);
    CHECK(owner.port->release_calls == 1);
}

void MapsTerminalAcquireFailuresAndRejectsRetries()
{
    for (const auto &[operation, expected] : {
             std::pair {
                 KeyedMutexOperationResult::Abandoned,
                 VideoSurfaceAcquireResult::Abandoned},
             std::pair {
                 KeyedMutexOperationResult::DeviceRemoved,
                 VideoSurfaceAcquireResult::DeviceRemoved},
             std::pair {
                 KeyedMutexOperationResult::DeviceReset,
                 VideoSurfaceAcquireResult::DeviceReset},
             std::pair {
                 KeyedMutexOperationResult::Failed,
                 VideoSurfaceAcquireResult::Failed},
         }) {
        auto owner = CreateSurface();
        owner.port->acquire_results = {
            operation,
            KeyedMutexOperationResult::Succeeded,
        };

        CHECK(owner.surface->AcquireForRead(
                  std::chrono::milliseconds(5)) == expected);
        CHECK(owner.surface->AcquireForRead(
                  std::chrono::milliseconds(5)) ==
              VideoSurfaceAcquireResult::Failed);
        CHECK(owner.port->acquire_calls == 1);
        CHECK(owner.port->release_calls == 0);
        CHECK(owner.surface->LastResult() == operation);
    }
}

void ReleaseFailureIsTerminalAndNotRetriedByDestruction()
{
    auto owner = CreateSurface();
    std::size_t observed_release_calls = 0;
    owner.port->release_observer = &observed_release_calls;
    owner.port->acquire_results = {
        KeyedMutexOperationResult::Succeeded,
    };
    owner.port->release_result =
        KeyedMutexOperationResult::DeviceRemoved;

    CHECK(owner.surface->AcquireForRead(std::chrono::milliseconds(5)) ==
          VideoSurfaceAcquireResult::Acquired);
    CHECK(owner.surface->ReleaseFromRead() ==
          VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(owner.port->release_calls == 1);
    CHECK(owner.surface->AcquireForRead(std::chrono::milliseconds(5)) ==
          VideoSurfaceAcquireResult::Failed);
    owner.surface.reset();
    CHECK(observed_release_calls == 1);
}

void DestructionReleasesAHeldMutexExactlyOnce()
{
    auto owner = CreateSurface();
    std::size_t observed_release_calls = 0;
    owner.port->release_observer = &observed_release_calls;
    owner.port->acquire_results = {
        KeyedMutexOperationResult::Succeeded,
    };
    CHECK(owner.surface->AcquireForRead(std::chrono::milliseconds(5)) ==
          VideoSurfaceAcquireResult::Acquired);

    owner.surface.reset();

    CHECK(observed_release_calls == 1);
}

void RejectsInvalidPortsAndDescriptors()
{
    vrrec_status_t status = VRREC_STATUS_OK;
    CHECK(!CreateKeyedMutexVideoSurface({}, Descriptor(), status));
    CHECK(status == VRREC_STATUS_INVALID_ARGUMENT);

    auto invalid_descriptor = Descriptor();
    invalid_descriptor.generation_id = 0;
    auto port = std::make_unique<ScriptedKeyedMutexPort>();
    CHECK(!CreateKeyedMutexVideoSurface(
        std::move(port), invalid_descriptor, status));
    CHECK(status == VRREC_STATUS_INVALID_ARGUMENT);

    auto no_handle = std::make_unique<ScriptedKeyedMutexPort>();
    no_handle->native_handle = nullptr;
    CHECK(!CreateKeyedMutexVideoSurface(
        std::move(no_handle), Descriptor(), status));
    CHECK(status == VRREC_STATUS_INVALID_ARGUMENT);
}

}

int main()
{
    TimeoutIsRetryableAndSuccessReleasesOnce();
    RejectsDuplicateAcquireAndReleaseWithoutCallingThePort();
    MapsTerminalAcquireFailuresAndRejectsRetries();
    ReleaseFailureIsTerminalAndNotRetriedByDestruction();
    DestructionReleasesAHeldMutexExactlyOnce();
    RejectsInvalidPortsAndDescriptors();
    return 0;
}
