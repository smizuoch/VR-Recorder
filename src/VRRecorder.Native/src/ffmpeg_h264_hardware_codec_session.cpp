#include "ffmpeg_h264_hardware_codec_session.hpp"

#if !defined(_WIN32)
#error "The D3D11 hardware encoder session requires Windows"
#endif

#if !defined(NOMINMAX)
#define NOMINMAX
#endif
#include <Windows.h>
#include <d3d11.h>

#include <cerrno>
#include <cstdint>
#include <cstring>
#include <memory>
#include <new>
#include <utility>

#include "ffmpeg_libavcodec_encoder_port.hpp"
#include "video_surface.hpp"

extern "C" {
#include <libavcodec/avcodec.h>
#include <libavutil/buffer.h>
#include <libavutil/dict.h>
#include <libavutil/error.h>
#include <libavutil/frame.h>
#include <libavutil/hwcontext.h>
#include <libavutil/hwcontext_d3d11va.h>
#include <libavutil/pixfmt.h>
}

namespace vrrecorder::native {
namespace {

vrrec_status_t ErrorStatus(int error) noexcept
{
    return error == AVERROR(ENOMEM)
        ? VRREC_STATUS_OUT_OF_MEMORY
        : VRREC_STATUS_BACKEND_UNAVAILABLE;
}

bool AreSameComObjects(IUnknown *left, IUnknown *right) noexcept
{
    if (left == nullptr || right == nullptr) {
        return false;
    }
    IUnknown *left_identity = nullptr;
    IUnknown *right_identity = nullptr;
    const auto left_result = left->QueryInterface(
        __uuidof(IUnknown),
        reinterpret_cast<void **>(&left_identity));
    const auto right_result = right->QueryInterface(
        __uuidof(IUnknown),
        reinterpret_cast<void **>(&right_identity));
    const auto same = SUCCEEDED(left_result) && SUCCEEDED(right_result) &&
        left_identity == right_identity;
    if (left_identity != nullptr) {
        left_identity->Release();
    }
    if (right_identity != nullptr) {
        right_identity->Release();
    }
    return same;
}

void ReleaseTextureDescriptor(void *opaque, std::uint8_t *data) noexcept
{
    auto *texture = static_cast<ID3D11Texture2D *>(opaque);
    if (texture != nullptr) {
        texture->Release();
    }
    av_free(data);
}

AVBufferRef *WrapTexture(ID3D11Texture2D *texture) noexcept
{
    if (texture == nullptr) {
        return nullptr;
    }
    auto *descriptor = static_cast<AVD3D11FrameDescriptor *>(
        av_mallocz(sizeof(AVD3D11FrameDescriptor)));
    if (descriptor == nullptr) {
        return nullptr;
    }
    texture->AddRef();
    descriptor->texture = texture;
    descriptor->index = 0;
    auto *buffer = av_buffer_create(
        reinterpret_cast<std::uint8_t *>(descriptor),
        sizeof(AVD3D11FrameDescriptor),
        ReleaseTextureDescriptor,
        texture,
        0);
    if (buffer == nullptr) {
        texture->Release();
        av_free(descriptor);
    }
    return buffer;
}

vrrec_status_t SetOption(
    AVDictionary *&options,
    const char *name,
    const char *value) noexcept
{
    const auto result = av_dict_set(&options, name, value, 0);
    return result < 0 ? ErrorStatus(result) : VRREC_STATUS_OK;
}

vrrec_status_t ConfigureHardwareOptions(
    const H264VideoEncoderConfig &config,
    const ProductionVideoEncoderRoute &route,
    AVDictionary *&options) noexcept
{
    auto status = SetOption(
        options,
        "profile",
        config.profile == H264Profile::High ? "high" : "main");
    if (status != VRREC_STATUS_OK) {
        return status;
    }
    switch (route.requested_kind) {
    case VRREC_ENCODER_NVENC:
        status = SetOption(options, "preset", "p4");
        if (status == VRREC_STATUS_OK) {
            status = SetOption(options, "tune", "hq");
        }
        if (status == VRREC_STATUS_OK) {
            status = SetOption(options, "rc", "vbr");
        }
        if (status == VRREC_STATUS_OK) {
            status = SetOption(options, "zerolatency", "1");
        }
        if (status == VRREC_STATUS_OK) {
            status = SetOption(options, "forced-idr", "1");
        }
        return status;
    case VRREC_ENCODER_AMF:
        status = SetOption(options, "quality", "quality");
        if (status == VRREC_STATUS_OK) {
            status = SetOption(options, "rc", "qvbr");
        }
        return status;
    case VRREC_ENCODER_QSV:
        return SetOption(options, "preset", "medium");
    default:
        return VRREC_STATUS_INVALID_ARGUMENT;
    }
}

bool IsRouteValid(const ProductionVideoEncoderRoute &route) noexcept
{
    if (!route.hardware_accelerated || route.codec_name == nullptr ||
        route.source_adapter_luid == 0 ||
        route.encoder_adapter_luid != route.source_adapter_luid) {
        return false;
    }
    switch (route.requested_kind) {
    case VRREC_ENCODER_NVENC:
        return std::strcmp(route.codec_name, "h264_nvenc") == 0 &&
            route.input == ProductionVideoEncoderInput::D3d11Nv12;
    case VRREC_ENCODER_AMF:
        return std::strcmp(route.codec_name, "h264_amf") == 0 &&
            route.input == ProductionVideoEncoderInput::D3d11Nv12;
    case VRREC_ENCODER_QSV:
        return std::strcmp(route.codec_name, "h264_qsv") == 0 &&
            route.input == ProductionVideoEncoderInput::QsvDerivedD3d11Nv12;
    default:
        return false;
    }
}

class HardwareCodecSession final : public FfmpegH264CodecSession {
public:
    HardwareCodecSession(
        H264VideoEncoderConfig config,
        ProductionVideoEncoderRoute route,
        std::unique_ptr<LibavcodecEncoderPort> port,
        AVBufferRef *source_frames,
        AVBufferRef *encoder_frames,
        bool map_to_qsv) noexcept
        : config_(config),
          route_(route),
          port_(std::move(port)),
          state_machine_(*port_, MediaStreamKind::Video),
          source_frames_(source_frames),
          encoder_frames_(encoder_frames),
          source_frame_(av_frame_alloc()),
          mapped_frame_(map_to_qsv ? av_frame_alloc() : nullptr),
          map_to_qsv_(map_to_qsv)
    {
    }

    ~HardwareCodecSession() override
    {
        Abort();
        av_frame_free(&mapped_frame_);
        av_frame_free(&source_frame_);
        av_buffer_unref(&encoder_frames_);
        av_buffer_unref(&source_frames_);
    }

    bool HasResources() const noexcept
    {
        return port_ != nullptr && source_frames_ != nullptr &&
            encoder_frames_ != nullptr && source_frame_ != nullptr &&
            (!map_to_qsv_ || mapped_frame_ != nullptr);
    }

    vrrec_status_t PrepareFrame(const AVFrame &frame) noexcept override
    {
        return port_->PrepareFrame(frame);
    }

    vrrec_status_t PrepareD3d11Frame(
        const std::shared_ptr<VideoSurface> &surface,
        std::int64_t pts) noexcept override
    {
        if (aborted_ || !surface || surface->NativeHandle() == nullptr ||
            pts < 0) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }
        const auto surface_descriptor = surface->Descriptor();
        if (surface_descriptor.adapter_luid != route_.source_adapter_luid ||
            surface_descriptor.width != config_.width ||
            surface_descriptor.height != config_.height ||
            surface_descriptor.pixel_format !=
                VRREC_SOURCE_PIXEL_FORMAT_NV12) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }

        auto *texture = static_cast<ID3D11Texture2D *>(
            surface->NativeHandle());
        ID3D11Device *texture_device = nullptr;
        texture->GetDevice(&texture_device);
        auto *source_frames_context = reinterpret_cast<AVHWFramesContext *>(
            source_frames_->data);
        auto *device_context = source_frames_context == nullptr
            ? nullptr
            : reinterpret_cast<AVD3D11VADeviceContext *>(
                  source_frames_context->device_ctx->hwctx);
        const auto same_device = texture_device != nullptr &&
            device_context != nullptr &&
            AreSameComObjects(texture_device, device_context->device);
        if (texture_device != nullptr) {
            texture_device->Release();
        }
        if (!same_device) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }

        D3D11_TEXTURE2D_DESC texture_descriptor {};
        texture->GetDesc(&texture_descriptor);
        if (texture_descriptor.Width != config_.width ||
            texture_descriptor.Height != config_.height ||
            texture_descriptor.MipLevels != 1 ||
            texture_descriptor.ArraySize != 1 ||
            texture_descriptor.Format != DXGI_FORMAT_NV12 ||
            texture_descriptor.SampleDesc.Count != 1 ||
            texture_descriptor.Usage != D3D11_USAGE_DEFAULT ||
            (texture_descriptor.BindFlags & D3D11_BIND_RENDER_TARGET) == 0) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }

        av_frame_unref(source_frame_);
        source_frame_->buf[0] = WrapTexture(texture);
        source_frame_->hw_frames_ctx = av_buffer_ref(source_frames_);
        if (source_frame_->buf[0] == nullptr ||
            source_frame_->hw_frames_ctx == nullptr) {
            av_frame_unref(source_frame_);
            return VRREC_STATUS_OUT_OF_MEMORY;
        }
        source_frame_->data[0] = reinterpret_cast<std::uint8_t *>(texture);
        source_frame_->data[1] = nullptr;
        source_frame_->format = AV_PIX_FMT_D3D11;
        source_frame_->width = static_cast<int>(config_.width);
        source_frame_->height = static_cast<int>(config_.height);
        source_frame_->pts = pts;
        source_frame_->duration = 1;

        const AVFrame *prepared = source_frame_;
        if (map_to_qsv_) {
            av_frame_unref(mapped_frame_);
            mapped_frame_->format = AV_PIX_FMT_QSV;
            mapped_frame_->hw_frames_ctx = av_buffer_ref(encoder_frames_);
            if (mapped_frame_->hw_frames_ctx == nullptr) {
                av_frame_unref(mapped_frame_);
                return VRREC_STATUS_OUT_OF_MEMORY;
            }
            const auto mapped = av_hwframe_map(
                mapped_frame_,
                source_frame_,
                AV_HWFRAME_MAP_READ);
            if (mapped < 0) {
                av_frame_unref(mapped_frame_);
                return ErrorStatus(mapped);
            }
            mapped_frame_->pts = pts;
            mapped_frame_->duration = 1;
            prepared = mapped_frame_;
        }
        return port_->PrepareFrame(*prepared);
    }

    FfmpegEncodeBatch EncodePreparedFrame() noexcept override
    {
        return state_machine_.EncodePreparedFrame();
    }

    FfmpegEncodeBatch Finish() noexcept override
    {
        return state_machine_.Finish();
    }

    vrrec_status_t CopyCodecExtradata(
        std::vector<std::byte> &extradata) const noexcept override
    {
        return port_->CopyCodecExtradata(extradata);
    }

    void Abort() noexcept override
    {
        if (aborted_) {
            return;
        }
        aborted_ = true;
        state_machine_.Abort();
        if (source_frame_ != nullptr) {
            av_frame_unref(source_frame_);
        }
        if (mapped_frame_ != nullptr) {
            av_frame_unref(mapped_frame_);
        }
    }

private:
    H264VideoEncoderConfig config_;
    ProductionVideoEncoderRoute route_;
    std::unique_ptr<LibavcodecEncoderPort> port_;
    FfmpegEncoderStateMachine state_machine_;
    AVBufferRef *source_frames_ = nullptr;
    AVBufferRef *encoder_frames_ = nullptr;
    AVFrame *source_frame_ = nullptr;
    AVFrame *mapped_frame_ = nullptr;
    bool map_to_qsv_ = false;
    bool aborted_ = false;
};

}

FfmpegH264HardwareCodecSessionCreateResult
CreateFfmpegH264HardwareCodecSession(
    const H264VideoEncoderConfig &config,
    const ProductionVideoEncoderRoute &route,
    void *d3d11_device) noexcept
{
    if (!IsH264VideoEncoderConfigValid(config) || !IsRouteValid(route) ||
        d3d11_device == nullptr) {
        return {VRREC_STATUS_INVALID_ARGUMENT, nullptr, nullptr};
    }

    auto *device = static_cast<ID3D11Device *>(d3d11_device);
    AVBufferRef *d3d11_device_context = av_hwdevice_ctx_alloc(
        AV_HWDEVICE_TYPE_D3D11VA);
    if (d3d11_device_context == nullptr) {
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr, nullptr};
    }
    auto *hardware_device = reinterpret_cast<AVHWDeviceContext *>(
        d3d11_device_context->data);
    auto *hardware_d3d11 = reinterpret_cast<AVD3D11VADeviceContext *>(
        hardware_device->hwctx);
    device->AddRef();
    hardware_d3d11->device = device;
    auto result = av_hwdevice_ctx_init(d3d11_device_context);
    if (result < 0) {
        av_buffer_unref(&d3d11_device_context);
        return {ErrorStatus(result), nullptr, nullptr};
    }

    AVBufferRef *source_frames = av_hwframe_ctx_alloc(
        d3d11_device_context);
    if (source_frames == nullptr) {
        av_buffer_unref(&d3d11_device_context);
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr, nullptr};
    }
    auto *source_frames_context = reinterpret_cast<AVHWFramesContext *>(
        source_frames->data);
    source_frames_context->format = AV_PIX_FMT_D3D11;
    source_frames_context->sw_format = AV_PIX_FMT_NV12;
    source_frames_context->width = static_cast<int>(config.width);
    source_frames_context->height = static_cast<int>(config.height);
    source_frames_context->initial_pool_size = 0;
    auto *source_d3d11 = reinterpret_cast<AVD3D11VAFramesContext *>(
        source_frames_context->hwctx);
    source_d3d11->BindFlags = D3D11_BIND_RENDER_TARGET;
    result = av_hwframe_ctx_init(source_frames);
    if (result < 0) {
        av_buffer_unref(&source_frames);
        av_buffer_unref(&d3d11_device_context);
        return {ErrorStatus(result), nullptr, nullptr};
    }

    const auto map_to_qsv = route.requested_kind == VRREC_ENCODER_QSV;
    AVBufferRef *encoder_frames = nullptr;
    AVBufferRef *encoder_device = nullptr;
    if (map_to_qsv) {
        result = av_hwdevice_ctx_create_derived(
            &encoder_device,
            AV_HWDEVICE_TYPE_QSV,
            d3d11_device_context,
            0);
        if (result >= 0) {
            result = av_hwframe_ctx_create_derived(
                &encoder_frames,
                AV_PIX_FMT_QSV,
                encoder_device,
                source_frames,
                AV_HWFRAME_MAP_READ);
        }
        av_buffer_unref(&encoder_device);
        if (result < 0 || encoder_frames == nullptr) {
            av_buffer_unref(&encoder_frames);
            av_buffer_unref(&source_frames);
            av_buffer_unref(&d3d11_device_context);
            return {ErrorStatus(result), nullptr, nullptr};
        }
    } else {
        encoder_frames = av_buffer_ref(source_frames);
        if (encoder_frames == nullptr) {
            av_buffer_unref(&source_frames);
            av_buffer_unref(&d3d11_device_context);
            return {VRREC_STATUS_OUT_OF_MEMORY, nullptr, nullptr};
        }
    }
    av_buffer_unref(&d3d11_device_context);

    const AVCodec *codec = avcodec_find_encoder_by_name(route.codec_name);
    if (codec == nullptr) {
        av_buffer_unref(&encoder_frames);
        av_buffer_unref(&source_frames);
        return {VRREC_STATUS_BACKEND_UNAVAILABLE, nullptr, nullptr};
    }
    AVCodecContext *context = avcodec_alloc_context3(codec);
    if (context == nullptr) {
        av_buffer_unref(&encoder_frames);
        av_buffer_unref(&source_frames);
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr, nullptr};
    }
    context->codec_type = AVMEDIA_TYPE_VIDEO;
    context->codec_id = AV_CODEC_ID_H264;
    context->width = static_cast<int>(config.width);
    context->height = static_cast<int>(config.height);
    context->time_base = {1, static_cast<int>(config.frames_per_second)};
    context->framerate = {static_cast<int>(config.frames_per_second), 1};
    context->pix_fmt = map_to_qsv ? AV_PIX_FMT_QSV : AV_PIX_FMT_D3D11;
    context->bit_rate = static_cast<std::int64_t>(
        config.target_bitrate_bits_per_second);
    context->rc_max_rate = static_cast<std::int64_t>(
        config.maximum_bitrate_bits_per_second);
    context->rc_buffer_size = static_cast<int>(
        config.maximum_bitrate_bits_per_second);
    context->gop_size = static_cast<int>(config.gop_frame_count);
    context->max_b_frames = 0;
    context->profile = config.profile == H264Profile::High
        ? AV_PROFILE_H264_HIGH
        : AV_PROFILE_H264_MAIN;
    context->flags |= AV_CODEC_FLAG_GLOBAL_HEADER | AV_CODEC_FLAG_LOW_DELAY;
    context->hw_frames_ctx = av_buffer_ref(encoder_frames);
    if (context->hw_frames_ctx == nullptr) {
        avcodec_free_context(&context);
        av_buffer_unref(&encoder_frames);
        av_buffer_unref(&source_frames);
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr, nullptr};
    }

    AVDictionary *options = nullptr;
    auto status = ConfigureHardwareOptions(config, route, options);
    if (status == VRREC_STATUS_OK) {
        result = avcodec_open2(context, codec, &options);
        status = result < 0 ? ErrorStatus(result) : VRREC_STATUS_OK;
    }
    if (status == VRREC_STATUS_OK && av_dict_count(options) != 0) {
        status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    }
    av_dict_free(&options);
    if (status != VRREC_STATUS_OK) {
        avcodec_free_context(&context);
        av_buffer_unref(&encoder_frames);
        av_buffer_unref(&source_frames);
        return {status, nullptr, nullptr};
    }

    auto created_port = LibavcodecEncoderPort::Create(context);
    if (created_port.status != VRREC_STATUS_OK || created_port.port == nullptr) {
        av_buffer_unref(&encoder_frames);
        av_buffer_unref(&source_frames);
        return {
            created_port.status == VRREC_STATUS_OK
                ? VRREC_STATUS_INTERNAL_ERROR
                : created_port.status,
            nullptr,
            nullptr,
        };
    }
    std::unique_ptr<HardwareCodecSession> session(
        new (std::nothrow) HardwareCodecSession(
            config,
            route,
            std::move(created_port.port),
            source_frames,
            encoder_frames,
            map_to_qsv));
    if (session == nullptr) {
        av_buffer_unref(&encoder_frames);
        av_buffer_unref(&source_frames);
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr, nullptr};
    }
    if (!session->HasResources()) {
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr, nullptr};
    }
    AVFrame *owned_frame = av_frame_alloc();
    if (owned_frame == nullptr) {
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr, nullptr};
    }
    return {VRREC_STATUS_OK, std::move(session), owned_frame};
}

}
