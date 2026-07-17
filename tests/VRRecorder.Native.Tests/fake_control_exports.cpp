#include <cstdint>

#include "fake_media_backend.hpp"

#if defined(_WIN32)
#define VRREC_TEST_API __declspec(dllexport)
#else
#define VRREC_TEST_API __attribute__((visibility("default")))
#endif

typedef struct vrrec_test_media_config_v1 {
    std::uint32_t canvas_width;
    std::uint32_t canvas_height;
    std::uint32_t source_width;
    std::uint32_t source_height;
    std::uint32_t destination_x;
    std::uint32_t destination_y;
    std::uint32_t destination_width;
    std::uint32_t destination_height;
    std::uint32_t canvas_background;
    std::uint32_t rotation;
    std::uint32_t audio_routing;
    std::uint32_t quality_preset;
    const char *desktop_endpoint_id_utf8;
    const char *microphone_endpoint_id_utf8;
    double desktop_gain_db;
    double microphone_gain_db;
    const char *spout_sender_identity_utf8;
    std::uint64_t spout_adapter_luid;
    std::uint64_t encoder_adapter_luid;
    const char *gpu_identity_utf8;
    std::uint32_t source_pixel_format;
    double estimated_source_fps;
} vrrec_test_media_config_v1;

typedef struct vrrec_test_video_layout_v1 {
    std::uint32_t source_width;
    std::uint32_t source_height;
    std::uint32_t canvas_width;
    std::uint32_t canvas_height;
    std::uint32_t destination_x;
    std::uint32_t destination_y;
    std::uint32_t destination_width;
    std::uint32_t destination_height;
    std::uint32_t canvas_background;
    std::uint32_t rotation;
} vrrec_test_video_layout_v1;

typedef struct vrrec_test_encoder_probe_config_v1 {
    std::uint32_t encoder_kind;
    std::uint32_t synthetic_frame_count;
    std::uint64_t adapter_luid;
    std::uint32_t width;
    std::uint32_t height;
    std::uint32_t fps_numerator;
    std::uint32_t fps_denominator;
    const char *gpu_identity_utf8;
} vrrec_test_encoder_probe_config_v1;

extern "C" VRREC_TEST_API void vrrec_test_commit_muxed_video_packet(void)
{
    vrrecorder::native::testing::CommitMuxedVideoPacket();
}

extern "C" VRREC_TEST_API void vrrec_test_complete_trailer_flush_close(
    std::uint64_t video_packet_count,
    std::uint64_t audio_packet_count)
{
    vrrecorder::native::testing::CompleteTrailerFlushClose(
        video_packet_count,
        audio_packet_count);
}

extern "C" VRREC_TEST_API void vrrec_test_fail(
    std::int32_t status,
    const char *message_utf8)
{
    vrrecorder::native::testing::Fail(
        status,
        message_utf8 == nullptr ? "" : message_utf8);
}

extern "C" VRREC_TEST_API void vrrec_test_emit_video_encoder_failed(
    std::int32_t status,
    const char *message_utf8)
{
    vrrecorder::native::testing::EmitVideoEncoderFailed(
        status,
        message_utf8 == nullptr ? "" : message_utf8);
}

extern "C" VRREC_TEST_API void vrrec_test_emit_av_drift(
    std::uint64_t video_pts_microseconds,
    std::uint64_t audio_pts_microseconds)
{
    vrrecorder::native::testing::EmitAvDrift(
        video_pts_microseconds,
        audio_pts_microseconds);
}

extern "C" VRREC_TEST_API void vrrec_test_emit_audio_buffer_health(
    std::uint32_t role,
    std::uint32_t health,
    std::uint64_t frame_position)
{
    vrrecorder::native::testing::EmitAudioBufferHealth(
        role == 0
            ? vrrecorder::native::AudioEndpointRole::Desktop
            : vrrecorder::native::AudioEndpointRole::Microphone,
        health == 0
            ? vrrecorder::native::AudioBufferHealth::Underrun
            : vrrecorder::native::AudioBufferHealth::Overrun,
        frame_position);
}

extern "C" VRREC_TEST_API void
vrrec_test_set_desktop_audio_endpoint_available(
    std::uint8_t available,
    std::uint64_t frame_position)
{
    vrrecorder::native::testing::SetDesktopAudioEndpointAvailable(
        available != 0,
        frame_position);
}

extern "C" VRREC_TEST_API void
vrrec_test_set_microphone_audio_endpoint_available(
    std::uint8_t available,
    std::uint64_t frame_position)
{
    vrrecorder::native::testing::SetMicrophoneAudioEndpointAvailable(
        available != 0,
        frame_position);
}

extern "C" VRREC_TEST_API std::uint32_t vrrec_test_encoder_kind(void)
{
    return vrrecorder::native::testing::EncoderKind();
}

extern "C" VRREC_TEST_API std::uint32_t vrrec_test_audio_routing(void)
{
    return vrrecorder::native::testing::AudioRouting();
}

extern "C" VRREC_TEST_API std::uint32_t
vrrec_test_audio_routing_update_count(void)
{
    return vrrecorder::native::testing::AudioRoutingUpdateCount();
}

extern "C" VRREC_TEST_API void vrrec_test_copy_media_config_v1(
    vrrec_test_media_config_v1 *out_config)
{
    if (out_config == nullptr) {
        return;
    }

    const auto &config = vrrecorder::native::testing::SessionConfig();
    *out_config = vrrec_test_media_config_v1 {
        config.canvas_width,
        config.canvas_height,
        config.source_width,
        config.source_height,
        config.destination_x,
        config.destination_y,
        config.destination_width,
        config.destination_height,
        config.canvas_background,
        config.rotation,
        config.audio_routing,
        config.quality_preset,
        config.desktop_endpoint_id.c_str(),
        config.microphone_endpoint_id.c_str(),
        config.desktop_gain_db,
        config.microphone_gain_db,
        config.spout_sender_identity.c_str(),
        config.spout_adapter_luid,
        config.encoder_adapter_luid,
        config.gpu_identity.c_str(),
        config.source_pixel_format,
        config.estimated_source_fps,
    };
}

extern "C" VRREC_TEST_API void vrrec_test_copy_video_layout_v1(
    vrrec_test_video_layout_v1 *out_layout)
{
    if (out_layout == nullptr) {
        return;
    }

    const auto &layout = vrrecorder::native::testing::VideoLayout();
    *out_layout = vrrec_test_video_layout_v1 {
        layout.source_width,
        layout.source_height,
        layout.canvas_width,
        layout.canvas_height,
        layout.destination_x,
        layout.destination_y,
        layout.destination_width,
        layout.destination_height,
        layout.canvas_background,
        layout.rotation,
    };
}

extern "C" VRREC_TEST_API void vrrec_test_encoder_probe_reset(void)
{
    vrrecorder::native::testing::ResetEncoderProbe();
}

extern "C" VRREC_TEST_API void vrrec_test_encoder_probe_set_result(
    std::int32_t status,
    std::uint8_t packet_produced)
{
    vrrecorder::native::testing::SetEncoderProbeResult(
        status,
        packet_produced != 0);
}

extern "C" VRREC_TEST_API void vrrec_test_encoder_probe_set_evidence_v2(
    std::uint32_t actual_encoder_kind,
    std::uint8_t hardware_accelerated,
    std::uint64_t adapter_luid,
    std::uint32_t opened_input_format,
    std::uint32_t width,
    std::uint32_t height,
    std::uint32_t fps_numerator,
    std::uint32_t fps_denominator,
    std::uint32_t validation_flags,
    const char *codec_name_utf8,
    const char *driver_identity_utf8,
    const char *ffmpeg_build_identity_utf8,
    const char *profile_utf8,
    const char *device_identity_utf8)
{
    const auto text = [](const char *value) {
        return value == nullptr ? "" : value;
    };
    vrrecorder::native::testing::SetEncoderProbeEvidence(
        vrrecorder::native::testing::TestEncoderProbeEvidence {
            actual_encoder_kind,
            hardware_accelerated != 0,
            adapter_luid,
            opened_input_format,
            width,
            height,
            fps_numerator,
            fps_denominator,
            validation_flags,
            text(codec_name_utf8),
            text(driver_identity_utf8),
            text(ffmpeg_build_identity_utf8),
            text(profile_utf8),
            text(device_identity_utf8),
        });
}

extern "C" VRREC_TEST_API std::uint32_t
vrrec_test_encoder_probe_call_count(void)
{
    return vrrecorder::native::testing::EncoderProbeCallCount();
}

extern "C" VRREC_TEST_API void
vrrec_test_encoder_probe_copy_config_v1(
    vrrec_test_encoder_probe_config_v1 *out_config)
{
    if (out_config == nullptr) {
        return;
    }

    static thread_local std::string gpu_identity;
    const auto config = vrrecorder::native::testing::EncoderProbeConfig();
    gpu_identity = config.gpu_identity;
    *out_config = vrrec_test_encoder_probe_config_v1 {
        config.encoder_kind,
        config.synthetic_frame_count,
        config.adapter_luid,
        config.width,
        config.height,
        config.fps_numerator,
        config.fps_denominator,
        gpu_identity.c_str(),
    };
}

extern "C" VRREC_TEST_API void vrrec_test_encoder_probe_block_next(void)
{
    vrrecorder::native::testing::BlockNextEncoderProbe();
}

extern "C" VRREC_TEST_API int vrrec_test_encoder_probe_wait_until_entered(
    std::uint32_t milliseconds)
{
    return vrrecorder::native::testing::WaitUntilEncoderProbeEntered(
        std::chrono::milliseconds(milliseconds));
}

extern "C" VRREC_TEST_API void vrrec_test_encoder_probe_release(void)
{
    vrrecorder::native::testing::ReleaseEncoderProbe();
}

extern "C" VRREC_TEST_API void vrrec_test_set_statistics_v1(
    std::uint64_t source_video_frame_count,
    std::uint64_t muxed_video_packet_count,
    std::uint64_t muxed_audio_packet_count,
    std::uint64_t dropped_source_video_frame_count,
    std::uint64_t duplicated_output_video_frame_count,
    std::uint64_t latest_encode_latency_microseconds,
    std::uint64_t maximum_encode_latency_microseconds,
    std::int64_t audio_video_offset_microseconds)
{
    vrrecorder::native::testing::SetStatistics(
        vrrec_session_statistics_v1 {
            sizeof(vrrec_session_statistics_v1),
            VRREC_ABI_V1,
            source_video_frame_count,
            muxed_video_packet_count,
            muxed_audio_packet_count,
            dropped_source_video_frame_count,
            duplicated_output_video_frame_count,
            latest_encode_latency_microseconds,
            maximum_encode_latency_microseconds,
            audio_video_offset_microseconds,
        });
}

extern "C" VRREC_TEST_API void vrrec_test_set_statistics_status(
    std::int32_t status)
{
    vrrecorder::native::testing::SetStatisticsStatus(status);
}

extern "C" VRREC_TEST_API void vrrec_test_set_steamvr_digital_state(
    std::uint8_t is_active,
    std::uint8_t state,
    std::uint8_t changed)
{
    vrrecorder::native::testing::SetSteamVrDigitalState(
        is_active != 0,
        state != 0,
        changed != 0);
}

extern "C" VRREC_TEST_API std::uint8_t vrrec_test_steamvr_input_active(void)
{
    return vrrecorder::native::testing::HasActiveSteamVrInput() ? 1 : 0;
}

extern "C" VRREC_TEST_API const char *vrrec_test_steamvr_manifest_path(void)
{
    return vrrecorder::native::testing::SteamVrManifestPath().data();
}

extern "C" VRREC_TEST_API const char *vrrec_test_steamvr_action_set_path(void)
{
    return vrrecorder::native::testing::SteamVrActionSetPath().data();
}

extern "C" VRREC_TEST_API const char *vrrec_test_steamvr_action_path(void)
{
    return vrrecorder::native::testing::SteamVrDigitalActionPath().data();
}

extern "C" VRREC_TEST_API std::uint32_t vrrec_test_steamvr_poll_count(void)
{
    return vrrecorder::native::testing::SteamVrPollCount();
}

extern "C" VRREC_TEST_API void vrrec_test_steamvr_haptic_set_status(
    std::int32_t status)
{
    vrrecorder::native::testing::SetSteamVrHapticStatus(status);
}

extern "C" VRREC_TEST_API std::uint8_t
vrrec_test_steamvr_haptic_active(void)
{
    return vrrecorder::native::testing::HasActiveSteamVrHaptic() ? 1 : 0;
}

extern "C" VRREC_TEST_API const char *
vrrec_test_steamvr_haptic_manifest_path(void)
{
    return vrrecorder::native::testing::SteamVrHapticManifestPath().data();
}

extern "C" VRREC_TEST_API const char *
vrrec_test_steamvr_haptic_action_path(void)
{
    return vrrecorder::native::testing::SteamVrHapticActionPath().data();
}

extern "C" VRREC_TEST_API const char *
vrrec_test_steamvr_haptic_input_source_path(void)
{
    return vrrecorder::native::testing::SteamVrHapticInputSourcePath().data();
}

extern "C" VRREC_TEST_API std::uint32_t
vrrec_test_steamvr_haptic_trigger_count(void)
{
    return vrrecorder::native::testing::SteamVrHapticTriggerCount();
}

extern "C" VRREC_TEST_API float
vrrec_test_steamvr_haptic_last_duration_seconds(void)
{
    return vrrecorder::native::testing::SteamVrLastHapticPulse()
        .duration_seconds;
}

extern "C" VRREC_TEST_API float
vrrec_test_steamvr_haptic_last_frequency_hertz(void)
{
    return vrrecorder::native::testing::SteamVrLastHapticPulse()
        .frequency_hertz;
}

extern "C" VRREC_TEST_API float
vrrec_test_steamvr_haptic_last_amplitude(void)
{
    return vrrecorder::native::testing::SteamVrLastHapticPulse().amplitude;
}

extern "C" VRREC_TEST_API void vrrec_test_steamvr_overlay_reset(void)
{
    vrrecorder::native::testing::ResetSteamVrOverlay();
}

extern "C" VRREC_TEST_API std::uint8_t
vrrec_test_steamvr_overlay_active(void)
{
    return vrrecorder::native::testing::HasActiveSteamVrOverlay() ? 1 : 0;
}

extern "C" VRREC_TEST_API std::uint8_t
vrrec_test_steamvr_overlay_visible(void)
{
    return vrrecorder::native::testing::IsSteamVrOverlayVisible() ? 1 : 0;
}

extern "C" VRREC_TEST_API const char *
vrrec_test_steamvr_overlay_manifest_path(void)
{
    return vrrecorder::native::testing::SteamVrOverlayManifestPath().data();
}

extern "C" VRREC_TEST_API const char *vrrec_test_steamvr_overlay_key(void)
{
    return vrrecorder::native::testing::SteamVrOverlayKey().data();
}

extern "C" VRREC_TEST_API const char *vrrec_test_steamvr_overlay_name(void)
{
    return vrrecorder::native::testing::SteamVrOverlayName().data();
}

extern "C" VRREC_TEST_API float
vrrec_test_steamvr_overlay_width_in_meters(void)
{
    return vrrecorder::native::testing::SteamVrOverlayWidthInMeters();
}

extern "C" VRREC_TEST_API std::uint32_t
vrrec_test_steamvr_overlay_show_count(void)
{
    return vrrecorder::native::testing::SteamVrOverlayShowCount();
}

extern "C" VRREC_TEST_API std::uint32_t
vrrec_test_steamvr_overlay_hide_count(void)
{
    return vrrecorder::native::testing::SteamVrOverlayHideCount();
}

extern "C" VRREC_TEST_API std::uint32_t
vrrec_test_steamvr_overlay_close_count(void)
{
    return vrrecorder::native::testing::SteamVrOverlayCloseCount();
}

extern "C" VRREC_TEST_API std::uint32_t
vrrec_test_steamvr_overlay_destroy_count(void)
{
    return vrrecorder::native::testing::SteamVrOverlayDestroyCount();
}

extern "C" VRREC_TEST_API std::uint32_t
vrrec_test_steamvr_overlay_texture_update_count(void)
{
    return vrrecorder::native::testing::SteamVrOverlayTextureUpdateCount();
}

extern "C" VRREC_TEST_API std::uint32_t
vrrec_test_steamvr_overlay_clear_texture_count(void)
{
    return vrrecorder::native::testing::SteamVrOverlayClearTextureCount();
}

extern "C" VRREC_TEST_API std::uint8_t
vrrec_test_steamvr_overlay_texture_first_byte(void)
{
    return vrrecorder::native::testing::SteamVrOverlayTextureFirstByte();
}

extern "C" VRREC_TEST_API std::uint8_t
vrrec_test_steamvr_overlay_texture_last_byte(void)
{
    return vrrecorder::native::testing::SteamVrOverlayTextureLastByte();
}

extern "C" VRREC_TEST_API void
vrrec_test_steamvr_overlay_push_pointer_event(
    std::uint32_t kind,
    std::uint32_t pixel_x,
    std::uint32_t pixel_y,
    std::uint32_t button,
    std::uint32_t cursor_index)
{
    vrrecorder::native::testing::PushSteamVrOverlayPointerEvent(
        vrrecorder::native::testing::TestSteamVrOverlayPointerEvent {
            kind,
            pixel_x,
            pixel_y,
            button,
            cursor_index,
        });
}

extern "C" VRREC_TEST_API void vrrec_test_spout_reset(void)
{
    vrrecorder::native::testing::ResetSpoutSource();
}

extern "C" VRREC_TEST_API void vrrec_test_spout_add_snapshot_sender(
    const char *sender_id_utf8,
    std::uint64_t latest_frame_generation)
{
    vrrecorder::native::testing::AddSpoutSnapshotSender(
        vrrecorder::native::testing::TestSpoutSenderSnapshot {
            sender_id_utf8 == nullptr ? "" : sender_id_utf8,
            latest_frame_generation,
        });
}

extern "C" VRREC_TEST_API void vrrec_test_spout_push_frame(
    const char *sender_id_utf8,
    std::uint64_t adapter_luid,
    const char *gpu_identity_utf8,
    std::uint32_t gpu_vendor,
    std::uint32_t width,
    std::uint32_t height,
    std::uint32_t pixel_format,
    double estimated_source_fps,
    std::uint64_t frame_sequence,
    std::int64_t monotonic_timestamp_microseconds)
{
    vrrecorder::native::testing::PushSpoutFrame(
        vrrecorder::native::testing::TestSpoutFrame {
            sender_id_utf8 == nullptr ? "" : sender_id_utf8,
            adapter_luid,
            gpu_identity_utf8 == nullptr ? "" : gpu_identity_utf8,
            gpu_vendor,
            width,
            height,
            pixel_format,
            estimated_source_fps,
            frame_sequence,
            monotonic_timestamp_microseconds,
        });
}

extern "C" VRREC_TEST_API void vrrec_test_spout_block_next_poll(void)
{
    vrrecorder::native::testing::BlockNextSpoutPoll();
}

extern "C" VRREC_TEST_API int vrrec_test_spout_wait_until_poll_entered(
    std::uint32_t milliseconds)
{
    return vrrecorder::native::testing::WaitUntilSpoutPollEntered(
        std::chrono::milliseconds(milliseconds)) ? 1 : 0;
}

extern "C" VRREC_TEST_API void vrrec_test_spout_release_poll(void)
{
    vrrecorder::native::testing::ReleaseSpoutPoll();
}

extern "C" VRREC_TEST_API std::uint32_t
vrrec_test_spout_active_source_count(void)
{
    return vrrecorder::native::testing::ActiveSpoutSourceCount();
}

extern "C" VRREC_TEST_API std::uint32_t vrrec_test_spout_destroy_count(void)
{
    return vrrecorder::native::testing::SpoutSourceDestroyCount();
}
