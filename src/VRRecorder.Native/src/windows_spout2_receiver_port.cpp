#include "windows_spout2_receiver_port.hpp"

#if !defined(_WIN32)
#error "The Spout2 receiver Port requires Windows"
#endif

#include <SpoutDX/SpoutDX.h>

#include <d3d11.h>
#include <dxgi1_2.h>
#include <windows.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstdint>
#include <cstdio>
#include <limits>
#include <map>
#include <memory>
#include <mutex>
#include <new>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

#include "windows_d3d11_owned_video_surface.hpp"
#include "windows_d3d11_multithread_protection.hpp"

namespace vrrecorder::native {
namespace {

using MonotonicClock = std::chrono::steady_clock;
constexpr auto retry_interval = std::chrono::milliseconds(2);

template <typename T>
class ComOwner final {
public:
    ~ComOwner()
    {
        Reset();
    }

    ComOwner(const ComOwner &) = delete;
    ComOwner &operator=(const ComOwner &) = delete;
    ComOwner() noexcept = default;

    T *Get() const noexcept
    {
        return value_;
    }

    T **Put() noexcept
    {
        Reset();
        return &value_;
    }

    void Reset() noexcept
    {
        if (value_ != nullptr) {
            value_->Release();
            value_ = nullptr;
        }
    }

private:
    T *value_ = nullptr;
};

struct SenderHistory final {
    std::uint64_t receiver_epoch = 0;
    std::uint64_t latest_sequence = 0;
};

struct ReceiverState final {
    std::unique_ptr<spoutDX> receiver;
    std::uint64_t receiver_epoch;
};

struct AdapterIdentity final {
    std::uint64_t luid;
    std::string name;
    vrrec_gpu_vendor_t vendor;
};

std::uint64_t PackLuid(const LUID &luid) noexcept
{
    return static_cast<std::uint64_t>(luid.LowPart) |
           (static_cast<std::uint64_t>(
                static_cast<std::uint32_t>(luid.HighPart)) << 32U);
}

vrrec_gpu_vendor_t VendorFromId(UINT vendor_id) noexcept
{
    switch (vendor_id) {
    case 0x10DE:
        return VRREC_GPU_VENDOR_NVIDIA;
    case 0x1002:
    case 0x1022:
        return VRREC_GPU_VENDOR_AMD;
    case 0x8086:
        return VRREC_GPU_VENDOR_INTEL;
    default:
        return VRREC_GPU_VENDOR_UNKNOWN;
    }
}

bool TryWideToUtf8(const wchar_t *value, std::string &output) noexcept
{
    output.clear();
    if (value == nullptr || value[0] == L'\0') {
        return false;
    }
    const auto required = WideCharToMultiByte(
        CP_UTF8,
        WC_ERR_INVALID_CHARS,
        value,
        -1,
        nullptr,
        0,
        nullptr,
        nullptr);
    if (required <= 1) {
        return false;
    }
    try {
        output.resize(static_cast<std::size_t>(required));
    } catch (...) {
        return false;
    }
    const auto converted = WideCharToMultiByte(
        CP_UTF8,
        WC_ERR_INVALID_CHARS,
        value,
        -1,
        output.data(),
        required,
        nullptr,
        nullptr);
    if (converted != required) {
        output.clear();
        return false;
    }
    output.resize(static_cast<std::size_t>(required - 1));
    return true;
}

bool TryGetAdapterIdentity(
    ID3D11Device *device,
    AdapterIdentity &identity) noexcept
{
    if (device == nullptr) {
        return false;
    }
    ComOwner<IDXGIDevice> dxgi_device;
    auto result = device->QueryInterface(
        __uuidof(IDXGIDevice),
        reinterpret_cast<void **>(dxgi_device.Put()));
    if (FAILED(result)) {
        return false;
    }
    ComOwner<IDXGIAdapter> adapter;
    result = dxgi_device.Get()->GetAdapter(adapter.Put());
    if (FAILED(result)) {
        return false;
    }
    DXGI_ADAPTER_DESC descriptor {};
    result = adapter.Get()->GetDesc(&descriptor);
    if (FAILED(result)) {
        return false;
    }

    std::string description;
    if (!TryWideToUtf8(descriptor.Description, description)) {
        description = "unidentified-adapter";
    }
    char suffix[64] {};
    const auto written = std::snprintf(
        suffix,
        sizeof(suffix),
        " [vendor:%04X device:%04X]",
        descriptor.VendorId,
        descriptor.DeviceId);
    if (written <= 0 ||
        static_cast<std::size_t>(written) >= sizeof(suffix)) {
        return false;
    }
    try {
        description.append(suffix, static_cast<std::size_t>(written));
    } catch (...) {
        return false;
    }
    identity = AdapterIdentity {
        PackLuid(descriptor.AdapterLuid),
        std::move(description),
        VendorFromId(descriptor.VendorId),
    };
    return identity.luid != 0;
}

bool TryMapFormat(
    DXGI_FORMAT format,
    vrrec_source_pixel_format_t &pixel_format) noexcept
{
    switch (format) {
    case DXGI_FORMAT_B8G8R8A8_UNORM:
        pixel_format = VRREC_SOURCE_PIXEL_FORMAT_BGRA8;
        return true;
    case DXGI_FORMAT_R8G8B8A8_UNORM:
        pixel_format = VRREC_SOURCE_PIXEL_FORMAT_RGBA8;
        return true;
    case DXGI_FORMAT_NV12:
        pixel_format = VRREC_SOURCE_PIXEL_FORMAT_NV12;
        return true;
    default:
        return false;
    }
}

std::int64_t MonotonicMicroseconds() noexcept
{
    return std::chrono::duration_cast<std::chrono::microseconds>(
        MonotonicClock::now().time_since_epoch()).count();
}

class WindowsSpout2ReceiverPort final : public Spout2ReceiverPort {
public:
    vrrec_status_t Snapshot(
        std::vector<SpoutSenderSnapshot> &senders) override
    {
        senders.clear();
        if (aborted_.load()) {
            return VRREC_STATUS_INVALID_STATE;
        }
        auto names = SenderNames();
        if (!AreNamesValid(names)) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        senders.reserve(names.size());
        for (const auto &name : names) {
            const auto history = histories_.find(name);
            senders.push_back({
                name,
                history == histories_.end()
                    ? 0
                    : history->second.latest_sequence,
            });
        }
        return VRREC_STATUS_OK;
    }

    Spout2ReceiverResult Receive(
        std::chrono::milliseconds timeout,
        Spout2TextureMetadata &metadata) noexcept override
    {
        metadata = {};
        pending_sender_.clear();
        if (aborted_.load()) {
            return Spout2ReceiverResult::Aborted;
        }

        const auto deadline = MonotonicClock::now() + timeout;
        do {
            std::vector<std::string> names;
            try {
                names = SenderNames();
            } catch (const std::bad_alloc &) {
                return Spout2ReceiverResult::OutOfMemory;
            } catch (...) {
                return Spout2ReceiverResult::Failed;
            }
            if (!AreNamesValid(names)) {
                return Spout2ReceiverResult::Failed;
            }
            const auto reconcile = Reconcile(names);
            if (reconcile != Spout2ReceiverResult::Timeout) {
                return reconcile;
            }

            const auto received = PollReceivers(metadata);
            if (received != Spout2ReceiverResult::Timeout) {
                return received;
            }
            if (timeout.count() == 0 || MonotonicClock::now() >= deadline) {
                return Spout2ReceiverResult::Timeout;
            }

            std::unique_lock wait_lock(wait_mutex_);
            wait_condition_.wait_until(
                wait_lock,
                std::min(deadline, MonotonicClock::now() + retry_interval),
                [this] { return aborted_.load(); });
            if (aborted_.load()) {
                return Spout2ReceiverResult::Aborted;
            }
        } while (MonotonicClock::now() < deadline);
        return Spout2ReceiverResult::Timeout;
    }

    vrrec_status_t CopySurface(
        const Spout2TextureMetadata &metadata,
        std::uint64_t generation_id,
        std::shared_ptr<VideoSurface> &surface) noexcept override
    {
        surface.reset();
        if (aborted_.load()) {
            pending_sender_.clear();
            return VRREC_STATUS_INVALID_STATE;
        }
        if (pending_sender_ != metadata.sender_id ||
            pending_resource_identity_ != metadata.resource_identity ||
            pending_receiver_epoch_ != metadata.receiver_epoch) {
            pending_sender_.clear();
            return VRREC_STATUS_INVALID_STATE;
        }
        const auto receiver = receivers_.find(pending_sender_);
        pending_sender_.clear();
        if (receiver == receivers_.end() || !receiver->second.receiver) {
            return VRREC_STATUS_BACKEND_UNAVAILABLE;
        }

        auto *source_texture = receiver->second.receiver->GetSenderTexture();
        auto *device = receiver->second.receiver->GetDX11Device();
        auto *context = receiver->second.receiver->GetDX11Context();
        if (source_texture == nullptr || device == nullptr || context == nullptr) {
            return VRREC_STATUS_BACKEND_UNAVAILABLE;
        }
        const auto multithread_status =
            EnableWindowsD3d11MultithreadProtection(context);
        if (multithread_status != VRREC_STATUS_OK) {
            return multithread_status;
        }
        D3D11_TEXTURE2D_DESC source_descriptor {};
        source_texture->GetDesc(&source_descriptor);
        if (source_descriptor.Width != metadata.width ||
            source_descriptor.Height != metadata.height ||
            source_descriptor.MipLevels != 1 ||
            source_descriptor.ArraySize != 1 ||
            source_descriptor.SampleDesc.Count != 1 ||
            source_descriptor.Usage != D3D11_USAGE_DEFAULT) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }

        D3D11_TEXTURE2D_DESC copy_descriptor = source_descriptor;
        copy_descriptor.BindFlags = 0;
        copy_descriptor.CPUAccessFlags = 0;
        copy_descriptor.MiscFlags = 0;
        ComOwner<ID3D11Texture2D> copied_texture;
        const auto result = device->CreateTexture2D(
            &copy_descriptor,
            nullptr,
            copied_texture.Put());
        if (FAILED(result)) {
            return result == E_OUTOFMEMORY
                ? VRREC_STATUS_OUT_OF_MEMORY
                : VRREC_STATUS_BACKEND_UNAVAILABLE;
        }
        context->CopyResource(copied_texture.Get(), source_texture);
        context->Flush();
        if (aborted_.load()) {
            return VRREC_STATUS_INVALID_STATE;
        }

        auto status = VRREC_STATUS_INTERNAL_ERROR;
        surface = CreateWindowsD3d11OwnedVideoSurface(
            copied_texture.Get(),
            VideoSurfaceDescriptor {
                metadata.adapter_luid,
                metadata.width,
                metadata.height,
                metadata.pixel_format,
                generation_id,
            },
            status);
        if (status != VRREC_STATUS_OK || !surface) {
            surface.reset();
            return status == VRREC_STATUS_OK
                ? VRREC_STATUS_INTERNAL_ERROR
                : status;
        }
        return VRREC_STATUS_OK;
    }

    void Abort() noexcept override
    {
        if (!aborted_.exchange(true)) {
            wait_condition_.notify_all();
        }
    }

private:
    std::vector<std::string> SenderNames()
    {
        auto names = registry_.GetSenderList();
        std::sort(names.begin(), names.end());
        return names;
    }

    static bool AreNamesValid(
        const std::vector<std::string> &names) noexcept
    {
        if (names.size() > VRREC_SPOUT_MAX_SNAPSHOT_ENTRIES) {
            return false;
        }
        std::string_view previous;
        for (const auto &name : names) {
            if (name.empty() ||
                name.size() > VRREC_SPOUT_MAX_IDENTITY_UTF8_SIZE ||
                name.find('\0') != std::string::npos ||
                (!previous.empty() && previous == name)) {
                return false;
            }
            previous = name;
        }
        return true;
    }

    Spout2ReceiverResult Reconcile(
        const std::vector<std::string> &names) noexcept
    {
        for (auto receiver = receivers_.begin();
             receiver != receivers_.end();) {
            if (!std::binary_search(
                    names.begin(), names.end(), receiver->first)) {
                receiver = receivers_.erase(receiver);
            } else {
                ++receiver;
            }
        }
        for (const auto &name : names) {
            if (receivers_.contains(name)) {
                continue;
            }
            auto receiver = std::unique_ptr<spoutDX>(
                new (std::nothrow) spoutDX());
            if (!receiver) {
                return Spout2ReceiverResult::OutOfMemory;
            }
            auto history = histories_.find(name);
            if (history == histories_.end()) {
                try {
                    history = histories_.emplace(
                        name,
                        SenderHistory {}).first;
                } catch (const std::bad_alloc &) {
                    return Spout2ReceiverResult::OutOfMemory;
                } catch (...) {
                    return Spout2ReceiverResult::Failed;
                }
            }
            if (history->second.receiver_epoch ==
                std::numeric_limits<std::uint64_t>::max()) {
                return Spout2ReceiverResult::Failed;
            }
            ++history->second.receiver_epoch;
            receiver->SetReceiverName(name.c_str());
            receiver->SetAdapterAuto(true);
            try {
                receivers_.emplace(
                    name,
                    ReceiverState {
                        std::move(receiver),
                        history->second.receiver_epoch,
                    });
            } catch (const std::bad_alloc &) {
                return Spout2ReceiverResult::OutOfMemory;
            } catch (...) {
                return Spout2ReceiverResult::Failed;
            }
        }
        return Spout2ReceiverResult::Timeout;
    }

    Spout2ReceiverResult PollReceivers(
        Spout2TextureMetadata &metadata) noexcept
    {
        if (receivers_.empty()) {
            return Spout2ReceiverResult::Timeout;
        }
        const auto start = next_receiver_index_ % receivers_.size();
        auto receiver = receivers_.begin();
        std::advance(receiver, static_cast<std::ptrdiff_t>(start));
        for (std::size_t count = 0; count < receivers_.size(); ++count) {
            if (receiver == receivers_.end()) {
                receiver = receivers_.begin();
            }
            const auto result = PollReceiver(*receiver, metadata);
            if (result != Spout2ReceiverResult::Timeout) {
                next_receiver_index_ = (start + count + 1) % receivers_.size();
                return result;
            }
            ++receiver;
        }
        next_receiver_index_ = (start + 1) % receivers_.size();
        return Spout2ReceiverResult::Timeout;
    }

    Spout2ReceiverResult PollReceiver(
        std::pair<const std::string, ReceiverState> &entry,
        Spout2TextureMetadata &metadata) noexcept
    {
        auto &receiver = *entry.second.receiver;
        if (!receiver.ReceiveTexture() || !receiver.IsConnected() ||
            !receiver.IsFrameNew()) {
            return Spout2ReceiverResult::Timeout;
        }
        const auto *received_name = receiver.GetSenderName();
        auto *texture = receiver.GetSenderTexture();
        auto *device = receiver.GetDX11Device();
        const auto share_handle = receiver.GetSenderHandle();
        if (received_name == nullptr || entry.first != received_name ||
            texture == nullptr || device == nullptr || share_handle == nullptr) {
            return Spout2ReceiverResult::Failed;
        }

        D3D11_TEXTURE2D_DESC descriptor {};
        texture->GetDesc(&descriptor);
        vrrec_source_pixel_format_t pixel_format {};
        AdapterIdentity adapter {};
        const auto fps = receiver.GetSenderFps();
        if (descriptor.Width == 0 || descriptor.Height == 0 ||
            descriptor.MipLevels != 1 || descriptor.ArraySize != 1 ||
            descriptor.SampleDesc.Count != 1 ||
            descriptor.Usage != D3D11_USAGE_DEFAULT ||
            !TryMapFormat(descriptor.Format, pixel_format) ||
            !TryGetAdapterIdentity(device, adapter) ||
            !std::isfinite(fps) || fps <= 0.0 || fps > 1'000.0) {
            return Spout2ReceiverResult::Failed;
        }

        const auto history_entry = histories_.find(entry.first);
        if (history_entry == histories_.end()) {
            return Spout2ReceiverResult::Failed;
        }
        auto &history = history_entry->second;
        if (history.latest_sequence ==
            std::numeric_limits<std::uint64_t>::max()) {
            return Spout2ReceiverResult::Failed;
        }
        ++history.latest_sequence;
        const auto resource_identity = static_cast<std::uint64_t>(
            reinterpret_cast<std::uintptr_t>(share_handle));
        if (resource_identity == 0) {
            return Spout2ReceiverResult::Failed;
        }
        try {
            metadata = Spout2TextureMetadata {
                entry.first,
                resource_identity,
                entry.second.receiver_epoch,
                adapter.luid,
                std::move(adapter.name),
                adapter.vendor,
                descriptor.Width,
                descriptor.Height,
                pixel_format,
                fps,
                history.latest_sequence,
                MonotonicMicroseconds(),
            };
            pending_sender_ = entry.first;
        } catch (const std::bad_alloc &) {
            return Spout2ReceiverResult::OutOfMemory;
        } catch (...) {
            return Spout2ReceiverResult::Failed;
        }
        pending_resource_identity_ = resource_identity;
        pending_receiver_epoch_ = entry.second.receiver_epoch;
        return Spout2ReceiverResult::FrameReady;
    }

    spoutDX registry_;
    std::map<std::string, ReceiverState> receivers_;
    std::map<std::string, SenderHistory> histories_;
    std::size_t next_receiver_index_ = 0;
    std::string pending_sender_;
    std::uint64_t pending_resource_identity_ = 0;
    std::uint64_t pending_receiver_epoch_ = 0;
    std::atomic_bool aborted_ = false;
    std::mutex wait_mutex_;
    std::condition_variable wait_condition_;
};

}

std::unique_ptr<Spout2ReceiverPort> CreateWindowsSpout2ReceiverPort(
    vrrec_status_t &status) noexcept
{
    auto port = std::unique_ptr<Spout2ReceiverPort>(
        new (std::nothrow) WindowsSpout2ReceiverPort());
    if (!port) {
        status = VRREC_STATUS_OUT_OF_MEMORY;
        return nullptr;
    }
    status = VRREC_STATUS_OK;
    return port;
}

}
