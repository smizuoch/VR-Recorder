#include "vrrecorder_native.h"

#include <cstddef>
#include <cstdint>
#include <cmath>
#include <condition_variable>
#include <limits>
#include <memory>
#include <mutex>
#include <new>
#include <optional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

#include "encoder_probe_backend.hpp"
#include "media_backend.hpp"
#include "spout_source_backend.hpp"
#include "steamvr_input_backend.hpp"

namespace {

enum class SessionState {
    Created,
    Started,
    Terminal,
    Aborted,
};

}

struct vrrec_session final : vrrecorder::native::MediaEventSink {
    vrrec_session(
        const vrrec_session_config_v1 &config,
        const vrrec_callbacks_v1 &callbacks)
        : temporary_output_path_(config.temporary_output_path_utf8),
          desktop_endpoint_id_(config.desktop_endpoint_id_utf8),
          microphone_endpoint_id_(config.microphone_endpoint_id_utf8),
          spout_sender_identity_(config.spout_sender_identity_utf8),
          gpu_identity_(config.gpu_identity_utf8),
          config_(config),
          callbacks_(callbacks)
    {
        config_.temporary_output_path_utf8 = temporary_output_path_.c_str();
        config_.desktop_endpoint_id_utf8 = desktop_endpoint_id_.c_str();
        config_.microphone_endpoint_id_utf8 = microphone_endpoint_id_.c_str();
        config_.spout_sender_identity_utf8 = spout_sender_identity_.c_str();
        config_.gpu_identity_utf8 = gpu_identity_.c_str();
    }

    vrrec_status_t InitializeBackend()
    {
        auto status = VRREC_STATUS_INTERNAL_ERROR;
        backend_ = vrrecorder::native::CreateMediaBackend(
            config_,
            *this,
            status);
        if (backend_ == nullptr) {
            return status == VRREC_STATUS_OK
                ? VRREC_STATUS_INTERNAL_ERROR
                : status;
        }

        return status;
    }

    vrrec_status_t Start() noexcept
    {
        {
            const std::lock_guard lock(state_mutex_);
            if (state_ != SessionState::Created) {
                return VRREC_STATUS_INVALID_STATE;
            }

            state_ = SessionState::Started;
        }

        const auto status = backend_->Start();
        if (status != VRREC_STATUS_OK) {
            const std::lock_guard lock(state_mutex_);
            if (state_ == SessionState::Started) {
                state_ = SessionState::Created;
            }
        }

        return status;
    }

    vrrec_status_t RequestStop() noexcept
    {
        {
            std::unique_lock lock(state_mutex_);
            if (state_ == SessionState::Terminal) {
                return VRREC_STATUS_OK;
            }

            if (state_ != SessionState::Started) {
                return state_ == SessionState::Aborted
                    ? VRREC_STATUS_OK
                    : VRREC_STATUS_INVALID_STATE;
            }

            if (stop_requested_) {
                return VRREC_STATUS_OK;
            }

            stop_requested_ = true;
            state_condition_.wait(lock, [this] {
                return active_runtime_operations_ == 0;
            });
            if (state_ == SessionState::Terminal ||
                state_ == SessionState::Aborted) {
                return VRREC_STATUS_OK;
            }
        }

        const auto status = backend_->RequestStop();
        if (status != VRREC_STATUS_OK) {
            const std::lock_guard lock(state_mutex_);
            if (state_ == SessionState::Started) {
                stop_requested_ = false;
            }
        }

        return status;
    }

    vrrec_status_t UpdateVideoLayout(
        const vrrec_video_layout_v1 &layout) noexcept
    {
        {
            const std::lock_guard lock(state_mutex_);
            if (state_ != SessionState::Started || stop_requested_) {
                return VRREC_STATUS_INVALID_STATE;
            }

            if (layout.canvas_width != config_.width ||
                layout.canvas_height != config_.height) {
                return VRREC_STATUS_INVALID_ARGUMENT;
            }

            ++active_runtime_operations_;
        }

        const auto status = backend_->UpdateVideoLayout(layout);
        EndRuntimeOperation();
        return status;
    }

    vrrec_status_t GetStatistics(
        vrrec_session_statistics_v1 &statistics) noexcept
    {
        {
            const std::lock_guard lock(state_mutex_);
            if ((state_ != SessionState::Started &&
                 state_ != SessionState::Terminal) ||
                (state_ == SessionState::Started && stop_requested_)) {
                return VRREC_STATUS_INVALID_STATE;
            }

            ++active_runtime_operations_;
        }

        const auto status = backend_->GetStatistics(statistics);
        EndRuntimeOperation();
        return status;
    }

    vrrec_status_t Abort() noexcept
    {
        {
            std::unique_lock lock(state_mutex_);
            if (state_ == SessionState::Aborted) {
                return VRREC_STATUS_OK;
            }

            state_ = SessionState::Aborted;
            state_condition_.wait(lock, [this] {
                return active_runtime_operations_ == 0;
            });
        }

        backend_->Abort();
        const std::lock_guard callbacks_quiesced(callback_mutex_);
        return VRREC_STATUS_OK;
    }

    void FirstVideoPacketMuxed() noexcept override
    {
        const std::lock_guard callback_lock(callback_mutex_);
        std::uint64_t sequence;
        {
            const std::lock_guard state_lock(state_mutex_);
            if (state_ != SessionState::Started || first_packet_emitted_) {
                return;
            }

            first_packet_emitted_ = true;
            sequence = ++sequence_;
        }

        Emit(vrrec_event_v1 {
            sizeof(vrrec_event_v1),
            VRREC_ABI_V1,
            VRREC_EVENT_FIRST_VIDEO_PACKET_MUXED,
            VRREC_STATUS_OK,
            sequence,
            0,
            0,
            nullptr,
        });
    }

    void Stopped(
        std::uint64_t video_packet_count,
        std::uint64_t audio_packet_count) noexcept override
    {
        const std::lock_guard callback_lock(callback_mutex_);
        std::uint64_t sequence;
        {
            const std::lock_guard state_lock(state_mutex_);
            if (state_ != SessionState::Started || !stop_requested_) {
                return;
            }

            state_ = SessionState::Terminal;
            sequence = ++sequence_;
        }

        Emit(vrrec_event_v1 {
            sizeof(vrrec_event_v1),
            VRREC_ABI_V1,
            VRREC_EVENT_STOPPED,
            VRREC_STATUS_OK,
            sequence,
            video_packet_count,
            audio_packet_count,
            nullptr,
        });
    }

    void Faulted(
        vrrec_status_t status,
        const char *message_utf8) noexcept override
    {
        const std::lock_guard callback_lock(callback_mutex_);
        std::uint64_t sequence;
        {
            const std::lock_guard state_lock(state_mutex_);
            if (state_ == SessionState::Terminal ||
                state_ == SessionState::Aborted) {
                return;
            }

            state_ = SessionState::Terminal;
            sequence = ++sequence_;
        }

        Emit(vrrec_event_v1 {
            sizeof(vrrec_event_v1),
            VRREC_ABI_V1,
            VRREC_EVENT_FAULTED,
            status,
            sequence,
            0,
            0,
            message_utf8,
        });
    }

private:
    void EndRuntimeOperation() noexcept
    {
        const std::lock_guard lock(state_mutex_);
        --active_runtime_operations_;
        state_condition_.notify_all();
    }

    void Emit(const vrrec_event_v1 &event) noexcept
    {
        try {
            callbacks_.on_event(callbacks_.user_data, &event);
        } catch (...) {
            // No exception may cross the C ABI callback boundary.
        }
    }

    std::string temporary_output_path_;
    std::string desktop_endpoint_id_;
    std::string microphone_endpoint_id_;
    std::string spout_sender_identity_;
    std::string gpu_identity_;
    vrrec_session_config_v1 config_;
    vrrec_callbacks_v1 callbacks_;
    std::unique_ptr<vrrecorder::native::MediaBackend> backend_;
    std::mutex state_mutex_;
    std::condition_variable state_condition_;
    std::mutex callback_mutex_;
    SessionState state_ = SessionState::Created;
    bool stop_requested_ = false;
    bool first_packet_emitted_ = false;
    std::uint64_t sequence_ = 0;
    std::size_t active_runtime_operations_ = 0;
};

struct vrrec_steamvr_input final {
    explicit vrrec_steamvr_input(
        const vrrec_steamvr_input_config_v1 &config)
        : manifest_path_(config.action_manifest_path_utf8),
          action_set_path_(config.action_set_path_utf8),
          digital_action_path_(config.digital_action_path_utf8),
          config_(config)
    {
        config_.action_manifest_path_utf8 = manifest_path_.c_str();
        config_.action_set_path_utf8 = action_set_path_.c_str();
        config_.digital_action_path_utf8 = digital_action_path_.c_str();
    }

    vrrec_status_t InitializeBackend()
    {
        auto status = VRREC_STATUS_INTERNAL_ERROR;
        backend_ = vrrecorder::native::CreateSteamVrInputBackend(
            config_,
            status);
        if (backend_ == nullptr) {
            return status == VRREC_STATUS_OK
                ? VRREC_STATUS_INTERNAL_ERROR
                : status;
        }

        return status;
    }

    vrrec_status_t Poll(vrrec_steamvr_digital_state_v1 &state) noexcept
    {
        const std::lock_guard lock(poll_mutex_);
        return backend_->Poll(state);
    }

private:
    std::string manifest_path_;
    std::string action_set_path_;
    std::string digital_action_path_;
    vrrec_steamvr_input_config_v1 config_;
    std::unique_ptr<vrrecorder::native::SteamVrInputBackend> backend_;
    std::mutex poll_mutex_;
};

struct vrrec_spout_source final {
    explicit vrrec_spout_source(
        const vrrec_spout_source_config_v1 &config)
        : config_(config)
    {
    }

    vrrec_status_t InitializeBackend()
    {
        auto status = VRREC_STATUS_INTERNAL_ERROR;
        backend_ = vrrecorder::native::CreateSpoutSourceBackend(
            config_,
            status);
        if (backend_ == nullptr) {
            return status == VRREC_STATUS_OK
                ? VRREC_STATUS_INTERNAL_ERROR
                : status;
        }

        return status;
    }

    bool BeginOperation() noexcept
    {
        const std::lock_guard lock(lifecycle_mutex_);
        if (destroying_) {
            return false;
        }

        ++active_operations_;
        return true;
    }

    void EndOperation() noexcept
    {
        const std::lock_guard lock(lifecycle_mutex_);
        --active_operations_;
        lifecycle_condition_.notify_all();
    }

    void JoinOperationsForDestroy() noexcept
    {
        std::unique_lock lock(lifecycle_mutex_);
        destroying_ = true;
        lifecycle_condition_.wait(lock, [this] {
            return active_operations_ == 0;
        });
    }

    vrrec_spout_source_config_v1 config_;
    std::unique_ptr<vrrecorder::native::SpoutSourceBackend> backend_;
    std::mutex operation_mutex_;
    std::optional<vrrecorder::native::SpoutFrame> pending_frame_;

private:
    std::mutex lifecycle_mutex_;
    std::condition_variable lifecycle_condition_;
    std::size_t active_operations_ = 0;
    bool destroying_ = false;
};

namespace {

class SpoutOperationLease final {
public:
    explicit SpoutOperationLease(vrrec_spout_source_t &source) noexcept
        : source_(source), acquired_(source_.BeginOperation())
    {
    }

    ~SpoutOperationLease()
    {
        if (acquired_) {
            source_.EndOperation();
        }
    }

    explicit operator bool() const noexcept
    {
        return acquired_;
    }

private:
    vrrec_spout_source_t &source_;
    bool acquired_;
};

bool IsAbsoluteUtf8Path(std::string_view path) noexcept
{
#if defined(_WIN32)
    const auto drive_path =
        path.size() >= 3 &&
        ((path[0] >= 'A' && path[0] <= 'Z') ||
         (path[0] >= 'a' && path[0] <= 'z')) &&
        path[1] == ':' &&
        (path[2] == '\\' || path[2] == '/');
    const auto unc_path =
        path.size() >= 2 &&
        (path[0] == '\\' && path[1] == '\\') ||
        (path.size() >= 2 && path[0] == '/' && path[1] == '/');
    return drive_path || unc_path;
#else
    return !path.empty() && path[0] == '/';
#endif
}

bool IsContinuationByte(unsigned char value) noexcept
{
    return value >= 0x80 && value <= 0xBF;
}

bool TryValidateUtf8Text(
    const char *text,
    std::size_t maximum_size,
    std::string_view &validated) noexcept
{
    if (text == nullptr) {
        return false;
    }

    std::size_t size = 0;
    while (size <= maximum_size && text[size] != '\0') {
        ++size;
    }

    if (size == 0 || size > maximum_size) {
        return false;
    }

    validated = std::string_view(text, size);
    auto has_non_whitespace = false;
    for (std::size_t index = 0; index < size;) {
        const auto first = static_cast<unsigned char>(text[index]);
        if (first <= 0x7F) {
            if (first < 0x20 || first == 0x7F) {
                return false;
            }

            has_non_whitespace = has_non_whitespace || first != ' ';
            ++index;
            continue;
        }

        has_non_whitespace = true;
        if (first >= 0xC2 && first <= 0xDF) {
            if (index + 1 >= size ||
                !IsContinuationByte(
                    static_cast<unsigned char>(text[index + 1]))) {
                return false;
            }

            index += 2;
            continue;
        }

        if (first >= 0xE0 && first <= 0xEF) {
            if (index + 2 >= size) {
                return false;
            }

            const auto second = static_cast<unsigned char>(text[index + 1]);
            const auto third = static_cast<unsigned char>(text[index + 2]);
            if (!IsContinuationByte(third) ||
                (first == 0xE0 && (second < 0xA0 || second > 0xBF)) ||
                (first == 0xED && (second < 0x80 || second > 0x9F)) ||
                ((first != 0xE0 && first != 0xED) &&
                 !IsContinuationByte(second))) {
                return false;
            }

            index += 3;
            continue;
        }

        if (first >= 0xF0 && first <= 0xF4) {
            if (index + 3 >= size) {
                return false;
            }

            const auto second = static_cast<unsigned char>(text[index + 1]);
            const auto third = static_cast<unsigned char>(text[index + 2]);
            const auto fourth = static_cast<unsigned char>(text[index + 3]);
            if (!IsContinuationByte(third) ||
                !IsContinuationByte(fourth) ||
                (first == 0xF0 && (second < 0x90 || second > 0xBF)) ||
                (first == 0xF4 && (second < 0x80 || second > 0x8F)) ||
                ((first != 0xF0 && first != 0xF4) &&
                 !IsContinuationByte(second))) {
                return false;
            }

            index += 4;
            continue;
        }

        return false;
    }

    return has_non_whitespace;
}

bool IsAbsoluteActionPath(const char *path) noexcept
{
    return path != nullptr && path[0] == '/' && path[1] != '\0';
}

bool IsEncoderKindSupported(vrrec_encoder_kind_t encoder_kind) noexcept
{
    return encoder_kind == VRREC_ENCODER_NVENC ||
        encoder_kind == VRREC_ENCODER_AMF ||
        encoder_kind == VRREC_ENCODER_QSV ||
        encoder_kind == VRREC_ENCODER_MEDIA_FOUNDATION_SOFTWARE;
}

bool IsAudioRoutingSupported(vrrec_audio_routing_t routing) noexcept
{
    return routing == VRREC_AUDIO_ROUTING_MIXED ||
        routing == VRREC_AUDIO_ROUTING_DESKTOP_ONLY ||
        routing == VRREC_AUDIO_ROUTING_MIC_ONLY ||
        routing == VRREC_AUDIO_ROUTING_MUTED;
}

bool IsQualityPresetSupported(vrrec_quality_preset_t quality) noexcept
{
    return quality == VRREC_QUALITY_PRESET_STANDARD ||
        quality == VRREC_QUALITY_PRESET_HIGH;
}

bool IsSourcePixelFormatSupported(
    vrrec_source_pixel_format_t pixel_format) noexcept
{
    return pixel_format == VRREC_SOURCE_PIXEL_FORMAT_BGRA8 ||
        pixel_format == VRREC_SOURCE_PIXEL_FORMAT_RGBA8 ||
        pixel_format == VRREC_SOURCE_PIXEL_FORMAT_NV12;
}

bool IsValidGain(double gain_db) noexcept
{
    return std::isfinite(gain_db) && gain_db >= -96.0 && gain_db <= 24.0;
}

bool IsValidExtendedGeometry(const vrrec_session_config_v1 &config) noexcept
{
    return config.width != 0 &&
        config.height != 0 &&
        config.width % 2 == 0 &&
        config.height % 2 == 0 &&
        config.source_width != 0 &&
        config.source_height != 0 &&
        config.destination_width != 0 &&
        config.destination_height != 0 &&
        config.destination_width % 2 == 0 &&
        config.destination_height % 2 == 0 &&
        config.destination_x <= config.width &&
        config.destination_y <= config.height &&
        config.destination_width <= config.width - config.destination_x &&
        config.destination_height <= config.height - config.destination_y;
}

bool IsValidVideoLayout(const vrrec_video_layout_v1 &layout) noexcept
{
    return layout.source_width != 0 &&
        layout.source_height != 0 &&
        layout.canvas_width != 0 &&
        layout.canvas_height != 0 &&
        layout.canvas_width % 2 == 0 &&
        layout.canvas_height % 2 == 0 &&
        layout.destination_width != 0 &&
        layout.destination_height != 0 &&
        layout.destination_width % 2 == 0 &&
        layout.destination_height % 2 == 0 &&
        layout.destination_x <= layout.canvas_width &&
        layout.destination_y <= layout.canvas_height &&
        layout.destination_width <=
            layout.canvas_width - layout.destination_x &&
        layout.destination_height <=
            layout.canvas_height - layout.destination_y &&
        layout.canvas_background == VRREC_CANVAS_BACKGROUND_BLACK &&
        layout.rotation == VRREC_VIDEO_ROTATION_NONE;
}

constexpr std::size_t SessionConfigBaseSize =
    offsetof(vrrec_session_config_v1, encoder_kind);
constexpr std::size_t SessionConfigEncoderSize =
    offsetof(vrrec_session_config_v1, source_width);
constexpr std::size_t SessionConfigLegacyMediaSize =
    offsetof(vrrec_session_config_v1, source_pixel_format);
constexpr std::size_t SessionConfigSourceFormatSize =
    sizeof(vrrec_session_config_v1);
constexpr double MaximumEstimatedSourceFps = 1000.0;
constexpr std::uint32_t EncoderProbeSyntheticFrameCount = 16;

vrrec_session_config_v1 NormalizeSessionConfig(
    const vrrec_session_config_v1 &config) noexcept
{
    const auto encoder_kind = config.struct_size >= SessionConfigEncoderSize
        ? config.encoder_kind
        : VRREC_ENCODER_MEDIA_FOUNDATION_SOFTWARE;
    const auto has_media_config =
        config.struct_size >= SessionConfigLegacyMediaSize;
    const auto has_source_format =
        config.struct_size >= SessionConfigSourceFormatSize;
    return vrrec_session_config_v1 {
        sizeof(vrrec_session_config_v1),
        VRREC_ABI_V1,
        config.temporary_output_path_utf8,
        config.width,
        config.height,
        config.fps_numerator,
        config.fps_denominator,
        config.started_at_unix_milliseconds_utc,
        encoder_kind,
        0,
        has_media_config ? config.source_width : config.width,
        has_media_config ? config.source_height : config.height,
        has_media_config ? config.destination_x : 0,
        has_media_config ? config.destination_y : 0,
        has_media_config ? config.destination_width : config.width,
        has_media_config ? config.destination_height : config.height,
        has_media_config
            ? config.canvas_background
            : VRREC_CANVAS_BACKGROUND_BLACK,
        has_media_config ? config.rotation : VRREC_VIDEO_ROTATION_NONE,
        has_media_config ? config.audio_routing : VRREC_AUDIO_ROUTING_MIXED,
        has_media_config ? config.quality_preset : VRREC_QUALITY_PRESET_HIGH,
        has_media_config
            ? config.desktop_endpoint_id_utf8
            : "default-render",
        has_media_config
            ? config.microphone_endpoint_id_utf8
            : "default-capture",
        has_media_config ? config.desktop_gain_db : -6.0,
        has_media_config ? config.microphone_gain_db : -6.0,
        has_media_config
            ? config.spout_sender_identity_utf8
            : "legacy-unspecified",
        has_media_config ? config.spout_adapter_luid : 0,
        has_media_config ? config.encoder_adapter_luid : 0,
        has_media_config
            ? config.gpu_identity_utf8
            : "legacy-unspecified",
        0,
        has_source_format
            ? config.source_pixel_format
            : VRREC_SOURCE_PIXEL_FORMAT_BGRA8,
        0,
        has_source_format
            ? config.estimated_source_fps
            : static_cast<double>(config.fps_numerator) /
                static_cast<double>(config.fps_denominator),
    };
}

vrrec_status_t ValidateCreateArguments(
    const vrrec_session_config_v1 *config,
    const vrrec_callbacks_v1 *callbacks,
    vrrec_session_t **out_session) noexcept
{
    if (out_session == nullptr) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    *out_session = nullptr;
    if (config == nullptr || callbacks == nullptr) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    if ((config->struct_size != SessionConfigBaseSize &&
         config->struct_size != SessionConfigEncoderSize &&
         config->struct_size != SessionConfigLegacyMediaSize &&
         config->struct_size < SessionConfigSourceFormatSize) ||
        callbacks->struct_size < sizeof(vrrec_callbacks_v1)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    if (config->abi_version != VRREC_ABI_V1 ||
        callbacks->abi_version != VRREC_ABI_V1) {
        return VRREC_STATUS_UNSUPPORTED_ABI;
    }

    std::string_view output_path;
    if (!TryValidateUtf8Text(
            config->temporary_output_path_utf8,
            32767,
            output_path) ||
        !IsAbsoluteUtf8Path(output_path) ||
        config->width == 0 ||
        config->height == 0 ||
        config->fps_numerator == 0 ||
        config->fps_denominator == 0 ||
        callbacks->on_event == nullptr) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    if (config->struct_size >= SessionConfigEncoderSize &&
        !IsEncoderKindSupported(config->encoder_kind)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    if (config->struct_size >= SessionConfigLegacyMediaSize) {
        std::string_view desktop_endpoint;
        std::string_view microphone_endpoint;
        std::string_view sender_identity;
        std::string_view gpu_identity;
        if (!IsValidExtendedGeometry(*config) ||
            config->canvas_background != VRREC_CANVAS_BACKGROUND_BLACK ||
            config->rotation != VRREC_VIDEO_ROTATION_NONE ||
            !IsAudioRoutingSupported(config->audio_routing) ||
            !IsQualityPresetSupported(config->quality_preset) ||
            !TryValidateUtf8Text(
                config->desktop_endpoint_id_utf8,
                4096,
                desktop_endpoint) ||
            !TryValidateUtf8Text(
                config->microphone_endpoint_id_utf8,
                4096,
                microphone_endpoint) ||
            !IsValidGain(config->desktop_gain_db) ||
            !IsValidGain(config->microphone_gain_db) ||
            !TryValidateUtf8Text(
                config->spout_sender_identity_utf8,
                4096,
                sender_identity) ||
            !TryValidateUtf8Text(
                config->gpu_identity_utf8,
                4096,
                gpu_identity) ||
            config->reserved_v1 != 0) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }
    }

    if (config->struct_size >= SessionConfigSourceFormatSize &&
        (!IsSourcePixelFormatSupported(config->source_pixel_format) ||
         config->reserved_v2 != 0 ||
         !std::isfinite(config->estimated_source_fps) ||
         config->estimated_source_fps <= 0.0 ||
         config->estimated_source_fps > MaximumEstimatedSourceFps)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    return VRREC_STATUS_OK;
}

vrrec_status_t ValidateSteamVrCreateArguments(
    const vrrec_steamvr_input_config_v1 *config,
    vrrec_steamvr_input_t **out_input) noexcept
{
    if (out_input == nullptr) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    *out_input = nullptr;
    if (config == nullptr ||
        config->struct_size < sizeof(vrrec_steamvr_input_config_v1)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    if (config->abi_version != VRREC_ABI_V1) {
        return VRREC_STATUS_UNSUPPORTED_ABI;
    }

    if (config->action_manifest_path_utf8 == nullptr ||
        config->action_manifest_path_utf8[0] == '\0' ||
        !IsAbsoluteUtf8Path(config->action_manifest_path_utf8) ||
        !IsAbsoluteActionPath(config->action_set_path_utf8) ||
        !IsAbsoluteActionPath(config->digital_action_path_utf8)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    return VRREC_STATUS_OK;
}

bool IsValidSpoutBackendText(const std::string &text) noexcept
{
    std::string_view validated;
    return TryValidateUtf8Text(
               text.c_str(),
               VRREC_SPOUT_MAX_IDENTITY_UTF8_SIZE,
               validated) &&
        validated.size() == text.size();
}

bool IsGpuVendorSupported(vrrec_gpu_vendor_t vendor) noexcept
{
    return vendor == VRREC_GPU_VENDOR_UNKNOWN ||
        vendor == VRREC_GPU_VENDOR_NVIDIA ||
        vendor == VRREC_GPU_VENDOR_AMD ||
        vendor == VRREC_GPU_VENDOR_INTEL;
}

bool TryValidateSpoutSnapshot(
    const std::vector<vrrecorder::native::SpoutSenderSnapshot> &senders,
    std::uint32_t &required_utf8_size) noexcept
{
    required_utf8_size = 0;
    if (senders.size() > VRREC_SPOUT_MAX_SNAPSHOT_ENTRIES) {
        return false;
    }

    std::size_t total_size = 0;
    for (const auto &sender : senders) {
        if (!IsValidSpoutBackendText(sender.sender_id)) {
            return false;
        }

        total_size += sender.sender_id.size();
        if (total_size > VRREC_SPOUT_MAX_UTF8_BUFFER_SIZE) {
            return false;
        }
    }

    required_utf8_size = static_cast<std::uint32_t>(total_size);
    return true;
}

bool TryValidateSpoutFrame(
    const vrrecorder::native::SpoutFrame &frame,
    std::uint32_t &required_utf8_size) noexcept
{
    required_utf8_size = 0;
    if (!IsValidSpoutBackendText(frame.sender_id) ||
        !IsValidSpoutBackendText(frame.gpu_identity) ||
        frame.adapter_luid == 0 ||
        !IsGpuVendorSupported(frame.gpu_vendor) ||
        frame.width == 0 ||
        frame.width > static_cast<std::uint32_t>(INT32_MAX) ||
        frame.height == 0 ||
        frame.height > static_cast<std::uint32_t>(INT32_MAX) ||
        !IsSourcePixelFormatSupported(frame.pixel_format) ||
        !std::isfinite(frame.estimated_source_fps) ||
        frame.estimated_source_fps <= 0.0 ||
        frame.estimated_source_fps > MaximumEstimatedSourceFps ||
        frame.monotonic_timestamp_microseconds < 0 ||
        frame.monotonic_timestamp_microseconds >
            INT64_C(922337203685477580)) {
        return false;
    }

    const auto total_size =
        frame.sender_id.size() + frame.gpu_identity.size();
    if (total_size > VRREC_SPOUT_MAX_UTF8_BUFFER_SIZE) {
        return false;
    }

    required_utf8_size = static_cast<std::uint32_t>(total_size);
    return true;
}

vrrec_status_t ValidateEncoderProbeArguments(
    const vrrec_encoder_probe_config_v1 *config,
    std::uint8_t *out_packet_produced) noexcept
{
    if (out_packet_produced == nullptr) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    *out_packet_produced = 0;
    if (config == nullptr ||
        config->struct_size < sizeof(vrrec_encoder_probe_config_v1)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    if (config->abi_version != VRREC_ABI_V1) {
        return VRREC_STATUS_UNSUPPORTED_ABI;
    }

    std::string_view gpu_identity;
    if (!IsEncoderKindSupported(config->encoder_kind) ||
        config->synthetic_frame_count !=
            EncoderProbeSyntheticFrameCount ||
        config->adapter_luid == 0 ||
        config->width == 0 ||
        config->width > static_cast<std::uint32_t>(INT32_MAX) ||
        (config->width & 1U) != 0 ||
        config->height == 0 ||
        config->height > static_cast<std::uint32_t>(INT32_MAX) ||
        (config->height & 1U) != 0 ||
        config->fps_numerator < 30 ||
        config->fps_numerator > 120 ||
        config->fps_denominator != 1 ||
        !TryValidateUtf8Text(
            config->gpu_identity_utf8,
            VRREC_SPOUT_MAX_IDENTITY_UTF8_SIZE,
            gpu_identity) ||
        config->reserved != 0) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    return VRREC_STATUS_OK;
}

vrrec_status_t ValidateSpoutCreateArguments(
    const vrrec_spout_source_config_v1 *config,
    vrrec_spout_source_t **out_source) noexcept
{
    if (out_source == nullptr) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    *out_source = nullptr;
    if (config == nullptr ||
        config->struct_size < sizeof(vrrec_spout_source_config_v1)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    if (config->abi_version != VRREC_ABI_V1) {
        return VRREC_STATUS_UNSUPPORTED_ABI;
    }

    if (config->reserved_v1 != 0 || config->reserved_v2 != 0) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    return VRREC_STATUS_OK;
}

vrrec_status_t ValidateSpoutSnapshotArguments(
    vrrec_spout_source_t *source,
    vrrec_spout_sender_snapshot_v1 *entries,
    std::uint32_t entry_capacity,
    char *utf8_buffer,
    std::uint32_t utf8_capacity,
    std::uint32_t *out_entry_count,
    std::uint32_t *out_required_utf8_size) noexcept
{
    if (source == nullptr ||
        out_entry_count == nullptr ||
        out_required_utf8_size == nullptr ||
        (entry_capacity != 0 && entries == nullptr) ||
        (utf8_capacity != 0 && utf8_buffer == nullptr) ||
        entry_capacity > VRREC_SPOUT_MAX_SNAPSHOT_ENTRIES ||
        utf8_capacity > VRREC_SPOUT_MAX_UTF8_BUFFER_SIZE) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    return VRREC_STATUS_OK;
}

vrrec_status_t ValidateSpoutFrameArguments(
    vrrec_spout_source_t *source,
    std::uint32_t timeout_milliseconds,
    vrrec_spout_frame_v1 *out_frame,
    char *utf8_buffer,
    std::uint32_t utf8_capacity,
    std::uint32_t *out_required_utf8_size) noexcept
{
    if (source == nullptr ||
        out_frame == nullptr ||
        out_required_utf8_size == nullptr ||
        (utf8_capacity != 0 && utf8_buffer == nullptr) ||
        utf8_capacity > VRREC_SPOUT_MAX_UTF8_BUFFER_SIZE ||
        timeout_milliseconds >
            VRREC_SPOUT_MAX_POLL_TIMEOUT_MILLISECONDS ||
        out_frame->struct_size < sizeof(vrrec_spout_frame_v1)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    if (out_frame->abi_version != VRREC_ABI_V1) {
        return VRREC_STATUS_UNSUPPORTED_ABI;
    }

    return VRREC_STATUS_OK;
}

}

extern "C" VRREC_API std::uint32_t VRREC_CALL vrrec_abi_version(void)
{
    return VRREC_ABI_V1;
}

extern "C" VRREC_API vrrec_status_t VRREC_CALL vrrec_encoder_probe_v1(
    const vrrec_encoder_probe_config_v1 *config,
    std::uint8_t *out_packet_produced)
{
    const auto validation = ValidateEncoderProbeArguments(
        config,
        out_packet_produced);
    if (validation != VRREC_STATUS_OK) {
        return validation;
    }

    try {
        auto creation_status = VRREC_STATUS_INTERNAL_ERROR;
        auto backend = vrrecorder::native::CreateEncoderProbeBackend(
            creation_status);
        if (backend == nullptr) {
            return creation_status == VRREC_STATUS_OK
                ? VRREC_STATUS_INTERNAL_ERROR
                : creation_status;
        }

        auto packet_produced = false;
        const auto status = backend->Probe(*config, packet_produced);
        if (status == VRREC_STATUS_OK && packet_produced) {
            *out_packet_produced = 1;
        }

        return status;
    } catch (const std::bad_alloc &) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

extern "C" VRREC_API vrrec_status_t VRREC_CALL vrrec_session_create_v1(
    const vrrec_session_config_v1 *config,
    const vrrec_callbacks_v1 *callbacks,
    vrrec_session_t **out_session)
{
    const auto validation = ValidateCreateArguments(
        config,
        callbacks,
        out_session);
    if (validation != VRREC_STATUS_OK) {
        return validation;
    }

    try {
        const auto normalized_config = NormalizeSessionConfig(*config);
        auto session = std::make_unique<vrrec_session>(
            normalized_config,
            *callbacks);
        const auto status = session->InitializeBackend();
        if (status != VRREC_STATUS_OK) {
            return status;
        }

        *out_session = session.release();
        return VRREC_STATUS_OK;
    } catch (const std::bad_alloc &) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

extern "C" VRREC_API vrrec_status_t VRREC_CALL vrrec_session_start_v1(
    vrrec_session_t *session)
{
    if (session == nullptr) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    try {
        return session->Start();
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

extern "C" VRREC_API vrrec_status_t VRREC_CALL
vrrec_session_update_video_layout_v1(
    vrrec_session_t *session,
    const vrrec_video_layout_v1 *layout)
{
    if (session == nullptr || layout == nullptr ||
        layout->struct_size < sizeof(vrrec_video_layout_v1) ||
        !IsValidVideoLayout(*layout)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    if (layout->abi_version != VRREC_ABI_V1) {
        return VRREC_STATUS_UNSUPPORTED_ABI;
    }

    try {
        const auto normalized_layout = vrrec_video_layout_v1 {
            sizeof(vrrec_video_layout_v1),
            VRREC_ABI_V1,
            layout->source_width,
            layout->source_height,
            layout->canvas_width,
            layout->canvas_height,
            layout->destination_x,
            layout->destination_y,
            layout->destination_width,
            layout->destination_height,
            layout->canvas_background,
            layout->rotation,
        };
        return session->UpdateVideoLayout(normalized_layout);
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

extern "C" VRREC_API vrrec_status_t VRREC_CALL
vrrec_session_get_statistics_v1(
    vrrec_session_t *session,
    vrrec_session_statistics_v1 *out_statistics)
{
    if (session == nullptr || out_statistics == nullptr ||
        out_statistics->struct_size < sizeof(vrrec_session_statistics_v1)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    if (out_statistics->abi_version != VRREC_ABI_V1) {
        return VRREC_STATUS_UNSUPPORTED_ABI;
    }

    try {
        auto statistics = vrrec_session_statistics_v1 {
            sizeof(vrrec_session_statistics_v1),
            VRREC_ABI_V1,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
        };
        const auto status = session->GetStatistics(statistics);
        if (status == VRREC_STATUS_OK) {
            *out_statistics = statistics;
        }

        return status;
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

extern "C" VRREC_API vrrec_status_t VRREC_CALL
vrrec_session_request_stop_v1(vrrec_session_t *session)
{
    if (session == nullptr) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    try {
        return session->RequestStop();
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

extern "C" VRREC_API vrrec_status_t VRREC_CALL vrrec_session_abort_v1(
    vrrec_session_t *session)
{
    if (session == nullptr) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    try {
        return session->Abort();
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

extern "C" VRREC_API void VRREC_CALL vrrec_session_destroy_v1(
    vrrec_session_t *session)
{
    if (session == nullptr) {
        return;
    }

    try {
        (void)session->Abort();
        delete session;
    } catch (...) {
        // Destruction is the final fail-safe and cannot report across C ABI.
    }
}

extern "C" VRREC_API vrrec_status_t VRREC_CALL
vrrec_steamvr_input_create_v1(
    const vrrec_steamvr_input_config_v1 *config,
    vrrec_steamvr_input_t **out_input)
{
    const auto validation = ValidateSteamVrCreateArguments(
        config,
        out_input);
    if (validation != VRREC_STATUS_OK) {
        return validation;
    }

    try {
        auto input = std::make_unique<vrrec_steamvr_input>(*config);
        const auto status = input->InitializeBackend();
        if (status != VRREC_STATUS_OK) {
            return status;
        }

        *out_input = input.release();
        return VRREC_STATUS_OK;
    } catch (const std::bad_alloc &) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

extern "C" VRREC_API vrrec_status_t VRREC_CALL vrrec_steamvr_input_poll_v1(
    vrrec_steamvr_input_t *input,
    vrrec_steamvr_digital_state_v1 *out_state)
{
    if (input == nullptr || out_state == nullptr ||
        out_state->struct_size < sizeof(vrrec_steamvr_digital_state_v1)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    if (out_state->abi_version != VRREC_ABI_V1) {
        return VRREC_STATUS_UNSUPPORTED_ABI;
    }

    try {
        out_state->reserved = 0;
        return input->Poll(*out_state);
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

extern "C" VRREC_API void VRREC_CALL vrrec_steamvr_input_destroy_v1(
    vrrec_steamvr_input_t *input)
{
    try {
        delete input;
    } catch (...) {
        // Destruction is the final fail-safe and cannot report across C ABI.
    }
}

extern "C" VRREC_API vrrec_status_t VRREC_CALL
vrrec_spout_source_create_v1(
    const vrrec_spout_source_config_v1 *config,
    vrrec_spout_source_t **out_source)
{
    const auto validation = ValidateSpoutCreateArguments(
        config,
        out_source);
    if (validation != VRREC_STATUS_OK) {
        return validation;
    }

    try {
        auto source = std::make_unique<vrrec_spout_source>(*config);
        const auto status = source->InitializeBackend();
        if (status != VRREC_STATUS_OK) {
            return status;
        }

        *out_source = source.release();
        return VRREC_STATUS_OK;
    } catch (const std::bad_alloc &) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

extern "C" VRREC_API vrrec_status_t VRREC_CALL
vrrec_spout_source_snapshot_v1(
    vrrec_spout_source_t *source,
    vrrec_spout_sender_snapshot_v1 *entries,
    std::uint32_t entry_capacity,
    char *utf8_buffer,
    std::uint32_t utf8_capacity,
    std::uint32_t *out_entry_count,
    std::uint32_t *out_required_utf8_size)
{
    const auto validation = ValidateSpoutSnapshotArguments(
        source,
        entries,
        entry_capacity,
        utf8_buffer,
        utf8_capacity,
        out_entry_count,
        out_required_utf8_size);
    if (validation != VRREC_STATUS_OK) {
        return validation;
    }

    *out_entry_count = 0;
    *out_required_utf8_size = 0;
    SpoutOperationLease operation(*source);
    if (!operation) {
        return VRREC_STATUS_INVALID_STATE;
    }

    try {
        const std::lock_guard lock(source->operation_mutex_);
        std::vector<vrrecorder::native::SpoutSenderSnapshot> senders;
        const auto status = source->backend_->Snapshot(senders);
        if (status != VRREC_STATUS_OK) {
            return status;
        }

        std::uint32_t required_utf8_size = 0;
        if (!TryValidateSpoutSnapshot(senders, required_utf8_size)) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }

        const auto required_entry_count =
            static_cast<std::uint32_t>(senders.size());
        *out_entry_count = required_entry_count;
        *out_required_utf8_size = required_utf8_size;
        const auto entries_to_validate =
            entry_capacity < required_entry_count
                ? entry_capacity
                : required_entry_count;
        for (std::uint32_t index = 0;
             index < entries_to_validate;
             ++index) {
            if (entries[index].struct_size <
                sizeof(vrrec_spout_sender_snapshot_v1)) {
                return VRREC_STATUS_INVALID_ARGUMENT;
            }

            if (entries[index].abi_version != VRREC_ABI_V1) {
                return VRREC_STATUS_UNSUPPORTED_ABI;
            }
        }

        if (entry_capacity < required_entry_count ||
            utf8_capacity < required_utf8_size) {
            return VRREC_STATUS_BUFFER_TOO_SMALL;
        }

        std::uint32_t offset = 0;
        for (std::uint32_t index = 0;
             index < required_entry_count;
             ++index) {
            const auto &sender = senders[index];
            const auto sender_size =
                static_cast<std::uint32_t>(sender.sender_id.size());
            sender.sender_id.copy(utf8_buffer + offset, sender_size);
            entries[index] = vrrec_spout_sender_snapshot_v1 {
                sizeof(vrrec_spout_sender_snapshot_v1),
                VRREC_ABI_V1,
                offset,
                sender_size,
                sender.latest_frame_generation,
            };
            offset += sender_size;
        }

        return VRREC_STATUS_OK;
    } catch (const std::bad_alloc &) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

extern "C" VRREC_API vrrec_status_t VRREC_CALL
vrrec_spout_source_poll_frame_v1(
    vrrec_spout_source_t *source,
    std::uint32_t timeout_milliseconds,
    vrrec_spout_frame_v1 *out_frame,
    char *utf8_buffer,
    std::uint32_t utf8_capacity,
    std::uint32_t *out_required_utf8_size)
{
    const auto validation = ValidateSpoutFrameArguments(
        source,
        timeout_milliseconds,
        out_frame,
        utf8_buffer,
        utf8_capacity,
        out_required_utf8_size);
    if (validation != VRREC_STATUS_OK) {
        return validation;
    }

    *out_required_utf8_size = 0;
    SpoutOperationLease operation(*source);
    if (!operation) {
        return VRREC_STATUS_INVALID_STATE;
    }

    try {
        const std::lock_guard lock(source->operation_mutex_);
        if (!source->pending_frame_.has_value()) {
            vrrecorder::native::SpoutFrame polled_frame;
            const auto status = source->backend_->Poll(
                std::chrono::milliseconds(timeout_milliseconds),
                polled_frame);
            if (status != VRREC_STATUS_OK) {
                return status;
            }

            std::uint32_t ignored_required_size = 0;
            if (!TryValidateSpoutFrame(
                    polled_frame,
                    ignored_required_size)) {
                return VRREC_STATUS_INTERNAL_ERROR;
            }

            source->pending_frame_ = std::move(polled_frame);
        }

        const auto &frame = *source->pending_frame_;
        std::uint32_t required_utf8_size = 0;
        if (!TryValidateSpoutFrame(frame, required_utf8_size)) {
            source->pending_frame_.reset();
            return VRREC_STATUS_INTERNAL_ERROR;
        }

        *out_required_utf8_size = required_utf8_size;
        if (utf8_capacity < required_utf8_size) {
            return VRREC_STATUS_BUFFER_TOO_SMALL;
        }

        const auto sender_size =
            static_cast<std::uint32_t>(frame.sender_id.size());
        const auto gpu_identity_size =
            static_cast<std::uint32_t>(frame.gpu_identity.size());
        frame.sender_id.copy(utf8_buffer, sender_size);
        frame.gpu_identity.copy(
            utf8_buffer + sender_size,
            gpu_identity_size);
        *out_frame = vrrec_spout_frame_v1 {
            sizeof(vrrec_spout_frame_v1),
            VRREC_ABI_V1,
            0,
            sender_size,
            sender_size,
            gpu_identity_size,
            frame.adapter_luid,
            frame.gpu_vendor,
            frame.width,
            frame.height,
            frame.pixel_format,
            frame.estimated_source_fps,
            frame.frame_sequence,
            frame.monotonic_timestamp_microseconds,
            0,
        };
        source->pending_frame_.reset();
        return VRREC_STATUS_OK;
    } catch (const std::bad_alloc &) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

extern "C" VRREC_API void VRREC_CALL vrrec_spout_source_destroy_v1(
    vrrec_spout_source_t **source)
{
    if (source == nullptr || *source == nullptr) {
        return;
    }

    auto *owned_source = *source;
    *source = nullptr;
    try {
        owned_source->JoinOperationsForDestroy();
        delete owned_source;
    } catch (...) {
        // Destruction is the final fail-safe and cannot report across C ABI.
    }
}
