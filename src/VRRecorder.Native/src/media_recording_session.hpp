#ifndef VRRECORDER_NATIVE_MEDIA_RECORDING_SESSION_HPP
#define VRRECORDER_NATIVE_MEDIA_RECORDING_SESSION_HPP

#include <atomic>
#include <condition_variable>
#include <cstdint>
#include <mutex>

#include "fragmented_mp4_mux_coordinator.hpp"
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
    virtual vrrec_status_t Start(
        const FragmentedMp4StreamConfiguration &configuration) noexcept = 0;
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
        FragmentedMp4StreamConfiguration mux_configuration,
        MediaEventSink &events);
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
    FragmentedMp4StreamConfiguration mux_configuration_;
    MediaEventSink &events_;
    std::atomic_bool video_started_ = false;
    std::atomic_bool audio_started_ = false;
    std::atomic_bool start_attempted_ = false;
    std::atomic_bool stop_requested_ = false;
    std::atomic_bool join_attempted_ = false;
    std::atomic_bool terminal_ = false;
    std::mutex stop_mutex_;
    std::condition_variable stop_changed_;
    bool stop_in_progress_ = false;
    bool stop_completed_ = false;
    vrrec_status_t stop_status_ = VRREC_STATUS_INVALID_STATE;
};

}

#endif
