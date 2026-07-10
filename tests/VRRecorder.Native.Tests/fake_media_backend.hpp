#ifndef VRRECORDER_NATIVE_TEST_FAKE_MEDIA_BACKEND_HPP
#define VRRECORDER_NATIVE_TEST_FAKE_MEDIA_BACKEND_HPP

#include <cstdint>
#include <string_view>

namespace vrrecorder::native::testing {

void CommitMuxedVideoPacket();
void CompleteTrailerFlushClose(
    std::uint64_t video_packet_count,
    std::uint64_t audio_packet_count);
void Fail(std::int32_t status, std::string_view message);
std::uint32_t EncoderKind();
void SetSteamVrDigitalState(bool is_active, bool state, bool changed);
std::string_view SteamVrManifestPath();
std::string_view SteamVrActionSetPath();
std::string_view SteamVrDigitalActionPath();
std::uint32_t SteamVrPollCount();
bool HasActiveSteamVrInput();

}

#endif
