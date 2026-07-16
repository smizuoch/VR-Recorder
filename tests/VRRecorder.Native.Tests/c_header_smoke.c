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
_Static_assert(
    sizeof(vrrec_steamvr_overlay_config_v1) == 40,
    "SteamVR overlay config ABI drift");
_Static_assert(
    sizeof(vrrec_steamvr_overlay_bgra_frame_v1) == 40,
    "SteamVR overlay BGRA frame ABI drift");
_Static_assert(
    sizeof(vrrec_steamvr_overlay_pointer_event_v1) == 32,
    "SteamVR overlay pointer event ABI drift");
_Static_assert(
    sizeof(vrrec_steamvr_overlay_pose_v1) == 72,
    "SteamVR overlay pose ABI drift");
_Static_assert(
    sizeof(vrrec_steamvr_device_profile_v1) == 40,
    "SteamVR device profile ABI drift");
_Static_assert(
    VRREC_STEAMVR_OVERLAY_PLACEMENT_WRIST_DOCK == 1,
    "SteamVR Wrist Dock ABI drift");
_Static_assert(
    VRREC_STEAMVR_OVERLAY_PLACEMENT_WORLD_PIN == 2,
    "SteamVR World Pin ABI drift");
_Static_assert(VRREC_STEAMVR_HAND_LEFT == 1, "SteamVR left hand ABI drift");
_Static_assert(VRREC_STEAMVR_HAND_RIGHT == 2, "SteamVR right hand ABI drift");
_Static_assert(
    VRREC_STEAMVR_TRACKING_ORIGIN_STANDING == 1,
    "SteamVR standing origin ABI drift");
_Static_assert(
    VRREC_STEAMVR_OVERLAY_POINTER_MOVE == 1,
    "SteamVR overlay pointer move ABI drift");
_Static_assert(
    VRREC_STEAMVR_OVERLAY_POINTER_BUTTON_DOWN == 2,
    "SteamVR overlay pointer down ABI drift");
_Static_assert(
    VRREC_STEAMVR_OVERLAY_POINTER_BUTTON_UP == 3,
    "SteamVR overlay pointer up ABI drift");
_Static_assert(
    VRREC_STEAMVR_OVERLAY_POINTER_BUTTON_LEFT == 1,
    "SteamVR overlay pointer left button ABI drift");
_Static_assert(
    VRREC_STEAMVR_OVERLAY_POINTER_BUTTON_RIGHT == 2,
    "SteamVR overlay pointer right button ABI drift");
_Static_assert(
    VRREC_STEAMVR_OVERLAY_POINTER_BUTTON_MIDDLE == 4,
    "SteamVR overlay pointer middle button ABI drift");
_Static_assert(
    sizeof(vrrec_spout_source_config_v1) == 16,
    "Spout source config ABI drift");
_Static_assert(
    sizeof(vrrec_spout_sender_snapshot_v1) == 24,
    "Spout sender snapshot ABI drift");
_Static_assert(
    sizeof(vrrec_spout_frame_v1) == 80,
    "Spout frame ABI drift");
_Static_assert(
    sizeof(vrrec_encoder_probe_config_v1) == 56,
    "encoder probe config ABI drift");
_Static_assert(
    sizeof(vrrec_encoder_probe_result_v2) == 96,
    "encoder probe result v2 ABI drift");
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
_Static_assert(
    VRREC_ENCODER_INPUT_SYSTEM_MEMORY_NV12 == 1,
    "system-memory encoder input ABI drift");
_Static_assert(
    VRREC_ENCODER_INPUT_D3D11_NV12 == 2,
    "D3D11 encoder input ABI drift");
_Static_assert(
    VRREC_ENCODER_INPUT_QSV_NV12 == 3,
    "QSV encoder input ABI drift");
_Static_assert(VRREC_GPU_VENDOR_UNKNOWN == 0, "unknown GPU ABI drift");
_Static_assert(VRREC_GPU_VENDOR_NVIDIA == 1, "NVIDIA ABI drift");
_Static_assert(VRREC_GPU_VENDOR_AMD == 2, "AMD ABI drift");
_Static_assert(VRREC_GPU_VENDOR_INTEL == 3, "Intel ABI drift");

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
