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

bool IsPresent(const char *value) noexcept
{
    return value != nullptr && value[0] != '\0';
}

class OpenVrOverlayLifecycleCore final : public OpenVrOverlayLifecycle {
public:
    OpenVrOverlayLifecycleCore(
        std::unique_ptr<OpenVrOverlayLifecyclePort> port) noexcept
        : port_(std::move(port))
    {
    }

    vrrec_status_t Initialize(
        const OpenVrOverlayLifecycleConfig &config) noexcept
    {
        std::uint64_t handle = 0;
        auto status = port_->CreateOverlay(
            config.overlay_key_utf8,
            config.overlay_name_utf8,
            handle);
        if (status != VRREC_STATUS_OK) {
            return status;
        }
        if (handle == 0) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }

        status = port_->SetOverlayWidthInMeters(
            handle,
            config.width_in_meters);
        if (status != VRREC_STATUS_OK) {
            static_cast<void>(port_->DestroyOverlay(handle));
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
        const auto status = port_->ShowOverlay(handle_);
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
        const auto status = port_->HideOverlay(handle_);
        if (status == VRREC_STATUS_OK) {
            visible_ = false;
        }
        return status;
    }

    vrrec_status_t Close() noexcept override
    {
        if (closed_) {
            return close_status_;
        }
        closed_ = true;
        const auto hide_status = visible_
            ? port_->HideOverlay(handle_)
            : VRREC_STATUS_OK;
        const auto destroy_status = port_->DestroyOverlay(handle_);
        visible_ = false;
        handle_ = 0;
        close_status_ = hide_status != VRREC_STATUS_OK
            ? hide_status
            : destroy_status;
        return close_status_;
    }

private:
    std::unique_ptr<OpenVrOverlayLifecyclePort> port_;
    std::uint64_t handle_ = 0;
    bool visible_ = false;
    bool closed_ = true;
    vrrec_status_t close_status_ = VRREC_STATUS_OK;
};

}

std::unique_ptr<OpenVrOverlayLifecycle> CreateOpenVrOverlayLifecycle(
    const OpenVrOverlayLifecycleConfig &config,
    std::unique_ptr<OpenVrOverlayLifecyclePort> port,
    vrrec_status_t &status) noexcept
{
    status = VRREC_STATUS_INVALID_ARGUMENT;
    if (!port || !IsPresent(config.overlay_key_utf8) ||
        !IsPresent(config.overlay_name_utf8) ||
        !std::isfinite(config.width_in_meters) ||
        config.width_in_meters < MinimumWidthInMeters ||
        config.width_in_meters > MaximumWidthInMeters) {
        return nullptr;
    }

    auto concrete = std::unique_ptr<OpenVrOverlayLifecycleCore>(
        new (std::nothrow) OpenVrOverlayLifecycleCore(std::move(port)));
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
