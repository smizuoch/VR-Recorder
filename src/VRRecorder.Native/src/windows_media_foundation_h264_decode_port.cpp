#include "windows_media_foundation_h264_decode_port.hpp"

#if !defined(_WIN32)
#error "The Media Foundation H.264 decode Port requires Windows"
#endif

#include <mfapi.h>
#include <mferror.h>
#include <mfidl.h>
#include <wmcodecdsp.h>
#include <windows.h>

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <limits>

namespace vrrecorder::native {
namespace {

constexpr std::uint32_t MaximumDimension = 16'384;
constexpr std::int64_t HundredNanosecondsPerMicrosecond = 10;

template <typename T>
class ComOwner final {
public:
    ComOwner() noexcept = default;
    ~ComOwner()
    {
        Reset();
    }

    ComOwner(const ComOwner &) = delete;
    ComOwner &operator=(const ComOwner &) = delete;

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

bool IsDimensionValid(std::uint32_t value) noexcept
{
    return value != 0 && value <= MaximumDimension && (value & 1U) == 0;
}

bool TryToMediaFoundationTime(
    std::int64_t microseconds,
    LONGLONG &value) noexcept
{
    constexpr auto minimum =
        std::numeric_limits<std::int64_t>::min() /
        HundredNanosecondsPerMicrosecond;
    constexpr auto maximum =
        std::numeric_limits<std::int64_t>::max() /
        HundredNanosecondsPerMicrosecond;
    if (microseconds < minimum || microseconds > maximum) {
        return false;
    }
    value = microseconds * HundredNanosecondsPerMicrosecond;
    return true;
}

std::uint32_t MinimumNv12BufferSize(
    std::uint32_t width,
    std::uint32_t height) noexcept
{
    const auto pixels = static_cast<std::uint64_t>(width) * height;
    const auto bytes = pixels + pixels / 2U;
    return bytes <= std::numeric_limits<DWORD>::max()
        ? static_cast<std::uint32_t>(bytes)
        : 0;
}

void ReleaseOutputData(
    MFT_OUTPUT_DATA_BUFFER &output,
    IMFSample *caller_owned_sample) noexcept
{
    if (output.pEvents != nullptr) {
        output.pEvents->Release();
        output.pEvents = nullptr;
    }
    if (output.pSample != nullptr && output.pSample != caller_owned_sample) {
        output.pSample->Release();
    }
    output.pSample = nullptr;
}

}

WindowsMediaFoundationH264DecodePort::
    ~WindowsMediaFoundationH264DecodePort()
{
    Abort();
}

vrrec_status_t WindowsMediaFoundationH264DecodePort::Begin(
    std::uint32_t width,
    std::uint32_t height) noexcept
{
    if (active_) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (!IsDimensionValid(width) || !IsDimensionValid(height)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    auto result = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (result == S_OK || result == S_FALSE) {
        com_uninitialize_required_ = true;
    } else if (result != RPC_E_CHANGED_MODE) {
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    }

    result = MFStartup(MF_VERSION, MFSTARTUP_FULL);
    if (FAILED(result)) {
        ReleaseResources(false);
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    }
    media_foundation_started_ = true;

    result = CoCreateInstance(
        CLSID_CMSH264DecoderMFT,
        nullptr,
        CLSCTX_INPROC_SERVER,
        __uuidof(IMFTransform),
        reinterpret_cast<void **>(&transform_));
    if (FAILED(result) || transform_ == nullptr) {
        ReleaseResources(false);
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    }

    ComOwner<IMFMediaType> input_type;
    result = MFCreateMediaType(input_type.Put());
    if (SUCCEEDED(result)) {
        result = input_type.Get()->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
    }
    if (SUCCEEDED(result)) {
        result = input_type.Get()->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264_ES);
    }
    if (SUCCEEDED(result)) {
        result = MFSetAttributeSize(input_type.Get(), MF_MT_FRAME_SIZE, width, height);
    }
    if (SUCCEEDED(result)) {
        result = MFSetAttributeRatio(
            input_type.Get(),
            MF_MT_PIXEL_ASPECT_RATIO,
            1,
            1);
    }
    if (SUCCEEDED(result)) {
        result = input_type.Get()->SetUINT32(
            MF_MT_INTERLACE_MODE,
            MFVideoInterlace_Progressive);
    }
    if (SUCCEEDED(result)) {
        result = transform_->SetInputType(0, input_type.Get(), 0);
    }
    if (FAILED(result)) {
        ReleaseResources(false);
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    }

    width_ = width;
    height_ = height;
    auto status = ConfigureOutputType();
    if (status != VRREC_STATUS_OK) {
        ReleaseResources(false);
        return status;
    }
    result = transform_->ProcessMessage(
        MFT_MESSAGE_NOTIFY_BEGIN_STREAMING,
        0);
    if (SUCCEEDED(result)) {
        result = transform_->ProcessMessage(
            MFT_MESSAGE_NOTIFY_START_OF_STREAM,
            0);
    }
    if (FAILED(result)) {
        ReleaseResources(true);
        return VRREC_STATUS_BACKEND_UNAVAILABLE;
    }

    decoded_frame_count_ = 0;
    presentation_start_microseconds_ = 0;
    has_presentation_start_ = false;
    active_ = true;
    return VRREC_STATUS_OK;
}

vrrec_status_t WindowsMediaFoundationH264DecodePort::ConfigureOutputType()
    noexcept
{
    if (transform_ == nullptr || width_ == 0 || height_ == 0) {
        return VRREC_STATUS_INVALID_STATE;
    }

    for (DWORD index = 0;; ++index) {
        ComOwner<IMFMediaType> output_type;
        const auto available = transform_->GetOutputAvailableType(
            0,
            index,
            output_type.Put());
        if (available == MF_E_NO_MORE_TYPES) {
            return VRREC_STATUS_BACKEND_UNAVAILABLE;
        }
        if (FAILED(available)) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }

        GUID subtype {};
        if (FAILED(output_type.Get()->GetGUID(MF_MT_SUBTYPE, &subtype)) ||
            subtype != MFVideoFormat_NV12) {
            continue;
        }
        auto result = MFSetAttributeSize(
            output_type.Get(),
            MF_MT_FRAME_SIZE,
            width_,
            height_);
        if (SUCCEEDED(result)) {
            result = output_type.Get()->SetUINT32(
                MF_MT_INTERLACE_MODE,
                MFVideoInterlace_Progressive);
        }
        if (SUCCEEDED(result)) {
            result = transform_->SetOutputType(0, output_type.Get(), 0);
        }
        if (SUCCEEDED(result)) {
            return VRREC_STATUS_OK;
        }
    }
}

vrrec_status_t WindowsMediaFoundationH264DecodePort::ProcessAvailableOutput()
    noexcept
{
    if (transform_ == nullptr) {
        return VRREC_STATUS_INVALID_STATE;
    }

    for (;;) {
        MFT_OUTPUT_STREAM_INFO stream_info {};
        auto result = transform_->GetOutputStreamInfo(0, &stream_info);
        if (FAILED(result)) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }

        ComOwner<IMFSample> supplied_sample;
        if ((stream_info.dwFlags & MFT_OUTPUT_STREAM_PROVIDES_SAMPLES) == 0) {
            result = MFCreateSample(supplied_sample.Put());
            if (FAILED(result)) {
                return VRREC_STATUS_OUT_OF_MEMORY;
            }
            ComOwner<IMFMediaBuffer> buffer;
            const auto buffer_size = std::max<DWORD>(
                stream_info.cbSize,
                static_cast<DWORD>(
                    MinimumNv12BufferSize(width_, height_)));
            if (buffer_size == 0) {
                return VRREC_STATUS_INTERNAL_ERROR;
            }
            result = stream_info.cbAlignment > 1
                ? MFCreateAlignedMemoryBuffer(
                    buffer_size,
                    stream_info.cbAlignment - 1U,
                    buffer.Put())
                : MFCreateMemoryBuffer(buffer_size, buffer.Put());
            if (SUCCEEDED(result)) {
                result = supplied_sample.Get()->AddBuffer(buffer.Get());
            }
            if (FAILED(result)) {
                return VRREC_STATUS_OUT_OF_MEMORY;
            }
        }

        MFT_OUTPUT_DATA_BUFFER output {};
        output.dwStreamID = 0;
        output.pSample = supplied_sample.Get();
        DWORD output_status = 0;
        result = transform_->ProcessOutput(
            0,
            1,
            &output,
            &output_status);
        auto *const caller_owned_sample = supplied_sample.Get();
        if (result == MF_E_TRANSFORM_NEED_MORE_INPUT) {
            ReleaseOutputData(output, caller_owned_sample);
            return VRREC_STATUS_OK;
        }
        if (result == MF_E_TRANSFORM_STREAM_CHANGE) {
            ReleaseOutputData(output, caller_owned_sample);
            const auto status = ConfigureOutputType();
            if (status != VRREC_STATUS_OK) {
                return status;
            }
            continue;
        }
        if (FAILED(result) || output.pSample == nullptr) {
            ReleaseOutputData(output, caller_owned_sample);
            return VRREC_STATUS_INTERNAL_ERROR;
        }

        LONGLONG sample_time = 0;
        result = output.pSample->GetSampleTime(&sample_time);
        if (FAILED(result) ||
            sample_time % HundredNanosecondsPerMicrosecond != 0) {
            ReleaseOutputData(output, caller_owned_sample);
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        const auto presentation_microseconds =
            sample_time / HundredNanosecondsPerMicrosecond;
        if (!has_presentation_start_) {
            presentation_start_microseconds_ = presentation_microseconds;
            has_presentation_start_ = true;
        }
        if (decoded_frame_count_ ==
            std::numeric_limits<std::uint32_t>::max()) {
            ReleaseOutputData(output, caller_owned_sample);
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        ++decoded_frame_count_;
        ReleaseOutputData(output, caller_owned_sample);
    }
}

vrrec_status_t WindowsMediaFoundationH264DecodePort::SubmitSample(
    IMFSample &sample) noexcept
{
    auto result = transform_->ProcessInput(0, &sample, 0);
    if (result == MF_E_NOTACCEPTING) {
        const auto status = ProcessAvailableOutput();
        if (status != VRREC_STATUS_OK) {
            return status;
        }
        result = transform_->ProcessInput(0, &sample, 0);
    }
    if (FAILED(result)) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
    return ProcessAvailableOutput();
}

vrrec_status_t WindowsMediaFoundationH264DecodePort::Submit(
    std::span<const std::byte> access_unit,
    std::int64_t pts_microseconds,
    std::int64_t duration_microseconds) noexcept
{
    if (!active_ || transform_ == nullptr) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (access_unit.empty() ||
        access_unit.size() > std::numeric_limits<DWORD>::max() ||
        duration_microseconds <= 0) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    LONGLONG sample_time = 0;
    LONGLONG sample_duration = 0;
    if (!TryToMediaFoundationTime(pts_microseconds, sample_time) ||
        !TryToMediaFoundationTime(duration_microseconds, sample_duration)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    ComOwner<IMFMediaBuffer> buffer;
    auto result = MFCreateMemoryBuffer(
        static_cast<DWORD>(access_unit.size()),
        buffer.Put());
    if (FAILED(result)) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    }
    BYTE *destination = nullptr;
    result = buffer.Get()->Lock(&destination, nullptr, nullptr);
    if (FAILED(result) || destination == nullptr) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
    std::memcpy(destination, access_unit.data(), access_unit.size());
    const auto unlock_result = buffer.Get()->Unlock();
    if (FAILED(unlock_result)) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
    result = buffer.Get()->SetCurrentLength(
        static_cast<DWORD>(access_unit.size()));

    ComOwner<IMFSample> sample;
    if (SUCCEEDED(result)) {
        result = MFCreateSample(sample.Put());
    }
    if (SUCCEEDED(result)) {
        result = sample.Get()->AddBuffer(buffer.Get());
    }
    if (SUCCEEDED(result)) {
        result = sample.Get()->SetSampleTime(sample_time);
    }
    if (SUCCEEDED(result)) {
        result = sample.Get()->SetSampleDuration(sample_duration);
    }
    if (FAILED(result)) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
    return SubmitSample(*sample.Get());
}

EncoderProbeDecodeResult WindowsMediaFoundationH264DecodePort::Finish()
    noexcept
{
    if (!active_ || transform_ == nullptr) {
        return {VRREC_STATUS_INVALID_STATE, 0, 0, 0, 0};
    }

    auto result = transform_->ProcessMessage(
        MFT_MESSAGE_NOTIFY_END_OF_STREAM,
        0);
    if (SUCCEEDED(result)) {
        result = transform_->ProcessMessage(MFT_MESSAGE_COMMAND_DRAIN, 0);
    }
    auto status = FAILED(result)
        ? VRREC_STATUS_INTERNAL_ERROR
        : ProcessAvailableOutput();
    if (status == VRREC_STATUS_OK) {
        result = transform_->ProcessMessage(
            MFT_MESSAGE_NOTIFY_END_STREAMING,
            0);
        if (FAILED(result)) {
            status = VRREC_STATUS_INTERNAL_ERROR;
        }
    }

    EncoderProbeDecodeResult decoded {
        status,
        status == VRREC_STATUS_OK ? width_ : 0,
        status == VRREC_STATUS_OK ? height_ : 0,
        status == VRREC_STATUS_OK ? decoded_frame_count_ : 0,
        status == VRREC_STATUS_OK && has_presentation_start_
            ? presentation_start_microseconds_
            : 0,
    };
    ReleaseResources(status != VRREC_STATUS_OK);
    return decoded;
}

void WindowsMediaFoundationH264DecodePort::Abort() noexcept
{
    ReleaseResources(true);
}

void WindowsMediaFoundationH264DecodePort::ReleaseResources(
    bool notify_transform) noexcept
{
    if (transform_ != nullptr) {
        if (notify_transform) {
            static_cast<void>(transform_->ProcessMessage(
                MFT_MESSAGE_COMMAND_FLUSH,
                0));
            static_cast<void>(transform_->ProcessMessage(
                MFT_MESSAGE_NOTIFY_END_STREAMING,
                0));
        }
        transform_->Release();
        transform_ = nullptr;
    }
    if (media_foundation_started_) {
        static_cast<void>(MFShutdown());
        media_foundation_started_ = false;
    }
    if (com_uninitialize_required_) {
        CoUninitialize();
        com_uninitialize_required_ = false;
    }
    width_ = 0;
    height_ = 0;
    decoded_frame_count_ = 0;
    presentation_start_microseconds_ = 0;
    has_presentation_start_ = false;
    active_ = false;
}

}
