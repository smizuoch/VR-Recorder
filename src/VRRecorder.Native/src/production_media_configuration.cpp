#include "production_media_configuration.hpp"

#include <cmath>
#include <cstddef>
#include <cstdint>
#include <string_view>

namespace vrrecorder::native {
namespace {

constexpr std::uint32_t MinimumFramesPerSecond = 30;
constexpr std::uint32_t MaximumFramesPerSecond = 120;
constexpr std::uint32_t MaximumVideoDimension = 16'384;
constexpr double MinimumGainDb = -96.0;
constexpr double MaximumGainDb = 24.0;
constexpr double MaximumEstimatedSourceFps = 1'000.0;

bool HasText(const char *value) noexcept
{
    return value != nullptr && value[0] != '\0';
}

bool IsAbsoluteWindowsPath(const char *value) noexcept
{
    if (!HasText(value)) {
        return false;
    }
    const std::string_view path(value);
    const auto drive_letter =
        path.size() >= 3 &&
        ((path[0] >= 'A' && path[0] <= 'Z') ||
         (path[0] >= 'a' && path[0] <= 'z')) &&
        path[1] == ':' &&
        (path[2] == '\\' || path[2] == '/');
    const auto unc = path.size() >= 3 && path[0] == '\\' &&
        path[1] == '\\';
    return drive_letter || unc;
}

bool IsAudioRoutingValid(vrrec_audio_routing_t value) noexcept
{
    return value == VRREC_AUDIO_ROUTING_MIXED ||
        value == VRREC_AUDIO_ROUTING_DESKTOP_ONLY ||
        value == VRREC_AUDIO_ROUTING_MIC_ONLY ||
        value == VRREC_AUDIO_ROUTING_MUTED;
}

bool IsQualityPresetValid(vrrec_quality_preset_t value) noexcept
{
    return value == VRREC_QUALITY_PRESET_STANDARD ||
        value == VRREC_QUALITY_PRESET_HIGH;
}

bool IsSourcePixelFormatValid(
    vrrec_source_pixel_format_t value) noexcept
{
    return value == VRREC_SOURCE_PIXEL_FORMAT_BGRA8 ||
        value == VRREC_SOURCE_PIXEL_FORMAT_RGBA8 ||
        value == VRREC_SOURCE_PIXEL_FORMAT_NV12;
}

bool IsGainValid(double value) noexcept
{
    return std::isfinite(value) && value >= MinimumGainDb &&
        value <= MaximumGainDb;
}

bool IsGeometryValid(const vrrec_session_config_v1 &input) noexcept
{
    return input.width != 0 && input.height != 0 &&
        input.width <= MaximumVideoDimension &&
        input.height <= MaximumVideoDimension &&
        (input.width & 1U) == 0 && (input.height & 1U) == 0 &&
        input.source_width != 0 && input.source_height != 0 &&
        input.source_width <= MaximumVideoDimension &&
        input.source_height <= MaximumVideoDimension &&
        input.destination_width != 0 &&
        input.destination_height != 0 &&
        (input.destination_width & 1U) == 0 &&
        (input.destination_height & 1U) == 0 &&
        input.destination_x <= input.width &&
        input.destination_y <= input.height &&
        input.destination_width <= input.width - input.destination_x &&
        input.destination_height <= input.height - input.destination_y;
}

}

vrrec_status_t ValidateProductionMediaConfiguration(
    const vrrec_session_config_v1 &input,
    ProductionMediaConfiguration &output) noexcept
{
    output = {};
    if (input.struct_size < sizeof(vrrec_session_config_v1)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }
    if (input.abi_version != VRREC_ABI_V1) {
        return VRREC_STATUS_UNSUPPORTED_ABI;
    }
    ProductionVideoEncoderRoute encoder_route {};
    const auto encoder_status = ResolveProductionVideoEncoderRoute(
        input.encoder_kind,
        input.spout_adapter_luid,
        input.encoder_adapter_luid,
        encoder_route);
    if (encoder_status != VRREC_STATUS_OK) {
        return encoder_status;
    }
    if (input.fps_denominator != 1 ||
        input.fps_numerator < MinimumFramesPerSecond ||
        input.fps_numerator > MaximumFramesPerSecond ||
        !IsGeometryValid(input) ||
        input.canvas_background != VRREC_CANVAS_BACKGROUND_BLACK ||
        input.rotation != VRREC_VIDEO_ROTATION_NONE ||
        !IsAudioRoutingValid(input.audio_routing) ||
        !IsQualityPresetValid(input.quality_preset) ||
        !IsGainValid(input.desktop_gain_db) ||
        !IsGainValid(input.microphone_gain_db) ||
        !IsSourcePixelFormatValid(input.source_pixel_format) ||
        !std::isfinite(input.estimated_source_fps) ||
        input.estimated_source_fps <= 0.0 ||
        input.estimated_source_fps > MaximumEstimatedSourceFps ||
        !IsAbsoluteWindowsPath(input.temporary_output_path_utf8) ||
        !HasText(input.desktop_endpoint_id_utf8) ||
        !HasText(input.microphone_endpoint_id_utf8) ||
        !HasText(input.spout_sender_identity_utf8) ||
        !HasText(input.gpu_identity_utf8) ||
        input.reserved != 0 || input.reserved_v1 != 0 ||
        input.reserved_v2 != 0) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    output = {
        input.fps_numerator,
        encoder_route,
        {
            sizeof(vrrec_video_layout_v1),
            VRREC_ABI_V1,
            input.source_width,
            input.source_height,
            input.width,
            input.height,
            input.destination_x,
            input.destination_y,
            input.destination_width,
            input.destination_height,
            input.canvas_background,
            input.rotation,
        },
    };
    return VRREC_STATUS_OK;
}

}
