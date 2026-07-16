#include "openvr_overlay_texture_presenter.hpp"

#include <cstddef>
#include <cstdint>
#include <memory>
#include <new>
#include <unordered_map>
#include <utility>

namespace vrrecorder::native {
namespace {

constexpr auto TextureWidth = std::uint32_t {1024};
constexpr auto TextureHeight = std::uint32_t {512};
constexpr auto TextureStrideBytes = std::uint32_t {4096};
constexpr auto TexturePixelBytesSize = std::size_t {2'097'152};

struct TextureEntry final {
    std::unique_ptr<OpenVrOverlayTextureGraphicsResource> resource;
    bool submitted = false;
};

class OpenVrOverlayTexturePresenter final : public OpenVrOverlayTexturePort {
public:
    explicit OpenVrOverlayTexturePresenter(
        std::unique_ptr<OpenVrOverlayTextureGraphicsPort> graphics_port)
        noexcept
        : graphics_port_(std::move(graphics_port))
    {
    }

    vrrec_status_t SetOverlayBgraTexture(
        std::uint64_t handle,
        const OpenVrBgraTextureFrame &frame) noexcept override
    {
        if (handle == 0 || frame.pixel_bytes == nullptr ||
            frame.pixel_bytes_size != TexturePixelBytesSize ||
            frame.width != TextureWidth ||
            frame.height != TextureHeight ||
            frame.stride_bytes != TextureStrideBytes) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }

        auto existing = textures_.find(handle);
        if (existing == textures_.end()) {
            auto resource =
                std::unique_ptr<OpenVrOverlayTextureGraphicsResource> {};
            const auto create_status = graphics_port_->CreateBgraTexture(
                frame.width,
                frame.height,
                resource);
            if (create_status != VRREC_STATUS_OK) {
                return create_status;
            }
            if (!resource) {
                return VRREC_STATUS_INTERNAL_ERROR;
            }
            try {
                existing = textures_.emplace(
                    handle,
                    TextureEntry {std::move(resource), false}).first;
            } catch (const std::bad_alloc &) {
                return VRREC_STATUS_OUT_OF_MEMORY;
            } catch (...) {
                return VRREC_STATUS_INTERNAL_ERROR;
            }
        }

        auto &entry = existing->second;
        const auto upload_status = graphics_port_->UploadBgraTexture(
            *entry.resource,
            frame);
        if (upload_status != VRREC_STATUS_OK) {
            if (!entry.submitted) {
                textures_.erase(existing);
            }
            return upload_status;
        }

        const auto submit_status = graphics_port_->SubmitOverlayTexture(
            handle,
            *entry.resource);
        if (submit_status != VRREC_STATUS_OK) {
            if (!entry.submitted) {
                textures_.erase(existing);
            }
            return submit_status;
        }
        entry.submitted = true;
        return VRREC_STATUS_OK;
    }

    vrrec_status_t ClearOverlayTexture(
        std::uint64_t handle) noexcept override
    {
        if (handle == 0) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }
        const auto existing = textures_.find(handle);
        if (existing == textures_.end()) {
            return VRREC_STATUS_OK;
        }
        if (existing->second.submitted) {
            const auto clear_status =
                graphics_port_->ClearOverlayTexture(handle);
            if (clear_status != VRREC_STATUS_OK) {
                return clear_status;
            }
        }
        textures_.erase(existing);
        return VRREC_STATUS_OK;
    }

private:
    std::unique_ptr<OpenVrOverlayTextureGraphicsPort> graphics_port_;
    std::unordered_map<std::uint64_t, TextureEntry> textures_;
};

}

std::unique_ptr<OpenVrOverlayTexturePort>
CreateOpenVrOverlayTexturePresenter(
    std::unique_ptr<OpenVrOverlayTextureGraphicsPort> graphics_port,
    vrrec_status_t &status) noexcept
{
    status = VRREC_STATUS_INVALID_ARGUMENT;
    if (!graphics_port) {
        return nullptr;
    }
    auto presenter = std::unique_ptr<OpenVrOverlayTexturePort>(
        new (std::nothrow) OpenVrOverlayTexturePresenter(
            std::move(graphics_port)));
    if (!presenter) {
        status = VRREC_STATUS_OUT_OF_MEMORY;
        return nullptr;
    }
    status = VRREC_STATUS_OK;
    return presenter;
}

}
