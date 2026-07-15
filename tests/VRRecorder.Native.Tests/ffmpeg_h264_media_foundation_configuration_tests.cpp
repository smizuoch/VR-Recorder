#include "ffmpeg_h264_media_foundation_configuration.hpp"

#include <cstdlib>
#include <cstring>
#include <iostream>
#include <string_view>

extern "C" {
#include <libavcodec/avcodec.h>
#include <libavutil/dict.h>
}

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

H264VideoEncoderConfig ExactConfig()
{
    H264VideoEncoderConfig config {};
    CHECK(CreateH264VideoEncoderConfig(
              1'920,
              1'080,
              30,
              true,
              config) == VRREC_STATUS_OK);
    return config;
}

void ConfiguresTheSoftwareMediaFoundationContract()
{
    CHECK(FfmpegH264MediaFoundationEncoderName ==
          std::string_view("h264_mf"));

    AVCodecContext *context = avcodec_alloc_context3(nullptr);
    CHECK(context != nullptr);
    AVDictionary *options = nullptr;

    CHECK(ConfigureFfmpegH264MediaFoundationContext(
              ExactConfig(),
              *context,
              options) == VRREC_STATUS_OK);

    CHECK(context->codec_type == AVMEDIA_TYPE_VIDEO);
    CHECK(context->codec_id == AV_CODEC_ID_H264);
    CHECK(context->width == 1'920);
    CHECK(context->height == 1'080);
    CHECK(context->pix_fmt == AV_PIX_FMT_NV12);
    CHECK(context->time_base.num == 1);
    CHECK(context->time_base.den == 30);
    CHECK(context->framerate.num == 30);
    CHECK(context->framerate.den == 1);
    CHECK(context->bit_rate == 8'709'120);
    CHECK(context->rc_max_rate == 13'063'680);
    CHECK(context->gop_size == 60);
    CHECK(context->max_b_frames == 0);
    CHECK(context->profile == AV_PROFILE_H264_HIGH);
    CHECK((context->flags & AV_CODEC_FLAG_GLOBAL_HEADER) != 0);

    const auto *hardware = av_dict_get(
        options,
        "hw_encoding",
        nullptr,
        AV_DICT_MATCH_CASE);
    CHECK(hardware != nullptr);
    CHECK(std::strcmp(hardware->value, "0") == 0);
    const auto *rate_control = av_dict_get(
        options,
        "rate_control",
        nullptr,
        AV_DICT_MATCH_CASE);
    CHECK(rate_control != nullptr);
    CHECK(std::strcmp(rate_control->value, "quality") == 0);
    CHECK(av_dict_count(options) == 2);

    av_dict_free(&options);
    avcodec_free_context(&context);
}

void MapsMainProfileWithoutChangingTheSoftwareFallback()
{
    auto config = ExactConfig();
    config.profile = H264Profile::Main;
    AVCodecContext *context = avcodec_alloc_context3(nullptr);
    CHECK(context != nullptr);
    AVDictionary *options = nullptr;

    CHECK(ConfigureFfmpegH264MediaFoundationContext(
              config,
              *context,
              options) == VRREC_STATUS_OK);
    CHECK(context->profile == AV_PROFILE_H264_MAIN);
    CHECK(std::strcmp(
              av_dict_get(
                  options,
                  "hw_encoding",
                  nullptr,
                  AV_DICT_MATCH_CASE)->value,
              "0") == 0);

    av_dict_free(&options);
    avcodec_free_context(&context);
}

void CheckRejectedWithoutMutation(const H264VideoEncoderConfig &config)
{
    AVCodecContext *context = avcodec_alloc_context3(nullptr);
    CHECK(context != nullptr);
    context->width = 17;
    AVDictionary *options = nullptr;

    CHECK(ConfigureFfmpegH264MediaFoundationContext(
              config,
              *context,
              options) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(context->width == 17);
    CHECK(options == nullptr);

    avcodec_free_context(&context);
}

void RejectsNonCanonicalInputWithoutMutation()
{
    auto config = ExactConfig();
    config.width = 0;
    CheckRejectedWithoutMutation(config);
    config = ExactConfig();
    config.height = 1'081;
    CheckRejectedWithoutMutation(config);
    config = ExactConfig();
    config.frames_per_second = 29;
    CheckRejectedWithoutMutation(config);
    config = ExactConfig();
    config.gop_frame_count = 61;
    CheckRejectedWithoutMutation(config);
    config = ExactConfig();
    config.target_bitrate_bits_per_second = 7'999'999;
    CheckRejectedWithoutMutation(config);
    config = ExactConfig();
    config.maximum_bitrate_bits_per_second = 13'063'681;
    CheckRejectedWithoutMutation(config);
    config = ExactConfig();
    config.input_pixel_format = VRREC_SOURCE_PIXEL_FORMAT_RGBA8;
    CheckRejectedWithoutMutation(config);
    config = ExactConfig();
    config.profile = static_cast<H264Profile>(-1);
    CheckRejectedWithoutMutation(config);
    config = ExactConfig();
    config.rate_control = static_cast<VideoRateControl>(-1);
    CheckRejectedWithoutMutation(config);
    config = ExactConfig();
    config.maximum_b_frame_count = 1;
    CheckRejectedWithoutMutation(config);
}

void RejectsCallerOwnedOptionsWithoutMutation()
{
    AVCodecContext *context = avcodec_alloc_context3(nullptr);
    CHECK(context != nullptr);
    context->width = 19;
    AVDictionary *options = nullptr;
    CHECK(av_dict_set(&options, "caller", "owned", 0) == 0);

    CHECK(ConfigureFfmpegH264MediaFoundationContext(
              ExactConfig(),
              *context,
              options) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(context->width == 19);
    CHECK(av_dict_count(options) == 1);
    CHECK(std::strcmp(
              av_dict_get(options, "caller", nullptr, AV_DICT_MATCH_CASE)
                  ->value,
              "owned") == 0);

    av_dict_free(&options);
    avcodec_free_context(&context);
}

}

int main()
{
    ConfiguresTheSoftwareMediaFoundationContract();
    MapsMainProfileWithoutChangingTheSoftwareFallback();
    RejectsNonCanonicalInputWithoutMutation();
    RejectsCallerOwnedOptionsWithoutMutation();
    return 0;
}
