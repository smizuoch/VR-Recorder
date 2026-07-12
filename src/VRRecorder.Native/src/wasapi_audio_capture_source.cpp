#include "audio_capture_source_factory.hpp"

#include <audioclient.h>
#include <ks.h>
#include <ksmedia.h>
#include <mmdeviceapi.h>
#include <windows.h>
#include <wrl/client.h>

#include <atomic>
#include <cstddef>
#include <cstdint>
#include <iterator>
#include <limits>
#include <memory>
#include <new>
#include <span>
#include <string>
#include <utility>

#include "audio_capture_normalizer.hpp"

namespace vrrecorder::native {
namespace {

using Microsoft::WRL::ComPtr;

constexpr auto DefaultRenderEndpoint = "default-render";
constexpr auto DefaultCaptureEndpoint = "default-capture";

bool IsDeviceLost(HRESULT result) noexcept
{
    return result == AUDCLNT_E_DEVICE_INVALIDATED ||
           result == AUDCLNT_E_RESOURCES_INVALIDATED ||
           result == AUDCLNT_E_SERVICE_NOT_RUNNING;
}

std::uint32_t DefaultSpeakerMask(std::uint16_t channel_count) noexcept
{
    if (channel_count == 1) {
        return 0x0000'0004;
    }

    if (channel_count == 2) {
        return 0x0000'0003;
    }

    return 0;
}

bool TryParseFormat(
    const WAVEFORMATEX &wave,
    CapturePcmFormat &format) noexcept
{
    if (wave.nSamplesPerSec == 0 || wave.nChannels == 0 ||
        wave.nBlockAlign == 0 || wave.wBitsPerSample == 0) {
        return false;
    }

    CaptureSampleEncoding encoding;
    auto valid_bits = wave.wBitsPerSample;
    auto speaker_mask = DefaultSpeakerMask(wave.nChannels);
    if (wave.wFormatTag == WAVE_FORMAT_IEEE_FLOAT) {
        encoding = CaptureSampleEncoding::IeeeFloat;
    } else if (wave.wFormatTag == WAVE_FORMAT_PCM) {
        encoding = CaptureSampleEncoding::PcmSignedInteger;
    } else if (wave.wFormatTag == WAVE_FORMAT_EXTENSIBLE &&
               wave.cbSize >= 22) {
        const auto &extended =
            reinterpret_cast<const WAVEFORMATEXTENSIBLE &>(wave);
        valid_bits = extended.Samples.wValidBitsPerSample;
        speaker_mask = extended.dwChannelMask;
        if (IsEqualGUID(
                extended.SubFormat,
                KSDATAFORMAT_SUBTYPE_IEEE_FLOAT)) {
            encoding = CaptureSampleEncoding::IeeeFloat;
        } else if (IsEqualGUID(
                       extended.SubFormat,
                       KSDATAFORMAT_SUBTYPE_PCM)) {
            encoding = CaptureSampleEncoding::PcmSignedInteger;
        } else {
            return false;
        }
    } else {
        return false;
    }

    if (speaker_mask == 0 && wave.nChannels > 2) {
        return false;
    }

    format = CapturePcmFormat {
        wave.nSamplesPerSec,
        wave.nChannels,
        encoding,
        wave.wBitsPerSample,
        valid_bits,
        wave.nBlockAlign,
        speaker_mask,
    };
    return true;
}

bool TryConvertUtf8(
    const std::string &value,
    std::wstring &converted) noexcept
{
    if (value.empty()) {
        converted.clear();
        return true;
    }

    if (value.size() > static_cast<std::size_t>(
            std::numeric_limits<int>::max())) {
        return false;
    }

    const auto required = MultiByteToWideChar(
        CP_UTF8,
        MB_ERR_INVALID_CHARS,
        value.data(),
        static_cast<int>(value.size()),
        nullptr,
        0);
    if (required <= 0) {
        return false;
    }

    try {
        converted.assign(static_cast<std::size_t>(required), L'\0');
    } catch (...) {
        return false;
    }

    return MultiByteToWideChar(
               CP_UTF8,
               MB_ERR_INVALID_CHARS,
               value.data(),
               static_cast<int>(value.size()),
               converted.data(),
               required) == required;
}

class WasapiAudioCaptureSource final : public AudioCaptureSource {
public:
    explicit WasapiAudioCaptureSource(HANDLE abort_event) noexcept
        : abort_event_(abort_event)
    {
    }

    ~WasapiAudioCaptureSource() override
    {
        ResetCapture();
        if (abort_event_ != nullptr) {
            CloseHandle(abort_event_);
        }
    }

    vrrec_status_t Start(
        const AudioCaptureSourceConfig &config) noexcept override
    {
        if (started_ || abort_requested_.load() ||
            config.session_start_qpc_100ns < 0 ||
            (config.role != AudioCaptureRole::DesktopLoopback &&
             config.role != AudioCaptureRole::Microphone)) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }

        capture_thread_id_ = GetCurrentThreadId();
        const auto com_status = CoInitializeEx(
            nullptr,
            COINIT_MULTITHREADED);
        if (FAILED(com_status)) {
            capture_thread_id_ = 0;
            return VRREC_STATUS_BACKEND_UNAVAILABLE;
        }

        com_initialized_ = true;
        auto result = CoCreateInstance(
            __uuidof(MMDeviceEnumerator),
            nullptr,
            CLSCTX_ALL,
            IID_PPV_ARGS(enumerator_.GetAddressOf()));
        if (FAILED(result)) {
            return FailStart(VRREC_STATUS_BACKEND_UNAVAILABLE);
        }

        const auto expected_flow =
            config.role == AudioCaptureRole::DesktopLoopback
            ? eRender
            : eCapture;
        const auto default_id =
            config.role == AudioCaptureRole::DesktopLoopback
            ? DefaultRenderEndpoint
            : DefaultCaptureEndpoint;
        if (config.endpoint_id_utf8.empty() ||
            config.endpoint_id_utf8 == default_id) {
            result = enumerator_->GetDefaultAudioEndpoint(
                expected_flow,
                eMultimedia,
                device_.GetAddressOf());
        } else {
            if (config.endpoint_id_utf8 == DefaultRenderEndpoint ||
                config.endpoint_id_utf8 == DefaultCaptureEndpoint ||
                config.endpoint_id_utf8.size() >
                    static_cast<std::size_t>(
                        std::numeric_limits<int>::max())) {
                return FailStart(VRREC_STATUS_INVALID_ARGUMENT);
            }

            std::wstring endpoint_id;
            if (!TryConvertUtf8(config.endpoint_id_utf8, endpoint_id)) {
                return FailStart(VRREC_STATUS_INVALID_ARGUMENT);
            }

            result = enumerator_->GetDevice(
                endpoint_id.c_str(),
                device_.GetAddressOf());
        }

        if (FAILED(result)) {
            return FailStart(VRREC_STATUS_BACKEND_UNAVAILABLE);
        }

        ComPtr<IMMEndpoint> endpoint;
        result = device_.As(&endpoint);
        EDataFlow actual_flow = eAll;
        if (FAILED(result) ||
            FAILED(endpoint->GetDataFlow(&actual_flow)) ||
            actual_flow != expected_flow) {
            return FailStart(VRREC_STATUS_INVALID_ARGUMENT);
        }

        result = device_->Activate(
            __uuidof(IAudioClient),
            CLSCTX_ALL,
            nullptr,
            reinterpret_cast<void **>(audio_client_.GetAddressOf()));
        if (FAILED(result)) {
            return FailStart(VRREC_STATUS_BACKEND_UNAVAILABLE);
        }

        WAVEFORMATEX *raw_format = nullptr;
        result = audio_client_->GetMixFormat(&raw_format);
        if (FAILED(result) || raw_format == nullptr) {
            if (raw_format != nullptr) {
                CoTaskMemFree(raw_format);
            }

            return FailStart(VRREC_STATUS_BACKEND_UNAVAILABLE);
        }

        const auto format_supported = TryParseFormat(*raw_format, format_);
        if (!format_supported) {
            CoTaskMemFree(raw_format);
            return FailStart(VRREC_STATUS_BACKEND_UNAVAILABLE);
        }

        sample_event_ = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        if (sample_event_ == nullptr) {
            CoTaskMemFree(raw_format);
            return FailStart(VRREC_STATUS_OUT_OF_MEMORY);
        }

        DWORD stream_flags = AUDCLNT_STREAMFLAGS_EVENTCALLBACK;
        if (config.role == AudioCaptureRole::DesktopLoopback) {
            stream_flags |= AUDCLNT_STREAMFLAGS_LOOPBACK;
        }

        result = audio_client_->Initialize(
            AUDCLNT_SHAREMODE_SHARED,
            stream_flags,
            0,
            0,
            raw_format,
            nullptr);
        CoTaskMemFree(raw_format);
        if (FAILED(result)) {
            return FailStart(VRREC_STATUS_BACKEND_UNAVAILABLE);
        }

        result = audio_client_->SetEventHandle(sample_event_);
        if (FAILED(result)) {
            return FailStart(VRREC_STATUS_BACKEND_UNAVAILABLE);
        }

        result = audio_client_->GetService(
            IID_PPV_ARGS(capture_client_.GetAddressOf()));
        if (FAILED(result)) {
            return FailStart(VRREC_STATUS_BACKEND_UNAVAILABLE);
        }

        try {
            normalizer_ = std::make_unique<StereoCaptureNormalizer48k>(
                config.session_start_qpc_100ns);
        } catch (const std::bad_alloc &) {
            return FailStart(VRREC_STATUS_OUT_OF_MEMORY);
        } catch (...) {
            return FailStart(VRREC_STATUS_INTERNAL_ERROR);
        }

        result = audio_client_->Start();
        if (FAILED(result)) {
            return FailStart(IsDeviceLost(result)
                ? VRREC_STATUS_BACKEND_UNAVAILABLE
                : VRREC_STATUS_INTERNAL_ERROR);
        }

        started_ = true;
        next_frame_48k_ = 0;
        return VRREC_STATUS_OK;
    }

    AudioCaptureRead Read() noexcept override
    {
        if (!started_ || GetCurrentThreadId() != capture_thread_id_) {
            return {};
        }

        while (!abort_requested_.load()) {
            UINT32 next_packet_size = 0;
            auto result = capture_client_->GetNextPacketSize(
                &next_packet_size);
            if (IsDeviceLost(result)) {
                return DeviceLost();
            }

            if (FAILED(result)) {
                return FailedRead();
            }

            if (next_packet_size == 0) {
                const HANDLE events[] {abort_event_, sample_event_};
                const auto wait_result = WaitForMultipleObjects(
                    static_cast<DWORD>(std::size(events)),
                    events,
                    FALSE,
                    INFINITE);
                if (wait_result == WAIT_OBJECT_0) {
                    return AbortedRead();
                }

                if (wait_result != WAIT_OBJECT_0 + 1U) {
                    return FailedRead();
                }

                continue;
            }

            BYTE *data = nullptr;
            UINT32 frame_count = 0;
            DWORD flags = 0;
            UINT64 device_position = 0;
            UINT64 qpc_100ns = 0;
            result = capture_client_->GetBuffer(
                &data,
                &frame_count,
                &flags,
                &device_position,
                &qpc_100ns);
            if (IsDeviceLost(result)) {
                return DeviceLost();
            }

            if (result == AUDCLNT_S_BUFFER_EMPTY) {
                continue;
            }

            if (FAILED(result) || frame_count == 0) {
                return FailedRead();
            }

            if ((flags & AUDCLNT_BUFFERFLAGS_TIMESTAMP_ERROR) == 0 &&
                qpc_100ns > static_cast<UINT64>(
                    std::numeric_limits<std::int64_t>::max())) {
                capture_client_->ReleaseBuffer(frame_count);
                return FailedRead();
            }

            const auto silent =
                (flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0;
            const auto timestamp_error =
                (flags & AUDCLNT_BUFFERFLAGS_TIMESTAMP_ERROR) != 0;
            std::span<const std::byte> bytes;
            if (!silent) {
                if (data == nullptr ||
                    frame_count >
                        std::numeric_limits<std::size_t>::max() /
                            format_.block_align) {
                    capture_client_->ReleaseBuffer(frame_count);
                    return FailedRead();
                }

                bytes = std::span<const std::byte>(
                    reinterpret_cast<const std::byte *>(data),
                    static_cast<std::size_t>(frame_count) *
                        format_.block_align);
            }

            CapturedStereoPacket48k normalized {};
            const auto normalization = normalizer_->Normalize(
                format_,
                {
                    device_position,
                    timestamp_error
                        ? 0
                        : static_cast<std::int64_t>(qpc_100ns),
                    frame_count,
                    bytes,
                    silent,
                    (flags & AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY) != 0,
                    timestamp_error,
                },
                normalized);
            const auto release_result =
                capture_client_->ReleaseBuffer(frame_count);
            if (IsDeviceLost(release_result)) {
                return DeviceLost();
            }

            if (FAILED(release_result) ||
                normalization != CaptureNormalizationResult::Ready ||
                normalized.start_frame_48k >
                    std::numeric_limits<std::uint64_t>::max() -
                        normalized.frame_count_48k) {
                return FailedRead();
            }

            next_frame_48k_ = normalized.start_frame_48k +
                normalized.frame_count_48k;
            return {
                AudioCaptureReadResult::Packet,
                normalized,
                0,
            };
        }

        return AbortedRead();
    }

    void Abort() noexcept override
    {
        if (abort_requested_.exchange(true)) {
            return;
        }

        SetEvent(abort_event_);
    }

private:
    vrrec_status_t FailStart(vrrec_status_t status) noexcept
    {
        ResetCapture();
        return status;
    }

    AudioCaptureRead DeviceLost() const noexcept
    {
        return {
            AudioCaptureReadResult::DeviceLost,
            {},
            next_frame_48k_,
        };
    }

    static AudioCaptureRead AbortedRead() noexcept
    {
        return {AudioCaptureReadResult::Aborted, {}, 0};
    }

    static AudioCaptureRead FailedRead() noexcept
    {
        return {AudioCaptureReadResult::Failed, {}, 0};
    }

    void ResetCapture() noexcept
    {
        if (started_ && audio_client_ != nullptr) {
            audio_client_->Stop();
        }

        started_ = false;
        normalizer_.reset();
        capture_client_.Reset();
        audio_client_.Reset();
        device_.Reset();
        enumerator_.Reset();
        if (sample_event_ != nullptr) {
            CloseHandle(sample_event_);
            sample_event_ = nullptr;
        }

        if (com_initialized_) {
            CoUninitialize();
            com_initialized_ = false;
        }

        capture_thread_id_ = 0;
    }

    HANDLE abort_event_ = nullptr;
    HANDLE sample_event_ = nullptr;
    ComPtr<IMMDeviceEnumerator> enumerator_;
    ComPtr<IMMDevice> device_;
    ComPtr<IAudioClient> audio_client_;
    ComPtr<IAudioCaptureClient> capture_client_;
    std::unique_ptr<StereoCaptureNormalizer48k> normalizer_;
    CapturePcmFormat format_ {};
    std::atomic_bool abort_requested_ = false;
    std::uint64_t next_frame_48k_ = 0;
    DWORD capture_thread_id_ = 0;
    bool com_initialized_ = false;
    bool started_ = false;
};

}

vrrec_status_t CreateWasapiAudioCaptureSource(
    std::unique_ptr<AudioCaptureSource> &output) noexcept
{
    output.reset();
    const auto abort_event = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (abort_event == nullptr) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    }

    try {
        output = std::make_unique<WasapiAudioCaptureSource>(abort_event);
        return VRREC_STATUS_OK;
    } catch (const std::bad_alloc &) {
        CloseHandle(abort_event);
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        CloseHandle(abort_event);
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

}
