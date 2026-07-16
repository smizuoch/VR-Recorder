#include <stdint.h>
#include <stdio.h>

#include "vrrecorder_native.h"

int vrrec_c_header_smoke(void);

#if defined(_WIN32)
#define VRREC_TEST_OUTPUT_PATH "C:\\VR Recorder\\capture.recording.mp4"
#define VRREC_TEST_MANIFEST_PATH "C:\\VR Recorder\\actions.json"
#define VRREC_TEST_APPLICATION_MANIFEST_PATH \
    "C:\\VR Recorder\\OpenVr\\steamvr.vrmanifest"
#else
#define VRREC_TEST_OUTPUT_PATH "/tmp/capture.recording.mp4"
#define VRREC_TEST_MANIFEST_PATH "/opt/VR Recorder/actions.json"
#define VRREC_TEST_APPLICATION_MANIFEST_PATH \
    "/opt/VR Recorder/OpenVr/steamvr.vrmanifest"
#endif

#define CHECK(condition)                                                        \
    do {                                                                        \
        if (!(condition)) {                                                     \
            fprintf(                                                            \
                stderr,                                                         \
                "%s:%d check failed: %s\n",                                   \
                __func__,                                                       \
                __LINE__,                                                       \
                #condition);                                                    \
            return 1;                                                           \
        }                                                                       \
    } while (0)

static void VRREC_CALL ignore_event(
    void *user_data,
    const vrrec_event_v1 *event)
{
    (void)user_data;
    (void)event;
}

int main(void)
{
    CHECK(vrrec_c_header_smoke() == 0);
    CHECK(vrrec_abi_version() == VRREC_ABI_V1);

    vrrec_session_config_v1 session_config = {
        sizeof(vrrec_session_config_v1),
        VRREC_ABI_V1,
        VRREC_TEST_OUTPUT_PATH,
        1920,
        1080,
        30,
        1,
        INT64_C(1784000000000),
        VRREC_ENCODER_MEDIA_FOUNDATION_SOFTWARE,
        0,
        1920,
        1080,
        0,
        0,
        1920,
        1080,
        VRREC_CANVAS_BACKGROUND_BLACK,
        VRREC_VIDEO_ROTATION_NONE,
        VRREC_AUDIO_ROUTING_MIXED,
        VRREC_QUALITY_PRESET_HIGH,
        "default-render",
        "default-capture",
        -6.0,
        -6.0,
        "VRChat-Spout-Sender",
        UINT64_C(0x00000001ABCDEF01),
        UINT64_C(0x00000001ABCDEF01),
        "pci\\ven_10de&dev_2684",
        0,
        VRREC_SOURCE_PIXEL_FORMAT_BGRA8,
        0,
        59.94,
    };
    vrrec_callbacks_v1 callbacks = {
        sizeof(vrrec_callbacks_v1),
        VRREC_ABI_V1,
        ignore_event,
        NULL,
    };
    vrrec_session_t *session = (vrrec_session_t *)(uintptr_t)UINTPTR_MAX;
    CHECK(vrrec_session_create_v1(
              &session_config,
              &callbacks,
              &session) == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(session == NULL);
    vrrec_video_layout_v1 layout = {
        sizeof(vrrec_video_layout_v1),
        VRREC_ABI_V1,
        1920,
        1080,
        1920,
        1080,
        0,
        0,
        1920,
        1080,
        VRREC_CANVAS_BACKGROUND_BLACK,
        VRREC_VIDEO_ROTATION_NONE,
    };
    CHECK(vrrec_session_update_video_layout_v1(NULL, &layout) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    vrrec_session_statistics_v1 statistics = {
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
    CHECK(vrrec_session_get_statistics_v1(NULL, &statistics) ==
          VRREC_STATUS_INVALID_ARGUMENT);

    vrrec_steamvr_input_config_v1 input_config = {
        sizeof(vrrec_steamvr_input_config_v1),
        VRREC_ABI_V1,
        VRREC_TEST_MANIFEST_PATH,
        "/actions/vrrecorder",
        "/actions/vrrecorder/in/toggle_recording",
    };
    vrrec_steamvr_input_t *input =
        (vrrec_steamvr_input_t *)(uintptr_t)UINTPTR_MAX;
    CHECK(vrrec_steamvr_input_create_v1(
              &input_config,
              &input) == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(input == NULL);

    vrrec_steamvr_overlay_config_v1 overlay_config = {
        sizeof(vrrec_steamvr_overlay_config_v1),
        VRREC_ABI_V1,
        VRREC_TEST_APPLICATION_MANIFEST_PATH,
        "com.vrrecorder.desktop.wrist",
        "VR Recorder Wrist",
        0.22F,
        0,
    };
    vrrec_steamvr_overlay_t *overlay =
        (vrrec_steamvr_overlay_t *)(uintptr_t)UINTPTR_MAX;
    CHECK(vrrec_steamvr_overlay_create_v1(
              &overlay_config,
              &overlay) == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(overlay == NULL);
    CHECK(vrrec_steamvr_overlay_show_v1(NULL) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(vrrec_steamvr_overlay_hide_v1(NULL) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(vrrec_steamvr_overlay_close_v1(NULL) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(vrrec_steamvr_overlay_update_bgra_v1(NULL, NULL) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(vrrec_steamvr_overlay_clear_texture_v1(NULL) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    vrrec_steamvr_overlay_pointer_event_v1 pointer_event = {
        sizeof(vrrec_steamvr_overlay_pointer_event_v1),
        VRREC_ABI_V1,
        0,
        0,
        0,
        0,
        0,
        0,
    };
    CHECK(vrrec_steamvr_overlay_poll_pointer_event_v1(
              NULL,
              &pointer_event) == VRREC_STATUS_INVALID_ARGUMENT);
    vrrec_steamvr_overlay_pose_v1 pose = {
        sizeof(vrrec_steamvr_overlay_pose_v1),
        VRREC_ABI_V1,
        VRREC_STEAMVR_OVERLAY_PLACEMENT_WRIST_DOCK,
        VRREC_STEAMVR_HAND_LEFT,
        VRREC_STEAMVR_TRACKING_ORIGIN_NONE,
        0,
        {1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0},
    };
    CHECK(vrrec_steamvr_overlay_set_pose_v1(NULL, &pose) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(vrrec_steamvr_overlay_get_pose_v1(NULL, &pose) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    vrrec_steamvr_device_profile_v1 device_profile = {
        sizeof(vrrec_steamvr_device_profile_v1),
        VRREC_ABI_V1,
        VRREC_STEAMVR_HAND_LEFT,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
    };
    uint32_t required_device_profile_utf8_size = UINT32_MAX;
    CHECK(vrrec_steamvr_overlay_get_device_profile_v1(
              NULL,
              &device_profile,
              NULL,
              0,
              &required_device_profile_utf8_size) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(required_device_profile_utf8_size == 0);
    vrrec_steamvr_overlay_destroy_v1(NULL);

    vrrec_spout_source_config_v1 spout_config = {
        sizeof(vrrec_spout_source_config_v1),
        VRREC_ABI_V1,
        0,
        0,
    };
    vrrec_spout_source_t *spout_source =
        (vrrec_spout_source_t *)(uintptr_t)UINTPTR_MAX;
    CHECK(vrrec_spout_source_create_v1(
              &spout_config,
              &spout_source) == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(spout_source == NULL);
    vrrec_spout_source_destroy_v1(&spout_source);
    vrrec_spout_source_destroy_v1(&spout_source);

    vrrec_encoder_probe_config_v1 encoder_probe = {
        sizeof(vrrec_encoder_probe_config_v1),
        VRREC_ABI_V1,
        VRREC_ENCODER_MEDIA_FOUNDATION_SOFTWARE,
        16,
        UINT64_C(1),
        1920,
        1080,
        30,
        1,
        "software-encoder-probe",
        0,
    };
    uint8_t packet_produced = UINT8_MAX;
    CHECK(vrrec_encoder_probe_v1(
              &encoder_probe,
              &packet_produced) == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(packet_produced == 0);

    vrrec_encoder_probe_result_v2 encoder_probe_result = {0};
    encoder_probe_result.struct_size =
        sizeof(vrrec_encoder_probe_result_v2);
    encoder_probe_result.abi_version = VRREC_ABI_V1;
    uint32_t required_probe_utf8_size = UINT32_MAX;
    CHECK(vrrec_encoder_probe_v2(
              &encoder_probe,
              &encoder_probe_result,
              NULL,
              0,
              &required_probe_utf8_size) ==
          VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(required_probe_utf8_size == 0);
    CHECK(encoder_probe_result.actual_encoder_kind == 0);
    CHECK(encoder_probe_result.validation_flags == 0);

    puts("native C header/runtime smoke tests passed");
    return 0;
}
