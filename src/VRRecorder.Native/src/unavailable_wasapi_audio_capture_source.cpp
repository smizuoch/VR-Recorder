#include "audio_capture_source_factory.hpp"

namespace vrrecorder::native {

vrrec_status_t CreateWasapiAudioCaptureSource(
    std::unique_ptr<AudioCaptureSource> &output) noexcept
{
    output.reset();
    return VRREC_STATUS_BACKEND_UNAVAILABLE;
}

}
