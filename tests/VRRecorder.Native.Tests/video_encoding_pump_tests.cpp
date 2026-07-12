#include "video_encoding_pump.hpp"

#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <vector>

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

class ScriptedVideoEncoderSink final : public VideoEncoderSink {
public:
    VideoEncoderWrite Write(
        const ScheduledVideoFrame &frame) noexcept override
    {
        frames.push_back(frame);
        if (next >= writes.size()) {
            return {VRREC_STATUS_INTERNAL_ERROR, 0, 0};
        }

        return writes[next++];
    }

    std::vector<VideoEncoderWrite> writes;
    std::vector<ScheduledVideoFrame> frames;
    std::size_t next = 0;
};

void DoesNotCallTheEncoderBeforeTheFirstSourceFrame()
{
    VideoCfrScheduler scheduler;
    ScriptedVideoEncoderSink sink;
    VideoEncodingPump pump(scheduler, sink);
    VideoEncodingRead read {};

    CHECK(pump.PumpTick(0, read) == VideoEncodingResult::NoFrame);
    CHECK(sink.frames.empty());
    const auto statistics = pump.Statistics();
    CHECK(statistics.scheduler.output_frame_count == 0);
    CHECK(statistics.muxed_packet_count == 0);
}

void DetectsTheFirstMuxedPacketAfterEncoderBuffering()
{
    VideoCfrScheduler scheduler;
    ScriptedVideoEncoderSink sink;
    sink.writes.push_back({VRREC_STATUS_OK, 0, 100});
    sink.writes.push_back({VRREC_STATUS_OK, 1, 250});
    VideoEncodingPump pump(scheduler, sink);
    CHECK(scheduler.Push({10, 1'000'000}) == VRREC_STATUS_OK);

    VideoEncodingRead read {};
    CHECK(pump.PumpTick(0, read) == VideoEncodingResult::Submitted);
    CHECK(!read.first_packet_muxed);
    CHECK(read.muxed_packet_count == 0);
    CHECK(read.encode_latency_microseconds == 100);
    CHECK(pump.PumpTick(1, read) == VideoEncodingResult::Submitted);
    CHECK(read.scheduled.duplicated);
    CHECK(read.first_packet_muxed);
    CHECK(read.muxed_packet_count == 1);
    CHECK(read.encode_latency_microseconds == 250);

    const auto statistics = pump.Statistics();
    CHECK(statistics.scheduler.source_frame_count == 1);
    CHECK(statistics.scheduler.output_frame_count == 2);
    CHECK(statistics.scheduler.duplicated_output_frame_count == 1);
    CHECK(statistics.muxed_packet_count == 1);
    CHECK(statistics.latest_encode_latency_microseconds == 250);
    CHECK(statistics.maximum_encode_latency_microseconds == 250);
}

void CountsLaterPacketsWithoutRepeatingTheFirstPacketEvent()
{
    VideoCfrScheduler scheduler;
    ScriptedVideoEncoderSink sink;
    sink.writes.push_back({VRREC_STATUS_OK, 1, 200});
    sink.writes.push_back({VRREC_STATUS_OK, 2, 150});
    VideoEncodingPump pump(scheduler, sink);
    VideoEncodingRead read {};
    CHECK(scheduler.Push({20, 2'000'000}) == VRREC_STATUS_OK);
    CHECK(pump.PumpTick(0, read) == VideoEncodingResult::Submitted);
    CHECK(read.first_packet_muxed);
    CHECK(scheduler.Push({21, 2'010'000}) == VRREC_STATUS_OK);
    CHECK(pump.PumpTick(1, read) == VideoEncodingResult::Submitted);
    CHECK(!read.first_packet_muxed);

    const auto statistics = pump.Statistics();
    CHECK(statistics.muxed_packet_count == 3);
    CHECK(statistics.latest_encode_latency_microseconds == 150);
    CHECK(statistics.maximum_encode_latency_microseconds == 200);
}

void EncoderFailureDoesNotCommitPacketOrLatencyStatistics()
{
    VideoCfrScheduler scheduler;
    ScriptedVideoEncoderSink sink;
    sink.writes.push_back({VRREC_STATUS_INTERNAL_ERROR, 5, 999});
    VideoEncodingPump pump(scheduler, sink);
    CHECK(scheduler.Push({30, 3'000'000}) == VRREC_STATUS_OK);

    VideoEncodingRead read {};
    CHECK(pump.PumpTick(0, read) == VideoEncodingResult::EncoderFailed);
    CHECK(read.encoder_status == VRREC_STATUS_INTERNAL_ERROR);
    const auto statistics = pump.Statistics();
    CHECK(statistics.muxed_packet_count == 0);
    CHECK(statistics.latest_encode_latency_microseconds == 0);
    CHECK(statistics.maximum_encode_latency_microseconds == 0);
}

}

int main()
{
    DoesNotCallTheEncoderBeforeTheFirstSourceFrame();
    DetectsTheFirstMuxedPacketAfterEncoderBuffering();
    CountsLaterPacketsWithoutRepeatingTheFirstPacketEvent();
    EncoderFailureDoesNotCommitPacketOrLatencyStatistics();
    return 0;
}
