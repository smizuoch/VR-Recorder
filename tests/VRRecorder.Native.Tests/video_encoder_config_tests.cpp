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
              0,
              1'080,
              30,
              true,
              config) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(CreateH264VideoEncoderConfig(
              1'920,
              0,
              30,
              true,
              config) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(CreateH264VideoEncoderConfig(
              16'386,
              1'080,
              30,
              true,
              config) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(CreateH264VideoEncoderConfig(
              1'920,
              16'386,
              30,
              true,
              config) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(CreateH264VideoEncoderConfig(
              1'919,
              1'080,
              30,
              true,
              config) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(CreateH264VideoEncoderConfig(
              1'920,
              1'079,
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

void ValidatesEveryPersistedEncoderField()
{
    auto valid = H264VideoEncoderConfig {};
    CHECK(CreateH264VideoEncoderConfig(
              1'920,
              1'080,
              30,
              true,
              valid) == VRREC_STATUS_OK);
    CHECK(IsH264VideoEncoderConfigValid(valid));

    const auto rejects = [&](const auto mutate) {
        auto candidate = valid;
        mutate(candidate);
        CHECK(!IsH264VideoEncoderConfigValid(candidate));
    };
    rejects([](auto &value) { value.width = 0; });
    rejects([](auto &value) { value.height = 0; });
    rejects([](auto &value) { value.width = 16'386; });
    rejects([](auto &value) { value.height = 16'386; });
    rejects([](auto &value) { value.width = 1'919; });
    rejects([](auto &value) { value.height = 1'079; });
    rejects([](auto &value) { value.frames_per_second = 29; });
    rejects([](auto &value) { value.frames_per_second = 121; });
    rejects([](auto &value) { value.gop_frame_count = 59; });
    rejects([](auto &value) {
        value.target_bitrate_bits_per_second = 7'999'999;
    });
    rejects([](auto &value) {
        value.target_bitrate_bits_per_second = 80'000'001;
    });
    rejects([](auto &value) {
        value.maximum_bitrate_bits_per_second--;
    });
    rejects([](auto &value) {
        value.input_pixel_format = VRREC_SOURCE_PIXEL_FORMAT_BGRA8;
    });
    rejects([](auto &value) {
        value.profile = static_cast<H264Profile>(2);
    });
    rejects([](auto &value) {
        value.rate_control = static_cast<VideoRateControl>(1);
    });
    rejects([](auto &value) { value.maximum_b_frame_count = 1; });

    auto main_profile = valid;
    main_profile.profile = H264Profile::Main;
    CHECK(IsH264VideoEncoderConfigValid(main_profile));
}

}

int main()
{
    UsesTheDesignDefaultsFor1080p30();
    ClampsLowResolutionToEightMegabits();
    ClampsHighRate4kToEightyMegabits();
    RejectsOddDimensionsAndUnsupportedFrameRates();
    ValidatesEveryPersistedEncoderField();
    return 0;
}
