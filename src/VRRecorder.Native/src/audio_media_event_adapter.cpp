#include "audio_media_event_adapter.hpp"

namespace vrrecorder::native {

MediaAudioCaptureAvailabilitySink::MediaAudioCaptureAvailabilitySink(
    MediaEventSink &events) noexcept
    : events_(events)
{
}

void MediaAudioCaptureAvailabilitySink::AvailabilityChanged(
    AudioCaptureRole role,
    bool available,
    std::uint64_t frame_position) noexcept
{
    const auto endpoint_role =
        role == AudioCaptureRole::DesktopLoopback
        ? AudioEndpointRole::Desktop
        : AudioEndpointRole::Microphone;
    events_.AudioEndpointAvailabilityChanged(
        endpoint_role,
        available,
        frame_position);
}

void MediaAudioCaptureAvailabilitySink::BufferHealthChanged(
    AudioCaptureRole role,
    AudioBufferHealth health,
    std::uint64_t frame_position) noexcept
{
    const auto endpoint_role =
        role == AudioCaptureRole::DesktopLoopback
        ? AudioEndpointRole::Desktop
        : AudioEndpointRole::Microphone;
    events_.AudioBufferHealthChanged(
        endpoint_role,
        health,
        frame_position);
}

}
