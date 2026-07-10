#include <stddef.h>
#include <stdint.h>

#include "vrrecorder_native.h"

#if UINTPTR_MAX == UINT64_MAX
_Static_assert(sizeof(vrrec_session_config_v1) == 176, "config ABI drift");
_Static_assert(
    offsetof(vrrec_session_config_v1, source_pixel_format) == 160,
    "source pixel format ABI drift");
_Static_assert(
    offsetof(vrrec_session_config_v1, reserved_v2) == 164,
    "source format reserved ABI drift");
_Static_assert(
    offsetof(vrrec_session_config_v1, estimated_source_fps) == 168,
    "estimated source FPS ABI drift");
_Static_assert(sizeof(vrrec_video_layout_v1) == 48, "layout ABI drift");
_Static_assert(
    sizeof(vrrec_session_statistics_v1) == 72,
    "statistics ABI drift");
_Static_assert(sizeof(vrrec_event_v1) == 48, "event ABI drift");
_Static_assert(sizeof(vrrec_callbacks_v1) == 24, "callback ABI drift");
_Static_assert(
    sizeof(vrrec_steamvr_input_config_v1) == 32,
    "SteamVR input config ABI drift");
_Static_assert(
    sizeof(vrrec_steamvr_digital_state_v1) == 12,
    "SteamVR digital state ABI drift");
#endif

_Static_assert(
    VRREC_SOURCE_PIXEL_FORMAT_BGRA8 == 1,
    "BGRA8 ABI value drift");
_Static_assert(
    VRREC_SOURCE_PIXEL_FORMAT_RGBA8 == 2,
    "RGBA8 ABI value drift");
_Static_assert(
    VRREC_SOURCE_PIXEL_FORMAT_NV12 == 3,
    "NV12 ABI value drift");

static void VRREC_CALL consume_event(
    void *user_data,
    const vrrec_event_v1 *event)
{
    (void)user_data;
    (void)event;
}

int vrrec_c_header_smoke(void)
{
    vrrec_callbacks_v1 callbacks = {
        sizeof(vrrec_callbacks_v1),
        VRREC_ABI_V1,
        consume_event,
        0,
    };
    return callbacks.on_event == 0;
}
