#include "production_media_configuration.hpp"

#include <cstdlib>
#include <iostream>

namespace {

#define CHECK(condition)                                                        \
    do {                                                                        \
        if (!(condition)) {                                                     \
            std::cerr << "check failed at " << __FILE__ << ':' << __LINE__      \
                      << ": " #condition << '\n';                              \
            std::abort();                                                       \
        }                                                                       \
    } while (false)

using namespace vrrecorder::native;

vrrec_session_config_v1 ValidConfiguration()
{
    return {
        sizeof(vrrec_session_config_v1),
        VRREC_ABI_V1,
        "C:\\recordings\\pending.mp4",
        1'920,
        1'080,
        60,
        1,
        0,
        VRREC_ENCODER_MEDIA_FOUNDATION_SOFTWARE,
        0,
        1'920,
        1'080,
        0,
        0,
        1'920,
        1'080,
        VRREC_CANVAS_BACKGROUND_BLACK,
        VRREC_VIDEO_ROTATION_NONE,
        VRREC_AUDIO_ROUTING_MIXED,
        VRREC_QUALITY_PRESET_HIGH,
        "desktop-endpoint",
        "microphone-endpoint",
        -6.0,
        -6.0,
        "spout-sender",
        UINT64_C(0x00000001ABCDEF01),
        UINT64_C(0x00000001ABCDEF01),
        "gpu-identity",
        0,
        VRREC_SOURCE_PIXEL_FORMAT_BGRA8,
        0,
        59.94,
    };
}

void ProducesTheExactInitialLayoutAndFrameRate()
{
    const auto input = ValidConfiguration();
    ProductionMediaConfiguration output {};

    CHECK(ValidateProductionMediaConfiguration(input, output) ==
          VRREC_STATUS_OK);
    CHECK(output.frames_per_second == 60);
    CHECK(output.layout.struct_size == sizeof(vrrec_video_layout_v1));
    CHECK(output.layout.abi_version == VRREC_ABI_V1);
    CHECK(output.layout.source_width == input.source_width);
    CHECK(output.layout.source_height == input.source_height);
    CHECK(output.layout.canvas_width == input.width);
    CHECK(output.layout.canvas_height == input.height);
    CHECK(output.layout.destination_x == input.destination_x);
    CHECK(output.layout.destination_y == input.destination_y);
    CHECK(output.layout.destination_width == input.destination_width);
    CHECK(output.layout.destination_height == input.destination_height);
}

void RejectsUnsupportedAbiWithoutChangingOutput()
{
    auto input = ValidConfiguration();
    input.abi_version++;
    ProductionMediaConfiguration output {};
    output.frames_per_second = 777;
    output.layout.struct_size = 777;

    CHECK(ValidateProductionMediaConfiguration(input, output) ==
          VRREC_STATUS_UNSUPPORTED_ABI);
    CHECK(output.frames_per_second == 0);
    CHECK(output.layout.struct_size == 0);
}

void RejectsNonSoftwareEncodersAndNonIntegralFps()
{
    auto input = ValidConfiguration();
    ProductionMediaConfiguration output {};
    input.encoder_kind = VRREC_ENCODER_NVENC;
    CHECK(ValidateProductionMediaConfiguration(input, output) ==
          VRREC_STATUS_BACKEND_UNAVAILABLE);

    input = ValidConfiguration();
    input.fps_denominator = 2;
    CHECK(ValidateProductionMediaConfiguration(input, output) ==
          VRREC_STATUS_INVALID_ARGUMENT);

    input = ValidConfiguration();
    input.fps_numerator = 121;
    CHECK(ValidateProductionMediaConfiguration(input, output) ==
          VRREC_STATUS_INVALID_ARGUMENT);
}

void RejectsUnknownOrMismatchedAdapters()
{
    auto input = ValidConfiguration();
    ProductionMediaConfiguration output {};
    input.spout_adapter_luid = 0;
    CHECK(ValidateProductionMediaConfiguration(input, output) ==
          VRREC_STATUS_INVALID_ARGUMENT);

    input = ValidConfiguration();
    input.encoder_adapter_luid++;
    CHECK(ValidateProductionMediaConfiguration(input, output) ==
          VRREC_STATUS_INVALID_ARGUMENT);
}

void RejectsInvalidGeometryAndEndpointInputs()
{
    auto input = ValidConfiguration();
    ProductionMediaConfiguration output {};
    input.destination_width = input.width + 2;
    CHECK(ValidateProductionMediaConfiguration(input, output) ==
          VRREC_STATUS_INVALID_ARGUMENT);

    input = ValidConfiguration();
    input.destination_width--;
    CHECK(ValidateProductionMediaConfiguration(input, output) ==
          VRREC_STATUS_INVALID_ARGUMENT);

    input = ValidConfiguration();
    input.desktop_endpoint_id_utf8 = "";
    CHECK(ValidateProductionMediaConfiguration(input, output) ==
          VRREC_STATUS_INVALID_ARGUMENT);

    input = ValidConfiguration();
    input.estimated_source_fps = 0.0;
    CHECK(ValidateProductionMediaConfiguration(input, output) ==
          VRREC_STATUS_INVALID_ARGUMENT);
}

}

int main()
{
    ProducesTheExactInitialLayoutAndFrameRate();
    RejectsUnsupportedAbiWithoutChangingOutput();
    RejectsNonSoftwareEncodersAndNonIntegralFps();
    RejectsUnknownOrMismatchedAdapters();
    RejectsInvalidGeometryAndEndpointInputs();
    return 0;
}
