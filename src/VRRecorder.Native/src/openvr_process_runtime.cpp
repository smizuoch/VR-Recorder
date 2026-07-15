#include "openvr_process_runtime.hpp"

#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <memory>
#include <mutex>
#include <new>
#include <string>
#include <string_view>
#include <thread>
#include <unordered_map>
#include <utility>

#include "native_thread_factory.hpp"

namespace vrrecorder::native {

class OpenVrProcessRuntime final {
public:
    explicit OpenVrProcessRuntime(
        std::unique_ptr<OpenVrRuntimePort> api,
        NativeThreadFactoryPort &thread_factory) noexcept
        : api_(std::move(api)), thread_factory_(thread_factory)
    {
    }

    ~OpenVrProcessRuntime()
    {
        std::thread poll_thread;
        {
            const std::lock_guard lock(mutex_);
            stop_requested_ = true;
            condition_.notify_all();
            poll_thread = std::move(poll_thread_);
        }
        if (poll_thread.joinable()) {
            poll_thread.join();
        }
        const std::lock_guard lock(mutex_);
        if (references_ != 0) {
            api_->Shutdown();
        }
    }

    vrrec_status_t Acquire() noexcept
    {
        std::unique_lock lock(mutex_);
        try {
            condition_.wait(lock, [this] { return !generation_stopping_; });
        } catch (...) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
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
        if (references_ == 0) {
            return VRREC_STATUS_INVALID_STATE;
        }
        const auto status = api_->GetActionSetHandle(action_set_path, handle);
        if (status != VRREC_STATUS_OK || handle == 0) {
            return status;
        }
        if (action_set_handle_ != 0 && action_set_handle_ != handle) {
            handle = 0;
            return VRREC_STATUS_INVALID_ARGUMENT;
        }
        action_set_handle_ = handle;
        return VRREC_STATUS_OK;
    }

    vrrec_status_t GetDigitalActionHandle(
        std::string_view action_path,
        std::uint64_t &handle,
        std::uint64_t &registration_revision) noexcept
    {
        registration_revision = 0;
        const std::lock_guard lock(mutex_);
        if (references_ == 0) {
            return VRREC_STATUS_INVALID_STATE;
        }
        const auto status = api_->GetDigitalActionHandle(action_path, handle);
        if (status != VRREC_STATUS_OK || handle == 0) {
            return status;
        }
        try {
            action_snapshots_.try_emplace(handle);
        } catch (const std::bad_alloc &) {
            handle = 0;
            return VRREC_STATUS_OUT_OF_MEMORY;
        } catch (...) {
            handle = 0;
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        registration_revision = revision_;
        return VRREC_STATUS_OK;
    }

    vrrec_status_t WaitForActionState(
        std::uint64_t action_set_handle,
        std::uint64_t action_handle,
        std::uint64_t &last_revision,
        OpenVrDigitalActionData &data,
        vrrec_status_t &digital_status) noexcept
    {
        data = {};
        digital_status = VRREC_STATUS_INVALID_STATE;
        std::unique_lock lock(mutex_);
        if (references_ == 0 || action_set_handle == 0 ||
            action_set_handle != action_set_handle_ ||
            action_snapshots_.find(action_handle) == action_snapshots_.end()) {
            return VRREC_STATUS_INVALID_STATE;
        }
        if (!poll_thread_.joinable()) {
            const auto start_status = thread_factory_.Start(
                poll_thread_,
                &OpenVrProcessRuntime::PollLoopEntry,
                this);
            if (start_status != VRREC_STATUS_OK) {
                return start_status;
            }
            if (!poll_thread_.joinable()) {
                return VRREC_STATUS_INTERNAL_ERROR;
            }
        }
        if (last_revision >= revision_) {
            requested_revision_ = revision_ + 1;
            condition_.notify_all();
        }
        try {
            condition_.wait(lock, [this, last_revision] {
                return revision_ > last_revision || poll_thread_failed_ ||
                       references_ == 0;
            });
        } catch (...) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        if (references_ == 0 || poll_thread_failed_) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }

        last_revision = revision_;
        if (poll_status_ != VRREC_STATUS_OK) {
            return poll_status_;
        }
        const auto snapshot = action_snapshots_.find(action_handle);
        if (snapshot == action_snapshots_.end() ||
            snapshot->second.revision != revision_) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        data = snapshot->second.data;
        digital_status = snapshot->second.status;
        return VRREC_STATUS_OK;
    }

    vrrec_status_t CreateOverlay(
        std::string_view key,
        std::string_view name,
        std::uint64_t &handle) noexcept
    {
        const std::lock_guard lock(mutex_);
        return references_ == 0
            ? VRREC_STATUS_INVALID_STATE
            : api_->CreateOverlay(key, name, handle);
    }

    vrrec_status_t SetOverlayWidthInMeters(
        std::uint64_t handle,
        float width) noexcept
    {
        const std::lock_guard lock(mutex_);
        return references_ == 0
            ? VRREC_STATUS_INVALID_STATE
            : api_->SetOverlayWidthInMeters(handle, width);
    }

    vrrec_status_t ShowOverlay(std::uint64_t handle) noexcept
    {
        const std::lock_guard lock(mutex_);
        return references_ == 0
            ? VRREC_STATUS_INVALID_STATE
            : api_->ShowOverlay(handle);
    }

    vrrec_status_t HideOverlay(std::uint64_t handle) noexcept
    {
        const std::lock_guard lock(mutex_);
        return references_ == 0
            ? VRREC_STATUS_INVALID_STATE
            : api_->HideOverlay(handle);
    }

    vrrec_status_t DestroyOverlay(std::uint64_t handle) noexcept
    {
        const std::lock_guard lock(mutex_);
        return references_ == 0
            ? VRREC_STATUS_INVALID_STATE
            : api_->DestroyOverlay(handle);
    }

    void Release() noexcept
    {
        std::thread poll_thread;
        {
            const std::lock_guard lock(mutex_);
            if (references_ == 0) {
                return;
            }
            --references_;
            if (references_ != 0) {
                return;
            }
            generation_stopping_ = true;
            stop_requested_ = true;
            condition_.notify_all();
            poll_thread = std::move(poll_thread_);
        }
        if (poll_thread.joinable()) {
            poll_thread.join();
        }
        {
            const std::lock_guard lock(mutex_);
            api_->Shutdown();
            application_manifest_path_.clear();
            application_manifest_temporary_ = false;
            manifest_path_.clear();
            action_set_handle_ = 0;
            action_snapshots_.clear();
            revision_ = 0;
            requested_revision_ = 0;
            poll_status_ = VRREC_STATUS_INVALID_STATE;
            stop_requested_ = false;
            poll_thread_failed_ = false;
            generation_stopping_ = false;
        }
        condition_.notify_all();
    }

private:
    struct ActionSnapshot final {
        OpenVrDigitalActionData data {};
        vrrec_status_t status = VRREC_STATUS_INVALID_STATE;
        std::uint64_t revision = 0;
    };

    static void PollLoopEntry(void *context) noexcept
    {
        auto &runtime = *static_cast<OpenVrProcessRuntime *>(context);
        try {
            runtime.PollLoop();
        } catch (...) {
            const std::lock_guard lock(runtime.mutex_);
            runtime.poll_thread_failed_ = true;
            runtime.condition_.notify_all();
        }
    }

    void PollLoop()
    {
        constexpr auto minimum_interval =
            std::chrono::nanoseconds(11'111'112);
        auto next_poll = std::chrono::steady_clock::now();
        std::unique_lock lock(mutex_);
        while (!stop_requested_) {
            condition_.wait(lock, [this] {
                return stop_requested_ || requested_revision_ > revision_;
            });
            if (stop_requested_) {
                break;
            }
            if (const auto now = std::chrono::steady_clock::now();
                now < next_poll) {
                condition_.wait_until(
                    lock,
                    next_poll,
                    [this] { return stop_requested_; });
                if (stop_requested_) {
                    break;
                }
            }

            const auto next_revision = revision_ + 1;
            poll_status_ = api_->UpdateActionState(action_set_handle_);
            for (auto &[handle, snapshot] : action_snapshots_) {
                snapshot.data = {};
                snapshot.status = poll_status_ == VRREC_STATUS_OK
                    ? api_->GetDigitalActionData(handle, snapshot.data)
                    : poll_status_;
                if (snapshot.status != VRREC_STATUS_OK) {
                    snapshot.data = {};
                }
                snapshot.revision = next_revision;
            }
            revision_ = next_revision;
            next_poll = std::chrono::steady_clock::now() + minimum_interval;
            condition_.notify_all();
        }
    }

    std::unique_ptr<OpenVrRuntimePort> api_;
    NativeThreadFactoryPort &thread_factory_;
    std::mutex mutex_;
    std::condition_variable condition_;
    std::thread poll_thread_;
    std::size_t references_ = 0;
    std::string application_manifest_path_;
    bool application_manifest_temporary_ = false;
    std::string manifest_path_;
    std::uint64_t action_set_handle_ = 0;
    std::unordered_map<std::uint64_t, ActionSnapshot> action_snapshots_;
    std::uint64_t revision_ = 0;
    std::uint64_t requested_revision_ = 0;
    vrrec_status_t poll_status_ = VRREC_STATUS_INVALID_STATE;
    bool stop_requested_ = false;
    bool poll_thread_failed_ = false;
    bool generation_stopping_ = false;
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
        if (!initialized_) {
            return VRREC_STATUS_INVALID_STATE;
        }
        auto registration_revision = std::uint64_t {0};
        const auto status = runtime_->GetDigitalActionHandle(
            action_path,
            handle,
            registration_revision);
        if (status == VRREC_STATUS_OK) {
            action_handle_ = handle;
            last_revision_ = registration_revision;
            has_snapshot_ = false;
        }
        return status;
    }

    vrrec_status_t UpdateActionState(
        std::uint64_t action_set_handle) noexcept override
    {
        if (!initialized_ || action_handle_ == 0) {
            return VRREC_STATUS_INVALID_STATE;
        }
        auto data = OpenVrDigitalActionData {};
        auto digital_status = VRREC_STATUS_INVALID_STATE;
        const auto status = runtime_->WaitForActionState(
            action_set_handle,
            action_handle_,
            last_revision_,
            data,
            digital_status);
        if (status == VRREC_STATUS_OK) {
            snapshot_ = data;
            snapshot_status_ = digital_status;
            has_snapshot_ = true;
        } else {
            snapshot_ = {};
            snapshot_status_ = VRREC_STATUS_INVALID_STATE;
            has_snapshot_ = false;
        }
        return status;
    }

    vrrec_status_t GetDigitalActionData(
        std::uint64_t action_handle,
        OpenVrDigitalActionData &data) noexcept override
    {
        data = {};
        if (!initialized_ || !has_snapshot_ ||
            action_handle != action_handle_) {
            return VRREC_STATUS_INVALID_STATE;
        }
        data = snapshot_;
        return snapshot_status_;
    }

    void Shutdown() noexcept override
    {
        if (!initialized_) {
            return;
        }
        initialized_ = false;
        action_handle_ = 0;
        last_revision_ = 0;
        snapshot_ = {};
        snapshot_status_ = VRREC_STATUS_INVALID_STATE;
        has_snapshot_ = false;
        runtime_->Release();
    }

private:
    std::shared_ptr<OpenVrProcessRuntime> runtime_;
    bool initialized_ = false;
    std::uint64_t action_handle_ = 0;
    std::uint64_t last_revision_ = 0;
    OpenVrDigitalActionData snapshot_ {};
    vrrec_status_t snapshot_status_ = VRREC_STATUS_INVALID_STATE;
    bool has_snapshot_ = false;
};

class OpenVrProcessOverlayLifecyclePort final
    : public OpenVrOverlayLifecyclePort {
public:
    explicit OpenVrProcessOverlayLifecyclePort(
        std::shared_ptr<OpenVrProcessRuntime> runtime) noexcept
        : runtime_(std::move(runtime))
    {
    }

    ~OpenVrProcessOverlayLifecyclePort() override
    {
        if (acquired_) {
            runtime_->Release();
        }
    }

    vrrec_status_t Acquire() noexcept
    {
        if (acquired_) {
            return VRREC_STATUS_INVALID_STATE;
        }
        const auto status = runtime_->Acquire();
        if (status == VRREC_STATUS_OK) {
            acquired_ = true;
        }
        return status;
    }

    vrrec_status_t CreateOverlay(
        std::string_view key,
        std::string_view name,
        std::uint64_t &handle) noexcept override
    {
        return acquired_
            ? runtime_->CreateOverlay(key, name, handle)
            : VRREC_STATUS_INVALID_STATE;
    }

    vrrec_status_t SetOverlayWidthInMeters(
        std::uint64_t handle,
        float width) noexcept override
    {
        return acquired_
            ? runtime_->SetOverlayWidthInMeters(handle, width)
            : VRREC_STATUS_INVALID_STATE;
    }

    vrrec_status_t ShowOverlay(std::uint64_t handle) noexcept override
    {
        return acquired_
            ? runtime_->ShowOverlay(handle)
            : VRREC_STATUS_INVALID_STATE;
    }

    vrrec_status_t HideOverlay(std::uint64_t handle) noexcept override
    {
        return acquired_
            ? runtime_->HideOverlay(handle)
            : VRREC_STATUS_INVALID_STATE;
    }

    vrrec_status_t DestroyOverlay(std::uint64_t handle) noexcept override
    {
        return acquired_
            ? runtime_->DestroyOverlay(handle)
            : VRREC_STATUS_INVALID_STATE;
    }

private:
    std::shared_ptr<OpenVrProcessRuntime> runtime_;
    bool acquired_ = false;
};

}

std::shared_ptr<OpenVrProcessRuntime> CreateOpenVrProcessRuntime(
    std::unique_ptr<OpenVrRuntimePort> api,
    vrrec_status_t &status,
    NativeThreadFactoryPort *thread_factory) noexcept
{
    status = VRREC_STATUS_INVALID_ARGUMENT;
    if (!api) {
        return nullptr;
    }
    try {
        auto &factory = thread_factory != nullptr
            ? *thread_factory
            : DefaultNativeThreadFactory();
        auto runtime = std::make_shared<OpenVrProcessRuntime>(
            std::move(api),
            factory);
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

std::unique_ptr<OpenVrOverlayLifecyclePort>
CreateOpenVrProcessOverlayLifecyclePort(
    std::shared_ptr<OpenVrProcessRuntime> runtime,
    vrrec_status_t &status) noexcept
{
    status = VRREC_STATUS_INVALID_ARGUMENT;
    if (!runtime) {
        return nullptr;
    }
    auto port = std::unique_ptr<OpenVrProcessOverlayLifecyclePort>(
        new (std::nothrow) OpenVrProcessOverlayLifecyclePort(
            std::move(runtime)));
    if (!port) {
        status = VRREC_STATUS_OUT_OF_MEMORY;
        return nullptr;
    }
    status = port->Acquire();
    if (status != VRREC_STATUS_OK) {
        return nullptr;
    }
    return port;
}

std::unique_ptr<OpenVrOverlayLifecycle>
CreateOpenVrProcessOverlayLifecycle(
    std::shared_ptr<OpenVrProcessRuntime> runtime,
    std::string_view application_manifest_path,
    const OpenVrOverlayLifecycleConfig &config,
    vrrec_status_t &status) noexcept
{
    status = VRREC_STATUS_INVALID_ARGUMENT;
    if (!runtime || application_manifest_path.empty()) {
        return nullptr;
    }

    auto input = CreateOpenVrProcessInputPort(runtime, status);
    if (!input) {
        return nullptr;
    }
    status = input->Initialize();
    if (status != VRREC_STATUS_OK) {
        return nullptr;
    }
    status = input->AddApplicationManifest(
        application_manifest_path,
        true);
    if (status != VRREC_STATUS_OK) {
        return nullptr;
    }

    auto overlay_port = CreateOpenVrProcessOverlayLifecyclePort(
        std::move(runtime),
        status);
    if (!overlay_port) {
        return nullptr;
    }
    return CreateOpenVrOverlayLifecycle(
        config,
        std::move(overlay_port),
        status);
}

}
