#include "av_sync_media_event_adapter.hpp"

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
        AudioEndpointRole,
        bool,
        std::uint64_t) noexcept override
    {
    }

    void AvSyncDriftExceeded(
        std::uint64_t video_pts_microseconds,
        std::uint64_t audio_pts_microseconds,
        std::uint64_t absolute_drift_microseconds) noexcept override
    {
        ++calls;
        video_pts = video_pts_microseconds;
        audio_pts = audio_pts_microseconds;
        absolute_drift = absolute_drift_microseconds;
    }

    std::size_t calls = 0;
    std::uint64_t video_pts = 0;
    std::uint64_t audio_pts = 0;
    std::uint64_t absolute_drift = 0;
};

void ForwardsExactPrivacySafeDriftValuesToMediaEvents()
{
    RecordingMediaEvents events;
    AvSyncMediaEventAdapter adapter(events);

    adapter.DriftThresholdExceeded(180'001, 100'000, 80'001);
    CHECK(events.calls == 1);
    CHECK(events.video_pts == 180'001);
    CHECK(events.audio_pts == 100'000);
    CHECK(events.absolute_drift == 80'001);
}

void DropsDriftEventsWithNegativeMediaTimestamps()
{
    RecordingMediaEvents events;
    AvSyncMediaEventAdapter adapter(events);

    adapter.DriftThresholdExceeded(-1, 100'000, 100'001);
    adapter.DriftThresholdExceeded(100'000, -1, 100'001);
    CHECK(events.calls == 0);
}

}

int main()
{
    ForwardsExactPrivacySafeDriftValuesToMediaEvents();
    DropsDriftEventsWithNegativeMediaTimestamps();
    return 0;
}
