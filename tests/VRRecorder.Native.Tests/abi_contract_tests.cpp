#include <chrono>
#include <cstdint>
#include <future>
#include <limits>
#include <iostream>
#include <string>
#include <thread>
#include <vector>

#include "fake_media_backend.hpp"
#include "vrrecorder_native.h"

static_assert(VRREC_ABI_V1 == 1);
static_assert(sizeof(vrrec_session_config_v1) == 160);
static_assert(sizeof(vrrec_video_layout_v1) == 48);
static_assert(sizeof(vrrec_session_statistics_v1) == 72);
static_assert(sizeof(vrrec_event_v1) == 48);
static_assert(sizeof(vrrec_callbacks_v1) == 24);
static_assert(sizeof(vrrec_steamvr_input_config_v1) == 32);
static_assert(sizeof(vrrec_steamvr_digital_state_v1) == 12);

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
    config = ValidConfig();
    config.struct_size = sizeof(config) - 1;
    CHECK(rejected(config));

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

}

int main()
{
    if (vrrec_abi_version() != VRREC_ABI_V1 ||
        !RejectsInvalidAbiInputs() ||
        !LegacySessionConfigDefaultsToSoftwareEncoder() ||
        !FullSessionConfigCrossesTheBackendBoundaryExactly() ||
        !LegacySessionConfigsReceiveDeterministicMediaDefaults() ||
        !RejectsTruncatedOrInvalidExtendedSessionConfig() ||
        !UpdatesStableLayoutWithoutChangingTheOutputCanvas() ||
        !RejectsInvalidRuntimeLayoutAbiInputs() ||
        !SynchronousLayoutFaultDoesNotDeadlockTheAbi() ||
        !StopWaitsForAnInFlightLayoutUpdateWithoutHoldingTheStateLock() ||
        !QueriesVersionedSessionStatistics() ||
        !EmitsMuxAndStoppedEventsOnlyAfterBackendMilestones() ||
        !FaultIsTerminalAndAbortQuiescesCallbacks() ||
        !RejectsInvalidSteamVrAbiInputs() ||
        !PollsSteamVrDigitalStateThroughVersionedAbi()) {
        return 1;
    }

    std::cout << "native ABI contract tests passed\n";
    return 0;
}
