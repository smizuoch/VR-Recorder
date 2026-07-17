#include "keyed_mutex_video_surface.hpp"
#include "allocation_failure_test_support.hpp"

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
             std::pair {
                 KeyedMutexOperationResult::None,
                 VideoSurfaceAcquireResult::Failed},
             std::pair {
                 static_cast<KeyedMutexOperationResult>(UINT32_MAX),
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

void RejectsNegativeAcquireTimeoutWithoutCallingThePort()
{
    auto owner = CreateSurface();
    owner.port->acquire_results = {
        KeyedMutexOperationResult::Succeeded,
    };

    CHECK(owner.surface->AcquireForRead(std::chrono::milliseconds(-1)) ==
          VideoSurfaceAcquireResult::Failed);
    CHECK(owner.port->acquire_calls == 0);
    CHECK(owner.surface->AcquireForRead(std::chrono::milliseconds(0)) ==
          VideoSurfaceAcquireResult::Acquired);
    CHECK(owner.surface->ReleaseFromRead() == VRREC_STATUS_OK);
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

void MapsEveryTerminalReleaseFailure()
{
    using Case = std::pair<KeyedMutexOperationResult, vrrec_status_t>;
    for (const auto &[operation, expected] : {
             Case {KeyedMutexOperationResult::Abandoned,
                   VRREC_STATUS_BACKEND_UNAVAILABLE},
             Case {KeyedMutexOperationResult::DeviceRemoved,
                   VRREC_STATUS_BACKEND_UNAVAILABLE},
             Case {KeyedMutexOperationResult::DeviceReset,
                   VRREC_STATUS_BACKEND_UNAVAILABLE},
             Case {KeyedMutexOperationResult::None,
                   VRREC_STATUS_INTERNAL_ERROR},
             Case {KeyedMutexOperationResult::Timeout,
                   VRREC_STATUS_INTERNAL_ERROR},
             Case {KeyedMutexOperationResult::Failed,
                   VRREC_STATUS_INTERNAL_ERROR},
             Case {static_cast<KeyedMutexOperationResult>(UINT32_MAX),
                   VRREC_STATUS_INTERNAL_ERROR},
         }) {
        auto owner = CreateSurface();
        owner.port->acquire_results = {
            KeyedMutexOperationResult::Succeeded,
        };
        owner.port->release_result = operation;

        CHECK(owner.surface->AcquireForRead(std::chrono::milliseconds(1)) ==
              VideoSurfaceAcquireResult::Acquired);
        CHECK(owner.surface->ReleaseFromRead() == expected);
        CHECK(owner.surface->LastResult() == operation);
        CHECK(owner.surface->AcquireForRead(std::chrono::milliseconds(1)) ==
              VideoSurfaceAcquireResult::Failed);
    }
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

    for (const auto mutate : {
             std::uint32_t {0},
             std::uint32_t {1},
             std::uint32_t {2},
             std::uint32_t {3},
         }) {
        auto descriptor = Descriptor();
        if (mutate == 0) {
            descriptor.adapter_luid = 0;
        } else if (mutate == 1) {
            descriptor.width = 0;
        } else if (mutate == 2) {
            descriptor.height = 0;
        } else {
            descriptor.pixel_format = UINT32_MAX;
        }
        auto invalid_port = std::make_unique<ScriptedKeyedMutexPort>();
        CHECK(!CreateKeyedMutexVideoSurface(
            std::move(invalid_port), descriptor, status));
        CHECK(status == VRREC_STATUS_INVALID_ARGUMENT);
    }

    for (const auto pixel_format : {
             VRREC_SOURCE_PIXEL_FORMAT_BGRA8,
             VRREC_SOURCE_PIXEL_FORMAT_RGBA8,
             VRREC_SOURCE_PIXEL_FORMAT_NV12,
         }) {
        auto descriptor = Descriptor();
        descriptor.pixel_format = pixel_format;
        auto valid_port = std::make_unique<ScriptedKeyedMutexPort>();
        auto surface = CreateKeyedMutexVideoSurface(
            std::move(valid_port), descriptor, status);
        CHECK(status == VRREC_STATUS_OK);
        CHECK(surface != nullptr);
    }
}

void ReportsEverySurfaceOwnershipAllocationFailure()
{
    for (const auto failing_allocation : {std::size_t {1}, std::size_t {2}}) {
        auto port = std::make_unique<ScriptedKeyedMutexPort>();
        auto status = VRREC_STATUS_OK;
        allocation_failure::fail_on_allocation = failing_allocation;
        auto surface = CreateKeyedMutexVideoSurface(
            std::move(port),
            Descriptor(),
            status);
        allocation_failure::fail_on_allocation = 0;

        CHECK(surface == nullptr);
        CHECK(status == VRREC_STATUS_OUT_OF_MEMORY);
    }
}

}

int main()
{
    TimeoutIsRetryableAndSuccessReleasesOnce();
    RejectsDuplicateAcquireAndReleaseWithoutCallingThePort();
    MapsTerminalAcquireFailuresAndRejectsRetries();
    RejectsNegativeAcquireTimeoutWithoutCallingThePort();
    ReleaseFailureIsTerminalAndNotRetriedByDestruction();
    MapsEveryTerminalReleaseFailure();
    DestructionReleasesAHeldMutexExactlyOnce();
    RejectsInvalidPortsAndDescriptors();
    ReportsEverySurfaceOwnershipAllocationFailure();
    return 0;
}
