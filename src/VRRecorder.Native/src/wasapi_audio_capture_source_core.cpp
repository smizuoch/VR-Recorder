#include "wasapi_audio_capture_source_core.hpp"

#include <limits>
#include <new>
#include <utility>

namespace vrrecorder::native {

WasapiAudioCaptureSourceCore::WasapiAudioCaptureSourceCore(
    std::unique_ptr<WasapiCapturePort> port) noexcept
    : port_(std::move(port))
{
}

WasapiAudioCaptureSourceCore::~WasapiAudioCaptureSourceCore()
{
    CloseOnce();
}

vrrec_status_t WasapiAudioCaptureSourceCore::Start(
    const AudioCaptureSourceConfig &config) noexcept
{
    if (port_ == nullptr || started_ || closed_ || abort_requested_.load() ||
        config.session_start_qpc_100ns < 0 ||
        (config.role != AudioCaptureRole::DesktopLoopback &&
         config.role != AudioCaptureRole::Microphone)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    capture_thread_ = std::this_thread::get_id();
    const auto start_result = port_->Start(config, format_);
    if (start_result != WasapiCapturePortResult::Ok) {
        const auto status = MapStartResult(start_result);
        CloseOnce();
        return status;
    }

    try {
        normalizer_ = std::make_unique<StereoCaptureNormalizer48k>(
            config.session_start_qpc_100ns);
    } catch (const std::bad_alloc &) {
        CloseOnce();
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        CloseOnce();
        return VRREC_STATUS_INTERNAL_ERROR;
    }

    started_ = true;
    next_frame_48k_ = 0;
    return VRREC_STATUS_OK;
}

AudioCaptureRead WasapiAudioCaptureSourceCore::Read() noexcept
{
    if (!started_ || port_ == nullptr || normalizer_ == nullptr ||
        std::this_thread::get_id() != capture_thread_) {
        return FailedRead();
    }

    while (!abort_requested_.load()) {
        WasapiCapturePacket packet {};
        const auto acquire_result = port_->Acquire(packet);
        if (acquire_result == WasapiCapturePortResult::Empty) {
            continue;
        }
        if (acquire_result == WasapiCapturePortResult::DeviceLost) {
            return DeviceLost();
        }
        if (acquire_result == WasapiCapturePortResult::Aborted ||
            abort_requested_.load()) {
            if (acquire_result == WasapiCapturePortResult::Ok) {
                static_cast<void>(port_->Release(packet.frame_count));
            }
            return AbortedRead();
        }
        if (acquire_result != WasapiCapturePortResult::Ok) {
            return FailedRead();
        }

        CaptureNormalizationResult normalization =
            CaptureNormalizationResult::InvalidPacket;
        CapturedStereoPacket48k normalized {};
        if (packet.timestamp_error ||
            packet.qpc_100ns <= static_cast<std::uint64_t>(
                std::numeric_limits<std::int64_t>::max())) {
            normalization = normalizer_->Normalize(
                format_,
                {
                    packet.device_position,
                    packet.timestamp_error
                        ? 0
                        : static_cast<std::int64_t>(packet.qpc_100ns),
                    packet.frame_count,
                    packet.bytes,
                    packet.silent,
                    packet.discontinuity,
                    packet.timestamp_error,
                },
                normalized);
        }

        const auto release_result = port_->Release(packet.frame_count);
        if (abort_requested_.load()) {
            return AbortedRead();
        }
        if (release_result != WasapiCapturePortResult::Ok) {
            return MapReleaseResult(release_result);
        }
        if (normalization ==
            CaptureNormalizationResult::BeforeSessionEpoch) {
            continue;
        }
        if (normalization != CaptureNormalizationResult::Ready ||
            normalized.start_frame_48k >
                std::numeric_limits<std::uint64_t>::max() -
                    normalized.frame_count_48k) {
            return FailedRead();
        }

        next_frame_48k_ = normalized.start_frame_48k +
            normalized.frame_count_48k;
        return {
            AudioCaptureReadResult::Packet,
            normalized,
            0,
        };
    }

    return AbortedRead();
}

void WasapiAudioCaptureSourceCore::Abort() noexcept
{
    if (abort_requested_.exchange(true)) {
        return;
    }
    if (port_ != nullptr) {
        port_->Abort();
    }
}

vrrec_status_t WasapiAudioCaptureSourceCore::MapStartResult(
    WasapiCapturePortResult result) noexcept
{
    switch (result) {
    case WasapiCapturePortResult::OutOfMemory:
        return VRREC_STATUS_OUT_OF_MEMORY;
    case WasapiCapturePortResult::InvalidArgument:
        return VRREC_STATUS_INVALID_ARGUMENT;
    case WasapiCapturePortResult::DeviceLost:
    case WasapiCapturePortResult::BackendUnavailable:
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    default:
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

AudioCaptureRead WasapiAudioCaptureSourceCore::FailedRead() noexcept
{
    return {AudioCaptureReadResult::Failed, {}, 0};
}

AudioCaptureRead WasapiAudioCaptureSourceCore::AbortedRead() noexcept
{
    return {AudioCaptureReadResult::Aborted, {}, 0};
}

AudioCaptureRead WasapiAudioCaptureSourceCore::DeviceLost() const noexcept
{
    return {
        AudioCaptureReadResult::DeviceLost,
        {},
        next_frame_48k_,
    };
}

AudioCaptureRead WasapiAudioCaptureSourceCore::MapReleaseResult(
    WasapiCapturePortResult result) const noexcept
{
    return result == WasapiCapturePortResult::DeviceLost
        ? DeviceLost()
        : FailedRead();
}

void WasapiAudioCaptureSourceCore::CloseOnce() noexcept
{
    if (closed_) {
        return;
    }

    closed_ = true;
    started_ = false;
    normalizer_.reset();
    if (port_ != nullptr) {
        port_->Close();
    }
}

}
