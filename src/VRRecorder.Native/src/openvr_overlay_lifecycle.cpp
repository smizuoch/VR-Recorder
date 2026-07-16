#include "openvr_overlay_lifecycle.hpp"

#include <cmath>
#include <cstdint>
#include <memory>
#include <new>
#include <utility>

namespace vrrecorder::native {
namespace {

constexpr auto MinimumWidthInMeters = 0.18F;
constexpr auto MaximumWidthInMeters = 0.32F;
constexpr auto TextureWidth = std::uint32_t {1024};
constexpr auto TextureHeight = std::uint32_t {512};
constexpr auto TextureStrideBytes = std::uint32_t {4096};
constexpr auto TexturePixelBytesSize = std::size_t {2'097'152};

bool IsPresent(const char *value) noexcept
{
    return value != nullptr && value[0] != '\0';
}

class OpenVrOverlayLifecycleCore final : public OpenVrOverlayLifecycle {
public:
    OpenVrOverlayLifecycleCore(
        std::unique_ptr<OpenVrOverlayLifecyclePort> lifecycle_port,
        std::unique_ptr<OpenVrOverlayTexturePort> texture_port) noexcept
        : lifecycle_port_(std::move(lifecycle_port)),
          texture_port_(std::move(texture_port))
    {
    }

    vrrec_status_t Initialize(
        const OpenVrOverlayLifecycleConfig &config) noexcept
    {
        std::uint64_t handle = 0;
        auto status = lifecycle_port_->CreateOverlay(
            config.overlay_key_utf8,
            config.overlay_name_utf8,
            handle);
        if (status != VRREC_STATUS_OK) {
            return status;
        }
        if (handle == 0) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }

        status = lifecycle_port_->SetOverlayWidthInMeters(
            handle,
            config.width_in_meters);
        if (status != VRREC_STATUS_OK) {
            static_cast<void>(lifecycle_port_->DestroyOverlay(handle));
            return status;
        }

        handle_ = handle;
        closed_ = false;
        return VRREC_STATUS_OK;
    }

    ~OpenVrOverlayLifecycleCore() override
    {
        static_cast<void>(Close());
    }

    vrrec_status_t Show() noexcept override
    {
        if (closed_) {
            return VRREC_STATUS_INVALID_STATE;
        }
        if (visible_) {
            return VRREC_STATUS_OK;
        }
        const auto status = lifecycle_port_->ShowOverlay(handle_);
        if (status == VRREC_STATUS_OK) {
            visible_ = true;
        }
        return status;
    }

    vrrec_status_t Hide() noexcept override
    {
        if (closed_) {
            return VRREC_STATUS_INVALID_STATE;
        }
        if (!visible_) {
            return VRREC_STATUS_OK;
        }
        const auto status = lifecycle_port_->HideOverlay(handle_);
        if (status == VRREC_STATUS_OK) {
            visible_ = false;
        }
        return status;
    }

    vrrec_status_t UpdateBgraTexture(
        const OpenVrBgraTextureFrame &frame) noexcept override
    {
        if (closed_) {
            return VRREC_STATUS_INVALID_STATE;
        }
        if (frame.pixel_bytes == nullptr ||
            frame.width != TextureWidth ||
            frame.height != TextureHeight ||
            frame.stride_bytes != TextureStrideBytes ||
            frame.pixel_bytes_size != TexturePixelBytesSize) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }
        const auto status = texture_port_->SetOverlayBgraTexture(
            handle_,
            frame);
        if (status == VRREC_STATUS_OK) {
            texture_set_ = true;
        }
        return status;
    }

    vrrec_status_t ClearTexture() noexcept override
    {
        if (closed_) {
            return VRREC_STATUS_INVALID_STATE;
        }
        if (!texture_set_) {
            return VRREC_STATUS_OK;
        }
        const auto status = texture_port_->ClearOverlayTexture(handle_);
        if (status == VRREC_STATUS_OK) {
            texture_set_ = false;
        }
        return status;
    }

    vrrec_status_t Close() noexcept override
    {
        if (closed_) {
            return close_status_;
        }
        closed_ = true;
        const auto clear_status = texture_set_
            ? texture_port_->ClearOverlayTexture(handle_)
            : VRREC_STATUS_OK;
        const auto hide_status = visible_
            ? lifecycle_port_->HideOverlay(handle_)
            : VRREC_STATUS_OK;
        const auto destroy_status = lifecycle_port_->DestroyOverlay(handle_);
        texture_set_ = false;
        visible_ = false;
        handle_ = 0;
        close_status_ = clear_status != VRREC_STATUS_OK
            ? clear_status
            : hide_status != VRREC_STATUS_OK
                ? hide_status
                : destroy_status;
        return close_status_;
    }

private:
    std::unique_ptr<OpenVrOverlayLifecyclePort> lifecycle_port_;
    std::unique_ptr<OpenVrOverlayTexturePort> texture_port_;
    std::uint64_t handle_ = 0;
    bool visible_ = false;
    bool texture_set_ = false;
    bool closed_ = true;
    vrrec_status_t close_status_ = VRREC_STATUS_OK;
};

class UnavailableOpenVrOverlayTexturePort final
    : public OpenVrOverlayTexturePort {
public:
    vrrec_status_t SetOverlayBgraTexture(
        std::uint64_t handle,
        const OpenVrBgraTextureFrame &frame) noexcept override
    {
        (void)handle;
        (void)frame;
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    }

    vrrec_status_t ClearOverlayTexture(
        std::uint64_t handle) noexcept override
    {
        (void)handle;
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    }
};

}

std::unique_ptr<OpenVrOverlayLifecycle> CreateOpenVrOverlayLifecycle(
    const OpenVrOverlayLifecycleConfig &config,
    std::unique_ptr<OpenVrOverlayLifecyclePort> port,
    vrrec_status_t &status) noexcept
{
    auto texture_port = std::unique_ptr<OpenVrOverlayTexturePort>(
        new (std::nothrow) UnavailableOpenVrOverlayTexturePort());
    if (!texture_port) {
        status = VRREC_STATUS_OUT_OF_MEMORY;
        return nullptr;
    }
    return CreateOpenVrOverlayLifecycle(
        config,
        std::move(port),
        std::move(texture_port),
        status);
}

std::unique_ptr<OpenVrOverlayLifecycle> CreateOpenVrOverlayLifecycle(
    const OpenVrOverlayLifecycleConfig &config,
    std::unique_ptr<OpenVrOverlayLifecyclePort> lifecycle_port,
    std::unique_ptr<OpenVrOverlayTexturePort> texture_port,
    vrrec_status_t &status) noexcept
{
    status = VRREC_STATUS_INVALID_ARGUMENT;
    if (!lifecycle_port || !texture_port ||
        !IsPresent(config.overlay_key_utf8) ||
        !IsPresent(config.overlay_name_utf8) ||
        !std::isfinite(config.width_in_meters) ||
        config.width_in_meters < MinimumWidthInMeters ||
        config.width_in_meters > MaximumWidthInMeters) {
        return nullptr;
    }

    auto concrete = std::unique_ptr<OpenVrOverlayLifecycleCore>(
        new (std::nothrow) OpenVrOverlayLifecycleCore(
            std::move(lifecycle_port),
            std::move(texture_port)));
    if (!concrete) {
        status = VRREC_STATUS_OUT_OF_MEMORY;
        return nullptr;
    }

    status = concrete->Initialize(config);
    if (status != VRREC_STATUS_OK) {
        return nullptr;
    }
    return concrete;
}

}
