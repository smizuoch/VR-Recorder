#ifndef VRRECORDER_NATIVE_AUDIO_PIPELINE_SESSION_HPP
#define VRRECORDER_NATIVE_AUDIO_PIPELINE_SESSION_HPP

#include <atomic>
#include <cstddef>
#include <cstdint>
#include <mutex>

#include "audio_capture_session.hpp"
#include "audio_encoding_worker.hpp"

namespace vrrecorder::native {

struct StereoAudioPipelineStatistics final {
    std::uint64_t submitted_frame_count;
    std::uint64_t muxed_packet_count;
};

class StereoAudioPipelineSessionPort {
public:
    virtual ~StereoAudioPipelineSessionPort() = default;
    virtual vrrec_status_t Start(
        const StereoAudioCaptureSessionConfig &config,
        std::size_t encoding_frame_count_48k) noexcept = 0;
    virtual vrrec_status_t SetRouting(
        vrrec_audio_routing_t routing) noexcept = 0;
    virtual vrrec_status_t RequestStop() noexcept = 0;
    virtual void RequestAbort() noexcept = 0;
    virtual void JoinAfterAbort() noexcept = 0;
    virtual void Abort() noexcept = 0;
    virtual StereoAudioEncodingWorkerResult Join() noexcept = 0;
    virtual StereoAudioPipelineStatistics Statistics() const noexcept = 0;
};

class StereoAudioPipelineSession final
    : public StereoAudioPipelineSessionPort {
public:
    StereoAudioPipelineSession(
        StereoAudioCaptureSessionPort &capture,
        StereoAudioEncoderSink &encoder) noexcept;
    StereoAudioPipelineSession(
        StereoAudioCaptureSessionPort &capture,
        StereoAudioEncoderSink &encoder,
        NativeThreadFactoryPort &thread_factory) noexcept;
    ~StereoAudioPipelineSession();

    StereoAudioPipelineSession(const StereoAudioPipelineSession &) = delete;
    StereoAudioPipelineSession &operator=(
        const StereoAudioPipelineSession &) = delete;

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
    enum class StartPhase : std::uint8_t {
        NotStarted,
        Starting,
        Completed,
    };

    enum class TerminalOutcome : std::uint8_t {
        Open,
        AbortRequested,
        Completed,
    };

    StereoAudioCaptureSessionPort &capture_;
    StereoAudioEncodingWorker encoding_;
    std::mutex start_abort_mutex_;
    std::mutex abort_join_mutex_;
    std::atomic<StartPhase> start_phase_ = StartPhase::NotStarted;
    std::atomic_bool capture_started_ = false;
    bool encoding_starting_ = false;
    std::atomic_bool encoding_started_ = false;
    bool encoding_abort_committed_ = false;
    std::atomic_bool join_in_progress_ = false;
    std::atomic_bool active_ = false;
    std::atomic<TerminalOutcome> terminal_outcome_ =
        TerminalOutcome::Open;
};

}

#endif
