#include <chrono>
#include <cstddef>
#include <cstdint>
#include <future>
#include <limits>
#include <iostream>
#include <string>
#include <thread>
#include <utility>
#include <vector>

#include "fake_media_backend.hpp"
#include "media_backend.hpp"
#include "vrrecorder_native.h"

static_assert(VRREC_ABI_V1 == 1);
static_assert(VRREC_EVENT_DESKTOP_AUDIO_DEVICE_LOST == 4);
static_assert(VRREC_EVENT_DESKTOP_AUDIO_DEVICE_RECOVERED == 5);
static_assert(VRREC_EVENT_MICROPHONE_AUDIO_DEVICE_LOST == 6);
static_assert(VRREC_EVENT_MICROPHONE_AUDIO_DEVICE_RECOVERED == 7);
static_assert(VRREC_EVENT_DESKTOP_AUDIO_BUFFER_UNDERRUN == 9);
static_assert(VRREC_EVENT_DESKTOP_AUDIO_BUFFER_OVERRUN == 10);
static_assert(VRREC_EVENT_MICROPHONE_AUDIO_BUFFER_UNDERRUN == 11);
static_assert(VRREC_EVENT_MICROPHONE_AUDIO_BUFFER_OVERRUN == 12);
static_assert(sizeof(vrrec_session_config_v1) == 176);
static_assert(offsetof(vrrec_session_config_v1, source_pixel_format) == 160);
static_assert(offsetof(vrrec_session_config_v1, reserved_v2) == 164);
static_assert(offsetof(vrrec_session_config_v1, estimated_source_fps) == 168);
static_assert(sizeof(vrrec_video_layout_v1) == 48);
static_assert(sizeof(vrrec_audio_routing_update_v1) == 16);
static_assert(sizeof(vrrec_session_statistics_v1) == 72);
static_assert(sizeof(vrrec_event_v1) == 48);
static_assert(sizeof(vrrec_callbacks_v1) == 24);
static_assert(sizeof(vrrec_steamvr_input_config_v1) == 32);
static_assert(sizeof(vrrec_steamvr_digital_state_v1) == 12);
static_assert(sizeof(vrrec_spout_source_config_v1) == 16);
static_assert(sizeof(vrrec_spout_sender_snapshot_v1) == 24);
static_assert(sizeof(vrrec_spout_frame_v1) == 80);
static_assert(sizeof(vrrec_encoder_probe_config_v1) == 56);

namespace {

struct CapturedEvent {
    vrrec_event_kind_t kind;
    vrrec_status_t status;
    std::uint64_t sequence;
    std::uint64_t video_packet_count;
    std::uint64_t audio_packet_count;
    std::string message;
};

struct EventLog {
    std::vector<CapturedEvent> events;
};

struct AbortFromCallbackContext {
    vrrec_session_t *session = nullptr;
    vrrec_status_t abort_status = VRREC_STATUS_INTERNAL_ERROR;
    std::promise<void> completed;
};

void VRREC_CALL CaptureEvent(
    void *user_data,
    const vrrec_event_v1 *event)
{
    auto &log = *static_cast<EventLog *>(user_data);
    log.events.push_back(CapturedEvent {
        event->kind,
        event->status,
        event->sequence,
        event->video_packet_count,
        event->audio_packet_count,
        event->message_utf8 == nullptr ? "" : event->message_utf8,
    });
}

void VRREC_CALL AbortFromCallback(
    void *user_data,
    const vrrec_event_v1 *)
{
    auto &context = *static_cast<AbortFromCallbackContext *>(user_data);
    context.abort_status = vrrec_session_abort_v1(context.session);
    context.completed.set_value();
}

vrrec_session_config_v1 ValidConfig()
{
    return vrrec_session_config_v1 {
        sizeof(vrrec_session_config_v1),
        VRREC_ABI_V1,
        "/tmp/vr-recorder-native.recording.mp4",
        1920,
        1080,
        30,
        1,
        1'784'000'000'000,
        VRREC_ENCODER_AMF,
        0,
        1920,
        1080,
        240,
        0,
        1440,
        1080,
        VRREC_CANVAS_BACKGROUND_BLACK,
        VRREC_VIDEO_ROTATION_NONE,
        VRREC_AUDIO_ROUTING_MIXED,
        VRREC_QUALITY_PRESET_HIGH,
        "{0.0.0.00000000}.desktop-endpoint",
        "{0.0.1.00000000}.microphone-endpoint",
        -6.0,
        -3.5,
        "VRChat-Spout-Sender-42",
        UINT64_C(0x00000001ABCDEF01),
        UINT64_C(0x00000001ABCDEF01),
        "pci\\ven_10de&dev_2684|driver-32.0.15.6094",
        0,
        VRREC_SOURCE_PIXEL_FORMAT_RGBA8,
        0,
        59.94,
    };
}

vrrec_callbacks_v1 ValidCallbacks(EventLog &log)
{
    return vrrec_callbacks_v1 {
        sizeof(vrrec_callbacks_v1),
        VRREC_ABI_V1,
        CaptureEvent,
        &log,
    };
}

vrrec_video_layout_v1 ValidRuntimeLayout()
{
    return vrrec_video_layout_v1 {
        sizeof(vrrec_video_layout_v1),
        VRREC_ABI_V1,
        1080,
        1920,
        1920,
        1080,
        657,
        0,
        606,
        1080,
        VRREC_CANVAS_BACKGROUND_BLACK,
        VRREC_VIDEO_ROTATION_NONE,
    };
}

vrrec_session_statistics_v1 ValidStatisticsOutput()
{
    return vrrec_session_statistics_v1 {
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
}

vrrec_audio_routing_update_v1 ValidAudioRoutingUpdate()
{
    return vrrec_audio_routing_update_v1 {
        sizeof(vrrec_audio_routing_update_v1),
        VRREC_ABI_V1,
        VRREC_AUDIO_ROUTING_DESKTOP_ONLY,
        0,
    };
}

vrrec_steamvr_input_config_v1 ValidSteamVrConfig()
{
#if defined(_WIN32)
    constexpr auto manifest_path = "C:\\VR Recorder\\OpenVr\\actions.json";
#else
    constexpr auto manifest_path = "/opt/VR Recorder/OpenVr/actions.json";
#endif
    return vrrec_steamvr_input_config_v1 {
        sizeof(vrrec_steamvr_input_config_v1),
        VRREC_ABI_V1,
        manifest_path,
        "/actions/vrrecorder",
        "/actions/vrrecorder/in/toggle_recording",
    };
}

vrrec_spout_source_config_v1 ValidSpoutSourceConfig()
{
    return vrrec_spout_source_config_v1 {
        sizeof(vrrec_spout_source_config_v1),
        VRREC_ABI_V1,
        0,
        0,
    };
}

vrrec_encoder_probe_config_v1 ValidEncoderProbeConfig()
{
    return vrrec_encoder_probe_config_v1 {
        sizeof(vrrec_encoder_probe_config_v1),
        VRREC_ABI_V1,
        VRREC_ENCODER_NVENC,
        16,
        UINT64_C(0x00000001ABCDEF01),
        1920,
        1080,
        60,
        1,
        "pci\\ven_10de&dev_2684|driver-32.0.15.6094",
        0,
    };
}

vrrec_spout_frame_v1 ValidSpoutFrameOutput()
{
    return vrrec_spout_frame_v1 {
        sizeof(vrrec_spout_frame_v1),
        VRREC_ABI_V1,
        0,
        0,
        0,
        0,
        0,
        VRREC_GPU_VENDOR_UNKNOWN,
        0,
        0,
        0,
        0.0,
        0,
        0,
        0,
    };
}

vrrec_spout_sender_snapshot_v1 ValidSpoutSnapshotOutput()
{
    return vrrec_spout_sender_snapshot_v1 {
        sizeof(vrrec_spout_sender_snapshot_v1),
        VRREC_ABI_V1,
        0,
        0,
        0,
    };
}

vrrecorder::native::testing::TestSpoutFrame ValidTestSpoutFrame()
{
    return vrrecorder::native::testing::TestSpoutFrame {
        "VRChat-Spout-Sender-42",
        UINT64_C(0x00000001ABCDEF01),
        "pci\\ven_10de&dev_2684|driver-32.0.15.6094",
        VRREC_GPU_VENDOR_NVIDIA,
        1920,
        1080,
        VRREC_SOURCE_PIXEL_FORMAT_RGBA8,
        59.94,
        42,
        INT64_C(1234567),
    };
}

#define CHECK(condition)                                                        \
    do {                                                                        \
        if (!(condition)) {                                                     \
            std::cerr << __func__ << ':' << __LINE__                            \
                      << " check failed: " #condition << '\n';                 \
            return false;                                                       \
        }                                                                       \
    } while (false)

bool RejectsInvalidAbiInputs()
{
    EventLog log;
    auto config = ValidConfig();
    auto callbacks = ValidCallbacks(log);
    auto *session = reinterpret_cast<vrrec_session_t *>(UINTPTR_MAX);

    CHECK(vrrec_session_create_v1(nullptr, &callbacks, &session) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(session == nullptr);

    config.struct_size = sizeof(config) - 1;
    CHECK(vrrec_session_create_v1(&config, &callbacks, &session) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(session == nullptr);

    config = ValidConfig();
    config.abi_version = VRREC_ABI_V1 + 1;
    CHECK(vrrec_session_create_v1(&config, &callbacks, &session) ==
          VRREC_STATUS_UNSUPPORTED_ABI);
    CHECK(session == nullptr);

    config = ValidConfig();
    config.encoder_kind = UINT32_MAX;
    CHECK(vrrec_session_create_v1(&config, &callbacks, &session) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(session == nullptr);

    config = ValidConfig();
    callbacks.struct_size = sizeof(callbacks) - 1;
    CHECK(vrrec_session_create_v1(&config, &callbacks, &session) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(session == nullptr);
    return true;
}

bool LegacySessionConfigDefaultsToSoftwareEncoder()
{
    EventLog log;
    auto config = ValidConfig();
    config.struct_size = 40;
    config.encoder_kind = UINT32_MAX;
    auto callbacks = ValidCallbacks(log);
    vrrec_session_t *session = nullptr;

    CHECK(vrrec_session_create_v1(&config, &callbacks, &session) ==
          VRREC_STATUS_OK);
    CHECK(session != nullptr);
    CHECK(vrrecorder::native::testing::EncoderKind() ==
          VRREC_ENCODER_MEDIA_FOUNDATION_SOFTWARE);

    vrrec_session_destroy_v1(session);
    return true;
}

bool FullSessionConfigCrossesTheBackendBoundaryExactly()
{
    EventLog log;
    const auto config = ValidConfig();
    auto callbacks = ValidCallbacks(log);
    vrrec_session_t *session = nullptr;

    CHECK(vrrec_session_create_v1(&config, &callbacks, &session) ==
          VRREC_STATUS_OK);
    const auto &observed = vrrecorder::native::testing::SessionConfig();
    CHECK(observed.canvas_width == 1920);
    CHECK(observed.canvas_height == 1080);
    CHECK(observed.source_width == 1920);
    CHECK(observed.source_height == 1080);
    CHECK(observed.destination_x == 240);
    CHECK(observed.destination_y == 0);
    CHECK(observed.destination_width == 1440);
    CHECK(observed.destination_height == 1080);
    CHECK(observed.canvas_background == VRREC_CANVAS_BACKGROUND_BLACK);
    CHECK(observed.rotation == VRREC_VIDEO_ROTATION_NONE);
    CHECK(observed.audio_routing == VRREC_AUDIO_ROUTING_MIXED);
    CHECK(observed.quality_preset == VRREC_QUALITY_PRESET_HIGH);
    CHECK(observed.desktop_endpoint_id ==
          "{0.0.0.00000000}.desktop-endpoint");
    CHECK(observed.microphone_endpoint_id ==
          "{0.0.1.00000000}.microphone-endpoint");
    CHECK(observed.desktop_gain_db == -6.0);
    CHECK(observed.microphone_gain_db == -3.5);
    CHECK(observed.spout_sender_identity == "VRChat-Spout-Sender-42");
    CHECK(observed.spout_adapter_luid == UINT64_C(0x00000001ABCDEF01));
    CHECK(observed.encoder_adapter_luid == UINT64_C(0x00000001ABCDEF01));
    CHECK(observed.gpu_identity ==
          "pci\\ven_10de&dev_2684|driver-32.0.15.6094");
    CHECK(observed.source_pixel_format == VRREC_SOURCE_PIXEL_FORMAT_RGBA8);
    CHECK(observed.estimated_source_fps == 59.94);

    vrrec_session_destroy_v1(session);
    return true;
}

bool LegacySessionConfigsReceiveDeterministicMediaDefaults()
{
    EventLog log;
    auto config = ValidConfig();
    auto callbacks = ValidCallbacks(log);
    vrrec_session_t *session = nullptr;

    config.struct_size = 40;
    config.encoder_kind = UINT32_MAX;
    config.source_width = UINT32_MAX;
    config.audio_routing = UINT32_MAX;
    config.desktop_gain_db = std::numeric_limits<double>::quiet_NaN();
    config.spout_sender_identity_utf8 = nullptr;
    CHECK(vrrec_session_create_v1(&config, &callbacks, &session) ==
          VRREC_STATUS_OK);
    auto observed = vrrecorder::native::testing::SessionConfig();
    CHECK(vrrecorder::native::testing::EncoderKind() ==
          VRREC_ENCODER_MEDIA_FOUNDATION_SOFTWARE);
    CHECK(observed.source_width == 1920);
    CHECK(observed.source_height == 1080);
    CHECK(observed.destination_x == 0);
    CHECK(observed.destination_y == 0);
    CHECK(observed.destination_width == 1920);
    CHECK(observed.destination_height == 1080);
    CHECK(observed.canvas_background == VRREC_CANVAS_BACKGROUND_BLACK);
    CHECK(observed.rotation == VRREC_VIDEO_ROTATION_NONE);
    CHECK(observed.audio_routing == VRREC_AUDIO_ROUTING_MIXED);
    CHECK(observed.quality_preset == VRREC_QUALITY_PRESET_HIGH);
    CHECK(observed.desktop_endpoint_id == "default-render");
    CHECK(observed.microphone_endpoint_id == "default-capture");
    CHECK(observed.desktop_gain_db == -6.0);
    CHECK(observed.microphone_gain_db == -6.0);
    CHECK(observed.spout_sender_identity == "legacy-unspecified");
    CHECK(observed.spout_adapter_luid == 0);
    CHECK(observed.encoder_adapter_luid == 0);
    CHECK(observed.gpu_identity == "legacy-unspecified");
    vrrec_session_destroy_v1(session);

    config = ValidConfig();
    config.struct_size = 48;
    config.encoder_kind = VRREC_ENCODER_QSV;
    config.source_width = UINT32_MAX;
    config.audio_routing = UINT32_MAX;
    config.desktop_gain_db = std::numeric_limits<double>::infinity();
    config.spout_sender_identity_utf8 = nullptr;
    session = nullptr;
    CHECK(vrrec_session_create_v1(&config, &callbacks, &session) ==
          VRREC_STATUS_OK);
    observed = vrrecorder::native::testing::SessionConfig();
    CHECK(vrrecorder::native::testing::EncoderKind() == VRREC_ENCODER_QSV);
    CHECK(observed.source_width == 1920);
    CHECK(observed.destination_width == 1920);
    CHECK(observed.audio_routing == VRREC_AUDIO_ROUTING_MIXED);
    CHECK(observed.desktop_gain_db == -6.0);
    CHECK(observed.spout_sender_identity == "legacy-unspecified");
    vrrec_session_destroy_v1(session);
    return true;
}

bool LegacyMediaSessionConfigDefaultsSourceFormat()
{
    EventLog log;
    auto config = ValidConfig();
    config.struct_size = 160;
    config.source_pixel_format = UINT32_MAX;
    config.reserved_v2 = UINT32_MAX;
    config.estimated_source_fps =
        std::numeric_limits<double>::quiet_NaN();
    auto callbacks = ValidCallbacks(log);
    vrrec_session_t *session = nullptr;

    CHECK(vrrec_session_create_v1(&config, &callbacks, &session) ==
          VRREC_STATUS_OK);
    const auto &observed = vrrecorder::native::testing::SessionConfig();
    CHECK(observed.source_pixel_format == VRREC_SOURCE_PIXEL_FORMAT_BGRA8);
    CHECK(observed.estimated_source_fps == 30.0);

    vrrec_session_destroy_v1(session);
    return true;
}

bool RejectsTruncatedOrInvalidExtendedSessionConfig()
{
    EventLog log;
    auto callbacks = ValidCallbacks(log);
    vrrec_session_t *session = nullptr;
    const auto rejected = [&](vrrec_session_config_v1 config) {
        session = reinterpret_cast<vrrec_session_t *>(UINTPTR_MAX);
        return vrrec_session_create_v1(&config, &callbacks, &session) ==
                   VRREC_STATUS_INVALID_ARGUMENT &&
            session == nullptr;
    };

    auto config = ValidConfig();
    config.struct_size = 49;
    CHECK(rejected(config));
    for (std::uint32_t struct_size = 161; struct_size < sizeof(config);
         ++struct_size) {
        config = ValidConfig();
        config.struct_size = struct_size;
        CHECK(rejected(config));
    }

    config = ValidConfig();
    config.width = 1919;
    CHECK(rejected(config));
    config = ValidConfig();
    config.source_width = 0;
    CHECK(rejected(config));
    config = ValidConfig();
    config.destination_width = 1439;
    CHECK(rejected(config));
    config = ValidConfig();
    config.destination_x = 481;
    CHECK(rejected(config));
    config = ValidConfig();
    config.canvas_background = UINT32_MAX;
    CHECK(rejected(config));
    config = ValidConfig();
    config.rotation = UINT32_MAX;
    CHECK(rejected(config));
    config = ValidConfig();
    config.audio_routing = UINT32_MAX;
    CHECK(rejected(config));
    config = ValidConfig();
    config.quality_preset = UINT32_MAX;
    CHECK(rejected(config));

    config = ValidConfig();
    config.desktop_gain_db = std::numeric_limits<double>::quiet_NaN();
    CHECK(rejected(config));
    config = ValidConfig();
    config.microphone_gain_db = std::numeric_limits<double>::infinity();
    CHECK(rejected(config));
    config = ValidConfig();
    config.desktop_gain_db = -96.1;
    CHECK(rejected(config));
    config = ValidConfig();
    config.microphone_gain_db = 24.1;
    CHECK(rejected(config));

    constexpr char invalid_utf8[] = "\xC3\x28";
    config = ValidConfig();
    config.temporary_output_path_utf8 = "relative.recording.mp4";
    CHECK(rejected(config));
    config = ValidConfig();
    config.desktop_endpoint_id_utf8 = nullptr;
    CHECK(rejected(config));
    config = ValidConfig();
    config.microphone_endpoint_id_utf8 = "   ";
    CHECK(rejected(config));
    config = ValidConfig();
    config.spout_sender_identity_utf8 = invalid_utf8;
    CHECK(rejected(config));
    config = ValidConfig();
    config.gpu_identity_utf8 = "";
    CHECK(rejected(config));
    config = ValidConfig();
    config.reserved_v1 = 1;
    CHECK(rejected(config));
    config = ValidConfig();
    config.source_pixel_format = 0;
    CHECK(rejected(config));
    config = ValidConfig();
    config.source_pixel_format = UINT32_MAX;
    CHECK(rejected(config));
    config = ValidConfig();
    config.reserved_v2 = 1;
    CHECK(rejected(config));
    config = ValidConfig();
    config.estimated_source_fps = 0.0;
    CHECK(rejected(config));
    config = ValidConfig();
    config.estimated_source_fps = -1.0;
    CHECK(rejected(config));
    config = ValidConfig();
    config.estimated_source_fps =
        std::numeric_limits<double>::quiet_NaN();
    CHECK(rejected(config));
    config = ValidConfig();
    config.estimated_source_fps =
        std::numeric_limits<double>::infinity();
    CHECK(rejected(config));
    config = ValidConfig();
    config.estimated_source_fps = 1000.1;
    CHECK(rejected(config));
    return true;
}

bool EmitsMuxAndStoppedEventsOnlyAfterBackendMilestones()
{
    EventLog log;
    auto config = ValidConfig();
    auto callbacks = ValidCallbacks(log);
    vrrec_session_t *session = nullptr;

    CHECK(vrrec_session_create_v1(&config, &callbacks, &session) ==
          VRREC_STATUS_OK);
    CHECK(session != nullptr);
    CHECK(vrrecorder::native::testing::EncoderKind() == VRREC_ENCODER_AMF);
    CHECK(log.events.empty());
    CHECK(vrrec_session_start_v1(session) == VRREC_STATUS_OK);
    CHECK(log.events.empty());

    vrrecorder::native::testing::CommitMuxedVideoPacket();
    vrrecorder::native::testing::CommitMuxedVideoPacket();
    CHECK(log.events.size() == 1);
    CHECK(log.events[0].kind == VRREC_EVENT_FIRST_VIDEO_PACKET_MUXED);
    CHECK(log.events[0].sequence == 1);

    CHECK(vrrec_session_request_stop_v1(session) == VRREC_STATUS_OK);
    CHECK(vrrec_session_request_stop_v1(session) == VRREC_STATUS_OK);
    CHECK(log.events.size() == 1);

    vrrecorder::native::testing::CompleteTrailerFlushClose(90, 142);
    vrrecorder::native::testing::CompleteTrailerFlushClose(91, 143);
    CHECK(log.events.size() == 2);
    CHECK(log.events[1].kind == VRREC_EVENT_STOPPED);
    CHECK(log.events[1].sequence == 2);
    CHECK(log.events[1].video_packet_count == 90);
    CHECK(log.events[1].audio_packet_count == 142);

    vrrec_session_destroy_v1(session);
    return true;
}

bool CallbackCanAbortItsOwnSessionWithoutDeadlocking()
{
    AbortFromCallbackContext context;
    const auto config = ValidConfig();
    const auto callbacks = vrrec_callbacks_v1 {
        sizeof(vrrec_callbacks_v1),
        VRREC_ABI_V1,
        AbortFromCallback,
        &context,
    };
    CHECK(vrrec_session_create_v1(
              &config,
              &callbacks,
              &context.session) == VRREC_STATUS_OK);
    CHECK(vrrec_session_start_v1(context.session) == VRREC_STATUS_OK);
    auto completed = context.completed.get_future();
    std::thread emitting([] {
        vrrecorder::native::testing::CommitMuxedVideoPacket();
    });

    if (completed.wait_for(std::chrono::milliseconds(250)) !=
        std::future_status::ready) {
        emitting.detach();
        std::cerr << __func__
                  << " timed out waiting for callback Abort\n";
        return false;
    }

    emitting.join();
    completed.get();
    CHECK(context.abort_status == VRREC_STATUS_OK);
    vrrec_session_destroy_v1(context.session);
    return true;
}

bool EmitsPrivacySafeNonterminalAudioDeviceEvents()
{
    using vrrecorder::native::testing::SetDesktopAudioEndpointAvailable;
    using vrrecorder::native::testing::SetMicrophoneAudioEndpointAvailable;

    EventLog log;
    const auto config = ValidConfig();
    auto callbacks = ValidCallbacks(log);
    vrrec_session_t *session = nullptr;
    CHECK(vrrec_session_create_v1(&config, &callbacks, &session) ==
          VRREC_STATUS_OK);
    SetDesktopAudioEndpointAvailable(false, 100);
    CHECK(log.events.empty());
    CHECK(vrrec_session_start_v1(session) == VRREC_STATUS_OK);
    vrrecorder::native::testing::CommitMuxedVideoPacket();

    SetDesktopAudioEndpointAvailable(false, 4'800);
    SetDesktopAudioEndpointAvailable(false, 4'900);
    SetDesktopAudioEndpointAvailable(true, 9'600);
    SetDesktopAudioEndpointAvailable(true, 9'700);
    SetMicrophoneAudioEndpointAvailable(false, 14'400);
    SetMicrophoneAudioEndpointAvailable(true, 19'200);

    CHECK(log.events.size() == 5);
    CHECK(log.events[0].kind == VRREC_EVENT_FIRST_VIDEO_PACKET_MUXED);
    const vrrec_event_kind_t expected_kinds[] = {
        VRREC_EVENT_DESKTOP_AUDIO_DEVICE_LOST,
        VRREC_EVENT_DESKTOP_AUDIO_DEVICE_RECOVERED,
        VRREC_EVENT_MICROPHONE_AUDIO_DEVICE_LOST,
        VRREC_EVENT_MICROPHONE_AUDIO_DEVICE_RECOVERED,
    };
    const std::uint64_t expected_positions[] = {
        4'800,
        9'600,
        14'400,
        19'200,
    };
    for (std::size_t index = 0; index < 4; ++index) {
        const auto &event = log.events[index + 1];
        CHECK(event.kind == expected_kinds[index]);
        CHECK(event.status == VRREC_STATUS_OK);
        CHECK(event.sequence == index + 2);
        CHECK(event.video_packet_count == 0);
        CHECK(event.audio_packet_count == expected_positions[index]);
        CHECK(event.message.empty());
    }

    CHECK(vrrec_session_request_stop_v1(session) == VRREC_STATUS_OK);
    SetDesktopAudioEndpointAvailable(false, 24'000);
    CHECK(log.events.size() == 5);
    vrrecorder::native::testing::CompleteTrailerFlushClose(90, 142);
    CHECK(log.events.size() == 6);
    CHECK(log.events[5].kind == VRREC_EVENT_STOPPED);
    CHECK(log.events[5].sequence == 6);
    SetMicrophoneAudioEndpointAvailable(false, 28'800);
    CHECK(log.events.size() == 6);
    vrrec_session_destroy_v1(session);

    EventLog abort_log;
    callbacks = ValidCallbacks(abort_log);
    session = nullptr;
    CHECK(vrrec_session_create_v1(&config, &callbacks, &session) ==
          VRREC_STATUS_OK);
    CHECK(vrrec_session_start_v1(session) == VRREC_STATUS_OK);
    CHECK(vrrec_session_abort_v1(session) == VRREC_STATUS_OK);
    SetDesktopAudioEndpointAvailable(false, 1);
    CHECK(abort_log.events.empty());
    vrrec_session_destroy_v1(session);
    return true;
}

bool EmitsPrivacySafeNonterminalAudioBufferHealthEvents()
{
    using vrrecorder::native::testing::EmitAudioBufferHealth;
    using vrrecorder::native::AudioBufferHealth;
    using vrrecorder::native::AudioEndpointRole;

    EventLog log;
    const auto config = ValidConfig();
    auto callbacks = ValidCallbacks(log);
    vrrec_session_t *session = nullptr;
    CHECK(vrrec_session_create_v1(&config, &callbacks, &session) ==
          VRREC_STATUS_OK);
    CHECK(vrrec_session_start_v1(session) == VRREC_STATUS_OK);
    vrrecorder::native::testing::CommitMuxedVideoPacket();

    EmitAudioBufferHealth(
        AudioEndpointRole::Desktop,
        AudioBufferHealth::Underrun,
        24'000);
    EmitAudioBufferHealth(
        AudioEndpointRole::Desktop,
        AudioBufferHealth::Overrun,
        24'480);
    EmitAudioBufferHealth(
        AudioEndpointRole::Microphone,
        AudioBufferHealth::Underrun,
        24'960);
    EmitAudioBufferHealth(
        AudioEndpointRole::Microphone,
        AudioBufferHealth::Overrun,
        25'440);

    const vrrec_event_kind_t expected_kinds[] = {
        VRREC_EVENT_DESKTOP_AUDIO_BUFFER_UNDERRUN,
        VRREC_EVENT_DESKTOP_AUDIO_BUFFER_OVERRUN,
        VRREC_EVENT_MICROPHONE_AUDIO_BUFFER_UNDERRUN,
        VRREC_EVENT_MICROPHONE_AUDIO_BUFFER_OVERRUN,
    };
    CHECK(log.events.size() == 5);
    for (std::size_t index = 0; index < 4; ++index) {
        const auto &event = log.events[index + 1];
        CHECK(event.kind == expected_kinds[index]);
        CHECK(event.status == VRREC_STATUS_OK);
        CHECK(event.sequence == index + 2);
        CHECK(event.video_packet_count == 0);
        CHECK(event.audio_packet_count == 24'000 + index * 480);
        CHECK(event.message.empty());
    }

    CHECK(vrrec_session_request_stop_v1(session) == VRREC_STATUS_OK);
    EmitAudioBufferHealth(
        AudioEndpointRole::Desktop,
        AudioBufferHealth::Underrun,
        30'000);
    CHECK(log.events.size() == 5);
    vrrecorder::native::testing::CompleteTrailerFlushClose(90, 142);
    vrrec_session_destroy_v1(session);
    return true;
}

bool UpdatesStableLayoutWithoutChangingTheOutputCanvas()
{
    EventLog log;
    const auto config = ValidConfig();
    auto callbacks = ValidCallbacks(log);
    vrrec_session_t *session = nullptr;
    CHECK(vrrec_session_create_v1(&config, &callbacks, &session) ==
          VRREC_STATUS_OK);

    auto layout = ValidRuntimeLayout();
    CHECK(vrrec_session_update_video_layout_v1(session, &layout) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(vrrec_session_start_v1(session) == VRREC_STATUS_OK);
    CHECK(vrrec_session_update_video_layout_v1(session, &layout) ==
          VRREC_STATUS_OK);
    CHECK(vrrecorder::native::testing::VideoLayoutUpdateCount() == 1);
    const auto observed = vrrecorder::native::testing::VideoLayout();
    CHECK(observed.source_width == 1080);
    CHECK(observed.source_height == 1920);
    CHECK(observed.canvas_width == 1920);
    CHECK(observed.canvas_height == 1080);
    CHECK(observed.destination_x == 657);
    CHECK(observed.destination_y == 0);
    CHECK(observed.destination_width == 606);
    CHECK(observed.destination_height == 1080);
    CHECK(observed.canvas_background == VRREC_CANVAS_BACKGROUND_BLACK);
    CHECK(observed.rotation == VRREC_VIDEO_ROTATION_NONE);

    layout.canvas_width = 1922;
    CHECK(vrrec_session_update_video_layout_v1(session, &layout) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(vrrecorder::native::testing::VideoLayoutUpdateCount() == 1);
    CHECK(vrrec_session_request_stop_v1(session) == VRREC_STATUS_OK);
    layout.canvas_width = 1920;
    CHECK(vrrec_session_update_video_layout_v1(session, &layout) ==
          VRREC_STATUS_INVALID_STATE);
    vrrec_session_destroy_v1(session);
    return true;
}

bool RejectsInvalidRuntimeLayoutAbiInputs()
{
    EventLog log;
    const auto config = ValidConfig();
    auto callbacks = ValidCallbacks(log);
    vrrec_session_t *session = nullptr;
    CHECK(vrrec_session_create_v1(&config, &callbacks, &session) ==
          VRREC_STATUS_OK);
    CHECK(vrrec_session_start_v1(session) == VRREC_STATUS_OK);
    auto layout = ValidRuntimeLayout();

    CHECK(vrrec_session_update_video_layout_v1(nullptr, &layout) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(vrrec_session_update_video_layout_v1(session, nullptr) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    layout.struct_size = sizeof(layout) - 1;
    CHECK(vrrec_session_update_video_layout_v1(session, &layout) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    layout = ValidRuntimeLayout();
    layout.abi_version = VRREC_ABI_V1 + 1;
    CHECK(vrrec_session_update_video_layout_v1(session, &layout) ==
          VRREC_STATUS_UNSUPPORTED_ABI);
    layout = ValidRuntimeLayout();
    layout.source_width = 0;
    CHECK(vrrec_session_update_video_layout_v1(session, &layout) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    layout = ValidRuntimeLayout();
    layout.canvas_height = 1079;
    CHECK(vrrec_session_update_video_layout_v1(session, &layout) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    layout = ValidRuntimeLayout();
    layout.destination_width = 607;
    CHECK(vrrec_session_update_video_layout_v1(session, &layout) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    layout = ValidRuntimeLayout();
    layout.destination_x = 1315;
    CHECK(vrrec_session_update_video_layout_v1(session, &layout) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    layout = ValidRuntimeLayout();
    layout.canvas_background = UINT32_MAX;
    CHECK(vrrec_session_update_video_layout_v1(session, &layout) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    layout = ValidRuntimeLayout();
    layout.rotation = UINT32_MAX;
    CHECK(vrrec_session_update_video_layout_v1(session, &layout) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(vrrecorder::native::testing::VideoLayoutUpdateCount() == 0);
    vrrec_session_destroy_v1(session);
    return true;
}

bool SynchronousLayoutFaultDoesNotDeadlockTheAbi()
{
    EventLog log;
    const auto config = ValidConfig();
    auto callbacks = ValidCallbacks(log);
    vrrec_session_t *session = nullptr;
    CHECK(vrrec_session_create_v1(&config, &callbacks, &session) ==
          VRREC_STATUS_OK);
    CHECK(vrrec_session_start_v1(session) == VRREC_STATUS_OK);
    vrrecorder::native::testing::FaultDuringNextVideoLayoutUpdate();
    auto layout = ValidRuntimeLayout();
    std::promise<vrrec_status_t> update_result;
    auto completed = update_result.get_future();
    std::thread update([&] {
        update_result.set_value(
            vrrec_session_update_video_layout_v1(session, &layout));
    });

    if (completed.wait_for(std::chrono::milliseconds(250)) !=
        std::future_status::ready) {
        update.detach();
        std::cerr << __func__
                  << " timed out waiting for reentrant fault callback\n";
        return false;
    }

    update.join();
    CHECK(completed.get() == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(log.events.size() == 1);
    CHECK(log.events[0].kind == VRREC_EVENT_FAULTED);
    CHECK(log.events[0].message == "layout update failed");
    CHECK(vrrec_session_update_video_layout_v1(session, &layout) ==
          VRREC_STATUS_INVALID_STATE);
    vrrec_session_destroy_v1(session);
    return true;
}

bool StopWaitsForAnInFlightLayoutUpdateWithoutHoldingTheStateLock()
{
    EventLog log;
    const auto config = ValidConfig();
    auto callbacks = ValidCallbacks(log);
    vrrec_session_t *session = nullptr;
    CHECK(vrrec_session_create_v1(&config, &callbacks, &session) ==
          VRREC_STATUS_OK);
    CHECK(vrrec_session_start_v1(session) == VRREC_STATUS_OK);
    vrrecorder::native::testing::BlockNextVideoLayoutUpdate();
    auto layout = ValidRuntimeLayout();
    auto updating = std::async(std::launch::async, [&] {
        return vrrec_session_update_video_layout_v1(session, &layout);
    });
    CHECK(vrrecorder::native::testing::WaitUntilVideoLayoutUpdateEntered(
        std::chrono::milliseconds(250)));
    auto stopping = std::async(std::launch::async, [&] {
        return vrrec_session_request_stop_v1(session);
    });

    CHECK(stopping.wait_for(std::chrono::milliseconds(50)) ==
          std::future_status::timeout);
    CHECK(vrrecorder::native::testing::RequestStopCallCount() == 0);
    vrrecorder::native::testing::ReleaseVideoLayoutUpdate();
    CHECK(updating.get() == VRREC_STATUS_OK);
    CHECK(stopping.get() == VRREC_STATUS_OK);
    CHECK(vrrecorder::native::testing::RequestStopCallCount() == 1);
    vrrec_session_destroy_v1(session);
    return true;
}

bool QueriesVersionedSessionStatistics()
{
    EventLog log;
    const auto config = ValidConfig();
    auto callbacks = ValidCallbacks(log);
    vrrec_session_t *session = nullptr;
    CHECK(vrrec_session_create_v1(&config, &callbacks, &session) ==
          VRREC_STATUS_OK);
    auto statistics = ValidStatisticsOutput();
    CHECK(vrrec_session_get_statistics_v1(session, &statistics) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(vrrec_session_start_v1(session) == VRREC_STATUS_OK);
    vrrecorder::native::testing::SetStatistics(
        vrrec_session_statistics_v1 {
            sizeof(vrrec_session_statistics_v1),
            VRREC_ABI_V1,
            120,
            90,
            142,
            30,
            4,
            2400,
            8000,
            -15000,
        });
    statistics = ValidStatisticsOutput();

    CHECK(vrrec_session_get_statistics_v1(session, &statistics) ==
          VRREC_STATUS_OK);
    CHECK(statistics.struct_size == sizeof(vrrec_session_statistics_v1));
    CHECK(statistics.abi_version == VRREC_ABI_V1);
    CHECK(statistics.source_video_frame_count == 120);
    CHECK(statistics.muxed_video_packet_count == 90);
    CHECK(statistics.muxed_audio_packet_count == 142);
    CHECK(statistics.dropped_source_video_frame_count == 30);
    CHECK(statistics.duplicated_output_video_frame_count == 4);
    CHECK(statistics.latest_encode_latency_microseconds == 2400);
    CHECK(statistics.maximum_encode_latency_microseconds == 8000);
    CHECK(statistics.audio_video_offset_microseconds == -15000);

    CHECK(vrrec_session_request_stop_v1(session) == VRREC_STATUS_OK);
    vrrecorder::native::testing::CompleteTrailerFlushClose(90, 142);
    statistics = ValidStatisticsOutput();
    CHECK(vrrec_session_get_statistics_v1(session, &statistics) ==
          VRREC_STATUS_OK);
    CHECK(statistics.muxed_video_packet_count == 90);
    CHECK(statistics.muxed_audio_packet_count == 142);
    CHECK(statistics.dropped_source_video_frame_count == 30);
    CHECK(statistics.duplicated_output_video_frame_count == 4);
    CHECK(statistics.maximum_encode_latency_microseconds == 8000);
    CHECK(statistics.audio_video_offset_microseconds == -15000);

    CHECK(vrrec_session_get_statistics_v1(nullptr, &statistics) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(vrrec_session_get_statistics_v1(session, nullptr) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    statistics = ValidStatisticsOutput();
    statistics.struct_size = sizeof(statistics) - 1;
    CHECK(vrrec_session_get_statistics_v1(session, &statistics) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    statistics = ValidStatisticsOutput();
    statistics.abi_version = VRREC_ABI_V1 + 1;
    CHECK(vrrec_session_get_statistics_v1(session, &statistics) ==
          VRREC_STATUS_UNSUPPORTED_ABI);
    CHECK(vrrec_session_abort_v1(session) == VRREC_STATUS_OK);
    statistics = ValidStatisticsOutput();
    CHECK(vrrec_session_get_statistics_v1(session, &statistics) ==
          VRREC_STATUS_INVALID_STATE);
    vrrec_session_destroy_v1(session);
    return true;
}

bool UpdatesAudioRoutingOnlyWhileSessionIsActive()
{
    using vrrecorder::native::testing::AudioRouting;
    using vrrecorder::native::testing::AudioRoutingUpdateCount;

    EventLog log;
    const auto config = ValidConfig();
    auto callbacks = ValidCallbacks(log);
    vrrec_session_t *session = nullptr;
    CHECK(vrrec_session_create_v1(&config, &callbacks, &session) ==
          VRREC_STATUS_OK);
    auto update = ValidAudioRoutingUpdate();
    CHECK(vrrec_session_update_audio_routing_v1(session, &update) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(vrrec_session_start_v1(session) == VRREC_STATUS_OK);

    for (const auto routing : {
             VRREC_AUDIO_ROUTING_DESKTOP_ONLY,
             VRREC_AUDIO_ROUTING_MIC_ONLY,
             VRREC_AUDIO_ROUTING_MUTED,
             VRREC_AUDIO_ROUTING_MIXED,
         }) {
        update.audio_routing = routing;
        CHECK(vrrec_session_update_audio_routing_v1(session, &update) ==
              VRREC_STATUS_OK);
        CHECK(AudioRouting() == routing);
    }

    CHECK(AudioRoutingUpdateCount() == 4);
    CHECK(vrrec_session_update_audio_routing_v1(nullptr, &update) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(vrrec_session_update_audio_routing_v1(session, nullptr) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    update = ValidAudioRoutingUpdate();
    update.struct_size = sizeof(update) - 1;
    CHECK(vrrec_session_update_audio_routing_v1(session, &update) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    update = ValidAudioRoutingUpdate();
    update.abi_version = VRREC_ABI_V1 + 1;
    CHECK(vrrec_session_update_audio_routing_v1(session, &update) ==
          VRREC_STATUS_UNSUPPORTED_ABI);
    update = ValidAudioRoutingUpdate();
    update.audio_routing = UINT32_MAX;
    CHECK(vrrec_session_update_audio_routing_v1(session, &update) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    update = ValidAudioRoutingUpdate();
    update.reserved = 1;
    CHECK(vrrec_session_update_audio_routing_v1(session, &update) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(AudioRoutingUpdateCount() == 4);

    CHECK(vrrec_session_request_stop_v1(session) == VRREC_STATUS_OK);
    update = ValidAudioRoutingUpdate();
    CHECK(vrrec_session_update_audio_routing_v1(session, &update) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(AudioRoutingUpdateCount() == 4);
    vrrec_session_destroy_v1(session);
    return true;
}

bool SynchronousAudioRoutingFaultDoesNotDeadlockTheAbi()
{
    EventLog log;
    const auto config = ValidConfig();
    auto callbacks = ValidCallbacks(log);
    vrrec_session_t *session = nullptr;
    CHECK(vrrec_session_create_v1(&config, &callbacks, &session) ==
          VRREC_STATUS_OK);
    CHECK(vrrec_session_start_v1(session) == VRREC_STATUS_OK);
    vrrecorder::native::testing::FaultDuringNextAudioRoutingUpdate();
    auto update = ValidAudioRoutingUpdate();
    std::promise<vrrec_status_t> update_result;
    auto completed = update_result.get_future();
    std::thread updating([&] {
        update_result.set_value(
            vrrec_session_update_audio_routing_v1(session, &update));
    });

    if (completed.wait_for(std::chrono::milliseconds(250)) !=
        std::future_status::ready) {
        updating.detach();
        std::cerr << __func__
                  << " timed out waiting for reentrant fault callback\n";
        return false;
    }

    updating.join();
    CHECK(completed.get() == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(log.events.size() == 1);
    CHECK(log.events[0].kind == VRREC_EVENT_FAULTED);
    CHECK(log.events[0].message == "audio routing update failed");
    CHECK(vrrec_session_update_audio_routing_v1(session, &update) ==
          VRREC_STATUS_INVALID_STATE);
    vrrec_session_destroy_v1(session);
    return true;
}

bool StopWaitsForAnInFlightAudioRoutingUpdate()
{
    EventLog log;
    const auto config = ValidConfig();
    auto callbacks = ValidCallbacks(log);
    vrrec_session_t *session = nullptr;
    CHECK(vrrec_session_create_v1(&config, &callbacks, &session) ==
          VRREC_STATUS_OK);
    CHECK(vrrec_session_start_v1(session) == VRREC_STATUS_OK);
    vrrecorder::native::testing::BlockNextAudioRoutingUpdate();
    auto update = ValidAudioRoutingUpdate();
    auto updating = std::async(std::launch::async, [&] {
        return vrrec_session_update_audio_routing_v1(session, &update);
    });
    CHECK(vrrecorder::native::testing::WaitUntilAudioRoutingUpdateEntered(
        std::chrono::milliseconds(250)));
    auto stopping = std::async(std::launch::async, [&] {
        return vrrec_session_request_stop_v1(session);
    });

    CHECK(stopping.wait_for(std::chrono::milliseconds(50)) ==
          std::future_status::timeout);
    CHECK(vrrecorder::native::testing::RequestStopCallCount() == 0);
    CHECK(vrrec_session_update_audio_routing_v1(session, &update) ==
          VRREC_STATUS_INVALID_STATE);
    vrrecorder::native::testing::ReleaseAudioRoutingUpdate();
    CHECK(updating.get() == VRREC_STATUS_OK);
    CHECK(stopping.get() == VRREC_STATUS_OK);
    CHECK(vrrecorder::native::testing::RequestStopCallCount() == 1);
    vrrec_session_destroy_v1(session);
    return true;
}

bool FaultIsTerminalAndAbortQuiescesCallbacks()
{
    EventLog fault_log;
    auto config = ValidConfig();
    auto callbacks = ValidCallbacks(fault_log);
    vrrec_session_t *session = nullptr;
    CHECK(vrrec_session_create_v1(&config, &callbacks, &session) ==
          VRREC_STATUS_OK);
    CHECK(vrrec_session_start_v1(session) == VRREC_STATUS_OK);

    vrrecorder::native::testing::Fail(
        VRREC_STATUS_INTERNAL_ERROR,
        "encoder failed");
    vrrecorder::native::testing::CommitMuxedVideoPacket();
    CHECK(fault_log.events.size() == 1);
    CHECK(fault_log.events[0].kind == VRREC_EVENT_FAULTED);
    CHECK(fault_log.events[0].status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(fault_log.events[0].message == "encoder failed");
    vrrec_session_destroy_v1(session);

    EventLog abort_log;
    callbacks = ValidCallbacks(abort_log);
    session = nullptr;
    CHECK(vrrec_session_create_v1(&config, &callbacks, &session) ==
          VRREC_STATUS_OK);
    CHECK(vrrec_session_start_v1(session) == VRREC_STATUS_OK);
    CHECK(vrrec_session_abort_v1(session) == VRREC_STATUS_OK);
    CHECK(vrrec_session_abort_v1(session) == VRREC_STATUS_OK);
    vrrecorder::native::testing::CommitMuxedVideoPacket();
    vrrecorder::native::testing::Fail(
        VRREC_STATUS_INTERNAL_ERROR,
        "ignored");
    CHECK(abort_log.events.empty());
    vrrec_session_destroy_v1(session);
    vrrec_session_destroy_v1(nullptr);
    return true;
}

bool RejectsInvalidSteamVrAbiInputs()
{
    auto config = ValidSteamVrConfig();
    auto *input = reinterpret_cast<vrrec_steamvr_input_t *>(UINTPTR_MAX);
    CHECK(vrrec_steamvr_input_create_v1(nullptr, &input) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(input == nullptr);

    config.struct_size = sizeof(config) - 1;
    CHECK(vrrec_steamvr_input_create_v1(&config, &input) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(input == nullptr);

    config = ValidSteamVrConfig();
    config.abi_version = VRREC_ABI_V1 + 1;
    CHECK(vrrec_steamvr_input_create_v1(&config, &input) ==
          VRREC_STATUS_UNSUPPORTED_ABI);
    CHECK(input == nullptr);

    config = ValidSteamVrConfig();
    config.action_manifest_path_utf8 = "relative/actions.json";
    CHECK(vrrec_steamvr_input_create_v1(&config, &input) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(input == nullptr);
    CHECK(vrrec_steamvr_input_poll_v1(nullptr, nullptr) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    vrrec_steamvr_input_destroy_v1(nullptr);
    return true;
}

bool PollsSteamVrDigitalStateThroughVersionedAbi()
{
    auto config = ValidSteamVrConfig();
    vrrec_steamvr_input_t *input = nullptr;
    CHECK(vrrec_steamvr_input_create_v1(&config, &input) ==
          VRREC_STATUS_OK);
    CHECK(input != nullptr);
    CHECK(vrrecorder::native::testing::SteamVrManifestPath() ==
          config.action_manifest_path_utf8);
    CHECK(vrrecorder::native::testing::SteamVrActionSetPath() ==
          config.action_set_path_utf8);
    CHECK(vrrecorder::native::testing::SteamVrDigitalActionPath() ==
          config.digital_action_path_utf8);

    vrrecorder::native::testing::SetSteamVrDigitalState(
        true,
        true,
        true);
    vrrec_steamvr_digital_state_v1 state {
        sizeof(vrrec_steamvr_digital_state_v1),
        VRREC_ABI_V1,
        0,
        0,
        0,
        0,
    };
    CHECK(vrrec_steamvr_input_poll_v1(input, &state) == VRREC_STATUS_OK);
    CHECK(state.is_active == 1);
    CHECK(state.state == 1);
    CHECK(state.changed == 1);
    CHECK(state.reserved == 0);
    CHECK(vrrecorder::native::testing::SteamVrPollCount() == 1);

    state.struct_size = sizeof(state) - 1;
    CHECK(vrrec_steamvr_input_poll_v1(input, &state) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    state.struct_size = sizeof(state);
    state.abi_version = VRREC_ABI_V1 + 1;
    CHECK(vrrec_steamvr_input_poll_v1(input, &state) ==
          VRREC_STATUS_UNSUPPORTED_ABI);
    vrrec_steamvr_input_destroy_v1(input);
    return true;
}

bool RejectsInvalidSpoutSourceAbiInputs()
{
    using vrrecorder::native::testing::ResetSpoutSource;
    using vrrecorder::native::testing::SetSpoutSnapshot;
    using vrrecorder::native::testing::TestSpoutSenderSnapshot;

    ResetSpoutSource();
    auto config = ValidSpoutSourceConfig();
    auto *source = reinterpret_cast<vrrec_spout_source_t *>(UINTPTR_MAX);
    CHECK(vrrec_spout_source_create_v1(nullptr, &source) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(source == nullptr);
    CHECK(vrrec_spout_source_create_v1(&config, nullptr) ==
          VRREC_STATUS_INVALID_ARGUMENT);

    config.struct_size = sizeof(config) - 1;
    CHECK(vrrec_spout_source_create_v1(&config, &source) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(source == nullptr);
    config = ValidSpoutSourceConfig();
    config.abi_version = VRREC_ABI_V1 + 1;
    CHECK(vrrec_spout_source_create_v1(&config, &source) ==
          VRREC_STATUS_UNSUPPORTED_ABI);
    CHECK(source == nullptr);
    config = ValidSpoutSourceConfig();
    config.reserved_v1 = 1;
    CHECK(vrrec_spout_source_create_v1(&config, &source) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(source == nullptr);

    config = ValidSpoutSourceConfig();
    CHECK(vrrec_spout_source_create_v1(&config, &source) ==
          VRREC_STATUS_OK);
    CHECK(source != nullptr);
    SetSpoutSnapshot({TestSpoutSenderSnapshot {"sender", 1}});
    std::uint32_t entry_count = 0;
    std::uint32_t utf8_size = 0;
    auto entry = ValidSpoutSnapshotOutput();
    char byte = '\0';
    CHECK(vrrec_spout_source_snapshot_v1(
              nullptr,
              nullptr,
              0,
              nullptr,
              0,
              &entry_count,
              &utf8_size) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(vrrec_spout_source_snapshot_v1(
              source,
              nullptr,
              0,
              nullptr,
              0,
              nullptr,
              &utf8_size) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(vrrec_spout_source_snapshot_v1(
              source,
              nullptr,
              0,
              nullptr,
              0,
              &entry_count,
              nullptr) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(vrrec_spout_source_snapshot_v1(
              source,
              nullptr,
              1,
              nullptr,
              0,
              &entry_count,
              &utf8_size) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(vrrec_spout_source_snapshot_v1(
              source,
              &entry,
              1,
              nullptr,
              1,
              &entry_count,
              &utf8_size) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(vrrec_spout_source_snapshot_v1(
              source,
              &entry,
              VRREC_SPOUT_MAX_SNAPSHOT_ENTRIES + 1,
              &byte,
              1,
              &entry_count,
              &utf8_size) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(vrrec_spout_source_snapshot_v1(
              source,
              &entry,
              1,
              &byte,
              VRREC_SPOUT_MAX_UTF8_BUFFER_SIZE + 1,
              &entry_count,
              &utf8_size) == VRREC_STATUS_INVALID_ARGUMENT);

    entry.struct_size = sizeof(entry) - 1;
    CHECK(vrrec_spout_source_snapshot_v1(
              source,
              &entry,
              1,
              &byte,
              1,
              &entry_count,
              &utf8_size) == VRREC_STATUS_INVALID_ARGUMENT);
    entry = ValidSpoutSnapshotOutput();
    entry.abi_version = VRREC_ABI_V1 + 1;
    CHECK(vrrec_spout_source_snapshot_v1(
              source,
              &entry,
              1,
              &byte,
              1,
              &entry_count,
              &utf8_size) == VRREC_STATUS_UNSUPPORTED_ABI);

    auto frame = ValidSpoutFrameOutput();
    std::uint32_t required_size = 0;
    CHECK(vrrec_spout_source_poll_frame_v1(
              nullptr,
              0,
              &frame,
              nullptr,
              0,
              &required_size) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(vrrec_spout_source_poll_frame_v1(
              source,
              0,
              nullptr,
              nullptr,
              0,
              &required_size) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(vrrec_spout_source_poll_frame_v1(
              source,
              0,
              &frame,
              nullptr,
              0,
              nullptr) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(vrrec_spout_source_poll_frame_v1(
              source,
              VRREC_SPOUT_MAX_POLL_TIMEOUT_MILLISECONDS + 1,
              &frame,
              nullptr,
              0,
              &required_size) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(vrrec_spout_source_poll_frame_v1(
              source,
              0,
              &frame,
              nullptr,
              1,
              &required_size) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(vrrec_spout_source_poll_frame_v1(
              source,
              0,
              &frame,
              &byte,
              VRREC_SPOUT_MAX_UTF8_BUFFER_SIZE + 1,
              &required_size) == VRREC_STATUS_INVALID_ARGUMENT);
    frame.struct_size = sizeof(frame) - 1;
    CHECK(vrrec_spout_source_poll_frame_v1(
              source,
              0,
              &frame,
              nullptr,
              0,
              &required_size) == VRREC_STATUS_INVALID_ARGUMENT);
    frame = ValidSpoutFrameOutput();
    frame.abi_version = VRREC_ABI_V1 + 1;
    CHECK(vrrec_spout_source_poll_frame_v1(
              source,
              0,
              &frame,
              nullptr,
              0,
              &required_size) == VRREC_STATUS_UNSUPPORTED_ABI);

    vrrec_spout_source_destroy_v1(&source);
    CHECK(source == nullptr);
    vrrec_spout_source_destroy_v1(&source);
    vrrec_spout_source_destroy_v1(nullptr);
    return true;
}

bool SnapshotsPackedUtf8WithRequiredSizing()
{
    using vrrecorder::native::testing::ResetSpoutSource;
    using vrrecorder::native::testing::SetSpoutSnapshot;
    using vrrecorder::native::testing::TestSpoutSenderSnapshot;

    ResetSpoutSource();
    const std::string first_id = "VRChat-Spout-Sender-42";
    const std::string second_id = "VRChat-\xE9\x80\x81\xE4\xBF\xA1";
    SetSpoutSnapshot({
        TestSpoutSenderSnapshot {first_id, 41},
        TestSpoutSenderSnapshot {second_id, 92},
    });
    auto config = ValidSpoutSourceConfig();
    vrrec_spout_source_t *source = nullptr;
    CHECK(vrrec_spout_source_create_v1(&config, &source) ==
          VRREC_STATUS_OK);

    std::uint32_t entry_count = 0;
    std::uint32_t utf8_size = 0;
    CHECK(vrrec_spout_source_snapshot_v1(
              source,
              nullptr,
              0,
              nullptr,
              0,
              &entry_count,
              &utf8_size) == VRREC_STATUS_BUFFER_TOO_SMALL);
    CHECK(entry_count == 2);
    CHECK(utf8_size == first_id.size() + second_id.size());

    std::vector<vrrec_spout_sender_snapshot_v1> short_entries(
        entry_count - 1,
        ValidSpoutSnapshotOutput());
    short_entries[0].sender_id_offset = UINT32_MAX;
    std::vector<char> utf8_buffer(utf8_size, '#');
    CHECK(vrrec_spout_source_snapshot_v1(
              source,
              short_entries.data(),
              static_cast<std::uint32_t>(short_entries.size()),
              utf8_buffer.data(),
              static_cast<std::uint32_t>(utf8_buffer.size()),
              &entry_count,
              &utf8_size) == VRREC_STATUS_BUFFER_TOO_SMALL);
    CHECK(short_entries[0].sender_id_offset == UINT32_MAX);
    CHECK(utf8_buffer.front() == '#');

    std::vector<vrrec_spout_sender_snapshot_v1> entries(
        entry_count,
        ValidSpoutSnapshotOutput());
    entries[0].sender_id_offset = UINT32_MAX;
    std::vector<char> short_buffer(utf8_size - 1, '#');
    CHECK(vrrec_spout_source_snapshot_v1(
              source,
              entries.data(),
              static_cast<std::uint32_t>(entries.size()),
              short_buffer.data(),
              static_cast<std::uint32_t>(short_buffer.size()),
              &entry_count,
              &utf8_size) == VRREC_STATUS_BUFFER_TOO_SMALL);
    CHECK(entries[0].sender_id_offset == UINT32_MAX);
    CHECK(short_buffer.front() == '#');

    CHECK(vrrec_spout_source_snapshot_v1(
              source,
              entries.data(),
              static_cast<std::uint32_t>(entries.size()),
              utf8_buffer.data(),
              static_cast<std::uint32_t>(utf8_buffer.size()),
              &entry_count,
              &utf8_size) == VRREC_STATUS_OK);
    CHECK(entries[0].struct_size == sizeof(entries[0]));
    CHECK(entries[0].abi_version == VRREC_ABI_V1);
    CHECK(entries[0].sender_id_offset == 0);
    CHECK(entries[0].sender_id_size == first_id.size());
    CHECK(entries[0].latest_frame_generation == 41);
    CHECK(entries[1].sender_id_offset == first_id.size());
    CHECK(entries[1].sender_id_size == second_id.size());
    CHECK(entries[1].latest_frame_generation == 92);
    CHECK(std::string(
              utf8_buffer.data() + entries[0].sender_id_offset,
              entries[0].sender_id_size) == first_id);
    CHECK(std::string(
              utf8_buffer.data() + entries[1].sender_id_offset,
              entries[1].sender_id_size) == second_id);

    SetSpoutSnapshot({});
    entry_count = UINT32_MAX;
    utf8_size = UINT32_MAX;
    CHECK(vrrec_spout_source_snapshot_v1(
              source,
              nullptr,
              0,
              nullptr,
              0,
              &entry_count,
              &utf8_size) == VRREC_STATUS_OK);
    CHECK(entry_count == 0);
    CHECK(utf8_size == 0);

    vrrec_spout_source_destroy_v1(&source);
    return true;
}

bool PollsFrameWithoutConsumingOnBufferRetry()
{
    using vrrecorder::native::testing::PushSpoutFrame;
    using vrrecorder::native::testing::ResetSpoutSource;

    ResetSpoutSource();
    auto expected = ValidTestSpoutFrame();
    PushSpoutFrame(expected);
    auto config = ValidSpoutSourceConfig();
    vrrec_spout_source_t *source = nullptr;
    CHECK(vrrec_spout_source_create_v1(&config, &source) ==
          VRREC_STATUS_OK);

    auto frame = ValidSpoutFrameOutput();
    frame.sender_id_offset = UINT32_MAX;
    std::uint32_t required_size = 0;
    CHECK(vrrec_spout_source_poll_frame_v1(
              source,
              0,
              &frame,
              nullptr,
              0,
              &required_size) == VRREC_STATUS_BUFFER_TOO_SMALL);
    CHECK(required_size ==
          expected.sender_id.size() + expected.gpu_identity.size());
    CHECK(frame.sender_id_offset == UINT32_MAX);

    std::vector<char> short_buffer(required_size - 1, '#');
    CHECK(vrrec_spout_source_poll_frame_v1(
              source,
              0,
              &frame,
              short_buffer.data(),
              static_cast<std::uint32_t>(short_buffer.size()),
              &required_size) == VRREC_STATUS_BUFFER_TOO_SMALL);
    CHECK(frame.sender_id_offset == UINT32_MAX);
    CHECK(short_buffer.front() == '#');

    char byte = '#';
    CHECK(vrrec_spout_source_poll_frame_v1(
              source,
              0,
              &frame,
              &byte,
              VRREC_SPOUT_MAX_UTF8_BUFFER_SIZE + 1,
              &required_size) == VRREC_STATUS_INVALID_ARGUMENT);

    std::vector<char> buffer(required_size, '#');
    CHECK(vrrec_spout_source_poll_frame_v1(
              source,
              0,
              &frame,
              buffer.data(),
              static_cast<std::uint32_t>(buffer.size()),
              &required_size) == VRREC_STATUS_OK);
    CHECK(frame.sender_id_offset == 0);
    CHECK(frame.sender_id_size == expected.sender_id.size());
    CHECK(frame.gpu_identity_offset == expected.sender_id.size());
    CHECK(frame.gpu_identity_size == expected.gpu_identity.size());
    CHECK(frame.adapter_luid == expected.adapter_luid);
    CHECK(frame.gpu_vendor == expected.gpu_vendor);
    CHECK(frame.width == expected.width);
    CHECK(frame.height == expected.height);
    CHECK(frame.pixel_format == expected.pixel_format);
    CHECK(frame.estimated_source_fps == expected.estimated_source_fps);
    CHECK(frame.frame_sequence == expected.frame_sequence);
    CHECK(frame.monotonic_timestamp_microseconds ==
          expected.monotonic_timestamp_microseconds);
    CHECK(frame.reserved == 0);
    CHECK(std::string(
              buffer.data() + frame.sender_id_offset,
              frame.sender_id_size) == expected.sender_id);
    CHECK(std::string(
              buffer.data() + frame.gpu_identity_offset,
              frame.gpu_identity_size) == expected.gpu_identity);

    frame = ValidSpoutFrameOutput();
    required_size = UINT32_MAX;
    CHECK(vrrec_spout_source_poll_frame_v1(
              source,
              0,
              &frame,
              nullptr,
              0,
              &required_size) == VRREC_STATUS_TIMEOUT);
    CHECK(required_size == 0);
    vrrec_spout_source_destroy_v1(&source);
    return true;
}

bool RejectsMalformedOrOversizeSpoutBackendData()
{
    using vrrecorder::native::testing::PushSpoutFrame;
    using vrrecorder::native::testing::ResetSpoutSource;
    using vrrecorder::native::testing::SetSpoutSnapshot;
    using vrrecorder::native::testing::TestSpoutFrame;
    using vrrecorder::native::testing::TestSpoutSenderSnapshot;

    ResetSpoutSource();
    auto config = ValidSpoutSourceConfig();
    vrrec_spout_source_t *source = nullptr;
    CHECK(vrrec_spout_source_create_v1(&config, &source) ==
          VRREC_STATUS_OK);

    const auto rejects_snapshot = [&](std::string sender_id) {
        SetSpoutSnapshot({TestSpoutSenderSnapshot {
            std::move(sender_id),
            1,
        }});
        std::uint32_t entry_count = 0;
        std::uint32_t utf8_size = 0;
        return vrrec_spout_source_snapshot_v1(
            source,
            nullptr,
            0,
            nullptr,
            0,
            &entry_count,
            &utf8_size) == VRREC_STATUS_INTERNAL_ERROR;
    };
    CHECK(rejects_snapshot(std::string("\xC3\x28", 2)));
    CHECK(rejects_snapshot(std::string("a\0b", 3)));
    CHECK(rejects_snapshot(std::string(
        VRREC_SPOUT_MAX_IDENTITY_UTF8_SIZE + 1,
        'a')));

    std::vector<TestSpoutSenderSnapshot> too_many_senders;
    too_many_senders.reserve(VRREC_SPOUT_MAX_SNAPSHOT_ENTRIES + 1);
    for (std::uint32_t index = 0;
         index <= VRREC_SPOUT_MAX_SNAPSHOT_ENTRIES;
         ++index) {
        too_many_senders.push_back(TestSpoutSenderSnapshot {
            "sender-" + std::to_string(index),
            index,
        });
    }
    SetSpoutSnapshot(std::move(too_many_senders));
    std::uint32_t entry_count = 0;
    std::uint32_t utf8_size = 0;
    CHECK(vrrec_spout_source_snapshot_v1(
              source,
              nullptr,
              0,
              nullptr,
              0,
              &entry_count,
              &utf8_size) == VRREC_STATUS_INTERNAL_ERROR);

    const auto rejects_frame = [&](TestSpoutFrame invalid) {
        PushSpoutFrame(std::move(invalid));
        auto frame = ValidSpoutFrameOutput();
        std::vector<char> buffer(VRREC_SPOUT_MAX_UTF8_BUFFER_SIZE);
        std::uint32_t required_size = 0;
        return vrrec_spout_source_poll_frame_v1(
            source,
            0,
            &frame,
            buffer.data(),
            static_cast<std::uint32_t>(buffer.size()),
            &required_size) == VRREC_STATUS_INTERNAL_ERROR;
    };
    auto invalid = ValidTestSpoutFrame();
    invalid.sender_id = std::string("\xC3\x28", 2);
    CHECK(rejects_frame(invalid));
    invalid = ValidTestSpoutFrame();
    invalid.gpu_identity = std::string("gpu\0identity", 12);
    CHECK(rejects_frame(invalid));
    invalid = ValidTestSpoutFrame();
    invalid.gpu_identity = std::string(
        VRREC_SPOUT_MAX_IDENTITY_UTF8_SIZE + 1,
        'g');
    CHECK(rejects_frame(invalid));
    invalid = ValidTestSpoutFrame();
    invalid.adapter_luid = 0;
    CHECK(rejects_frame(invalid));
    invalid = ValidTestSpoutFrame();
    invalid.gpu_vendor = VRREC_GPU_VENDOR_INTEL + 1;
    CHECK(rejects_frame(invalid));
    invalid = ValidTestSpoutFrame();
    invalid.width = 0;
    CHECK(rejects_frame(invalid));
    invalid = ValidTestSpoutFrame();
    invalid.height = 0;
    CHECK(rejects_frame(invalid));
    invalid = ValidTestSpoutFrame();
    invalid.width = static_cast<std::uint32_t>(INT32_MAX) + 1U;
    CHECK(rejects_frame(invalid));
    invalid = ValidTestSpoutFrame();
    invalid.pixel_format = VRREC_SOURCE_PIXEL_FORMAT_NV12 + 1;
    CHECK(rejects_frame(invalid));
    invalid = ValidTestSpoutFrame();
    invalid.estimated_source_fps =
        std::numeric_limits<double>::quiet_NaN();
    CHECK(rejects_frame(invalid));
    invalid = ValidTestSpoutFrame();
    invalid.estimated_source_fps = 0.0;
    CHECK(rejects_frame(invalid));
    invalid = ValidTestSpoutFrame();
    invalid.estimated_source_fps = 1000.01;
    CHECK(rejects_frame(invalid));
    invalid = ValidTestSpoutFrame();
    invalid.monotonic_timestamp_microseconds = -1;
    CHECK(rejects_frame(invalid));
    invalid = ValidTestSpoutFrame();
    invalid.monotonic_timestamp_microseconds =
        INT64_C(922337203685477581);
    CHECK(rejects_frame(invalid));

    vrrec_spout_source_destroy_v1(&source);
    return true;
}

bool DestroyWaitsForActiveSpoutPollAndIsIdempotent()
{
    using namespace std::chrono_literals;
    using vrrecorder::native::testing::ActiveSpoutSourceCount;
    using vrrecorder::native::testing::BlockNextSpoutPoll;
    using vrrecorder::native::testing::ReleaseSpoutPoll;
    using vrrecorder::native::testing::ResetSpoutSource;
    using vrrecorder::native::testing::SpoutSourceDestroyCount;
    using vrrecorder::native::testing::WaitUntilSpoutPollEntered;

    ResetSpoutSource();
    auto config = ValidSpoutSourceConfig();
    vrrec_spout_source_t *source = nullptr;
    CHECK(vrrec_spout_source_create_v1(&config, &source) ==
          VRREC_STATUS_OK);
    CHECK(ActiveSpoutSourceCount() == 1);
    BlockNextSpoutPoll();
    auto *poll_source = source;
    auto polling = std::async(std::launch::async, [poll_source] {
        auto frame = ValidSpoutFrameOutput();
        std::vector<char> buffer(256);
        std::uint32_t required_size = 0;
        return vrrec_spout_source_poll_frame_v1(
            poll_source,
            10,
            &frame,
            buffer.data(),
            static_cast<std::uint32_t>(buffer.size()),
            &required_size);
    });
    CHECK(WaitUntilSpoutPollEntered(1s));

    auto destroying = std::async(std::launch::async, [&source] {
        vrrec_spout_source_destroy_v1(&source);
    });
    CHECK(destroying.wait_for(25ms) == std::future_status::timeout);
    ReleaseSpoutPoll();
    CHECK(polling.get() == VRREC_STATUS_TIMEOUT);
    CHECK(destroying.wait_for(1s) == std::future_status::ready);
    destroying.get();
    CHECK(source == nullptr);
    CHECK(ActiveSpoutSourceCount() == 0);
    CHECK(SpoutSourceDestroyCount() == 1);
    vrrec_spout_source_destroy_v1(&source);
    vrrec_spout_source_destroy_v1(nullptr);
    CHECK(SpoutSourceDestroyCount() == 1);
    return true;
}

bool ProbesSixteenFramesAndReportsOnlyProducedPacket()
{
    using vrrecorder::native::testing::EncoderProbeCallCount;
    using vrrecorder::native::testing::EncoderProbeConfig;
    using vrrecorder::native::testing::ResetEncoderProbe;
    using vrrecorder::native::testing::SetEncoderProbeResult;

    ResetEncoderProbe();
    auto config = ValidEncoderProbeConfig();
    std::uint8_t packet_produced = 0;
    SetEncoderProbeResult(VRREC_STATUS_OK, true);
    CHECK(vrrec_encoder_probe_v1(&config, &packet_produced) ==
          VRREC_STATUS_OK);
    CHECK(packet_produced == 1);
    CHECK(EncoderProbeCallCount() == 1);
    const auto observed = EncoderProbeConfig();
    CHECK(observed.encoder_kind == VRREC_ENCODER_NVENC);
    CHECK(observed.synthetic_frame_count == 16);
    CHECK(observed.adapter_luid == config.adapter_luid);
    CHECK(observed.width == 1920);
    CHECK(observed.height == 1080);
    CHECK(observed.fps_numerator == 60);
    CHECK(observed.fps_denominator == 1);
    CHECK(observed.gpu_identity == config.gpu_identity_utf8);

    SetEncoderProbeResult(VRREC_STATUS_OK, false);
    packet_produced = UINT8_MAX;
    CHECK(vrrec_encoder_probe_v1(&config, &packet_produced) ==
          VRREC_STATUS_OK);
    CHECK(packet_produced == 0);

    SetEncoderProbeResult(VRREC_STATUS_BACKEND_UNAVAILABLE, true);
    packet_produced = UINT8_MAX;
    CHECK(vrrec_encoder_probe_v1(&config, &packet_produced) ==
          VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(packet_produced == 0);
    return true;
}

bool RejectsInvalidEncoderProbeAbiInputs()
{
    using vrrecorder::native::testing::EncoderProbeCallCount;
    using vrrecorder::native::testing::ResetEncoderProbe;

    ResetEncoderProbe();
    auto config = ValidEncoderProbeConfig();
    std::uint8_t packet_produced = UINT8_MAX;
    CHECK(vrrec_encoder_probe_v1(nullptr, &packet_produced) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(packet_produced == 0);
    CHECK(vrrec_encoder_probe_v1(&config, nullptr) ==
          VRREC_STATUS_INVALID_ARGUMENT);

    const auto rejects = [&](vrrec_encoder_probe_config_v1 invalid,
                             vrrec_status_t expected =
                                 VRREC_STATUS_INVALID_ARGUMENT) {
        packet_produced = UINT8_MAX;
        const auto status = vrrec_encoder_probe_v1(
            &invalid,
            &packet_produced);
        return status == expected && packet_produced == 0;
    };
    config.struct_size = sizeof(config) - 1;
    CHECK(rejects(config));
    config = ValidEncoderProbeConfig();
    config.abi_version = VRREC_ABI_V1 + 1;
    CHECK(rejects(config, VRREC_STATUS_UNSUPPORTED_ABI));
    config = ValidEncoderProbeConfig();
    config.encoder_kind = UINT32_MAX;
    CHECK(rejects(config));
    config = ValidEncoderProbeConfig();
    config.synthetic_frame_count = 15;
    CHECK(rejects(config));
    config = ValidEncoderProbeConfig();
    config.adapter_luid = 0;
    CHECK(rejects(config));
    config = ValidEncoderProbeConfig();
    config.width = 0;
    CHECK(rejects(config));
    config = ValidEncoderProbeConfig();
    config.width = 1919;
    CHECK(rejects(config));
    config = ValidEncoderProbeConfig();
    config.height = UINT32_MAX;
    CHECK(rejects(config));
    config = ValidEncoderProbeConfig();
    config.fps_numerator = 29;
    CHECK(rejects(config));
    config = ValidEncoderProbeConfig();
    config.fps_numerator = 121;
    CHECK(rejects(config));
    config = ValidEncoderProbeConfig();
    config.fps_denominator = 0;
    CHECK(rejects(config));
    config = ValidEncoderProbeConfig();
    config.gpu_identity_utf8 = " ";
    CHECK(rejects(config));
    const std::string malformed_utf8("\xC3\x28", 2);
    config = ValidEncoderProbeConfig();
    config.gpu_identity_utf8 = malformed_utf8.c_str();
    CHECK(rejects(config));
    const std::array<std::string, 12> malformed_utf8_boundaries {
        std::string("\xC2", 1),
        std::string("\xE1\x80", 2),
        std::string("\xE1\x80\x28", 3),
        std::string("\xE0\x9F\x80", 3),
        std::string("\xED\xA0\x80", 3),
        std::string("\xE1\x28\x80", 3),
        std::string("\xF1\x80\x80", 3),
        std::string("\xF1\x80\x80\x28", 4),
        std::string("\xF0\x8F\x80\x80", 4),
        std::string("\xF4\x90\x80\x80", 4),
        std::string("\xF1\x28\x80\x80", 4),
        std::string("\xFF", 1),
    };
    for (const auto &malformed : malformed_utf8_boundaries) {
        config = ValidEncoderProbeConfig();
        config.gpu_identity_utf8 = malformed.c_str();
        CHECK(rejects(config));
    }
    const std::string oversized_identity(4097, 'g');
    config = ValidEncoderProbeConfig();
    config.gpu_identity_utf8 = oversized_identity.c_str();
    CHECK(rejects(config));
    config = ValidEncoderProbeConfig();
    config.reserved = 1;
    CHECK(rejects(config));
    CHECK(EncoderProbeCallCount() == 0);
    return true;
}

}

int main()
{
    if (vrrec_abi_version() != VRREC_ABI_V1 ||
        !RejectsInvalidAbiInputs() ||
        !LegacySessionConfigDefaultsToSoftwareEncoder() ||
        !FullSessionConfigCrossesTheBackendBoundaryExactly() ||
        !LegacySessionConfigsReceiveDeterministicMediaDefaults() ||
        !LegacyMediaSessionConfigDefaultsSourceFormat() ||
        !RejectsTruncatedOrInvalidExtendedSessionConfig() ||
        !UpdatesStableLayoutWithoutChangingTheOutputCanvas() ||
        !RejectsInvalidRuntimeLayoutAbiInputs() ||
        !SynchronousLayoutFaultDoesNotDeadlockTheAbi() ||
        !StopWaitsForAnInFlightLayoutUpdateWithoutHoldingTheStateLock() ||
        !UpdatesAudioRoutingOnlyWhileSessionIsActive() ||
        !SynchronousAudioRoutingFaultDoesNotDeadlockTheAbi() ||
        !StopWaitsForAnInFlightAudioRoutingUpdate() ||
        !QueriesVersionedSessionStatistics() ||
        !EmitsMuxAndStoppedEventsOnlyAfterBackendMilestones() ||
        !CallbackCanAbortItsOwnSessionWithoutDeadlocking() ||
        !EmitsPrivacySafeNonterminalAudioDeviceEvents() ||
        !EmitsPrivacySafeNonterminalAudioBufferHealthEvents() ||
        !FaultIsTerminalAndAbortQuiescesCallbacks() ||
        !RejectsInvalidSteamVrAbiInputs() ||
        !PollsSteamVrDigitalStateThroughVersionedAbi() ||
        !RejectsInvalidSpoutSourceAbiInputs() ||
        !SnapshotsPackedUtf8WithRequiredSizing() ||
        !PollsFrameWithoutConsumingOnBufferRetry() ||
        !RejectsMalformedOrOversizeSpoutBackendData() ||
        !DestroyWaitsForActiveSpoutPollAndIsIdempotent() ||
        !ProbesSixteenFramesAndReportsOnlyProducedPacket() ||
        !RejectsInvalidEncoderProbeAbiInputs()) {
        return 1;
    }

    std::cout << "native ABI contract tests passed\n";
    return 0;
}
