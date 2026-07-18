#include "ffmpeg_h264_d3d11_packet_encoder_adapter.hpp"

#if !defined(_WIN32)
#error "The D3D11 hardware encoder adapter requires Windows"
#endif

#if !defined(NOMINMAX)
#define NOMINMAX
#endif
#include <Windows.h>
#include <d3d11.h>

#include <chrono>
#include <cstdint>
#include <limits>
#include <utility>

#include "video_surface.hpp"

namespace vrrecorder::native {

FfmpegH264D3d11PacketEncoderAdapter::
FfmpegH264D3d11PacketEncoderAdapter(
    ProductionVideoEncoderRoute route,
    H264VideoEncoderConfig config) noexcept
    : route_(route), config_(config)
{
}

vrrec_status_t FfmpegH264D3d11PacketEncoderAdapter::EnsureEncoder(
    const std::shared_ptr<VideoSurface> &surface) noexcept
{
    if (encoder_ != nullptr) {
        return VRREC_STATUS_OK;
    }
    if (!surface || surface->NativeHandle() == nullptr) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }
    const auto descriptor = surface->Descriptor();
    if (descriptor.adapter_luid != route_.source_adapter_luid ||
        descriptor.width != config_.width ||
        descriptor.height != config_.height ||
        descriptor.pixel_format != VRREC_SOURCE_PIXEL_FORMAT_NV12) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }
    auto *texture = static_cast<ID3D11Texture2D *>(surface->NativeHandle());
    ID3D11Device *device = nullptr;
    texture->GetDevice(&device);
    if (device == nullptr) {
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    }
    auto created = FfmpegH264PacketEncoder::CreateHardware(
        config_,
        route_,
        device);
    device->Release();
    if (created.status != VRREC_STATUS_OK || created.encoder == nullptr) {
        return created.status == VRREC_STATUS_OK
            ? VRREC_STATUS_INTERNAL_ERROR
            : created.status;
    }
    encoder_ = std::move(created.encoder);
    return VRREC_STATUS_OK;
}

PacketVideoEncoderWrite FfmpegH264D3d11PacketEncoderAdapter::Complete(
    FfmpegH264PacketEncoderWrite encoded,
    std::uint64_t latency) noexcept
{
    if (encoded.status == VRREC_STATUS_OK && !descriptor_published_ &&
        !encoded.packets.empty() && encoder_ != nullptr &&
        encoder_->Descriptor() != nullptr) {
        encoded.descriptor_became_ready = true;
    } else if (encoded.packets.empty()) {
        encoded.descriptor_became_ready = false;
    }
    auto completed = MakeMuxingVideoEncoderWrite(
        *encoder_,
        std::move(encoded),
        latency,
        this);
    if (completed.status == VRREC_STATUS_OK &&
        completed.descriptor_became_ready) {
        descriptor_published_ = true;
    }
    return completed;
}

PacketVideoEncoderWrite FfmpegH264D3d11PacketEncoderAdapter::Encode(
    const ScheduledVideoFrame &frame) noexcept
{
    const std::lock_guard lock(mutex_);
    if (aborted_.load() || finished_ || config_.frames_per_second == 0 ||
        frame.output_tick > static_cast<std::uint64_t>(
            std::numeric_limits<std::int64_t>::max())) {
        return {VRREC_STATUS_INVALID_STATE, 0, {}};
    }
    const auto status = EnsureEncoder(frame.surface);
    if (status != VRREC_STATUS_OK) {
        return {status, 0, {}};
    }

    const auto started = std::chrono::steady_clock::now();
    auto encoded = encoder_->EncodeD3d11Nv12(
        frame.surface,
        static_cast<std::int64_t>(frame.output_tick));
    const auto completed = std::chrono::steady_clock::now();
    const auto elapsed = std::chrono::duration_cast<
        std::chrono::microseconds>(completed - started).count();
    const auto latency = elapsed > 0
        ? static_cast<std::uint64_t>(elapsed)
        : 0U;
    return Complete(std::move(encoded), latency);
}

PacketVideoEncoderWrite FfmpegH264D3d11PacketEncoderAdapter::Finish()
    noexcept
{
    const std::lock_guard lock(mutex_);
    if (aborted_.load() || finished_ || encoder_ == nullptr) {
        return {VRREC_STATUS_INVALID_STATE, 0, {}};
    }
    finished_ = true;
    const auto started = std::chrono::steady_clock::now();
    auto encoded = encoder_->Finish();
    const auto completed = std::chrono::steady_clock::now();
    const auto elapsed = std::chrono::duration_cast<
        std::chrono::microseconds>(completed - started).count();
    const auto latency = elapsed > 0
        ? static_cast<std::uint64_t>(elapsed)
        : 0U;
    return Complete(std::move(encoded), latency);
}

void FfmpegH264D3d11PacketEncoderAdapter::Abort() noexcept
{
    if (aborted_.exchange(true)) {
        return;
    }
    const std::lock_guard lock(mutex_);
    if (encoder_ != nullptr) {
        encoder_->Abort();
    }
}

}
