#include <stdint.h>
#include <stdio.h>

#include "vrrecorder_native.h"

int vrrec_c_header_smoke(void);

#if defined(_WIN32)
#define VRREC_TEST_OUTPUT_PATH "C:\\VR Recorder\\capture.recording.mp4"
#define VRREC_TEST_MANIFEST_PATH "C:\\VR Recorder\\actions.json"
#else
#define VRREC_TEST_OUTPUT_PATH "/tmp/capture.recording.mp4"
#define VRREC_TEST_MANIFEST_PATH "/opt/VR Recorder/actions.json"
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

    puts("native C header/runtime smoke tests passed");
    return 0;
}
