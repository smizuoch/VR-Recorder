#include "spout_capture_pump.hpp"

#include <cmath>
#include <utility>

namespace vrrecorder::native {

SpoutCapturePump::SpoutCapturePump(
    SpoutSourceBackend &backend,
    VideoCfrScheduler &scheduler,
    std::string selected_sender_id)
    : backend_(backend),
      scheduler_(scheduler),
      selected_sender_id_(std::move(selected_sender_id))
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

    if (frame.sender_id != selected_sender_id_ ||
        !IsFrameValid(frame)) {
        return SpoutCaptureResult::InvalidFrame;
    }

    const auto push_status = scheduler_.Push({
        frame.frame_sequence,
        frame.monotonic_timestamp_microseconds,
    });
    return push_status == VRREC_STATUS_OK
        ? SpoutCaptureResult::FrameAccepted
        : SpoutCaptureResult::InvalidFrame;
}

void SpoutCapturePump::Abort() noexcept
{
    if (aborted_.exchange(true)) {
        return;
    }

    backend_.Abort();
}

bool SpoutCapturePump::IsFrameValid(const SpoutFrame &frame) noexcept
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
    return !frame.sender_id.empty() && !frame.gpu_identity.empty() &&
           frame.width > 0 && frame.height > 0 &&
           vendor_defined && format_defined &&
           std::isfinite(frame.estimated_source_fps) &&
           frame.estimated_source_fps > 0.0 &&
           frame.monotonic_timestamp_microseconds >= 0;
}

}
