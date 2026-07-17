#ifndef VRRECORDER_NATIVE_TEST_FAKE_MEDIA_BACKEND_HPP
#define VRRECORDER_NATIVE_TEST_FAKE_MEDIA_BACKEND_HPP

#include <cstdint>
#include <chrono>
#include <string>
#include <string_view>
#include <vector>

#include "vrrecorder_native.h"
#include "media_backend.hpp"

namespace vrrecorder::native::testing {

struct ObservedMediaSessionConfig {
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
    std::string desktop_endpoint_id;
    std::string microphone_endpoint_id;
    double desktop_gain_db;
    double microphone_gain_db;
    std::string spout_sender_identity;
    std::uint64_t spout_adapter_luid;
    std::uint64_t encoder_adapter_luid;
    std::string gpu_identity;
    std::uint32_t source_pixel_format;
    double estimated_source_fps;
};

struct ObservedEncoderProbeConfig {
    std::uint32_t encoder_kind;
    std::uint32_t synthetic_frame_count;
    std::uint64_t adapter_luid;
    std::uint32_t width;
    std::uint32_t height;
    std::uint32_t fps_numerator;
    std::uint32_t fps_denominator;
    std::string gpu_identity;
};

struct TestEncoderProbeEvidence {
    std::uint32_t actual_encoder_kind;
    bool hardware_accelerated;
    std::uint64_t adapter_luid;
    std::uint32_t opened_input_format;
    std::uint32_t width;
    std::uint32_t height;
    std::uint32_t fps_numerator;
    std::uint32_t fps_denominator;
    std::uint32_t validation_flags;
    std::string codec_name;
    std::string driver_identity;
    std::string ffmpeg_build_identity;
    std::string profile;
    std::string device_identity;
};

void CommitMuxedVideoPacket();
void BlockNextMediaStart(std::int32_t status);
bool WaitUntilMediaStartEntered(std::chrono::milliseconds timeout);
void ReleaseMediaStart();
void BlockNextMediaStop(std::int32_t status);
void FailNextMediaStop(std::int32_t status);
bool WaitUntilMediaStopEntered(std::chrono::milliseconds timeout);
void ReleaseMediaStop();
bool WaitUntilAbiStopWaiter(
    vrrec_session_t *session,
    std::chrono::milliseconds timeout) noexcept;
bool WaitUntilAbiStopInProgress(
    vrrec_session_t *session,
    std::chrono::milliseconds timeout) noexcept;
void BlockNextAbiStartCommit(vrrec_session_t *session) noexcept;
bool WaitUntilAbiStartCommit(
    vrrec_session_t *session,
    std::chrono::milliseconds timeout) noexcept;
void ReleaseAbiStartCommit(vrrec_session_t *session) noexcept;
bool WaitUntilMediaAbortRequested(std::chrono::milliseconds timeout);
void CompleteTrailerFlushClose(
    std::uint64_t video_packet_count,
    std::uint64_t audio_packet_count);
void Fail(std::int32_t status, std::string_view message);
void EmitVideoEncoderFailed(
    std::int32_t status,
    std::string_view message);
void EmitAvDrift(
    std::uint64_t video_pts_microseconds,
    std::uint64_t audio_pts_microseconds);
void EmitAudioBufferHealth(
    AudioEndpointRole role,
    AudioBufferHealth health,
    std::uint64_t frame_position);
void SetDesktopAudioEndpointAvailable(
    bool available,
    std::uint64_t frame_position);
void SetMicrophoneAudioEndpointAvailable(
    bool available,
    std::uint64_t frame_position);
std::uint32_t EncoderKind();
const ObservedMediaSessionConfig &SessionConfig();
const vrrec_video_layout_v1 &VideoLayout();
std::uint32_t VideoLayoutUpdateCount();
std::uint32_t AudioRouting();
std::uint32_t AudioRoutingUpdateCount();
void FaultDuringNextAudioRoutingUpdate();
void BlockNextAudioRoutingUpdate();
bool WaitUntilAudioRoutingUpdateEntered(std::chrono::milliseconds timeout);
void ReleaseAudioRoutingUpdate();
void SetStatistics(const vrrec_session_statistics_v1 &statistics);
void SetStatisticsStatus(std::int32_t status);
void FaultDuringNextVideoLayoutUpdate();
void BlockNextVideoLayoutUpdate();
bool WaitUntilVideoLayoutUpdateEntered(std::chrono::milliseconds timeout);
void ReleaseVideoLayoutUpdate();
std::uint32_t RequestStopCallCount();
std::uint32_t StatisticsCallCount();
std::uint32_t RequestAbortCallCount();
void SetSteamVrDigitalState(bool is_active, bool state, bool changed);
void SetSteamVrPollStatus(std::int32_t status);
std::string_view SteamVrManifestPath();
std::string_view SteamVrActionSetPath();
std::string_view SteamVrDigitalActionPath();
std::uint32_t SteamVrPollCount();
bool HasActiveSteamVrInput();
struct TestSteamVrHapticPulse {
    float duration_seconds;
    float frequency_hertz;
    float amplitude;
};
void SetSteamVrHapticStatus(std::int32_t status);
bool HasActiveSteamVrHaptic();
std::string_view SteamVrHapticManifestPath();
std::string_view SteamVrHapticActionPath();
std::string_view SteamVrHapticInputSourcePath();
std::uint32_t SteamVrHapticTriggerCount();
TestSteamVrHapticPulse SteamVrLastHapticPulse();
void ResetSteamVrOverlay();
bool HasActiveSteamVrOverlay();
bool IsSteamVrOverlayVisible();
std::string_view SteamVrOverlayManifestPath();
std::string_view SteamVrOverlayKey();
std::string_view SteamVrOverlayName();
float SteamVrOverlayWidthInMeters();
std::uint32_t SteamVrOverlayShowCount();
std::uint32_t SteamVrOverlayHideCount();
std::uint32_t SteamVrOverlayCloseCount();
std::uint32_t SteamVrOverlayDestroyCount();
std::uint32_t SteamVrOverlayTextureUpdateCount();
std::uint32_t SteamVrOverlayClearTextureCount();
std::uint8_t SteamVrOverlayTextureFirstByte();
std::uint8_t SteamVrOverlayTextureLastByte();
struct TestSteamVrOverlayPointerEvent {
    std::uint32_t kind;
    std::uint32_t pixel_x;
    std::uint32_t pixel_y;
    std::uint32_t button;
    std::uint32_t cursor_index;
};
void PushSteamVrOverlayPointerEvent(TestSteamVrOverlayPointerEvent event);
void SetSteamVrOverlayDeviceProfile(
    std::string tracking_system_name,
    std::string hmd_model_number,
    std::string controller_input_profile_path);
struct TestSpoutSenderSnapshot {
    std::string sender_id;
    std::uint64_t latest_frame_generation;
};
struct TestSpoutFrame {
    std::string sender_id;
    std::uint64_t adapter_luid;
    std::string gpu_identity;
    std::uint32_t gpu_vendor;
    std::uint32_t width;
    std::uint32_t height;
    std::uint32_t pixel_format;
    double estimated_source_fps;
    std::uint64_t frame_sequence;
    std::int64_t monotonic_timestamp_microseconds;
};
void ResetSpoutSource();
void SetSpoutSnapshot(std::vector<TestSpoutSenderSnapshot> senders);
void AddSpoutSnapshotSender(TestSpoutSenderSnapshot sender);
void PushSpoutFrame(TestSpoutFrame frame);
void BlockNextSpoutPoll();
bool WaitUntilSpoutPollEntered(std::chrono::milliseconds timeout);
void ReleaseSpoutPoll();
std::uint32_t ActiveSpoutSourceCount();
std::uint32_t SpoutSourceDestroyCount();
void ResetEncoderProbe();
void SetEncoderProbeResult(std::int32_t status, bool packet_produced);
void SetEncoderProbeEvidence(TestEncoderProbeEvidence evidence);
std::uint32_t EncoderProbeCallCount();
ObservedEncoderProbeConfig EncoderProbeConfig();
void BlockNextEncoderProbe();
bool WaitUntilEncoderProbeEntered(std::chrono::milliseconds timeout);
void ReleaseEncoderProbe();

}

#endif
