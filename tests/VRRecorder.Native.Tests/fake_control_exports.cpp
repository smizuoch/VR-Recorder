#include <cstdint>

#include "fake_media_backend.hpp"

#if defined(_WIN32)
#define VRREC_TEST_API __declspec(dllexport)
#else
#define VRREC_TEST_API __attribute__((visibility("default")))
#endif

typedef struct vrrec_test_media_config_v1 {
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
    const char *desktop_endpoint_id_utf8;
    const char *microphone_endpoint_id_utf8;
    double desktop_gain_db;
    double microphone_gain_db;
    const char *spout_sender_identity_utf8;
    std::uint64_t spout_adapter_luid;
    std::uint64_t encoder_adapter_luid;
    const char *gpu_identity_utf8;
} vrrec_test_media_config_v1;

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

extern "C" VRREC_TEST_API std::uint32_t vrrec_test_encoder_kind(void)
{
    return vrrecorder::native::testing::EncoderKind();
}

extern "C" VRREC_TEST_API void vrrec_test_copy_media_config_v1(
    vrrec_test_media_config_v1 *out_config)
{
    if (out_config == nullptr) {
        return;
    }

    const auto &config = vrrecorder::native::testing::SessionConfig();
    *out_config = vrrec_test_media_config_v1 {
        config.canvas_width,
        config.canvas_height,
        config.source_width,
        config.source_height,
        config.destination_x,
        config.destination_y,
        config.destination_width,
        config.destination_height,
        config.canvas_background,
        config.rotation,
        config.audio_routing,
        config.quality_preset,
        config.desktop_endpoint_id.c_str(),
        config.microphone_endpoint_id.c_str(),
        config.desktop_gain_db,
        config.microphone_gain_db,
        config.spout_sender_identity.c_str(),
        config.spout_adapter_luid,
        config.encoder_adapter_luid,
        config.gpu_identity.c_str(),
    };
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
