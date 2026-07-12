#ifndef VRRECORDER_NATIVE_AUDIO_CAPTURE_SOURCE_FACTORY_HPP
#define VRRECORDER_NATIVE_AUDIO_CAPTURE_SOURCE_FACTORY_HPP

#include <memory>

#include "audio_capture_source.hpp"

namespace vrrecorder::native {

vrrec_status_t CreateWasapiAudioCaptureSource(
    std::unique_ptr<AudioCaptureSource> &output) noexcept;

}

#endif
