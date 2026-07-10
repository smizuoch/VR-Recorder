#ifndef VRRECORDER_NATIVE_TEST_FAKE_MEDIA_BACKEND_HPP
#define VRRECORDER_NATIVE_TEST_FAKE_MEDIA_BACKEND_HPP

#include <cstdint>
#include <chrono>
#include <string>
#include <string_view>

#include "vrrecorder_native.h"

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

void CommitMuxedVideoPacket();
void CompleteTrailerFlushClose(
    std::uint64_t video_packet_count,
    std::uint64_t audio_packet_count);
void Fail(std::int32_t status, std::string_view message);
std::uint32_t EncoderKind();
const ObservedMediaSessionConfig &SessionConfig();
const vrrec_video_layout_v1 &VideoLayout();
std::uint32_t VideoLayoutUpdateCount();
void SetStatistics(const vrrec_session_statistics_v1 &statistics);
void SetStatisticsStatus(std::int32_t status);
void FaultDuringNextVideoLayoutUpdate();
void BlockNextVideoLayoutUpdate();
bool WaitUntilVideoLayoutUpdateEntered(std::chrono::milliseconds timeout);
void ReleaseVideoLayoutUpdate();
std::uint32_t RequestStopCallCount();
void SetSteamVrDigitalState(bool is_active, bool state, bool changed);
std::string_view SteamVrManifestPath();
std::string_view SteamVrActionSetPath();
std::string_view SteamVrDigitalActionPath();
std::uint32_t SteamVrPollCount();
bool HasActiveSteamVrInput();

}

#endif
