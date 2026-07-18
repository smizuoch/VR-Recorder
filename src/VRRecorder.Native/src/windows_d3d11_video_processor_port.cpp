#include "windows_d3d11_video_processor_port.hpp"

#if !defined(_WIN32)
#error "The Windows D3D11 video processor Port requires Windows"
#endif

#include <d3d11.h>
#include <dxgi1_2.h>

#include <atomic>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <mutex>
#include <new>
#include <utility>
#include <vector>

#include "windows_d3d11_multithread_protection.hpp"

namespace vrrecorder::native {
namespace {

template <typename T>
class ComOwner final {
public:
    ComOwner() noexcept = default;

    explicit ComOwner(T *value) noexcept : value_(value)
    {
    }

    ~ComOwner()
    {
        Reset();
    }

    ComOwner(const ComOwner &) = delete;
    ComOwner &operator=(const ComOwner &) = delete;

    ComOwner(ComOwner &&other) noexcept : value_(other.value_)
    {
        other.value_ = nullptr;
    }

    ComOwner &operator=(ComOwner &&other) noexcept
    {
        if (this != &other) {
            Reset();
            value_ = other.value_;
            other.value_ = nullptr;
        }
        return *this;
    }

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

    explicit operator bool() const noexcept
    {
        return value_ != nullptr;
    }

private:
    T *value_ = nullptr;
};

std::uint64_t PackLuid(const LUID &luid) noexcept
{
    return static_cast<std::uint64_t>(luid.LowPart) |
           (static_cast<std::uint64_t>(
                static_cast<std::uint32_t>(luid.HighPart)) << 32U);
}

class OwnedD3d11VideoSurface final : public VideoSurface {
public:
    OwnedD3d11VideoSurface(
        ID3D11Texture2D *texture,
        VideoSurfaceDescriptor descriptor) noexcept
        : texture_(texture), descriptor_(descriptor)
    {
        texture_->AddRef();
    }

    ~OwnedD3d11VideoSurface()
    {
        texture_->Release();
    }

    VideoSurfaceDescriptor Descriptor() const noexcept override
    {
        return descriptor_;
    }

    void *NativeHandle() const noexcept override
    {
        return texture_;
    }

    VideoSurfaceAcquireResult AcquireForRead(
        std::chrono::milliseconds) noexcept override
    {
        return VideoSurfaceAcquireResult::Acquired;
    }

    vrrec_status_t ReleaseFromRead() noexcept override
    {
        return VRREC_STATUS_OK;
    }

private:
    ID3D11Texture2D *texture_;
    VideoSurfaceDescriptor descriptor_;
};

class WindowsD3d11VideoProcessorPort final
    : public D3d11VideoProcessorPort {
public:
    static constexpr std::size_t OutputTexturePoolCapacity = 16;

    WindowsD3d11VideoProcessorPort(
        ID3D11Device *device,
        std::uint64_t adapter_luid) noexcept
        : device_(device), adapter_luid_(adapter_luid)
    {
        device_.Get()->AddRef();
    }

    vrrec_status_t Initialize() noexcept
    {
        device_.Get()->GetImmediateContext(context_.Put());
        if (!context_) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        const auto multithread_status =
            EnableWindowsD3d11MultithreadProtection(context_.Get());
        if (multithread_status != VRREC_STATUS_OK) {
            return multithread_status;
        }

        auto result = device_.Get()->QueryInterface(
            __uuidof(ID3D11VideoDevice),
            reinterpret_cast<void **>(video_device_.Put()));
        if (FAILED(result)) {
            return MapStatus(result);
        }
        result = context_.Get()->QueryInterface(
            __uuidof(ID3D11VideoContext),
            reinterpret_cast<void **>(video_context_.Put()));
        if (FAILED(result)) {
            return MapStatus(result);
        }

        ComOwner<IDXGIDevice> dxgi_device;
        result = device_.Get()->QueryInterface(
            __uuidof(IDXGIDevice),
            reinterpret_cast<void **>(dxgi_device.Put()));
        if (FAILED(result)) {
            return MapStatus(result);
        }
        ComOwner<IDXGIAdapter> adapter;
        result = dxgi_device.Get()->GetAdapter(adapter.Put());
        if (FAILED(result)) {
            return MapStatus(result);
        }
        DXGI_ADAPTER_DESC descriptor {};
        result = adapter.Get()->GetDesc(&descriptor);
        if (FAILED(result)) {
            return MapStatus(result);
        }
        if (PackLuid(descriptor.AdapterLuid) != adapter_luid_) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }
        return VRREC_STATUS_OK;
    }

    D3d11VideoProcessorResult Convert(
        const std::shared_ptr<VideoSurface> &source,
        const VideoProcessingPlan &plan,
        std::shared_ptr<VideoSurface> &output) noexcept override
    {
        output.reset();
        if (aborted_.load()) {
            return D3d11VideoProcessorResult::Aborted;
        }
        if (!source || source->NativeHandle() == nullptr) {
            return D3d11VideoProcessorResult::Failed;
        }

        auto *source_texture =
            static_cast<ID3D11Texture2D *>(source->NativeHandle());
        ComOwner<ID3D11Device> source_device;
        source_texture->GetDevice(source_device.Put());
        if (!IsSameComObject(device_.Get(), source_device.Get())) {
            return D3d11VideoProcessorResult::Failed;
        }

        D3D11_TEXTURE2D_DESC source_descriptor {};
        source_texture->GetDesc(&source_descriptor);
        const auto expected_format = InputFormat(plan.input_pixel_format);
        if (expected_format == DXGI_FORMAT_UNKNOWN ||
            source_descriptor.Width != plan.source_width ||
            source_descriptor.Height != plan.source_height ||
            source_descriptor.Format != expected_format ||
            source_descriptor.MipLevels != 1 ||
            source_descriptor.ArraySize != 1 ||
            source_descriptor.SampleDesc.Count != 1 ||
            source_descriptor.Usage != D3D11_USAGE_DEFAULT) {
            return D3d11VideoProcessorResult::Failed;
        }

        ComOwner<ID3D11Texture2D> padded_texture;
        auto *processor_input = source_texture;
        auto input_descriptor = source_descriptor;
        if (plan.pad_right != 0 || plan.pad_bottom != 0) {
            const auto padding = CreatePaddedInput(
                source_texture,
                source_descriptor,
                plan,
                padded_texture);
            if (padding != D3d11VideoProcessorResult::Converted) {
                return padding;
            }
            processor_input = padded_texture.Get();
            processor_input->GetDesc(&input_descriptor);
        }

        D3D11_VIDEO_PROCESSOR_CONTENT_DESC content {};
        content.InputFrameFormat =
            D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE;
        content.InputFrameRate = {60, 1};
        content.InputWidth = input_descriptor.Width;
        content.InputHeight = input_descriptor.Height;
        content.OutputFrameRate = {60, 1};
        content.OutputWidth = plan.output_width;
        content.OutputHeight = plan.output_height;
        content.Usage = D3D11_VIDEO_USAGE_PLAYBACK_NORMAL;

        ComOwner<ID3D11VideoProcessorEnumerator> enumerator;
        auto result = video_device_.Get()->CreateVideoProcessorEnumerator(
            &content,
            enumerator.Put());
        if (FAILED(result)) {
            return Classify(result);
        }

        UINT input_support = 0;
        result = enumerator.Get()->CheckVideoProcessorFormat(
            input_descriptor.Format,
            &input_support);
        if (FAILED(result)) {
            return Classify(result);
        }
        UINT output_support = 0;
        result = enumerator.Get()->CheckVideoProcessorFormat(
            DXGI_FORMAT_NV12,
            &output_support);
        if (FAILED(result) ||
            (input_support &
             D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_INPUT) == 0 ||
            (output_support &
             D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_OUTPUT) == 0) {
            return FAILED(result)
                ? Classify(result)
                : D3d11VideoProcessorResult::Failed;
        }

        ComOwner<ID3D11VideoProcessor> processor;
        result = video_device_.Get()->CreateVideoProcessor(
            enumerator.Get(),
            0,
            processor.Put());
        if (FAILED(result)) {
            return Classify(result);
        }

        D3D11_TEXTURE2D_DESC output_descriptor {};
        output_descriptor.Width = plan.output_width;
        output_descriptor.Height = plan.output_height;
        output_descriptor.MipLevels = 1;
        output_descriptor.ArraySize = 1;
        output_descriptor.Format = DXGI_FORMAT_NV12;
        output_descriptor.SampleDesc.Count = 1;
        output_descriptor.Usage = D3D11_USAGE_DEFAULT;
        output_descriptor.BindFlags = D3D11_BIND_RENDER_TARGET;
        ID3D11Texture2D *output_texture = nullptr;
        if (output_pool_width_ != plan.output_width ||
            output_pool_height_ != plan.output_height) {
            output_texture_pool_.clear();
            output_pool_index_ = 0;
            output_pool_width_ = plan.output_width;
            output_pool_height_ = plan.output_height;
        }
        if (output_texture_pool_.size() < OutputTexturePoolCapacity) {
            ComOwner<ID3D11Texture2D> created_texture;
            result = device_.Get()->CreateTexture2D(
                &output_descriptor,
                nullptr,
                created_texture.Put());
            if (FAILED(result)) {
                return Classify(result);
            }
            output_texture_pool_.push_back(std::move(created_texture));
            output_texture = output_texture_pool_.back().Get();
        } else {
            output_texture =
                output_texture_pool_[output_pool_index_].Get();
            output_pool_index_ =
                (output_pool_index_ + 1U) % OutputTexturePoolCapacity;
        }

        D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC input_view_descriptor {};
        input_view_descriptor.FourCC = 0;
        input_view_descriptor.ViewDimension =
            D3D11_VPIV_DIMENSION_TEXTURE2D;
        input_view_descriptor.Texture2D.MipSlice = 0;
        input_view_descriptor.Texture2D.ArraySlice = 0;
        ComOwner<ID3D11VideoProcessorInputView> input_view;
        result = video_device_.Get()->CreateVideoProcessorInputView(
            processor_input,
            enumerator.Get(),
            &input_view_descriptor,
            input_view.Put());
        if (FAILED(result)) {
            return Classify(result);
        }

        D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC output_view_descriptor {};
        output_view_descriptor.ViewDimension =
            D3D11_VPOV_DIMENSION_TEXTURE2D;
        output_view_descriptor.Texture2D.MipSlice = 0;
        ComOwner<ID3D11VideoProcessorOutputView> output_view;
        result = video_device_.Get()->CreateVideoProcessorOutputView(
            output_texture,
            enumerator.Get(),
            &output_view_descriptor,
            output_view.Put());
        if (FAILED(result)) {
            return Classify(result);
        }

        const RECT output_rect {
            0,
            0,
            static_cast<LONG>(plan.output_width),
            static_cast<LONG>(plan.output_height),
        };
        const RECT source_rect {
            0,
            0,
            static_cast<LONG>(input_descriptor.Width),
            static_cast<LONG>(input_descriptor.Height),
        };
        const RECT destination_rect {
            static_cast<LONG>(plan.offset_x),
            static_cast<LONG>(plan.offset_y),
            static_cast<LONG>(plan.offset_x + plan.destination_width),
            static_cast<LONG>(plan.offset_y + plan.destination_height),
        };
        D3D11_VIDEO_COLOR black {};
        black.RGBA.A = 1.0F;
        video_context_.Get()->VideoProcessorSetOutputTargetRect(
            processor.Get(), TRUE, &output_rect);
        video_context_.Get()->VideoProcessorSetOutputBackgroundColor(
            processor.Get(), FALSE, &black);
        video_context_.Get()->VideoProcessorSetStreamFrameFormat(
            processor.Get(),
            0,
            D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE);
        video_context_.Get()->VideoProcessorSetStreamSourceRect(
            processor.Get(), 0, TRUE, &source_rect);
        video_context_.Get()->VideoProcessorSetStreamDestRect(
            processor.Get(), 0, TRUE, &destination_rect);
        video_context_.Get()->VideoProcessorSetStreamAutoProcessingMode(
            processor.Get(), 0, FALSE);

        D3D11_VIDEO_PROCESSOR_STREAM stream {};
        stream.Enable = TRUE;
        stream.pInputSurface = input_view.Get();
        result = video_context_.Get()->VideoProcessorBlt(
            processor.Get(),
            output_view.Get(),
            0,
            1,
            &stream);
        if (FAILED(result)) {
            return Classify(result);
        }
        if (aborted_.load()) {
            return D3d11VideoProcessorResult::Aborted;
        }

        try {
            output = std::make_shared<OwnedD3d11VideoSurface>(
                output_texture,
                VideoSurfaceDescriptor {
                    adapter_luid_,
                    plan.output_width,
                    plan.output_height,
                    VRREC_SOURCE_PIXEL_FORMAT_NV12,
                    plan.source_generation_id,
                });
        } catch (const std::bad_alloc &) {
            return D3d11VideoProcessorResult::OutOfMemory;
        } catch (...) {
            return D3d11VideoProcessorResult::Failed;
        }
        return D3d11VideoProcessorResult::Converted;
    }

    void Abort() noexcept override
    {
        aborted_.store(true);
    }

private:
    static DXGI_FORMAT InputFormat(
        vrrec_source_pixel_format_t format) noexcept
    {
        switch (format) {
        case VRREC_SOURCE_PIXEL_FORMAT_BGRA8:
            return DXGI_FORMAT_B8G8R8A8_UNORM;
        case VRREC_SOURCE_PIXEL_FORMAT_RGBA8:
            return DXGI_FORMAT_R8G8B8A8_UNORM;
        case VRREC_SOURCE_PIXEL_FORMAT_NV12:
            return DXGI_FORMAT_NV12;
        default:
            return DXGI_FORMAT_UNKNOWN;
        }
    }

    static bool IsSameComObject(IUnknown *left, IUnknown *right) noexcept
    {
        if (left == nullptr || right == nullptr) {
            return false;
        }
        ComOwner<IUnknown> left_identity;
        ComOwner<IUnknown> right_identity;
        if (FAILED(left->QueryInterface(
                __uuidof(IUnknown),
                reinterpret_cast<void **>(left_identity.Put()))) ||
            FAILED(right->QueryInterface(
                __uuidof(IUnknown),
                reinterpret_cast<void **>(right_identity.Put())))) {
            return false;
        }
        return left_identity.Get() == right_identity.Get();
    }

    D3d11VideoProcessorResult CreatePaddedInput(
        ID3D11Texture2D *source,
        const D3D11_TEXTURE2D_DESC &source_descriptor,
        const VideoProcessingPlan &plan,
        ComOwner<ID3D11Texture2D> &padded) noexcept
    {
        if (source_descriptor.Format != DXGI_FORMAT_B8G8R8A8_UNORM &&
            source_descriptor.Format != DXGI_FORMAT_R8G8B8A8_UNORM) {
            return D3d11VideoProcessorResult::Failed;
        }

        auto padded_descriptor = source_descriptor;
        padded_descriptor.Width = plan.normalized_source_width;
        padded_descriptor.Height = plan.normalized_source_height;
        padded_descriptor.BindFlags |= D3D11_BIND_RENDER_TARGET;
        auto result = device_.Get()->CreateTexture2D(
            &padded_descriptor,
            nullptr,
            padded.Put());
        if (FAILED(result)) {
            return Classify(result);
        }

        ComOwner<ID3D11RenderTargetView> target;
        result = device_.Get()->CreateRenderTargetView(
            padded.Get(),
            nullptr,
            target.Put());
        if (FAILED(result)) {
            return Classify(result);
        }
        constexpr float black[] = {0.0F, 0.0F, 0.0F, 1.0F};
        context_.Get()->ClearRenderTargetView(target.Get(), black);
        const D3D11_BOX source_box {
            0,
            0,
            0,
            source_descriptor.Width,
            source_descriptor.Height,
            1,
        };
        context_.Get()->CopySubresourceRegion(
            padded.Get(),
            0,
            0,
            0,
            0,
            source,
            0,
            &source_box);
        return D3d11VideoProcessorResult::Converted;
    }

    D3d11VideoProcessorResult Classify(HRESULT result) const noexcept
    {
        if (result == E_OUTOFMEMORY) {
            return D3d11VideoProcessorResult::OutOfMemory;
        }
        if (result == DXGI_ERROR_DEVICE_RESET) {
            return D3d11VideoProcessorResult::DeviceReset;
        }
        if (result == DXGI_ERROR_DEVICE_REMOVED ||
            result == DXGI_ERROR_DEVICE_HUNG ||
            result == DXGI_ERROR_DRIVER_INTERNAL_ERROR) {
            return D3d11VideoProcessorResult::DeviceRemoved;
        }

        const auto removed = device_.Get()->GetDeviceRemovedReason();
        if (removed == DXGI_ERROR_DEVICE_RESET) {
            return D3d11VideoProcessorResult::DeviceReset;
        }
        if (removed == DXGI_ERROR_DEVICE_REMOVED ||
            removed == DXGI_ERROR_DEVICE_HUNG ||
            removed == DXGI_ERROR_DRIVER_INTERNAL_ERROR) {
            return D3d11VideoProcessorResult::DeviceRemoved;
        }
        return D3d11VideoProcessorResult::Failed;
    }

    vrrec_status_t MapStatus(HRESULT result) const noexcept
    {
        if (result == E_NOINTERFACE) {
            return VRREC_STATUS_BACKEND_UNAVAILABLE;
        }
        switch (Classify(result)) {
        case D3d11VideoProcessorResult::DeviceRemoved:
        case D3d11VideoProcessorResult::DeviceReset:
            return VRREC_STATUS_BACKEND_UNAVAILABLE;
        case D3d11VideoProcessorResult::OutOfMemory:
            return VRREC_STATUS_OUT_OF_MEMORY;
        case D3d11VideoProcessorResult::None:
        case D3d11VideoProcessorResult::Converted:
        case D3d11VideoProcessorResult::Failed:
        case D3d11VideoProcessorResult::Aborted:
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        return VRREC_STATUS_INTERNAL_ERROR;
    }

    ComOwner<ID3D11Device> device_;
    ComOwner<ID3D11DeviceContext> context_;
    ComOwner<ID3D11VideoDevice> video_device_;
    ComOwner<ID3D11VideoContext> video_context_;
    std::vector<ComOwner<ID3D11Texture2D>> output_texture_pool_;
    std::size_t output_pool_index_ = 0;
    std::uint32_t output_pool_width_ = 0;
    std::uint32_t output_pool_height_ = 0;
    std::uint64_t adapter_luid_;
    std::atomic_bool aborted_ = false;
};

bool AreSameComObjects(IUnknown *left, IUnknown *right) noexcept
{
    if (left == nullptr || right == nullptr) {
        return false;
    }
    ComOwner<IUnknown> left_identity;
    ComOwner<IUnknown> right_identity;
    if (FAILED(left->QueryInterface(
            __uuidof(IUnknown),
            reinterpret_cast<void **>(left_identity.Put()))) ||
        FAILED(right->QueryInterface(
            __uuidof(IUnknown),
            reinterpret_cast<void **>(right_identity.Put())))) {
        return false;
    }
    return left_identity.Get() == right_identity.Get();
}

D3d11VideoProcessorResult CreationFailure(vrrec_status_t status) noexcept
{
    return status == VRREC_STATUS_OUT_OF_MEMORY
        ? D3d11VideoProcessorResult::OutOfMemory
        : D3d11VideoProcessorResult::Failed;
}

class WindowsAdaptiveD3d11VideoProcessorPort final
    : public D3d11VideoProcessorPort {
public:
    explicit WindowsAdaptiveD3d11VideoProcessorPort(
        std::uint64_t adapter_luid) noexcept
        : adapter_luid_(adapter_luid)
    {
    }

    D3d11VideoProcessorResult Convert(
        const std::shared_ptr<VideoSurface> &source,
        const VideoProcessingPlan &plan,
        std::shared_ptr<VideoSurface> &output) noexcept override
    {
        output.reset();
        const std::lock_guard convert_lock(convert_mutex_);
        if (aborted_.load(std::memory_order_acquire)) {
            return D3d11VideoProcessorResult::Aborted;
        }
        if (!source || source->NativeHandle() == nullptr ||
            source->Descriptor().adapter_luid != adapter_luid_) {
            return D3d11VideoProcessorResult::Failed;
        }

        auto *texture = static_cast<ID3D11Texture2D *>(
            source->NativeHandle());
        ComOwner<ID3D11Device> source_device;
        texture->GetDevice(source_device.Put());
        if (!source_device) {
            return D3d11VideoProcessorResult::Failed;
        }

        std::shared_ptr<D3d11VideoProcessorPort> active;
        {
            const std::lock_guard state_lock(state_mutex_);
            if (aborted_.load(std::memory_order_acquire)) {
                return D3d11VideoProcessorResult::Aborted;
            }
            if (!port_ || !AreSameComObjects(
                    bound_device_.Get(),
                    source_device.Get())) {
                auto status = VRREC_STATUS_INTERNAL_ERROR;
                auto created = CreateWindowsD3d11VideoProcessorPort(
                    source_device.Get(),
                    adapter_luid_,
                    status);
                if (!created) {
                    return CreationFailure(status);
                }

                std::shared_ptr<D3d11VideoProcessorPort> shared;
                try {
                    shared = std::move(created);
                } catch (const std::bad_alloc &) {
                    return D3d11VideoProcessorResult::OutOfMemory;
                } catch (...) {
                    return D3d11VideoProcessorResult::Failed;
                }
                if (aborted_.load(std::memory_order_acquire)) {
                    shared->Abort();
                    return D3d11VideoProcessorResult::Aborted;
                }
                bound_device_ = std::move(source_device);
                port_ = std::move(shared);
            }
            active = port_;
        }

        auto result = active->Convert(source, plan, output);
        if (aborted_.load(std::memory_order_acquire)) {
            active->Abort();
            output.reset();
            return D3d11VideoProcessorResult::Aborted;
        }
        return result;
    }

    void Abort() noexcept override
    {
        if (aborted_.exchange(true, std::memory_order_acq_rel)) {
            return;
        }

        std::shared_ptr<D3d11VideoProcessorPort> active;
        {
            const std::lock_guard lock(state_mutex_);
            active = port_;
        }
        if (active) {
            active->Abort();
        }
    }

private:
    const std::uint64_t adapter_luid_;
    std::mutex convert_mutex_;
    std::mutex state_mutex_;
    ComOwner<ID3D11Device> bound_device_;
    std::shared_ptr<D3d11VideoProcessorPort> port_;
    std::atomic_bool aborted_ = false;
};

}

std::unique_ptr<D3d11VideoProcessorPort>
CreateWindowsD3d11VideoProcessorPort(
    void *d3d11_device,
    std::uint64_t adapter_luid,
    vrrec_status_t &status) noexcept
{
    status = VRREC_STATUS_INVALID_ARGUMENT;
    if (d3d11_device == nullptr || adapter_luid == 0) {
        return {};
    }

    auto port = std::unique_ptr<WindowsD3d11VideoProcessorPort>(
        new (std::nothrow) WindowsD3d11VideoProcessorPort(
            static_cast<ID3D11Device *>(d3d11_device),
            adapter_luid));
    if (!port) {
        status = VRREC_STATUS_OUT_OF_MEMORY;
        return {};
    }

    status = port->Initialize();
    if (status != VRREC_STATUS_OK) {
        return {};
    }
    return port;
}

std::unique_ptr<D3d11VideoProcessorPort>
CreateWindowsAdaptiveD3d11VideoProcessorPort(
    std::uint64_t adapter_luid,
    vrrec_status_t &status) noexcept
{
    status = VRREC_STATUS_INVALID_ARGUMENT;
    if (adapter_luid == 0) {
        return {};
    }

    auto *created = new (std::nothrow)
        WindowsAdaptiveD3d11VideoProcessorPort(adapter_luid);
    if (created == nullptr) {
        status = VRREC_STATUS_OUT_OF_MEMORY;
        return {};
    }
    status = VRREC_STATUS_OK;
    return std::unique_ptr<D3d11VideoProcessorPort>(created);
}

}
