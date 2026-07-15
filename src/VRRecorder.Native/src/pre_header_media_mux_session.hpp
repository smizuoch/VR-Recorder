#ifndef VRRECORDER_NATIVE_PRE_HEADER_MEDIA_MUX_SESSION_HPP
#define VRRECORDER_NATIVE_PRE_HEADER_MEDIA_MUX_SESSION_HPP

#include <atomic>
#include <cstdint>

#include "pre_header_coordinator.hpp"

namespace vrrecorder::native {

class PreHeaderMediaMuxSession final : public MediaMuxSessionPort {
public:
    PreHeaderMediaMuxSession(
        PreHeaderCoordinator &coordinator,
        std::int64_t capture_epoch,
        const void *video_encoder_identity,
        FragmentedMp4StreamConfiguration configuration);

    vrrec_status_t Start(
        const FragmentedMp4StreamConfiguration &configuration)
        noexcept override;
    void RequestAbort() noexcept override;
    void Abort() noexcept override;
    std::int64_t AudioVideoOffsetMicroseconds() const noexcept override;

private:
    PreHeaderCoordinator &coordinator_;
    std::int64_t capture_epoch_;
    const void *video_encoder_identity_;
    FragmentedMp4StreamConfiguration configuration_;
    std::atomic_bool start_attempted_ = false;
};

}

#endif
