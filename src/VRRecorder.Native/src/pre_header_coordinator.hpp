#ifndef VRRECORDER_NATIVE_PRE_HEADER_COORDINATOR_HPP
#define VRRECORDER_NATIVE_PRE_HEADER_COORDINATOR_HPP

#include <atomic>
#include <cstdint>
#include <mutex>
#include <optional>

#include "encoded_media_packet_submission_port.hpp"
#include "media_recording_session.hpp"

namespace vrrecorder::native {

enum class PreHeaderState : std::uint8_t {
    Created,
    Priming,
    HeaderStarting,
    DrainingPreHeader,
    Running,
    Finishing,
    Failed,
    Aborted,
};

class PreHeaderCoordinator final {
public:
    PreHeaderCoordinator(
        MediaMuxSessionPort &mux_session,
        EncodedMediaPacketSubmissionPort &submission,
        AacStreamDescriptor audio_descriptor,
        FragmentedMp4FragmentPolicy fragment_policy,
        const void *expected_video_encoder_identity);
    ~PreHeaderCoordinator();

    PreHeaderCoordinator(const PreHeaderCoordinator &) = delete;
    PreHeaderCoordinator &operator=(const PreHeaderCoordinator &) = delete;

    vrrec_status_t BeginPriming(std::int64_t capture_epoch) noexcept;
    vrrec_status_t ProducerStarted(MediaStreamKind producer) noexcept;
    vrrec_status_t PublishVideoDescriptor(
        const void *encoder_identity,
        const H264StreamDescriptor &descriptor) noexcept;
    void Abort() noexcept;
    PreHeaderState State() const noexcept;

private:
    vrrec_status_t TryStartHeaderLocked(
        std::unique_lock<std::mutex> &lock) noexcept;
    vrrec_status_t FailLocked(vrrec_status_t status) noexcept;
    void AbortDownstreamLocked() noexcept;

    MediaMuxSessionPort &mux_session_;
    EncodedMediaPacketSubmissionPort &submission_;
    AacStreamDescriptor audio_descriptor_;
    FragmentedMp4FragmentPolicy fragment_policy_;
    const void *expected_video_encoder_identity_;
    mutable std::mutex mutex_;
    std::optional<H264StreamDescriptor> video_descriptor_;
    std::int64_t capture_epoch_ = 0;
    bool video_started_ = false;
    bool audio_started_ = false;
    bool downstream_aborted_ = false;
    PreHeaderState state_ = PreHeaderState::Created;
    std::atomic_bool abort_requested_ = false;
};

}

#endif
