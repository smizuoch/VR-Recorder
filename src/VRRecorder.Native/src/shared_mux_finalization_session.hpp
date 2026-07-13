#ifndef VRRECORDER_NATIVE_SHARED_MUX_FINALIZATION_SESSION_HPP
#define VRRECORDER_NATIVE_SHARED_MUX_FINALIZATION_SESSION_HPP

#include <mutex>
#include <span>

#include "fragmented_mp4_mux_coordinator.hpp"

namespace vrrecorder::native {

class SharedMuxFinalizationSession final {
public:
    explicit SharedMuxFinalizationSession(
        FragmentedMp4MuxCoordinator &mux) noexcept;
    ~SharedMuxFinalizationSession();

    SharedMuxFinalizationSession(
        const SharedMuxFinalizationSession &) = delete;
    SharedMuxFinalizationSession &operator=(
        const SharedMuxFinalizationSession &) = delete;

    Mp4MuxResult Submit(const EncodedMediaPacket &packet) noexcept;
    Mp4MuxResult SubmitBatch(
        MediaStreamKind producer,
        std::span<const EncodedMediaPacket> packets) noexcept;
    vrrec_status_t EncoderFinished(MediaStreamKind stream) noexcept;
    void EncoderFailed(MediaStreamKind stream) noexcept;
    void Abort() noexcept;

private:
    FragmentedMp4MuxCoordinator &mux_;
    std::mutex operation_mutex_;
    std::mutex state_mutex_;
    bool video_finished_ = false;
    bool audio_finished_ = false;
    bool terminal_ = false;
    bool abort_requested_ = false;
    bool completed_ = false;
};

}

#endif
