#ifndef VRRECORDER_NATIVE_AUDIO_PIPELINE_SESSION_HPP
#define VRRECORDER_NATIVE_AUDIO_PIPELINE_SESSION_HPP

#include <atomic>
#include <cstddef>
#include <cstdint>

#include "audio_capture_session.hpp"
#include "audio_encoding_worker.hpp"

namespace vrrecorder::native {

struct StereoAudioPipelineStatistics final {
    std::uint64_t submitted_frame_count;
    std::uint64_t muxed_packet_count;
};

class StereoAudioPipelineSession final {
public:
    StereoAudioPipelineSession(
        StereoAudioCaptureSessionPort &capture,
        StereoAudioEncoderSink &encoder) noexcept;
    ~StereoAudioPipelineSession();

    StereoAudioPipelineSession(const StereoAudioPipelineSession &) = delete;
    StereoAudioPipelineSession &operator=(
        const StereoAudioPipelineSession &) = delete;

    vrrec_status_t Start(
        const StereoAudioCaptureSessionConfig &config,
        std::size_t encoding_frame_count_48k) noexcept;
    vrrec_status_t SetRouting(
        vrrec_audio_routing_t routing) noexcept;
    vrrec_status_t RequestStop() noexcept;
    void Abort() noexcept;
    StereoAudioEncodingWorkerResult Join() noexcept;
    StereoAudioPipelineStatistics Statistics() const noexcept;

private:
    StereoAudioCaptureSessionPort &capture_;
    StereoAudioEncodingWorker encoding_;
    std::atomic_bool start_attempted_ = false;
    std::atomic_bool capture_started_ = false;
    std::atomic_bool encoding_started_ = false;
    std::atomic_bool active_ = false;
    std::atomic_bool aborted_ = false;
};

}

#endif
