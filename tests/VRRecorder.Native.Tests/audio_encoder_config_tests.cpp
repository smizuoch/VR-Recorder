#include "audio_encoder_config.hpp"

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

void CreatesTheDesignAacLcConfiguration()
{
    AacAudioEncoderConfig config {};
    CHECK(CreateAacAudioEncoderConfig(config) == VRREC_STATUS_OK);
    CHECK(config.profile == AacProfile::LowComplexity);
    CHECK(config.sample_rate == 48'000);
    CHECK(config.channel_count == 2);
    CHECK(config.channel_layout == AudioChannelLayout::Stereo);
    CHECK(config.bitrate_bits_per_second == 192'000);
    CHECK(config.source_sample_format == AudioSampleFormat::Float32Interleaved);
}

}

int main()
{
    CreatesTheDesignAacLcConfiguration();
    return 0;
}
