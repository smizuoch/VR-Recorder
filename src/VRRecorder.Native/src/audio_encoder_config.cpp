#include "audio_encoder_config.hpp"

namespace vrrecorder::native {

vrrec_status_t CreateAacAudioEncoderConfig(
    AacAudioEncoderConfig &config) noexcept
{
    config = {
        AacProfile::LowComplexity,
        48'000,
        2,
        AudioChannelLayout::Stereo,
        AacTargetBitrateBitsPerSecond,
        AudioSampleFormat::Float32Interleaved,
    };
    return VRREC_STATUS_OK;
}

}
