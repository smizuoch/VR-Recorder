#include "media_stream_pipeline_adapters.hpp"

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <iostream>

namespace {

#define CHECK(condition) do { if (!(condition)) { std::cerr << "check failed at " << __FILE__ << ':' << __LINE__ << ": " #condition << '\n'; std::abort(); } } while (false)

using namespace vrrecorder::native;

class FakeVideoSession final : public VideoPipelineSessionPort {
public:
    vrrec_status_t Start(std::chrono::milliseconds timeout) noexcept override { start_timeout = timeout; return start_status; }
    vrrec_status_t RequestStop() noexcept override { ++stop_calls; return stop_status; }
    void RequestAbort() noexcept override { ++request_abort_calls; }
    void JoinAfterAbort() noexcept override { ++join_after_abort_calls; }
    void Abort() noexcept override { ++abort_calls; }
    VideoPipelineResult Join() noexcept override { ++join_calls; return join_result; }
    VideoEncodingStatistics Statistics() const noexcept override { return statistics; }
    std::chrono::milliseconds start_timeout {0};
    vrrec_status_t start_status = VRREC_STATUS_OK;
    vrrec_status_t stop_status = VRREC_STATUS_OK;
    VideoPipelineResult join_result = VideoPipelineResult::Stopped;
    VideoEncodingStatistics statistics {{0, 0, 0, 0}, 19, 0, 0};
    std::size_t stop_calls = 0;
    std::size_t request_abort_calls = 0;
    std::size_t join_after_abort_calls = 0;
    std::size_t abort_calls = 0;
    std::size_t join_calls = 0;
};

class FakeAudioSession final : public StereoAudioPipelineSessionPort {
public:
    vrrec_status_t Start(const StereoAudioCaptureSessionConfig &config, std::size_t frames) noexcept override { observed_config = config; observed_frames = frames; return start_status; }
    vrrec_status_t SetRouting(vrrec_audio_routing_t) noexcept override { return VRREC_STATUS_OK; }
    vrrec_status_t RequestStop() noexcept override { ++stop_calls; return stop_status; }
    void RequestAbort() noexcept override { ++request_abort_calls; }
    void JoinAfterAbort() noexcept override { ++join_after_abort_calls; }
    void Abort() noexcept override { ++abort_calls; }
    StereoAudioEncodingWorkerResult Join() noexcept override { ++join_calls; return join_result; }
    StereoAudioPipelineStatistics Statistics() const noexcept override { return statistics; }
    StereoAudioCaptureSessionConfig observed_config {};
    std::size_t observed_frames = 0;
    vrrec_status_t start_status = VRREC_STATUS_OK;
    vrrec_status_t stop_status = VRREC_STATUS_OK;
    StereoAudioEncodingWorkerResult join_result = StereoAudioEncodingWorkerResult::Stopped;
    StereoAudioPipelineStatistics statistics {0, 23};
    std::size_t stop_calls = 0;
    std::size_t request_abort_calls = 0;
    std::size_t join_after_abort_calls = 0;
    std::size_t abort_calls = 0;
    std::size_t join_calls = 0;
};

void AdaptsConfiguredVideoSession()
{
    FakeVideoSession session;
    VideoMediaStreamPipelineAdapter adapter(session, std::chrono::milliseconds(75));
    CHECK(adapter.Start() == VRREC_STATUS_OK);
    CHECK(session.start_timeout == std::chrono::milliseconds(75));
    CHECK(adapter.RequestStop() == VRREC_STATUS_OK);
    CHECK(adapter.Join() == VRREC_STATUS_OK);
    CHECK(adapter.MuxedPacketCount() == 19);
    adapter.RequestAbort();
    adapter.JoinAfterAbort();
    CHECK(session.request_abort_calls == 1);
    CHECK(session.join_after_abort_calls == 1);
    adapter.Abort();
    CHECK(session.abort_calls == 1);
    session.join_result = VideoPipelineResult::SenderLost;
    CHECK(adapter.Join() == VRREC_STATUS_BACKEND_UNAVAILABLE);
    session.join_result = VideoPipelineResult::EncoderFailed;
    CHECK(adapter.Join() == VRREC_STATUS_INTERNAL_ERROR);
}

void AdaptsConfiguredAudioSession()
{
    FakeAudioSession session;
    StereoAudioCaptureSessionConfig config {"desktop", "microphone", 123'456};
    AudioMediaStreamPipelineAdapter adapter(session, config, 1'024);
    CHECK(adapter.Start() == VRREC_STATUS_OK);
    CHECK(session.observed_config.desktop_endpoint_id_utf8 == "desktop");
    CHECK(session.observed_config.microphone_endpoint_id_utf8 == "microphone");
    CHECK(session.observed_config.session_start_qpc_100ns == 123'456);
    CHECK(session.observed_frames == 1'024);
    CHECK(adapter.RequestStop() == VRREC_STATUS_OK);
    CHECK(adapter.Join() == VRREC_STATUS_OK);
    CHECK(adapter.MuxedPacketCount() == 23);
    adapter.RequestAbort();
    adapter.JoinAfterAbort();
    CHECK(session.request_abort_calls == 1);
    CHECK(session.join_after_abort_calls == 1);
    adapter.Abort();
    CHECK(session.abort_calls == 1);
    session.join_result = StereoAudioEncodingWorkerResult::MuxFailed;
    CHECK(adapter.Join() == VRREC_STATUS_INTERNAL_ERROR);
}

}

int main()
{
    AdaptsConfiguredVideoSession();
    AdaptsConfiguredAudioSession();
    return 0;
}
