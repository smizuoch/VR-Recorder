#ifndef VRRECORDER_NATIVE_MEDIA_RECORDING_SESSION_HPP
#define VRRECORDER_NATIVE_MEDIA_RECORDING_SESSION_HPP

#include <cstdint>

#include "media_backend.hpp"

namespace vrrecorder::native {

class MediaStreamPipelinePort {
public:
    virtual ~MediaStreamPipelinePort() = default;
    virtual vrrec_status_t Start() noexcept = 0;
    virtual vrrec_status_t RequestStop() noexcept = 0;
    virtual void Abort() noexcept = 0;
    virtual vrrec_status_t Join() noexcept = 0;
    virtual std::uint64_t MuxedPacketCount() const noexcept = 0;
};

class MediaMuxSessionPort {
public:
    virtual ~MediaMuxSessionPort() = default;
    virtual void Abort() noexcept = 0;
    virtual std::int64_t AudioVideoOffsetMicroseconds() const noexcept
    {
        return 0;
    }
};

class MediaRecordingSession final {
public:
    MediaRecordingSession(
        MediaStreamPipelinePort &video,
        MediaStreamPipelinePort &audio,
        MediaMuxSessionPort &mux,
        MediaEventSink &events) noexcept;
    ~MediaRecordingSession();
    MediaRecordingSession(const MediaRecordingSession &) = delete;
    MediaRecordingSession &operator=(const MediaRecordingSession &) = delete;
    vrrec_status_t Start() noexcept;
    vrrec_status_t RequestStop() noexcept;
    void Abort() noexcept;
    vrrec_status_t Join() noexcept;

private:
    MediaStreamPipelinePort &video_;
    MediaStreamPipelinePort &audio_;
    MediaMuxSessionPort &mux_;
    MediaEventSink &events_;
    bool video_started_ = false;
    bool audio_started_ = false;
    bool start_attempted_ = false;
    bool stop_requested_ = false;
    bool terminal_ = false;
};

}

#endif
