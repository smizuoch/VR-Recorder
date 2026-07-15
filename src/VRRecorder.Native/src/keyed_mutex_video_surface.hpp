#ifndef VRRECORDER_NATIVE_KEYED_MUTEX_VIDEO_SURFACE_HPP
#define VRRECORDER_NATIVE_KEYED_MUTEX_VIDEO_SURFACE_HPP

#include <chrono>
#include <memory>
#include <mutex>

#include "video_surface.hpp"

namespace vrrecorder::native {

enum class KeyedMutexOperationResult {
    None,
    Succeeded,
    Timeout,
    Abandoned,
    DeviceRemoved,
    DeviceReset,
    Failed,
};

class KeyedMutexPort {
public:
    virtual ~KeyedMutexPort() = default;

    virtual void *NativeHandle() const noexcept = 0;
    virtual KeyedMutexOperationResult Acquire(
        std::chrono::milliseconds timeout) noexcept = 0;
    virtual KeyedMutexOperationResult Release() noexcept = 0;
};

class KeyedMutexVideoSurface final : public VideoSurface {
public:
    ~KeyedMutexVideoSurface() override;

    VideoSurfaceDescriptor Descriptor() const noexcept override;
    void *NativeHandle() const noexcept override;
    VideoSurfaceAcquireResult AcquireForRead(
        std::chrono::milliseconds timeout) noexcept override;
    vrrec_status_t ReleaseFromRead() noexcept override;
    KeyedMutexOperationResult LastResult() const noexcept;

private:
    friend std::shared_ptr<KeyedMutexVideoSurface>
    CreateKeyedMutexVideoSurface(
        std::unique_ptr<KeyedMutexPort> port,
        VideoSurfaceDescriptor descriptor,
        vrrec_status_t &status) noexcept;

    KeyedMutexVideoSurface(
        std::unique_ptr<KeyedMutexPort> port,
        VideoSurfaceDescriptor descriptor) noexcept;

    std::unique_ptr<KeyedMutexPort> port_;
    VideoSurfaceDescriptor descriptor_;
    mutable std::mutex mutex_;
    KeyedMutexOperationResult last_result_ =
        KeyedMutexOperationResult::None;
    bool acquired_ = false;
    bool terminal_ = false;
};

std::shared_ptr<KeyedMutexVideoSurface>
CreateKeyedMutexVideoSurface(
    std::unique_ptr<KeyedMutexPort> port,
    VideoSurfaceDescriptor descriptor,
    vrrec_status_t &status) noexcept;

}

#endif
