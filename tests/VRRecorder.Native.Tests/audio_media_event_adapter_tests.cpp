#include "audio_media_event_adapter.hpp"

#include <cstddef>
#include <cstdint>
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

class RecordingMediaEvents final : public MediaEventSink {
public:
    void FirstVideoPacketMuxed() noexcept override
    {
    }

    void Stopped(std::uint64_t, std::uint64_t) noexcept override
    {
    }

    void Faulted(vrrec_status_t, const char *) noexcept override
    {
    }

    void AudioEndpointAvailabilityChanged(
        AudioEndpointRole role,
        bool available,
        std::uint64_t frame_position) noexcept override
    {
        last_role = role;
        last_available = available;
        last_frame = frame_position;
        ++availability_calls;
    }

    AudioEndpointRole last_role = AudioEndpointRole::Desktop;
    bool last_available = true;
    std::uint64_t last_frame = 0;
    std::size_t availability_calls = 0;
};

void MapsCaptureRolesToMediaEndpointEvents()
{
    RecordingMediaEvents events;
    MediaAudioCaptureAvailabilitySink sink(events);

    sink.AvailabilityChanged(
        AudioCaptureRole::DesktopLoopback,
        false,
        12'000);
    CHECK(events.availability_calls == 1);
    CHECK(events.last_role == AudioEndpointRole::Desktop);
    CHECK(!events.last_available);
    CHECK(events.last_frame == 12'000);

    sink.AvailabilityChanged(
        AudioCaptureRole::Microphone,
        true,
        12'480);
    CHECK(events.availability_calls == 2);
    CHECK(events.last_role == AudioEndpointRole::Microphone);
    CHECK(events.last_available);
    CHECK(events.last_frame == 12'480);
}

}

int main()
{
    MapsCaptureRolesToMediaEndpointEvents();
    return 0;
}
