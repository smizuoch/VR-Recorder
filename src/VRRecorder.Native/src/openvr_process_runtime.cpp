#include "openvr_process_runtime.hpp"

#include <cstdint>
#include <memory>
#include <mutex>
#include <new>
#include <string>
#include <string_view>
#include <utility>

namespace vrrecorder::native {

class OpenVrProcessRuntime final {
public:
    explicit OpenVrProcessRuntime(
        std::unique_ptr<OpenVrInputPort> api) noexcept
        : api_(std::move(api))
    {
    }

    ~OpenVrProcessRuntime()
    {
        const std::lock_guard lock(mutex_);
        if (references_ != 0) {
            api_->Shutdown();
        }
    }

    vrrec_status_t Acquire() noexcept
    {
        const std::lock_guard lock(mutex_);
        if (references_ == 0) {
            const auto status = api_->Initialize();
            if (status != VRREC_STATUS_OK) {
                return status;
            }
        }
        ++references_;
        return VRREC_STATUS_OK;
    }

    vrrec_status_t SetActionManifestPath(
        std::string_view absolute_path) noexcept
    {
        std::string owned_path;
        try {
            owned_path.assign(absolute_path);
        } catch (const std::bad_alloc &) {
            return VRREC_STATUS_OUT_OF_MEMORY;
        } catch (...) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }

        const std::lock_guard lock(mutex_);
        if (references_ == 0) {
            return VRREC_STATUS_INVALID_STATE;
        }
        if (!manifest_path_.empty()) {
            return manifest_path_ == owned_path
                ? VRREC_STATUS_OK
                : VRREC_STATUS_INVALID_ARGUMENT;
        }
        const auto status = api_->SetActionManifestPath(owned_path);
        if (status == VRREC_STATUS_OK) {
            manifest_path_ = std::move(owned_path);
        }
        return status;
    }

    vrrec_status_t AddApplicationManifest(
        std::string_view absolute_path,
        bool temporary) noexcept
    {
        std::string owned_path;
        try {
            owned_path.assign(absolute_path);
        } catch (const std::bad_alloc &) {
            return VRREC_STATUS_OUT_OF_MEMORY;
        } catch (...) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }

        const std::lock_guard lock(mutex_);
        if (references_ == 0) {
            return VRREC_STATUS_INVALID_STATE;
        }
        if (!application_manifest_path_.empty()) {
            return application_manifest_path_ == owned_path &&
                   application_manifest_temporary_ == temporary
                ? VRREC_STATUS_OK
                : VRREC_STATUS_INVALID_ARGUMENT;
        }
        const auto status = api_->AddApplicationManifest(
            owned_path,
            temporary);
        if (status == VRREC_STATUS_OK) {
            application_manifest_path_ = std::move(owned_path);
            application_manifest_temporary_ = temporary;
        }
        return status;
    }

    vrrec_status_t GetActionSetHandle(
        std::string_view action_set_path,
        std::uint64_t &handle) noexcept
    {
        const std::lock_guard lock(mutex_);
        return references_ == 0
            ? VRREC_STATUS_INVALID_STATE
            : api_->GetActionSetHandle(action_set_path, handle);
    }

    vrrec_status_t GetDigitalActionHandle(
        std::string_view action_path,
        std::uint64_t &handle) noexcept
    {
        const std::lock_guard lock(mutex_);
        return references_ == 0
            ? VRREC_STATUS_INVALID_STATE
            : api_->GetDigitalActionHandle(action_path, handle);
    }

    vrrec_status_t UpdateActionState(
        std::uint64_t action_set_handle) noexcept
    {
        const std::lock_guard lock(mutex_);
        return references_ == 0
            ? VRREC_STATUS_INVALID_STATE
            : api_->UpdateActionState(action_set_handle);
    }

    vrrec_status_t GetDigitalActionData(
        std::uint64_t action_handle,
        OpenVrDigitalActionData &data) noexcept
    {
        const std::lock_guard lock(mutex_);
        return references_ == 0
            ? VRREC_STATUS_INVALID_STATE
            : api_->GetDigitalActionData(action_handle, data);
    }

    void Release() noexcept
    {
        const std::lock_guard lock(mutex_);
        if (references_ == 0) {
            return;
        }
        --references_;
        if (references_ == 0) {
            api_->Shutdown();
            application_manifest_path_.clear();
            application_manifest_temporary_ = false;
            manifest_path_.clear();
        }
    }

private:
    std::unique_ptr<OpenVrInputPort> api_;
    std::mutex mutex_;
    std::size_t references_ = 0;
    std::string application_manifest_path_;
    bool application_manifest_temporary_ = false;
    std::string manifest_path_;
};

namespace {

class OpenVrProcessInputPort final : public OpenVrInputPort {
public:
    explicit OpenVrProcessInputPort(
        std::shared_ptr<OpenVrProcessRuntime> runtime) noexcept
        : runtime_(std::move(runtime))
    {
    }

    ~OpenVrProcessInputPort() override
    {
        Shutdown();
    }

    vrrec_status_t Initialize() noexcept override
    {
        if (initialized_) {
            return VRREC_STATUS_INVALID_STATE;
        }
        const auto status = runtime_->Acquire();
        if (status == VRREC_STATUS_OK) {
            initialized_ = true;
        }
        return status;
    }

    vrrec_status_t SetActionManifestPath(
        std::string_view absolute_path) noexcept override
    {
        return initialized_
            ? runtime_->SetActionManifestPath(absolute_path)
            : VRREC_STATUS_INVALID_STATE;
    }

    vrrec_status_t AddApplicationManifest(
        std::string_view absolute_path,
        bool temporary) noexcept override
    {
        return initialized_
            ? runtime_->AddApplicationManifest(absolute_path, temporary)
            : VRREC_STATUS_INVALID_STATE;
    }

    vrrec_status_t GetActionSetHandle(
        std::string_view action_set_path,
        std::uint64_t &handle) noexcept override
    {
        return initialized_
            ? runtime_->GetActionSetHandle(action_set_path, handle)
            : VRREC_STATUS_INVALID_STATE;
    }

    vrrec_status_t GetDigitalActionHandle(
        std::string_view action_path,
        std::uint64_t &handle) noexcept override
    {
        return initialized_
            ? runtime_->GetDigitalActionHandle(action_path, handle)
            : VRREC_STATUS_INVALID_STATE;
    }

    vrrec_status_t UpdateActionState(
        std::uint64_t action_set_handle) noexcept override
    {
        return initialized_
            ? runtime_->UpdateActionState(action_set_handle)
            : VRREC_STATUS_INVALID_STATE;
    }

    vrrec_status_t GetDigitalActionData(
        std::uint64_t action_handle,
        OpenVrDigitalActionData &data) noexcept override
    {
        return initialized_
            ? runtime_->GetDigitalActionData(action_handle, data)
            : VRREC_STATUS_INVALID_STATE;
    }

    void Shutdown() noexcept override
    {
        if (!initialized_) {
            return;
        }
        initialized_ = false;
        runtime_->Release();
    }

private:
    std::shared_ptr<OpenVrProcessRuntime> runtime_;
    bool initialized_ = false;
};

}

std::shared_ptr<OpenVrProcessRuntime> CreateOpenVrProcessRuntime(
    std::unique_ptr<OpenVrInputPort> api,
    vrrec_status_t &status) noexcept
{
    status = VRREC_STATUS_INVALID_ARGUMENT;
    if (!api) {
        return nullptr;
    }
    try {
        auto runtime = std::make_shared<OpenVrProcessRuntime>(std::move(api));
        status = VRREC_STATUS_OK;
        return runtime;
    } catch (const std::bad_alloc &) {
        status = VRREC_STATUS_OUT_OF_MEMORY;
        return nullptr;
    } catch (...) {
        status = VRREC_STATUS_INTERNAL_ERROR;
        return nullptr;
    }
}

std::unique_ptr<OpenVrInputPort> CreateOpenVrProcessInputPort(
    std::shared_ptr<OpenVrProcessRuntime> runtime,
    vrrec_status_t &status) noexcept
{
    status = VRREC_STATUS_INVALID_ARGUMENT;
    if (!runtime) {
        return nullptr;
    }
    auto port = std::unique_ptr<OpenVrInputPort>(
        new (std::nothrow) OpenVrProcessInputPort(std::move(runtime)));
    if (!port) {
        status = VRREC_STATUS_OUT_OF_MEMORY;
        return nullptr;
    }
    status = VRREC_STATUS_OK;
    return port;
}

}
