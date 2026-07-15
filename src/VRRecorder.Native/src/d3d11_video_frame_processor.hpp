#ifndef VRRECORDER_NATIVE_D3D11_VIDEO_FRAME_PROCESSOR_HPP
#define VRRECORDER_NATIVE_D3D11_VIDEO_FRAME_PROCESSOR_HPP

#include <atomic>
#include <memory>

#include "video_processing_encoder_sink.hpp"

namespace vrrecorder::native {

enum class D3d11VideoProcessorResult {
    None,
    Converted,
    DeviceRemoved,
    DeviceReset,
    OutOfMemory,
    Failed,
    Aborted,
};

class D3d11VideoProcessorPort {
public:
    virtual ~D3d11VideoProcessorPort() = default;

    virtual D3d11VideoProcessorResult Convert(
        const std::shared_ptr<VideoSurface> &source,
        const VideoProcessingPlan &plan,
        std::shared_ptr<VideoSurface> &output) noexcept = 0;
    virtual void Abort() noexcept = 0;
};

class D3d11VideoFrameProcessor final : public VideoFrameProcessor {
public:
    explicit D3d11VideoFrameProcessor(
        D3d11VideoProcessorPort &port) noexcept;
    ~D3d11VideoFrameProcessor() override;

    vrrec_status_t Process(
        const std::shared_ptr<VideoSurface> &source,
        const VideoProcessingPlan &plan,
        std::shared_ptr<VideoSurface> &output) noexcept override;
    void Abort() noexcept override;
    D3d11VideoProcessorResult LastResult() const noexcept;

private:
    static bool IsPlanValid(
        const VideoSurfaceDescriptor &source,
        const VideoProcessingPlan &plan) noexcept;
    static bool IsOutputValid(
        const std::shared_ptr<VideoSurface> &source,
        const VideoProcessingPlan &plan,
        const std::shared_ptr<VideoSurface> &output) noexcept;
    vrrec_status_t Fail(
        D3d11VideoProcessorResult result,
        vrrec_status_t status,
        std::shared_ptr<VideoSurface> &output) noexcept;

    D3d11VideoProcessorPort &port_;
    std::atomic<D3d11VideoProcessorResult> last_result_ {
        D3d11VideoProcessorResult::None};
    std::atomic_bool terminal_ = false;
    std::atomic_bool abort_sent_ = false;
};

}

#endif
