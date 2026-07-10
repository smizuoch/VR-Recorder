#include <cstdint>
#include <iostream>
#include <string>
#include <vector>

#include "fake_media_backend.hpp"
#include "vrrecorder_native.h"

static_assert(VRREC_ABI_V1 == 1);
static_assert(sizeof(vrrec_session_config_v1) == 40);
static_assert(sizeof(vrrec_event_v1) == 48);
static_assert(sizeof(vrrec_callbacks_v1) == 24);

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
    callbacks.struct_size = sizeof(callbacks) - 1;
    CHECK(vrrec_session_create_v1(&config, &callbacks, &session) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(session == nullptr);
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

}

int main()
{
    if (vrrec_abi_version() != VRREC_ABI_V1 ||
        !RejectsInvalidAbiInputs() ||
        !EmitsMuxAndStoppedEventsOnlyAfterBackendMilestones() ||
        !FaultIsTerminalAndAbortQuiescesCallbacks()) {
        return 1;
    }

    std::cout << "native ABI contract tests passed\n";
    return 0;
}
