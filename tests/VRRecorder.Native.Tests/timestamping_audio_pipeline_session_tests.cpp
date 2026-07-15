#include "timestamping_audio_pipeline_session.hpp"

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

class FakeClock final : public AudioSessionStartClock {
public:
    vrrec_status_t NowQpc100ns(std::int64_t &value) noexcept override
    {
        ++calls;
        if (status == VRREC_STATUS_OK) {
            value = now;
        }
        return status;
    }

    vrrec_status_t status = VRREC_STATUS_OK;
    std::int64_t now = 123'456;
    std::size_t calls = 0;
};

class FakeSession final : public StereoAudioPipelineSessionPort {
public:
    vrrec_status_t Start(
        const StereoAudioCaptureSessionConfig &config,
        std::size_t frame_count) noexcept override
    {
        ++start_calls;
        observed_config = config;
        observed_frame_count = frame_count;
        return start_status;
    }

    vrrec_status_t SetRouting(vrrec_audio_routing_t routing) noexcept override
    {
        observed_routing = routing;
        return VRREC_STATUS_OK;
    }

    vrrec_status_t RequestStop() noexcept override
    {
        ++request_stop_calls;
        return VRREC_STATUS_OK;
    }

    void RequestAbort() noexcept override
    {
        ++request_abort_calls;
    }

    void JoinAfterAbort() noexcept override
    {
        ++join_after_abort_calls;
    }

    void Abort() noexcept override
    {
        ++abort_calls;
    }

    StereoAudioEncodingWorkerResult Join() noexcept override
    {
        ++join_calls;
        return StereoAudioEncodingWorkerResult::Stopped;
    }

    StereoAudioPipelineStatistics Statistics() const noexcept override
    {
        return {11, 22};
    }

    StereoAudioCaptureSessionConfig observed_config {};
    vrrec_status_t start_status = VRREC_STATUS_OK;
    vrrec_audio_routing_t observed_routing = 0;
    std::size_t observed_frame_count = 0;
    std::size_t start_calls = 0;
    std::size_t request_stop_calls = 0;
    std::size_t request_abort_calls = 0;
    std::size_t join_after_abort_calls = 0;
    std::size_t abort_calls = 0;
    std::size_t join_calls = 0;
};

void CapturesEpochAtStartAndPreservesOtherConfiguration()
{
    FakeSession inner;
    FakeClock clock;
    TimestampingStereoAudioPipelineSession session(inner, clock);
    const StereoAudioCaptureSessionConfig config {
        "desktop",
        "microphone",
        7,
    };

    CHECK(session.Start(config, 1'024) == VRREC_STATUS_OK);
    CHECK(clock.calls == 1);
    CHECK(inner.start_calls == 1);
    CHECK(inner.observed_config.desktop_endpoint_id_utf8 == "desktop");
    CHECK(inner.observed_config.microphone_endpoint_id_utf8 == "microphone");
    CHECK(inner.observed_config.session_start_qpc_100ns == clock.now);
    CHECK(inner.observed_frame_count == 1'024);
}

void DoesNotStartCaptureWhenTheClockFails()
{
    FakeSession inner;
    FakeClock clock;
    clock.status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    TimestampingStereoAudioPipelineSession session(inner, clock);

    CHECK(session.Start({"desktop", "microphone", 0}, 1'024) ==
          VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(inner.start_calls == 0);
}

void DelegatesLifecycleAndStatistics()
{
    FakeSession inner;
    FakeClock clock;
    TimestampingStereoAudioPipelineSession session(inner, clock);

    CHECK(session.SetRouting(VRREC_AUDIO_ROUTING_MIC_ONLY) ==
          VRREC_STATUS_OK);
    CHECK(inner.observed_routing == VRREC_AUDIO_ROUTING_MIC_ONLY);
    CHECK(session.RequestStop() == VRREC_STATUS_OK);
    session.RequestAbort();
    session.JoinAfterAbort();
    session.Abort();
    CHECK(session.Join() == StereoAudioEncodingWorkerResult::Stopped);
    CHECK(session.Statistics().submitted_frame_count == 11);
    CHECK(session.Statistics().muxed_packet_count == 22);
    CHECK(inner.request_stop_calls == 1);
    CHECK(inner.request_abort_calls == 1);
    CHECK(inner.join_after_abort_calls == 1);
    CHECK(inner.abort_calls == 1);
    CHECK(inner.join_calls == 1);
}

}

int main()
{
    CapturesEpochAtStartAndPreservesOtherConfiguration();
    DoesNotStartCaptureWhenTheClockFails();
    DelegatesLifecycleAndStatistics();
    return 0;
}
