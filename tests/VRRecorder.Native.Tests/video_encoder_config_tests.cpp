#include "video_encoder_config.hpp"

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

void UsesTheDesignDefaultsFor1080p30()
{
    H264VideoEncoderConfig config {};
    CHECK(CreateH264VideoEncoderConfig(
              1'920,
              1'080,
              30,
              true,
              config) == VRREC_STATUS_OK);
    CHECK(config.input_pixel_format == VRREC_SOURCE_PIXEL_FORMAT_NV12);
    CHECK(config.profile == H264Profile::High);
    CHECK(config.rate_control == VideoRateControl::QualityVbr);
    CHECK(config.maximum_b_frame_count == 0);
    CHECK(config.gop_frame_count == 60);
    CHECK(config.target_bitrate_bits_per_second == 8'709'120);
    CHECK(config.maximum_bitrate_bits_per_second == 13'063'680);
}

void ClampsLowResolutionToEightMegabits()
{
    H264VideoEncoderConfig config {};
    CHECK(CreateH264VideoEncoderConfig(
              1'280,
              720,
              30,
              true,
              config) == VRREC_STATUS_OK);
    CHECK(config.target_bitrate_bits_per_second == 8'000'000);
    CHECK(config.maximum_bitrate_bits_per_second == 12'000'000);
}

void ClampsHighRate4kToEightyMegabits()
{
    H264VideoEncoderConfig config {};
    CHECK(CreateH264VideoEncoderConfig(
              3'840,
              2'160,
              120,
              false,
              config) == VRREC_STATUS_OK);
    CHECK(config.profile == H264Profile::Main);
    CHECK(config.gop_frame_count == 240);
    CHECK(config.target_bitrate_bits_per_second == 80'000'000);
    CHECK(config.maximum_bitrate_bits_per_second == 120'000'000);
}

void RejectsOddDimensionsAndUnsupportedFrameRates()
{
    H264VideoEncoderConfig config {};
    CHECK(CreateH264VideoEncoderConfig(
              1'919,
              1'080,
              30,
              true,
              config) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(CreateH264VideoEncoderConfig(
              1'920,
              1'080,
              29,
              true,
              config) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(CreateH264VideoEncoderConfig(
              1'920,
              1'080,
              121,
              true,
              config) == VRREC_STATUS_INVALID_ARGUMENT);
}

}

int main()
{
    UsesTheDesignDefaultsFor1080p30();
    ClampsLowResolutionToEightMegabits();
    ClampsHighRate4kToEightyMegabits();
    RejectsOddDimensionsAndUnsupportedFrameRates();
    return 0;
}
