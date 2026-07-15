#ifndef VRRECORDER_NATIVE_TIMESTAMPING_AUDIO_PIPELINE_SESSION_HPP
#define VRRECORDER_NATIVE_TIMESTAMPING_AUDIO_PIPELINE_SESSION_HPP

#include <cstdint>

#include "audio_pipeline_session.hpp"

namespace vrrecorder::native {

class AudioSessionStartClock {
public:
    virtual ~AudioSessionStartClock() = default;
    virtual vrrec_status_t NowQpc100ns(
        std::int64_t &value) noexcept = 0;
};

class TimestampingStereoAudioPipelineSession final
    : public StereoAudioPipelineSessionPort {
public:
    TimestampingStereoAudioPipelineSession(
        StereoAudioPipelineSessionPort &session,
        AudioSessionStartClock &clock) noexcept;

    vrrec_status_t Start(
        const StereoAudioCaptureSessionConfig &config,
        std::size_t encoding_frame_count_48k) noexcept override;
    vrrec_status_t SetRouting(
        vrrec_audio_routing_t routing) noexcept override;
    vrrec_status_t RequestStop() noexcept override;
    void RequestAbort() noexcept override;
    void JoinAfterAbort() noexcept override;
    void Abort() noexcept override;
    StereoAudioEncodingWorkerResult Join() noexcept override;
    StereoAudioPipelineStatistics Statistics() const noexcept override;

private:
    StereoAudioPipelineSessionPort &session_;
    AudioSessionStartClock &clock_;
};

}

#endif
