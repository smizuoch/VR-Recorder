#ifndef VRRECORDER_NATIVE_AUDIO_CAPTURE_SESSION_HPP
#define VRRECORDER_NATIVE_AUDIO_CAPTURE_SESSION_HPP

#include <atomic>
#include <cstddef>
#include <cstdint>
#include <span>
#include <string>

#include "audio_capture_input_worker.hpp"
#include "audio_mix_coordinator.hpp"

namespace vrrecorder::native {

struct StereoAudioCaptureSessionConfig final {
    std::string desktop_endpoint_id_utf8;
    std::string microphone_endpoint_id_utf8;
    std::int64_t session_start_qpc_100ns;
};

class StereoAudioCaptureSession final : public StereoAudioMixSource {
public:
    StereoAudioCaptureSession(
        AudioCaptureSourceProvider &desktop_provider,
        AudioCaptureRecoveryWaiter &desktop_waiter,
        AudioCaptureSourceProvider &microphone_provider,
        AudioCaptureRecoveryWaiter &microphone_waiter,
        std::size_t timeline_capacity_frames,
        vrrec_audio_routing_t initial_routing,
        double desktop_gain_db,
        double microphone_gain_db);
    ~StereoAudioCaptureSession();

    StereoAudioCaptureSession(const StereoAudioCaptureSession &) = delete;
    StereoAudioCaptureSession &operator=(
        const StereoAudioCaptureSession &) = delete;

    vrrec_status_t Start(
        const StereoAudioCaptureSessionConfig &config) noexcept;
    StereoAudioMixResult MixNext(
        std::size_t frame_count_48k,
        std::span<float> output_interleaved,
        StereoAudioMixRead &read) noexcept override;
    vrrec_status_t SetRouting(
        vrrec_audio_routing_t routing) noexcept;
    void Abort() noexcept;

private:
    StereoCaptureTimeline desktop_timeline_;
    StereoCaptureTimeline microphone_timeline_;
    StereoAudioMixer mixer_;
    AudioCaptureInputWorker desktop_worker_;
    AudioCaptureInputWorker microphone_worker_;
    StereoAudioMixCoordinator mix_coordinator_;
    vrrec_audio_routing_t initial_routing_;
    std::atomic_bool start_attempted_ = false;
    std::atomic_bool active_ = false;
    std::atomic_bool aborted_ = false;
};

}

#endif
