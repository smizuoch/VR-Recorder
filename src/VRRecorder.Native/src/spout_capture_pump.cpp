#include "spout_capture_pump.hpp"

#include <cmath>
#include <utility>

namespace vrrecorder::native {
namespace {

constexpr auto GeometryStabilityDurationMicroseconds = 500'000;

}

SpoutCapturePump::SpoutCapturePump(
    SpoutSourceBackend &backend,
    VideoCfrScheduler &scheduler,
    std::string selected_sender_id)
    : backend_(backend),
      scheduler_(scheduler),
      selected_sender_id_(std::move(selected_sender_id))
{
}

SpoutCapturePump::SpoutCapturePump(
    SpoutSourceBackend &backend,
    VideoCfrScheduler &scheduler,
    std::string selected_sender_id,
    SpoutCaptureEventSink &events)
    : backend_(backend),
      scheduler_(scheduler),
      selected_sender_id_(std::move(selected_sender_id)),
      events_(&events)
{
}

SpoutCaptureResult SpoutCapturePump::PollOne(
    std::chrono::milliseconds timeout) noexcept
{
    if (aborted_.load()) {
        return SpoutCaptureResult::Aborted;
    }

    if (selected_sender_id_.empty() || timeout.count() < 0 ||
        timeout > std::chrono::milliseconds(
            VRREC_SPOUT_MAX_POLL_TIMEOUT_MILLISECONDS)) {
        return SpoutCaptureResult::InvalidFrame;
    }

    SpoutFrame frame;
    vrrec_status_t status = VRREC_STATUS_INTERNAL_ERROR;
    try {
        status = backend_.Poll(timeout, frame);
    } catch (...) {
        return SpoutCaptureResult::Failed;
    }

    if (aborted_.load()) {
        return SpoutCaptureResult::Aborted;
    }

    if (status == VRREC_STATUS_TIMEOUT) {
        return SpoutCaptureResult::Timeout;
    }

    if (status == VRREC_STATUS_BACKEND_UNAVAILABLE) {
        return SpoutCaptureResult::SenderLost;
    }

    if (status != VRREC_STATUS_OK) {
        return SpoutCaptureResult::Failed;
    }

    VideoSurfaceDescriptor descriptor {};
    if (frame.sender_id != selected_sender_id_ ||
        !IsFrameValid(frame, descriptor)) {
        return SpoutCaptureResult::InvalidFrame;
    }

    vrrec_status_t push_status = VRREC_STATUS_INTERNAL_ERROR;
    auto result = SpoutCaptureResult::InvalidFrame;
    auto notify_geometry_change = false;
    VideoSurfaceDescriptor stable_descriptor {};
    {
        const std::lock_guard lock(lifecycle_mutex_);
        if (aborted_.load()) {
            return SpoutCaptureResult::Aborted;
        }
        if (has_descriptor_ &&
            descriptor.generation_id < latest_descriptor_.generation_id) {
            return SpoutCaptureResult::StaleFrame;
        }
        if (has_descriptor_ && descriptor.adapter_luid !=
                latest_descriptor_.adapter_luid) {
            return SpoutCaptureResult::AdapterChanged;
        }
        if (has_descriptor_ && descriptor.generation_id ==
                latest_descriptor_.generation_id &&
            (descriptor.width != latest_descriptor_.width ||
             descriptor.height != latest_descriptor_.height ||
             descriptor.pixel_format != latest_descriptor_.pixel_format)) {
            return SpoutCaptureResult::InvalidFrame;
        }

        if (geometry_change_notified_) {
            return SpoutCaptureResult::GeometryChangePending;
        }

        if (has_descriptor_ &&
            !HasSameGeometry(descriptor, latest_descriptor_)) {
            if (has_geometry_candidate_ &&
                descriptor.generation_id <
                    candidate_descriptor_.generation_id) {
                return SpoutCaptureResult::StaleFrame;
            }
            if (has_geometry_candidate_ &&
                descriptor.generation_id ==
                    candidate_descriptor_.generation_id &&
                !HasSameGeometry(descriptor, candidate_descriptor_)) {
                return SpoutCaptureResult::InvalidFrame;
            }
            if (!has_geometry_candidate_ ||
                !HasSameCandidateSignature(
                    descriptor,
                    candidate_descriptor_)) {
                BeginGeometryCandidate(frame, descriptor);
                return SpoutCaptureResult::GeometryChangePending;
            }
            if (frame.frame_sequence <= candidate_last_sequence_ ||
                frame.monotonic_timestamp_microseconds <
                    candidate_last_timestamp_microseconds_) {
                return SpoutCaptureResult::StaleFrame;
            }

            candidate_last_sequence_ = frame.frame_sequence;
            candidate_last_timestamp_microseconds_ =
                frame.monotonic_timestamp_microseconds;
            if (candidate_last_timestamp_microseconds_ -
                    candidate_first_timestamp_microseconds_ >=
                GeometryStabilityDurationMicroseconds) {
                geometry_change_notified_ = true;
                stable_descriptor = candidate_descriptor_;
                notify_geometry_change = events_ != nullptr;
            }
            result = SpoutCaptureResult::GeometryChangePending;
        } else {
            ResetGeometryCandidate();
            geometry_change_notified_ = false;
            push_status = scheduler_.Push({
                frame.frame_sequence,
                frame.monotonic_timestamp_microseconds,
                frame.surface,
            });
            if (push_status == VRREC_STATUS_OK) {
                latest_descriptor_ = descriptor;
                has_descriptor_ = true;
            }
            result = push_status == VRREC_STATUS_OK
                ? SpoutCaptureResult::FrameAccepted
                : SpoutCaptureResult::InvalidFrame;
        }
    }

    if (notify_geometry_change && !aborted_.load()) {
        events_->StableVideoGeometryChanged(
            stable_descriptor.width,
            stable_descriptor.height,
            stable_descriptor.pixel_format);
    }
    return result;
}

vrrec_status_t SpoutCapturePump::AcknowledgeStableVideoGeometry(
    std::uint32_t width,
    std::uint32_t height) noexcept
{
    if (width == 0 || height == 0) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    const std::lock_guard lock(lifecycle_mutex_);
    if (aborted_.load() || !geometry_change_notified_ ||
        !has_geometry_candidate_) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (candidate_descriptor_.width != width ||
        candidate_descriptor_.height != height) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    latest_descriptor_ = candidate_descriptor_;
    has_descriptor_ = true;
    ResetGeometryCandidate();
    geometry_change_notified_ = false;
    return VRREC_STATUS_OK;
}

void SpoutCapturePump::Abort() noexcept
{
    {
        const std::lock_guard lock(lifecycle_mutex_);
        if (aborted_.exchange(true)) {
            return;
        }
    }

    backend_.Abort();
}

bool SpoutCapturePump::IsFrameValid(
    const SpoutFrame &frame,
    VideoSurfaceDescriptor &descriptor) noexcept
{
    const auto vendor_defined =
        frame.gpu_vendor == VRREC_GPU_VENDOR_UNKNOWN ||
        frame.gpu_vendor == VRREC_GPU_VENDOR_NVIDIA ||
        frame.gpu_vendor == VRREC_GPU_VENDOR_AMD ||
        frame.gpu_vendor == VRREC_GPU_VENDOR_INTEL;
    const auto format_defined =
        frame.pixel_format == VRREC_SOURCE_PIXEL_FORMAT_BGRA8 ||
        frame.pixel_format == VRREC_SOURCE_PIXEL_FORMAT_RGBA8 ||
        frame.pixel_format == VRREC_SOURCE_PIXEL_FORMAT_NV12;
    if (!frame.surface || frame.surface->NativeHandle() == nullptr) {
        return false;
    }

    descriptor = frame.surface->Descriptor();
    return !frame.sender_id.empty() && !frame.gpu_identity.empty() &&
           frame.width > 0 && frame.height > 0 &&
           descriptor.generation_id != 0 &&
           descriptor.adapter_luid == frame.adapter_luid &&
           descriptor.width == frame.width &&
           descriptor.height == frame.height &&
           descriptor.pixel_format == frame.pixel_format &&
           vendor_defined && format_defined &&
           std::isfinite(frame.estimated_source_fps) &&
           frame.estimated_source_fps > 0.0 &&
           frame.frame_sequence != 0 &&
           frame.monotonic_timestamp_microseconds >= 0;
}

bool SpoutCapturePump::HasSameGeometry(
    const VideoSurfaceDescriptor &left,
    const VideoSurfaceDescriptor &right) noexcept
{
    return left.width == right.width && left.height == right.height &&
           left.pixel_format == right.pixel_format;
}

bool SpoutCapturePump::HasSameCandidateSignature(
    const VideoSurfaceDescriptor &left,
    const VideoSurfaceDescriptor &right) noexcept
{
    return left.generation_id == right.generation_id &&
           HasSameGeometry(left, right);
}

void SpoutCapturePump::BeginGeometryCandidate(
    const SpoutFrame &frame,
    const VideoSurfaceDescriptor &descriptor) noexcept
{
    candidate_descriptor_ = descriptor;
    candidate_last_sequence_ = frame.frame_sequence;
    candidate_first_timestamp_microseconds_ =
        frame.monotonic_timestamp_microseconds;
    candidate_last_timestamp_microseconds_ =
        frame.monotonic_timestamp_microseconds;
    has_geometry_candidate_ = true;
}

void SpoutCapturePump::ResetGeometryCandidate() noexcept
{
    candidate_descriptor_ = {};
    candidate_last_sequence_ = 0;
    candidate_first_timestamp_microseconds_ = 0;
    candidate_last_timestamp_microseconds_ = 0;
    has_geometry_candidate_ = false;
}

}
