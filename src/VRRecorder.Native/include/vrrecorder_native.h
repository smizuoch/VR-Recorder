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
typedef int32_t vrrec_status_t;
typedef uint32_t vrrec_event_kind_t;
typedef uint32_t vrrec_encoder_kind_t;

#define VRREC_STATUS_OK INT32_C(0)
#define VRREC_STATUS_INVALID_ARGUMENT INT32_C(1)
#define VRREC_STATUS_UNSUPPORTED_ABI INT32_C(2)
#define VRREC_STATUS_INVALID_STATE INT32_C(3)
#define VRREC_STATUS_BACKEND_UNAVAILABLE INT32_C(4)
#define VRREC_STATUS_OUT_OF_MEMORY INT32_C(5)
#define VRREC_STATUS_INTERNAL_ERROR INT32_C(6)

#define VRREC_EVENT_FIRST_VIDEO_PACKET_MUXED UINT32_C(1)
#define VRREC_EVENT_STOPPED UINT32_C(2)
#define VRREC_EVENT_FAULTED UINT32_C(3)

#define VRREC_ENCODER_NVENC UINT32_C(1)
#define VRREC_ENCODER_AMF UINT32_C(2)
#define VRREC_ENCODER_QSV UINT32_C(3)
#define VRREC_ENCODER_MEDIA_FOUNDATION_SOFTWARE UINT32_C(4)

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
} vrrec_session_config_v1;

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

VRREC_API uint32_t VRREC_CALL vrrec_abi_version(void);

VRREC_API vrrec_status_t VRREC_CALL vrrec_session_create_v1(
    const vrrec_session_config_v1 *config,
    const vrrec_callbacks_v1 *callbacks,
    vrrec_session_t **out_session);

VRREC_API vrrec_status_t VRREC_CALL vrrec_session_start_v1(
    vrrec_session_t *session);

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

#ifdef __cplusplus
}
#endif

#endif
