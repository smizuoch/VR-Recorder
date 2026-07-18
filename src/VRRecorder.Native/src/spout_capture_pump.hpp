#ifndef VRRECORDER_NATIVE_SPOUT_CAPTURE_PUMP_HPP
#define VRRECORDER_NATIVE_SPOUT_CAPTURE_PUMP_HPP

#include <atomic>
#include <chrono>
#include <cstdint>
#include <mutex>
#include <string>

#include "spout_source_backend.hpp"
#include "video_cfr_scheduler.hpp"

namespace vrrecorder::native {

class SpoutCaptureEventSink {
public:
    virtual ~SpoutCaptureEventSink() = default;

    virtual void StableVideoGeometryChanged(
        std::uint32_t width,
        std::uint32_t height,
        vrrec_source_pixel_format_t pixel_format) noexcept = 0;
};

enum class SpoutCaptureResult {
    FrameAccepted,
    GeometryChangePending,
    Timeout,
    StaleFrame,
    SenderLost,
    AdapterChanged,
    Aborted,
    InvalidFrame,
    Failed,
};

class SpoutCaptureSource {
public:
    virtual ~SpoutCaptureSource() = default;

    virtual SpoutCaptureResult PollOne(
        std::chrono::milliseconds timeout) noexcept = 0;
    virtual void Abort() noexcept = 0;
};

class SpoutCapturePump final : public SpoutCaptureSource {
public:
    SpoutCapturePump(
        SpoutSourceBackend &backend,
        VideoCfrScheduler &scheduler,
        std::string selected_sender_id);
    SpoutCapturePump(
        SpoutSourceBackend &backend,
        VideoCfrScheduler &scheduler,
        std::string selected_sender_id,
        SpoutCaptureEventSink &events);

    SpoutCaptureResult PollOne(
        std::chrono::milliseconds timeout) noexcept override;
    vrrec_status_t AcknowledgeStableVideoGeometry(
        std::uint32_t width,
        std::uint32_t height) noexcept;
    void Abort() noexcept override;

private:
    static bool IsFrameValid(
        const SpoutFrame &frame,
        VideoSurfaceDescriptor &descriptor) noexcept;
    static bool HasSameGeometry(
        const VideoSurfaceDescriptor &left,
        const VideoSurfaceDescriptor &right) noexcept;
    static bool HasSameCandidateSignature(
        const VideoSurfaceDescriptor &left,
        const VideoSurfaceDescriptor &right) noexcept;
    void BeginGeometryCandidate(
        const SpoutFrame &frame,
        const VideoSurfaceDescriptor &descriptor) noexcept;
    void ResetGeometryCandidate() noexcept;

    SpoutSourceBackend &backend_;
    VideoCfrScheduler &scheduler_;
    std::string selected_sender_id_;
    SpoutCaptureEventSink *events_ = nullptr;
    std::mutex lifecycle_mutex_;
    VideoSurfaceDescriptor latest_descriptor_ {};
    VideoSurfaceDescriptor candidate_descriptor_ {};
    std::uint64_t candidate_last_sequence_ = 0;
    std::int64_t candidate_first_timestamp_microseconds_ = 0;
    std::int64_t candidate_last_timestamp_microseconds_ = 0;
    bool has_descriptor_ = false;
    bool has_geometry_candidate_ = false;
    bool geometry_change_notified_ = false;
    std::atomic_bool aborted_ = false;
};

}

#endif
