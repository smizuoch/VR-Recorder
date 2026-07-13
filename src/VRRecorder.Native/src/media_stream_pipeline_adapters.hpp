#ifndef VRRECORDER_NATIVE_MEDIA_STREAM_PIPELINE_ADAPTERS_HPP
#define VRRECORDER_NATIVE_MEDIA_STREAM_PIPELINE_ADAPTERS_HPP

#include <chrono>
#include <cstddef>

#include "audio_pipeline_session.hpp"
#include "media_recording_session.hpp"
#include "video_pipeline_session.hpp"

namespace vrrecorder::native {

class VideoMediaStreamPipelineAdapter final
    : public MediaStreamPipelinePort {
public:
    VideoMediaStreamPipelineAdapter(
        VideoPipelineSessionPort &session,
        std::chrono::milliseconds poll_timeout) noexcept;
    vrrec_status_t Start() noexcept override;
    vrrec_status_t RequestStop() noexcept override;
    void RequestAbort() noexcept override;
    void JoinAfterAbort() noexcept override;
    void Abort() noexcept override;
    vrrec_status_t Join() noexcept override;
    std::uint64_t MuxedPacketCount() const noexcept override;

private:
    VideoPipelineSessionPort &session_;
    std::chrono::milliseconds poll_timeout_;
};

class AudioMediaStreamPipelineAdapter final
    : public MediaStreamPipelinePort {
public:
    AudioMediaStreamPipelineAdapter(
        StereoAudioPipelineSessionPort &session,
        StereoAudioCaptureSessionConfig config,
        std::size_t encoding_frame_count_48k);
    vrrec_status_t Start() noexcept override;
    vrrec_status_t RequestStop() noexcept override;
    void RequestAbort() noexcept override;
    void JoinAfterAbort() noexcept override;
    void Abort() noexcept override;
    vrrec_status_t Join() noexcept override;
    std::uint64_t MuxedPacketCount() const noexcept override;

private:
    StereoAudioPipelineSessionPort &session_;
    StereoAudioCaptureSessionConfig config_;
    std::size_t encoding_frame_count_48k_;
};

}

#endif
