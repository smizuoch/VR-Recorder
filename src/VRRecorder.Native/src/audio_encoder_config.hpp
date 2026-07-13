#ifndef VRRECORDER_NATIVE_AUDIO_ENCODER_CONFIG_HPP
#define VRRECORDER_NATIVE_AUDIO_ENCODER_CONFIG_HPP

#include <cstdint>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

inline constexpr std::uint32_t AacTargetBitrateBitsPerSecond = 192'000;

enum class AacProfile {
    LowComplexity,
};

enum class AudioChannelLayout {
    Stereo,
};

enum class AudioSampleFormat {
    Float32Interleaved,
};

struct AacAudioEncoderConfig final {
    AacProfile profile = AacProfile::LowComplexity;
    std::uint32_t sample_rate = 0;
    std::uint32_t channel_count = 0;
    AudioChannelLayout channel_layout = AudioChannelLayout::Stereo;
    std::uint32_t bitrate_bits_per_second = 0;
    AudioSampleFormat source_sample_format =
        AudioSampleFormat::Float32Interleaved;
};

vrrec_status_t CreateAacAudioEncoderConfig(
    AacAudioEncoderConfig &config) noexcept;

}

#endif
