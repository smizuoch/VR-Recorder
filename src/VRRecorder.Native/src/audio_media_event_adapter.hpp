#ifndef VRRECORDER_NATIVE_AUDIO_MEDIA_EVENT_ADAPTER_HPP
#define VRRECORDER_NATIVE_AUDIO_MEDIA_EVENT_ADAPTER_HPP

#include "audio_capture_pump.hpp"
#include "media_backend.hpp"

namespace vrrecorder::native {

class MediaAudioCaptureAvailabilitySink final
    : public AudioCaptureAvailabilitySink {
public:
    explicit MediaAudioCaptureAvailabilitySink(
        MediaEventSink &events) noexcept;

    void AvailabilityChanged(
        AudioCaptureRole role,
        bool available,
        std::uint64_t frame_position) noexcept override;
    void BufferHealthChanged(
        AudioCaptureRole role,
        AudioBufferHealth health,
        std::uint64_t frame_position) noexcept override;

private:
    MediaEventSink &events_;
};

}

#endif
