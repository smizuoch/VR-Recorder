#ifndef VRRECORDER_NATIVE_D3D11_NV12_FRAME_MAPPER_HPP
#define VRRECORDER_NATIVE_D3D11_NV12_FRAME_MAPPER_HPP

#include <atomic>
#include <memory>

#include "ffmpeg_h264_system_memory_packet_encoder_adapter.hpp"

namespace vrrecorder::native {

class D3d11Nv12ReadbackPort {
public:
    virtual ~D3d11Nv12ReadbackPort() = default;

    virtual SystemMemoryNv12FrameMapResult Read(
        const std::shared_ptr<VideoSurface> &surface) noexcept = 0;
    virtual void Abort() noexcept = 0;
};

class D3d11SystemMemoryNv12FrameMapper final
    : public SystemMemoryNv12FrameMapper {
public:
    explicit D3d11SystemMemoryNv12FrameMapper(
        D3d11Nv12ReadbackPort &port) noexcept;

    SystemMemoryNv12FrameMapResult Map(
        const ScheduledVideoFrame &frame) noexcept override;
    void Abort() noexcept override;

private:
    D3d11Nv12ReadbackPort &port_;
    std::atomic_bool aborted_ = false;
};

}

#endif
