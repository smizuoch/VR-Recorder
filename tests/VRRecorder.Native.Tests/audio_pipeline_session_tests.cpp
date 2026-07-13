#include "audio_pipeline_session.hpp"

#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <future>
#include <iostream>
#include <mutex>
#include <span>

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

class FakeCaptureSession final : public StereoAudioCaptureSessionPort {
public:
    vrrec_status_t Start(
        const StereoAudioCaptureSessionConfig &config) noexcept override
    {
        std::unique_lock lock(mutex);
        ++start_calls;
        last_config = config;
        start_entered = true;
        changed.notify_all();
        if (block_start) {
            changed.wait(lock, [&] { return release_start; });
        }
        return start_status;
    }

    StereoAudioMixResult MixNext(
        std::size_t frame_count_48k,
        std::span<float> output_interleaved,
        StereoAudioMixRead &read) noexcept override
    {
        std::unique_lock lock(mutex);
        ++mix_calls;
        changed.notify_all();
        if (fail_immediately) {
            return StereoAudioMixResult::Failed;
        }

        if (windows_returned < ready_windows) {
            const auto start = windows_returned * frame_count_48k;
            ++windows_returned;
            for (auto &sample : output_interleaved) {
                sample = 0.25F;
            }

            read = {start, frame_count_48k, true, true, false, false};
            changed.notify_all();
            return StereoAudioMixResult::Mixed;
        }

        changed.wait(lock, [&] { return aborted; });
        return StereoAudioMixResult::Aborted;
    }

    vrrec_status_t SetRouting(
        vrrec_audio_routing_t routing) noexcept override
    {
        last_routing = routing;
        ++routing_calls;
        return routing_status;
    }

    void Abort() noexcept override
    {
        {
            const std::lock_guard lock(mutex);
            if (aborted) {
                return;
            }

            aborted = true;
            ++abort_calls;
        }

        changed.notify_all();
    }

    void WaitForWindows(std::size_t count)
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return windows_returned >= count; });
    }

    void WaitForMix()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return mix_calls > 0; });
    }

    void WaitForStart()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return start_entered; });
    }

    void ReleaseStart()
    {
        {
            const std::lock_guard lock(mutex);
            release_start = true;
        }
        changed.notify_all();
    }

    std::mutex mutex;
    std::condition_variable changed;
    StereoAudioCaptureSessionConfig last_config {};
    vrrec_status_t start_status = VRREC_STATUS_OK;
    vrrec_status_t routing_status = VRREC_STATUS_OK;
    vrrec_audio_routing_t last_routing = VRREC_AUDIO_ROUTING_MIXED;
    std::size_t ready_windows = 2;
    std::size_t windows_returned = 0;
    std::size_t start_calls = 0;
    std::size_t routing_calls = 0;
    std::size_t abort_calls = 0;
    std::size_t mix_calls = 0;
    bool aborted = false;
    bool fail_immediately = false;
    bool block_start = false;
    bool start_entered = false;
    bool release_start = false;
};

class CountingEncoderSink final : public StereoAudioEncoderSink {
public:
    StereoAudioEncoderWrite WritePcm48k(
        std::uint64_t,
        std::span<const float>) noexcept override
    {
        ++write_calls;
        return {VRREC_STATUS_OK, 1};
    }

    StereoAudioEncoderWrite Finish() noexcept override
    {
        ++finish_calls;
        return {VRREC_STATUS_OK, 1};
    }

    void Abort() noexcept override
    {
        {
            const std::lock_guard lock(mutex);
            ++abort_calls;
        }
        changed.notify_all();
    }

    void WaitForAbort()
    {
        std::unique_lock lock(mutex);
        changed.wait(lock, [&] { return abort_calls > 0; });
    }

    std::mutex mutex;
    std::condition_variable changed;
    std::size_t write_calls = 0;
    std::size_t finish_calls = 0;
    std::size_t abort_calls = 0;
};

StereoAudioCaptureSessionConfig Config()
{
    return {"render-id", "mic-id", 900'000};
}

void RunsACompleteGracefulAudioPipeline()
{
    FakeCaptureSession capture;
    CountingEncoderSink encoder;
    StereoAudioPipelineSession session(capture, encoder);

    CHECK(session.Start(Config(), 1'024) == VRREC_STATUS_OK);
    CHECK(capture.start_calls == 1);
    CHECK(capture.last_config.desktop_endpoint_id_utf8 == "render-id");
    CHECK(capture.last_config.microphone_endpoint_id_utf8 == "mic-id");
    capture.WaitForWindows(2);
    CHECK(session.SetRouting(VRREC_AUDIO_ROUTING_DESKTOP_ONLY) ==
          VRREC_STATUS_OK);
    CHECK(capture.routing_calls == 1);
    CHECK(capture.last_routing == VRREC_AUDIO_ROUTING_DESKTOP_ONLY);

    CHECK(session.RequestStop() == VRREC_STATUS_OK);
    CHECK(session.RequestStop() == VRREC_STATUS_OK);
    CHECK(session.Join() == StereoAudioEncodingWorkerResult::Stopped);
    const auto statistics = session.Statistics();
    CHECK(statistics.submitted_frame_count == 2'048);
    CHECK(statistics.muxed_packet_count == 3);
    CHECK(encoder.write_calls == 2);
    CHECK(encoder.finish_calls == 1);
    CHECK(encoder.abort_calls == 0);
}

void DoesNotStartEncodingWhenCaptureInitializationFails()
{
    FakeCaptureSession capture;
    capture.start_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    CountingEncoderSink encoder;
    StereoAudioPipelineSession session(capture, encoder);

    CHECK(session.Start(Config(), 1'024) ==
          VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(capture.start_calls == 1);
    CHECK(encoder.write_calls == 0);
    CHECK(session.RequestStop() == VRREC_STATUS_INVALID_STATE);
    CHECK(session.SetRouting(VRREC_AUDIO_ROUTING_MUTED) ==
          VRREC_STATUS_INVALID_STATE);
}

void RejectsInvalidWindowsBeforeStartingCapture()
{
    FakeCaptureSession capture;
    CountingEncoderSink encoder;
    StereoAudioPipelineSession session(capture, encoder);

    CHECK(session.Start(Config(), 0) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(capture.start_calls == 0);
}

void WorkerFailureTerminalizesThePipelineOnStopRequest()
{
    FakeCaptureSession capture;
    capture.fail_immediately = true;
    CountingEncoderSink encoder;
    StereoAudioPipelineSession session(capture, encoder);

    CHECK(session.Start(Config(), 1'024) == VRREC_STATUS_OK);
    encoder.WaitForAbort();
    CHECK(session.SetRouting(VRREC_AUDIO_ROUTING_MUTED) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(capture.routing_calls == 0);
    CHECK(session.RequestStop() == VRREC_STATUS_INVALID_STATE);
    CHECK(session.SetRouting(VRREC_AUDIO_ROUTING_MUTED) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(capture.routing_calls == 0);
    CHECK(capture.abort_calls == 1);
    CHECK(encoder.abort_calls == 1);
}

void AbortDuringCaptureStartRollsBackWithoutStartingEncoding()
{
    FakeCaptureSession capture;
    capture.block_start = true;
    capture.ready_windows = 0;
    CountingEncoderSink encoder;
    StereoAudioPipelineSession session(capture, encoder);

    auto starting = std::async(std::launch::async, [&] {
        return session.Start(Config(), 1'024);
    });
    capture.WaitForStart();
    session.Abort();
    capture.ReleaseStart();

    CHECK(starting.get() == VRREC_STATUS_INVALID_STATE);
    CHECK(capture.abort_calls == 1);
    CHECK(encoder.write_calls == 0);
    CHECK(encoder.finish_calls == 0);
    CHECK(encoder.abort_calls == 0);
}

}

int main()
{
    RunsACompleteGracefulAudioPipeline();
    DoesNotStartEncodingWhenCaptureInitializationFails();
    RejectsInvalidWindowsBeforeStartingCapture();
    WorkerFailureTerminalizesThePipelineOnStopRequest();
    AbortDuringCaptureStartRollsBackWithoutStartingEncoding();
    return 0;
}
