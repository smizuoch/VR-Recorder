#include "spout2_source_backend_core.hpp"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <limits>
#include <mutex>
#include <new>
#include <string>
#include <unordered_map>
#include <utility>

namespace vrrecorder::native {
namespace {

constexpr std::size_t maximum_text_size =
    VRREC_SPOUT_MAX_IDENTITY_UTF8_SIZE;
constexpr double maximum_estimated_fps = 1'000.0;

bool IsValidText(const std::string &value) noexcept
{
    return !value.empty() && value.size() <= maximum_text_size &&
           value.find('\0') == std::string::npos;
}

bool IsValidVendor(vrrec_gpu_vendor_t vendor) noexcept
{
    return vendor == VRREC_GPU_VENDOR_UNKNOWN ||
           vendor == VRREC_GPU_VENDOR_NVIDIA ||
           vendor == VRREC_GPU_VENDOR_AMD ||
           vendor == VRREC_GPU_VENDOR_INTEL;
}

bool IsValidPixelFormat(vrrec_source_pixel_format_t format) noexcept
{
    return format == VRREC_SOURCE_PIXEL_FORMAT_BGRA8 ||
           format == VRREC_SOURCE_PIXEL_FORMAT_RGBA8 ||
           format == VRREC_SOURCE_PIXEL_FORMAT_NV12;
}

bool IsValidMetadata(const Spout2TextureMetadata &metadata) noexcept
{
    return IsValidText(metadata.sender_id) &&
           metadata.resource_identity != 0 &&
           metadata.receiver_epoch != 0 && metadata.adapter_luid != 0 &&
           IsValidText(metadata.gpu_identity) &&
           IsValidVendor(metadata.gpu_vendor) && metadata.width != 0 &&
           metadata.height != 0 &&
           IsValidPixelFormat(metadata.pixel_format) &&
           std::isfinite(metadata.estimated_source_fps) &&
           metadata.estimated_source_fps > 0.0 &&
           metadata.estimated_source_fps <= maximum_estimated_fps &&
           metadata.frame_sequence != 0 &&
           metadata.monotonic_timestamp_microseconds >= 0;
}

bool SurfaceMatches(
    const std::shared_ptr<VideoSurface> &surface,
    const Spout2TextureMetadata &metadata,
    std::uint64_t generation_id) noexcept
{
    if (!surface || surface->NativeHandle() == nullptr) {
        return false;
    }
    const auto descriptor = surface->Descriptor();
    return descriptor.adapter_luid == metadata.adapter_luid &&
           descriptor.width == metadata.width &&
           descriptor.height == metadata.height &&
           descriptor.pixel_format == metadata.pixel_format &&
           descriptor.generation_id == generation_id;
}

struct SenderState final {
    std::uint64_t resource_identity;
    std::uint64_t receiver_epoch;
    std::uint64_t adapter_luid;
    std::uint32_t width;
    std::uint32_t height;
    vrrec_source_pixel_format_t pixel_format;
    std::uint64_t generation_id;
    std::uint64_t last_frame_sequence;
};

bool HasSameResourceShape(
    const SenderState &state,
    const Spout2TextureMetadata &metadata) noexcept
{
    return state.adapter_luid == metadata.adapter_luid &&
           state.width == metadata.width &&
           state.height == metadata.height &&
           state.pixel_format == metadata.pixel_format;
}

class Spout2SourceBackend final : public SpoutSourceBackend {
public:
    explicit Spout2SourceBackend(
        std::unique_ptr<Spout2ReceiverPort> port) noexcept
        : port_(std::move(port))
    {
    }

    vrrec_status_t Snapshot(
        std::vector<SpoutSenderSnapshot> &senders) override
    {
        senders.clear();
        if (aborted_.load()) {
            return VRREC_STATUS_INVALID_STATE;
        }

        const std::lock_guard operation_lock(operation_mutex_);
        if (aborted_.load()) {
            return VRREC_STATUS_INVALID_STATE;
        }

        std::vector<SpoutSenderSnapshot> received;
        const auto status = port_->Snapshot(received);
        if (aborted_.load()) {
            return VRREC_STATUS_INVALID_STATE;
        }
        if (status != VRREC_STATUS_OK) {
            return status;
        }
        if (received.size() > VRREC_SPOUT_MAX_SNAPSHOT_ENTRIES) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        for (const auto &sender : received) {
            if (!IsValidText(sender.sender_id)) {
                return VRREC_STATUS_INTERNAL_ERROR;
            }
        }
        std::sort(
            received.begin(),
            received.end(),
            [](const auto &left, const auto &right) {
                return left.sender_id < right.sender_id;
            });
        if (std::adjacent_find(
                received.begin(),
                received.end(),
                [](const auto &left, const auto &right) {
                    return left.sender_id == right.sender_id;
                }) != received.end()) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        senders = std::move(received);
        return VRREC_STATUS_OK;
    }

    vrrec_status_t Poll(
        std::chrono::milliseconds timeout,
        SpoutFrame &frame) override
    {
        frame = {};
        if (aborted_.load()) {
            return VRREC_STATUS_INVALID_STATE;
        }
        if (timeout.count() < 0 ||
            timeout > std::chrono::milliseconds(
                VRREC_SPOUT_MAX_POLL_TIMEOUT_MILLISECONDS)) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }

        const std::lock_guard operation_lock(operation_mutex_);
        if (aborted_.load()) {
            return VRREC_STATUS_INVALID_STATE;
        }

        Spout2TextureMetadata metadata {};
        const auto receive_result = port_->Receive(timeout, metadata);
        if (aborted_.load()) {
            return VRREC_STATUS_INVALID_STATE;
        }
        switch (receive_result) {
        case Spout2ReceiverResult::Timeout:
            return VRREC_STATUS_TIMEOUT;
        case Spout2ReceiverResult::SenderLost:
            return VRREC_STATUS_BACKEND_UNAVAILABLE;
        case Spout2ReceiverResult::Aborted:
            return VRREC_STATUS_INVALID_STATE;
        case Spout2ReceiverResult::OutOfMemory:
            return VRREC_STATUS_OUT_OF_MEMORY;
        case Spout2ReceiverResult::Failed:
            return VRREC_STATUS_INTERNAL_ERROR;
        case Spout2ReceiverResult::FrameReady:
            break;
        }
        if (!IsValidMetadata(metadata)) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }

        std::uint64_t generation_id = 1;
        auto existing = states_.find(metadata.sender_id);
        if (existing != states_.end()) {
            const auto &state = existing->second;
            if (metadata.receiver_epoch < state.receiver_epoch ||
                metadata.frame_sequence < state.last_frame_sequence) {
                return VRREC_STATUS_INTERNAL_ERROR;
            }
            if (metadata.frame_sequence == state.last_frame_sequence) {
                return VRREC_STATUS_TIMEOUT;
            }

            const auto same_resource =
                metadata.receiver_epoch == state.receiver_epoch &&
                metadata.resource_identity == state.resource_identity;
            if (same_resource && !HasSameResourceShape(state, metadata)) {
                return VRREC_STATUS_INTERNAL_ERROR;
            }
            generation_id = state.generation_id;
            if (!same_resource) {
                if (generation_id ==
                    std::numeric_limits<std::uint64_t>::max()) {
                    return VRREC_STATUS_INTERNAL_ERROR;
                }
                ++generation_id;
            }
        }

        std::shared_ptr<VideoSurface> surface;
        const auto copy_status = port_->CopySurface(
            metadata,
            generation_id,
            surface);
        if (aborted_.load()) {
            surface.reset();
            return VRREC_STATUS_INVALID_STATE;
        }
        if (copy_status != VRREC_STATUS_OK) {
            surface.reset();
            return copy_status;
        }
        if (!SurfaceMatches(surface, metadata, generation_id)) {
            surface.reset();
            return VRREC_STATUS_INTERNAL_ERROR;
        }

        states_.insert_or_assign(
            metadata.sender_id,
            SenderState {
                metadata.resource_identity,
                metadata.receiver_epoch,
                metadata.adapter_luid,
                metadata.width,
                metadata.height,
                metadata.pixel_format,
                generation_id,
                metadata.frame_sequence,
            });
        frame = SpoutFrame {
            std::move(metadata.sender_id),
            metadata.adapter_luid,
            std::move(metadata.gpu_identity),
            metadata.gpu_vendor,
            metadata.width,
            metadata.height,
            metadata.pixel_format,
            metadata.estimated_source_fps,
            metadata.frame_sequence,
            metadata.monotonic_timestamp_microseconds,
            std::move(surface),
        };
        return VRREC_STATUS_OK;
    }

    void Abort() noexcept override
    {
        if (!aborted_.exchange(true)) {
            port_->Abort();
        }
    }

private:
    std::unique_ptr<Spout2ReceiverPort> port_;
    std::atomic_bool aborted_ = false;
    std::mutex operation_mutex_;
    std::unordered_map<std::string, SenderState> states_;
};

}

std::unique_ptr<SpoutSourceBackend> CreateSpout2SourceBackend(
    std::unique_ptr<Spout2ReceiverPort> port,
    vrrec_status_t &status) noexcept
{
    status = VRREC_STATUS_INVALID_ARGUMENT;
    if (!port) {
        return nullptr;
    }
    auto backend = std::unique_ptr<SpoutSourceBackend>(
        new (std::nothrow) Spout2SourceBackend(std::move(port)));
    if (!backend) {
        status = VRREC_STATUS_OUT_OF_MEMORY;
        return nullptr;
    }
    status = VRREC_STATUS_OK;
    return backend;
}

}
