#include <cstdint>

#include "fake_media_backend.hpp"

#if defined(_WIN32)
#define VRREC_TEST_API __declspec(dllexport)
#else
#define VRREC_TEST_API __attribute__((visibility("default")))
#endif

extern "C" VRREC_TEST_API void vrrec_test_commit_muxed_video_packet(void)
{
    vrrecorder::native::testing::CommitMuxedVideoPacket();
}

extern "C" VRREC_TEST_API void vrrec_test_complete_trailer_flush_close(
    std::uint64_t video_packet_count,
    std::uint64_t audio_packet_count)
{
    vrrecorder::native::testing::CompleteTrailerFlushClose(
        video_packet_count,
        audio_packet_count);
}

extern "C" VRREC_TEST_API void vrrec_test_fail(
    std::int32_t status,
    const char *message_utf8)
{
    vrrecorder::native::testing::Fail(
        status,
        message_utf8 == nullptr ? "" : message_utf8);
}

extern "C" VRREC_TEST_API void vrrec_test_set_steamvr_digital_state(
    std::uint8_t is_active,
    std::uint8_t state,
    std::uint8_t changed)
{
    vrrecorder::native::testing::SetSteamVrDigitalState(
        is_active != 0,
        state != 0,
        changed != 0);
}

extern "C" VRREC_TEST_API std::uint8_t vrrec_test_steamvr_input_active(void)
{
    return vrrecorder::native::testing::HasActiveSteamVrInput() ? 1 : 0;
}

extern "C" VRREC_TEST_API const char *vrrec_test_steamvr_manifest_path(void)
{
    return vrrecorder::native::testing::SteamVrManifestPath().data();
}

extern "C" VRREC_TEST_API const char *vrrec_test_steamvr_action_set_path(void)
{
    return vrrecorder::native::testing::SteamVrActionSetPath().data();
}

extern "C" VRREC_TEST_API const char *vrrec_test_steamvr_action_path(void)
{
    return vrrecorder::native::testing::SteamVrDigitalActionPath().data();
}

extern "C" VRREC_TEST_API std::uint32_t vrrec_test_steamvr_poll_count(void)
{
    return vrrecorder::native::testing::SteamVrPollCount();
}
