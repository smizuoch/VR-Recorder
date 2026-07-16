#ifndef VRRECORDER_NATIVE_H
#define VRRECORDER_NATIVE_H

#include <stdint.h>

#if defined(_WIN32)
#define VRREC_CALL __cdecl
#if defined(VRRECORDER_NATIVE_EXPORTS)
#define VRREC_API __declspec(dllexport)
#else
#define VRREC_API __declspec(dllimport)
#endif
#else
#define VRREC_CALL
#define VRREC_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define VRREC_ABI_V1 UINT32_C(1)

typedef struct vrrec_session vrrec_session_t;
typedef struct vrrec_steamvr_input vrrec_steamvr_input_t;
typedef struct vrrec_steamvr_haptic vrrec_steamvr_haptic_t;
typedef struct vrrec_steamvr_overlay vrrec_steamvr_overlay_t;
typedef struct vrrec_spout_source vrrec_spout_source_t;
typedef int32_t vrrec_status_t;
typedef uint32_t vrrec_event_kind_t;
typedef uint32_t vrrec_encoder_kind_t;
typedef uint32_t vrrec_canvas_background_t;
typedef uint32_t vrrec_video_rotation_t;
typedef uint32_t vrrec_source_pixel_format_t;
typedef uint32_t vrrec_audio_routing_t;
typedef uint32_t vrrec_quality_preset_t;
typedef uint32_t vrrec_gpu_vendor_t;
typedef uint32_t vrrec_encoder_input_format_t;
typedef uint32_t vrrec_steamvr_overlay_pointer_event_kind_t;
typedef uint32_t vrrec_steamvr_overlay_placement_mode_t;
typedef uint32_t vrrec_steamvr_hand_t;
typedef uint32_t vrrec_steamvr_tracking_origin_t;

#define VRREC_STATUS_OK INT32_C(0)
#define VRREC_STATUS_INVALID_ARGUMENT INT32_C(1)
#define VRREC_STATUS_UNSUPPORTED_ABI INT32_C(2)
#define VRREC_STATUS_INVALID_STATE INT32_C(3)
#define VRREC_STATUS_BACKEND_UNAVAILABLE INT32_C(4)
#define VRREC_STATUS_OUT_OF_MEMORY INT32_C(5)
#define VRREC_STATUS_INTERNAL_ERROR INT32_C(6)
#define VRREC_STATUS_BUFFER_TOO_SMALL INT32_C(7)
#define VRREC_STATUS_TIMEOUT INT32_C(8)

#define VRREC_STEAMVR_OVERLAY_POINTER_MOVE UINT32_C(1)
#define VRREC_STEAMVR_OVERLAY_POINTER_BUTTON_DOWN UINT32_C(2)
#define VRREC_STEAMVR_OVERLAY_POINTER_BUTTON_UP UINT32_C(3)
#define VRREC_STEAMVR_OVERLAY_POINTER_BUTTON_LEFT UINT32_C(1)
#define VRREC_STEAMVR_OVERLAY_POINTER_BUTTON_RIGHT UINT32_C(2)
#define VRREC_STEAMVR_OVERLAY_POINTER_BUTTON_MIDDLE UINT32_C(4)

#define VRREC_STEAMVR_OVERLAY_PLACEMENT_WRIST_DOCK UINT32_C(1)
#define VRREC_STEAMVR_OVERLAY_PLACEMENT_WORLD_PIN UINT32_C(2)
#define VRREC_STEAMVR_HAND_NONE UINT32_C(0)
#define VRREC_STEAMVR_HAND_LEFT UINT32_C(1)
#define VRREC_STEAMVR_HAND_RIGHT UINT32_C(2)
#define VRREC_STEAMVR_TRACKING_ORIGIN_NONE UINT32_C(0)
#define VRREC_STEAMVR_TRACKING_ORIGIN_STANDING UINT32_C(1)

#define VRREC_EVENT_FIRST_VIDEO_PACKET_MUXED UINT32_C(1)
#define VRREC_EVENT_STOPPED UINT32_C(2)
#define VRREC_EVENT_FAULTED UINT32_C(3)
#define VRREC_EVENT_DESKTOP_AUDIO_DEVICE_LOST UINT32_C(4)
#define VRREC_EVENT_DESKTOP_AUDIO_DEVICE_RECOVERED UINT32_C(5)
#define VRREC_EVENT_MICROPHONE_AUDIO_DEVICE_LOST UINT32_C(6)
#define VRREC_EVENT_MICROPHONE_AUDIO_DEVICE_RECOVERED UINT32_C(7)
#define VRREC_EVENT_AUDIO_VIDEO_DRIFT_EXCEEDED UINT32_C(8)
#define VRREC_EVENT_DESKTOP_AUDIO_BUFFER_UNDERRUN UINT32_C(9)
#define VRREC_EVENT_DESKTOP_AUDIO_BUFFER_OVERRUN UINT32_C(10)
#define VRREC_EVENT_MICROPHONE_AUDIO_BUFFER_UNDERRUN UINT32_C(11)
#define VRREC_EVENT_MICROPHONE_AUDIO_BUFFER_OVERRUN UINT32_C(12)

#define VRREC_ENCODER_NVENC UINT32_C(1)
#define VRREC_ENCODER_AMF UINT32_C(2)
#define VRREC_ENCODER_QSV UINT32_C(3)
#define VRREC_ENCODER_MEDIA_FOUNDATION_SOFTWARE UINT32_C(4)

#define VRREC_ENCODER_INPUT_SYSTEM_MEMORY_NV12 UINT32_C(1)
#define VRREC_ENCODER_INPUT_D3D11_NV12 UINT32_C(2)
#define VRREC_ENCODER_INPUT_QSV_NV12 UINT32_C(3)

#define VRREC_ENCODER_PROBE_VALIDATION_NONEMPTY_PACKET UINT32_C(0x0001)
#define VRREC_ENCODER_PROBE_VALIDATION_PARSEABLE_ACCESS_UNIT UINT32_C(0x0002)
#define VRREC_ENCODER_PROBE_VALIDATION_SPS UINT32_C(0x0004)
#define VRREC_ENCODER_PROBE_VALIDATION_PPS UINT32_C(0x0008)
#define VRREC_ENCODER_PROBE_VALIDATION_IDR UINT32_C(0x0010)
#define VRREC_ENCODER_PROBE_VALIDATION_DISPLAY_DIMENSIONS UINT32_C(0x0020)
#define VRREC_ENCODER_PROBE_VALIDATION_PROFILE UINT32_C(0x0040)
#define VRREC_ENCODER_PROBE_VALIDATION_FRAME_RATE UINT32_C(0x0080)
#define VRREC_ENCODER_PROBE_VALIDATION_ZERO_B_FRAMES UINT32_C(0x0100)
#define VRREC_ENCODER_PROBE_VALIDATION_DECODED UINT32_C(0x0200)
#define VRREC_ENCODER_PROBE_VALIDATION_SAME_ADAPTER UINT32_C(0x0400)

#define VRREC_ENCODER_PROBE_MAX_UTF8_BUFFER_SIZE UINT32_C(32768)

#define VRREC_CANVAS_BACKGROUND_BLACK UINT32_C(1)

#define VRREC_VIDEO_ROTATION_NONE UINT32_C(1)

#define VRREC_SOURCE_PIXEL_FORMAT_BGRA8 UINT32_C(1)
#define VRREC_SOURCE_PIXEL_FORMAT_RGBA8 UINT32_C(2)
#define VRREC_SOURCE_PIXEL_FORMAT_NV12 UINT32_C(3)

#define VRREC_GPU_VENDOR_UNKNOWN UINT32_C(0)
#define VRREC_GPU_VENDOR_NVIDIA UINT32_C(1)
#define VRREC_GPU_VENDOR_AMD UINT32_C(2)
#define VRREC_GPU_VENDOR_INTEL UINT32_C(3)

#define VRREC_SPOUT_MAX_IDENTITY_UTF8_SIZE UINT32_C(4096)
#define VRREC_SPOUT_MAX_SNAPSHOT_ENTRIES UINT32_C(1024)
#define VRREC_SPOUT_MAX_UTF8_BUFFER_SIZE UINT32_C(1048576)
#define VRREC_SPOUT_MAX_POLL_TIMEOUT_MILLISECONDS UINT32_C(1000)

#define VRREC_AUDIO_ROUTING_MIXED UINT32_C(1)
#define VRREC_AUDIO_ROUTING_DESKTOP_ONLY UINT32_C(2)
#define VRREC_AUDIO_ROUTING_MIC_ONLY UINT32_C(3)
#define VRREC_AUDIO_ROUTING_MUTED UINT32_C(4)

#define VRREC_QUALITY_PRESET_STANDARD UINT32_C(1)
#define VRREC_QUALITY_PRESET_HIGH UINT32_C(2)

typedef struct vrrec_session_config_v1 {
    uint32_t struct_size;
    uint32_t abi_version;
    const char *temporary_output_path_utf8;
    uint32_t width;
    uint32_t height;
    uint32_t fps_numerator;
    uint32_t fps_denominator;
    int64_t started_at_unix_milliseconds_utc;
    vrrec_encoder_kind_t encoder_kind;
    uint32_t reserved;
    uint32_t source_width;
    uint32_t source_height;
    uint32_t destination_x;
    uint32_t destination_y;
    uint32_t destination_width;
    uint32_t destination_height;
    vrrec_canvas_background_t canvas_background;
    vrrec_video_rotation_t rotation;
    vrrec_audio_routing_t audio_routing;
    vrrec_quality_preset_t quality_preset;
    const char *desktop_endpoint_id_utf8;
    const char *microphone_endpoint_id_utf8;
    double desktop_gain_db;
    double microphone_gain_db;
    const char *spout_sender_identity_utf8;
    uint64_t spout_adapter_luid;
    uint64_t encoder_adapter_luid;
    const char *gpu_identity_utf8;
    uint64_t reserved_v1;
    vrrec_source_pixel_format_t source_pixel_format;
    uint32_t reserved_v2;
    double estimated_source_fps;
} vrrec_session_config_v1;

typedef struct vrrec_video_layout_v1 {
    uint32_t struct_size;
    uint32_t abi_version;
    uint32_t source_width;
    uint32_t source_height;
    uint32_t canvas_width;
    uint32_t canvas_height;
    uint32_t destination_x;
    uint32_t destination_y;
    uint32_t destination_width;
    uint32_t destination_height;
    vrrec_canvas_background_t canvas_background;
    vrrec_video_rotation_t rotation;
} vrrec_video_layout_v1;

typedef struct vrrec_audio_routing_update_v1 {
    uint32_t struct_size;
    uint32_t abi_version;
    vrrec_audio_routing_t audio_routing;
    uint32_t reserved;
} vrrec_audio_routing_update_v1;

typedef struct vrrec_session_statistics_v1 {
    uint32_t struct_size;
    uint32_t abi_version;
    uint64_t source_video_frame_count;
    uint64_t muxed_video_packet_count;
    uint64_t muxed_audio_packet_count;
    uint64_t dropped_source_video_frame_count;
    uint64_t duplicated_output_video_frame_count;
    uint64_t latest_encode_latency_microseconds;
    uint64_t maximum_encode_latency_microseconds;
    int64_t audio_video_offset_microseconds;
} vrrec_session_statistics_v1;

/*
 * For audio-device lost/recovered and audio-buffer underrun/overrun events,
 * audio_packet_count carries the scheduled 48 kHz audio-frame position.
 * video_packet_count is zero, status is VRREC_STATUS_OK, and message_utf8 is
 * null.
 */
typedef struct vrrec_event_v1 {
    uint32_t struct_size;
    uint32_t abi_version;
    vrrec_event_kind_t kind;
    vrrec_status_t status;
    uint64_t sequence;
    uint64_t video_packet_count;
    uint64_t audio_packet_count;
    const char *message_utf8;
} vrrec_event_v1;

/*
 * An event callback may call vrrec_session_abort_v1() for the same session.
 * That call publishes the abort request and may return before worker cleanup
 * has completed. No other session API, including request_stop or destroy, may
 * be called for that session from its callback.
 *
 * The session owner must call destroy only after every callback has returned
 * and no other ABI call for the session is in progress. Event data and
 * message_utf8 are borrowed and valid only for the duration of the callback.
 */
typedef void (VRREC_CALL *vrrec_event_callback_v1)(
    void *user_data,
    const vrrec_event_v1 *event);

typedef struct vrrec_callbacks_v1 {
    uint32_t struct_size;
    uint32_t abi_version;
    vrrec_event_callback_v1 on_event;
    void *user_data;
} vrrec_callbacks_v1;

typedef struct vrrec_steamvr_input_config_v1 {
    uint32_t struct_size;
    uint32_t abi_version;
    const char *action_manifest_path_utf8;
    const char *action_set_path_utf8;
    const char *digital_action_path_utf8;
} vrrec_steamvr_input_config_v1;

typedef struct vrrec_steamvr_digital_state_v1 {
    uint32_t struct_size;
    uint32_t abi_version;
    uint8_t is_active;
    uint8_t state;
    uint8_t changed;
    uint8_t reserved;
} vrrec_steamvr_digital_state_v1;

typedef struct vrrec_steamvr_haptic_config_v1 {
    uint32_t struct_size;
    uint32_t abi_version;
    const char *action_manifest_path_utf8;
    const char *haptic_action_path_utf8;
    const char *input_source_path_utf8;
    uint32_t reserved_v1;
} vrrec_steamvr_haptic_config_v1;

typedef struct vrrec_steamvr_haptic_pulse_v1 {
    uint32_t struct_size;
    uint32_t abi_version;
    float duration_seconds;
    float frequency_hertz;
    float amplitude;
    uint32_t reserved_v1;
} vrrec_steamvr_haptic_pulse_v1;

typedef struct vrrec_steamvr_overlay_config_v1 {
    uint32_t struct_size;
    uint32_t abi_version;
    const char *application_manifest_path_utf8;
    const char *overlay_key_utf8;
    const char *overlay_name_utf8;
    float width_in_meters;
    uint32_t reserved_v1;
} vrrec_steamvr_overlay_config_v1;

typedef struct vrrec_steamvr_overlay_bgra_frame_v1 {
    uint32_t struct_size;
    uint32_t abi_version;
    const uint8_t *pixel_bytes;
    uint64_t pixel_bytes_size;
    uint32_t width;
    uint32_t height;
    uint32_t stride_bytes;
    uint32_t reserved_v1;
} vrrec_steamvr_overlay_bgra_frame_v1;

/*
 * pixel_x and pixel_y use a top-left origin within the fixed 1024x512
 * overlay texture. When has_event is zero, every payload field is zero.
 * Move events have button zero. Button events use the LEFT, RIGHT, or MIDDLE
 * bit value declared above.
 */
typedef struct vrrec_steamvr_overlay_pointer_event_v1 {
    uint32_t struct_size;
    uint32_t abi_version;
    uint32_t has_event;
    vrrec_steamvr_overlay_pointer_event_kind_t kind;
    uint32_t pixel_x;
    uint32_t pixel_y;
    uint32_t button;
    uint32_t cursor_index;
} vrrec_steamvr_overlay_pointer_event_v1;

/*
 * transform is a row-major OpenVR HmdMatrix34_t in right-handed metres:
 * +X right, +Y up, and -Z forward. Wrist Dock requires LEFT/RIGHT hand and
 * origin NONE. World Pin requires hand NONE and origin STANDING.
 */
typedef struct vrrec_steamvr_overlay_pose_v1 {
    uint32_t struct_size;
    uint32_t abi_version;
    vrrec_steamvr_overlay_placement_mode_t placement_mode;
    vrrec_steamvr_hand_t hand;
    vrrec_steamvr_tracking_origin_t tracking_origin;
    uint32_t reserved_v1;
    float transform[12];
} vrrec_steamvr_overlay_pose_v1;

/*
 * The three exact runtime identity strings are packed without terminators in
 * utf8_buffer. Offsets and sizes are set only when the buffer is large enough.
 */
typedef struct vrrec_steamvr_device_profile_v1 {
    uint32_t struct_size;
    uint32_t abi_version;
    vrrec_steamvr_hand_t hand;
    uint32_t reserved_v1;
    uint32_t tracking_system_name_offset;
    uint32_t tracking_system_name_size;
    uint32_t hmd_model_number_offset;
    uint32_t hmd_model_number_size;
    uint32_t controller_input_profile_path_offset;
    uint32_t controller_input_profile_path_size;
} vrrec_steamvr_device_profile_v1;

typedef struct vrrec_spout_source_config_v1 {
    uint32_t struct_size;
    uint32_t abi_version;
    uint32_t reserved_v1;
    uint32_t reserved_v2;
} vrrec_spout_source_config_v1;

typedef struct vrrec_spout_sender_snapshot_v1 {
    uint32_t struct_size;
    uint32_t abi_version;
    uint32_t sender_id_offset;
    uint32_t sender_id_size;
    uint64_t latest_frame_generation;
} vrrec_spout_sender_snapshot_v1;

typedef struct vrrec_spout_frame_v1 {
    uint32_t struct_size;
    uint32_t abi_version;
    uint32_t sender_id_offset;
    uint32_t sender_id_size;
    uint32_t gpu_identity_offset;
    uint32_t gpu_identity_size;
    uint64_t adapter_luid;
    vrrec_gpu_vendor_t gpu_vendor;
    uint32_t width;
    uint32_t height;
    vrrec_source_pixel_format_t pixel_format;
    double estimated_source_fps;
    uint64_t frame_sequence;
    int64_t monotonic_timestamp_microseconds;
    uint64_t reserved;
} vrrec_spout_frame_v1;

typedef struct vrrec_encoder_probe_config_v1 {
    uint32_t struct_size;
    uint32_t abi_version;
    vrrec_encoder_kind_t encoder_kind;
    uint32_t synthetic_frame_count;
    uint64_t adapter_luid;
    uint32_t width;
    uint32_t height;
    uint32_t fps_numerator;
    uint32_t fps_denominator;
    const char *gpu_identity_utf8;
    uint64_t reserved;
} vrrec_encoder_probe_config_v1;

typedef struct vrrec_encoder_probe_result_v2 {
    uint32_t struct_size;
    uint32_t abi_version;
    vrrec_encoder_kind_t actual_encoder_kind;
    uint32_t hardware_accelerated;
    uint64_t adapter_luid;
    vrrec_encoder_input_format_t opened_input_format;
    uint32_t width;
    uint32_t height;
    uint32_t fps_numerator;
    uint32_t fps_denominator;
    uint32_t validation_flags;
    uint32_t codec_name_offset;
    uint32_t codec_name_size;
    uint32_t driver_identity_offset;
    uint32_t driver_identity_size;
    uint32_t ffmpeg_build_identity_offset;
    uint32_t ffmpeg_build_identity_size;
    uint32_t profile_offset;
    uint32_t profile_size;
    uint32_t device_identity_offset;
    uint32_t device_identity_size;
    uint64_t reserved;
} vrrec_encoder_probe_result_v2;

VRREC_API uint32_t VRREC_CALL vrrec_abi_version(void);

VRREC_API vrrec_status_t VRREC_CALL vrrec_encoder_probe_v1(
    const vrrec_encoder_probe_config_v1 *config,
    uint8_t *out_packet_produced);

VRREC_API vrrec_status_t VRREC_CALL vrrec_encoder_probe_v2(
    const vrrec_encoder_probe_config_v1 *config,
    vrrec_encoder_probe_result_v2 *out_result,
    char *utf8_buffer,
    uint32_t utf8_capacity,
    uint32_t *out_required_utf8_size);

VRREC_API vrrec_status_t VRREC_CALL vrrec_session_create_v1(
    const vrrec_session_config_v1 *config,
    const vrrec_callbacks_v1 *callbacks,
    vrrec_session_t **out_session);

VRREC_API vrrec_status_t VRREC_CALL vrrec_session_start_v1(
    vrrec_session_t *session);

VRREC_API vrrec_status_t VRREC_CALL vrrec_session_update_video_layout_v1(
    vrrec_session_t *session,
    const vrrec_video_layout_v1 *layout);

VRREC_API vrrec_status_t VRREC_CALL vrrec_session_update_audio_routing_v1(
    vrrec_session_t *session,
    const vrrec_audio_routing_update_v1 *update);

VRREC_API vrrec_status_t VRREC_CALL vrrec_session_get_statistics_v1(
    vrrec_session_t *session,
    vrrec_session_statistics_v1 *out_statistics);

VRREC_API vrrec_status_t VRREC_CALL vrrec_session_request_stop_v1(
    vrrec_session_t *session);

VRREC_API vrrec_status_t VRREC_CALL vrrec_session_abort_v1(
    vrrec_session_t *session);

VRREC_API void VRREC_CALL vrrec_session_destroy_v1(
    vrrec_session_t *session);

VRREC_API vrrec_status_t VRREC_CALL vrrec_steamvr_input_create_v1(
    const vrrec_steamvr_input_config_v1 *config,
    vrrec_steamvr_input_t **out_input);

VRREC_API vrrec_status_t VRREC_CALL vrrec_steamvr_input_poll_v1(
    vrrec_steamvr_input_t *input,
    vrrec_steamvr_digital_state_v1 *out_state);

VRREC_API void VRREC_CALL vrrec_steamvr_input_destroy_v1(
    vrrec_steamvr_input_t *input);

VRREC_API vrrec_status_t VRREC_CALL vrrec_steamvr_haptic_create_v1(
    const vrrec_steamvr_haptic_config_v1 *config,
    vrrec_steamvr_haptic_t **out_haptic);

VRREC_API vrrec_status_t VRREC_CALL vrrec_steamvr_haptic_trigger_v1(
    vrrec_steamvr_haptic_t *haptic,
    const vrrec_steamvr_haptic_pulse_v1 *pulse);

VRREC_API void VRREC_CALL vrrec_steamvr_haptic_destroy_v1(
    vrrec_steamvr_haptic_t *haptic);

VRREC_API vrrec_status_t VRREC_CALL vrrec_steamvr_overlay_create_v1(
    const vrrec_steamvr_overlay_config_v1 *config,
    vrrec_steamvr_overlay_t **out_overlay);

VRREC_API vrrec_status_t VRREC_CALL vrrec_steamvr_overlay_show_v1(
    vrrec_steamvr_overlay_t *overlay);

VRREC_API vrrec_status_t VRREC_CALL vrrec_steamvr_overlay_hide_v1(
    vrrec_steamvr_overlay_t *overlay);

VRREC_API vrrec_status_t VRREC_CALL vrrec_steamvr_overlay_update_bgra_v1(
    vrrec_steamvr_overlay_t *overlay,
    const vrrec_steamvr_overlay_bgra_frame_v1 *frame);

VRREC_API vrrec_status_t VRREC_CALL
vrrec_steamvr_overlay_clear_texture_v1(
    vrrec_steamvr_overlay_t *overlay);

VRREC_API vrrec_status_t VRREC_CALL
vrrec_steamvr_overlay_poll_pointer_event_v1(
    vrrec_steamvr_overlay_t *overlay,
    vrrec_steamvr_overlay_pointer_event_v1 *out_event);

VRREC_API vrrec_status_t VRREC_CALL vrrec_steamvr_overlay_set_pose_v1(
    vrrec_steamvr_overlay_t *overlay,
    const vrrec_steamvr_overlay_pose_v1 *pose);

VRREC_API vrrec_status_t VRREC_CALL vrrec_steamvr_overlay_get_pose_v1(
    vrrec_steamvr_overlay_t *overlay,
    vrrec_steamvr_overlay_pose_v1 *out_pose);

VRREC_API vrrec_status_t VRREC_CALL
vrrec_steamvr_overlay_get_device_profile_v1(
    vrrec_steamvr_overlay_t *overlay,
    vrrec_steamvr_device_profile_v1 *inout_profile,
    char *utf8_buffer,
    uint32_t utf8_capacity,
    uint32_t *out_required_utf8_size);

VRREC_API vrrec_status_t VRREC_CALL vrrec_steamvr_overlay_close_v1(
    vrrec_steamvr_overlay_t *overlay);

VRREC_API void VRREC_CALL vrrec_steamvr_overlay_destroy_v1(
    vrrec_steamvr_overlay_t *overlay);

VRREC_API vrrec_status_t VRREC_CALL vrrec_spout_source_create_v1(
    const vrrec_spout_source_config_v1 *config,
    vrrec_spout_source_t **out_source);

VRREC_API vrrec_status_t VRREC_CALL vrrec_spout_source_snapshot_v1(
    vrrec_spout_source_t *source,
    vrrec_spout_sender_snapshot_v1 *entries,
    uint32_t entry_capacity,
    char *utf8_buffer,
    uint32_t utf8_capacity,
    uint32_t *out_entry_count,
    uint32_t *out_required_utf8_size);

VRREC_API vrrec_status_t VRREC_CALL vrrec_spout_source_poll_frame_v1(
    vrrec_spout_source_t *source,
    uint32_t timeout_milliseconds,
    vrrec_spout_frame_v1 *out_frame,
    char *utf8_buffer,
    uint32_t utf8_capacity,
    uint32_t *out_required_utf8_size);

VRREC_API void VRREC_CALL vrrec_spout_source_destroy_v1(
    vrrec_spout_source_t **source);

#ifdef __cplusplus
}
#endif

#endif
