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
        std::unique_ptr<OpenVrOverlayTexturePort> texture_port,
        std::unique_ptr<OpenVrOverlayEventPort> event_port,
        std::unique_ptr<OpenVrOverlayPosePort> pose_port) noexcept
        : lifecycle_port_(std::move(lifecycle_port)),
          texture_port_(std::move(texture_port)),
          event_port_(std::move(event_port)),
          pose_port_(std::move(pose_port))
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

        status = event_port_->ConfigureOverlayPointerInput(
            handle,
            TextureWidth,
            TextureHeight);
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

    vrrec_status_t PollPointerEvent(
        OpenVrOverlayPointerEvent &event,
        bool &has_event) noexcept override
    {
        event = {};
        has_event = false;
        if (closed_) {
            return VRREC_STATUS_INVALID_STATE;
        }
        const auto status = event_port_->PollNextOverlayPointerEvent(
            handle_,
            event,
            has_event);
        if (status != VRREC_STATUS_OK) {
            event = {};
            has_event = false;
            return status;
        }
        if (!has_event) {
            event = {};
            return VRREC_STATUS_OK;
        }

        const auto known_kind =
            event.kind == OpenVrOverlayPointerEventKind::Move ||
            event.kind == OpenVrOverlayPointerEventKind::ButtonDown ||
            event.kind == OpenVrOverlayPointerEventKind::ButtonUp;
        const auto known_button = event.button == 1 ||
            event.button == 2 || event.button == 4;
        const auto valid_button =
            event.kind == OpenVrOverlayPointerEventKind::Move
                ? event.button == 0
                : known_button;
        if (!known_kind || !valid_button ||
            event.pixel_x >= TextureWidth ||
            event.pixel_y >= TextureHeight) {
            event = {};
            has_event = false;
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        return VRREC_STATUS_OK;
    }

    vrrec_status_t SetPose(
        const OpenVrOverlayPose &pose) noexcept override
    {
        if (closed_) {
            return VRREC_STATUS_INVALID_STATE;
        }
        if (!IsValidOpenVrOverlayPose(pose)) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }
        return pose_port_->SetOverlayPose(handle_, pose);
    }

    vrrec_status_t GetPose(OpenVrOverlayPose &pose) noexcept override
    {
        pose = {};
        if (closed_) {
            return VRREC_STATUS_INVALID_STATE;
        }
        const auto status = pose_port_->GetOverlayPose(handle_, pose);
        if (status != VRREC_STATUS_OK) {
            pose = {};
            return status;
        }
        if (!IsValidOpenVrOverlayPose(pose)) {
            pose = {};
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        return VRREC_STATUS_OK;
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
    std::unique_ptr<OpenVrOverlayEventPort> event_port_;
    std::unique_ptr<OpenVrOverlayPosePort> pose_port_;
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

class NoopOpenVrOverlayEventPort final : public OpenVrOverlayEventPort {
public:
    vrrec_status_t ConfigureOverlayPointerInput(
        std::uint64_t handle,
        std::uint32_t pixel_width,
        std::uint32_t pixel_height) noexcept override
    {
        (void)handle;
        (void)pixel_width;
        (void)pixel_height;
        return VRREC_STATUS_OK;
    }

    vrrec_status_t PollNextOverlayPointerEvent(
        std::uint64_t handle,
        OpenVrOverlayPointerEvent &event,
        bool &has_event) noexcept override
    {
        (void)handle;
        event = {};
        has_event = false;
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    }
};

class UnavailableOpenVrOverlayPosePort final : public OpenVrOverlayPosePort {
public:
    vrrec_status_t SetOverlayPose(
        std::uint64_t handle,
        const OpenVrOverlayPose &pose) noexcept override
    {
        (void)handle;
        (void)pose;
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    }

    vrrec_status_t GetOverlayPose(
        std::uint64_t handle,
        OpenVrOverlayPose &pose) noexcept override
    {
        (void)handle;
        pose = {};
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
    auto event_port = std::unique_ptr<OpenVrOverlayEventPort>(
        new (std::nothrow) NoopOpenVrOverlayEventPort());
    if (!event_port) {
        status = VRREC_STATUS_OUT_OF_MEMORY;
        return nullptr;
    }
    return CreateOpenVrOverlayLifecycle(
        config,
        std::move(lifecycle_port),
        std::move(texture_port),
        std::move(event_port),
        status);
}

std::unique_ptr<OpenVrOverlayLifecycle> CreateOpenVrOverlayLifecycle(
    const OpenVrOverlayLifecycleConfig &config,
    std::unique_ptr<OpenVrOverlayLifecyclePort> lifecycle_port,
    std::unique_ptr<OpenVrOverlayTexturePort> texture_port,
    std::unique_ptr<OpenVrOverlayEventPort> event_port,
    vrrec_status_t &status) noexcept
{
    auto pose_port = std::unique_ptr<OpenVrOverlayPosePort>(
        new (std::nothrow) UnavailableOpenVrOverlayPosePort());
    if (!pose_port) {
        status = VRREC_STATUS_OUT_OF_MEMORY;
        return nullptr;
    }
    return CreateOpenVrOverlayLifecycle(
        config,
        std::move(lifecycle_port),
        std::move(texture_port),
        std::move(event_port),
        std::move(pose_port),
        status);
}

std::unique_ptr<OpenVrOverlayLifecycle> CreateOpenVrOverlayLifecycle(
    const OpenVrOverlayLifecycleConfig &config,
    std::unique_ptr<OpenVrOverlayLifecyclePort> lifecycle_port,
    std::unique_ptr<OpenVrOverlayTexturePort> texture_port,
    std::unique_ptr<OpenVrOverlayEventPort> event_port,
    std::unique_ptr<OpenVrOverlayPosePort> pose_port,
    vrrec_status_t &status) noexcept
{
    status = VRREC_STATUS_INVALID_ARGUMENT;
    if (!lifecycle_port || !texture_port || !event_port || !pose_port ||
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
            std::move(texture_port),
            std::move(event_port),
            std::move(pose_port)));
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
