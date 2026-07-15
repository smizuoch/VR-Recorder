#include "keyed_mutex_video_surface.hpp"

#include <new>
#include <utility>

namespace vrrecorder::native {

KeyedMutexVideoSurface::KeyedMutexVideoSurface(
    std::unique_ptr<KeyedMutexPort> port,
    VideoSurfaceDescriptor descriptor) noexcept
    : port_(std::move(port)), descriptor_(descriptor)
{
}

KeyedMutexVideoSurface::~KeyedMutexVideoSurface()
{
    const std::lock_guard lock(mutex_);
    if (acquired_) {
        acquired_ = false;
        last_result_ = port_->Release();
    }
    terminal_ = true;
}

VideoSurfaceDescriptor
KeyedMutexVideoSurface::Descriptor() const noexcept
{
    return descriptor_;
}

void *KeyedMutexVideoSurface::NativeHandle() const noexcept
{
    return port_->NativeHandle();
}

VideoSurfaceAcquireResult KeyedMutexVideoSurface::AcquireForRead(
    std::chrono::milliseconds timeout) noexcept
{
    const std::lock_guard lock(mutex_);
    if (terminal_ || acquired_ || timeout.count() < 0) {
        return VideoSurfaceAcquireResult::Failed;
    }

    last_result_ = port_->Acquire(timeout);
    switch (last_result_) {
    case KeyedMutexOperationResult::Succeeded:
        acquired_ = true;
        return VideoSurfaceAcquireResult::Acquired;
    case KeyedMutexOperationResult::Timeout:
        return VideoSurfaceAcquireResult::Timeout;
    case KeyedMutexOperationResult::Abandoned:
        terminal_ = true;
        return VideoSurfaceAcquireResult::Abandoned;
    case KeyedMutexOperationResult::DeviceRemoved:
        terminal_ = true;
        return VideoSurfaceAcquireResult::DeviceRemoved;
    case KeyedMutexOperationResult::DeviceReset:
        terminal_ = true;
        return VideoSurfaceAcquireResult::DeviceReset;
    case KeyedMutexOperationResult::None:
    case KeyedMutexOperationResult::Failed:
        terminal_ = true;
        return VideoSurfaceAcquireResult::Failed;
    }
    terminal_ = true;
    return VideoSurfaceAcquireResult::Failed;
}

vrrec_status_t KeyedMutexVideoSurface::ReleaseFromRead() noexcept
{
    const std::lock_guard lock(mutex_);
    if (!acquired_) {
        return VRREC_STATUS_INVALID_STATE;
    }

    acquired_ = false;
    last_result_ = port_->Release();
    switch (last_result_) {
    case KeyedMutexOperationResult::Succeeded:
        return VRREC_STATUS_OK;
    case KeyedMutexOperationResult::Abandoned:
    case KeyedMutexOperationResult::DeviceRemoved:
    case KeyedMutexOperationResult::DeviceReset:
        terminal_ = true;
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    case KeyedMutexOperationResult::None:
    case KeyedMutexOperationResult::Timeout:
    case KeyedMutexOperationResult::Failed:
        terminal_ = true;
        return VRREC_STATUS_INTERNAL_ERROR;
    }
    terminal_ = true;
    return VRREC_STATUS_INTERNAL_ERROR;
}

KeyedMutexOperationResult
KeyedMutexVideoSurface::LastResult() const noexcept
{
    const std::lock_guard lock(mutex_);
    return last_result_;
}

std::shared_ptr<KeyedMutexVideoSurface>
CreateKeyedMutexVideoSurface(
    std::unique_ptr<KeyedMutexPort> port,
    VideoSurfaceDescriptor descriptor,
    vrrec_status_t &status) noexcept
{
    status = VRREC_STATUS_INVALID_ARGUMENT;
    const auto format_valid =
        descriptor.pixel_format == VRREC_SOURCE_PIXEL_FORMAT_BGRA8 ||
        descriptor.pixel_format == VRREC_SOURCE_PIXEL_FORMAT_RGBA8 ||
        descriptor.pixel_format == VRREC_SOURCE_PIXEL_FORMAT_NV12;
    if (!port || port->NativeHandle() == nullptr ||
        descriptor.adapter_luid == 0 || descriptor.width == 0 ||
        descriptor.height == 0 || descriptor.generation_id == 0 ||
        !format_valid) {
        return {};
    }

    auto *surface = new (std::nothrow) KeyedMutexVideoSurface(
        std::move(port),
        descriptor);
    if (surface == nullptr) {
        status = VRREC_STATUS_OUT_OF_MEMORY;
        return {};
    }

    try {
        auto owned = std::shared_ptr<KeyedMutexVideoSurface>(surface);
        status = VRREC_STATUS_OK;
        return owned;
    } catch (const std::bad_alloc &) {
        delete surface;
        status = VRREC_STATUS_OUT_OF_MEMORY;
        return {};
    } catch (...) {
        delete surface;
        status = VRREC_STATUS_INTERNAL_ERROR;
        return {};
    }
}

}
