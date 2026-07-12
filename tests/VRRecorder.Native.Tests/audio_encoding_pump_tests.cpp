#include "audio_encoding_pump.hpp"

#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <span>
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

struct ScriptedMix final {
    StereoAudioMixResult result = StereoAudioMixResult::Failed;
    StereoAudioMixRead read {};
    float sample = 0.0F;
};

class ScriptedMixSource final : public StereoAudioMixSource {
public:
    StereoAudioMixResult MixNext(
        std::size_t frame_count_48k,
        std::span<float> output_interleaved,
        StereoAudioMixRead &read) noexcept override
    {
        ++calls;
        requested_frame_counts.push_back(frame_count_48k);
        if (next >= scripts.size()) {
            return StereoAudioMixResult::Failed;
        }

        const auto &script = scripts[next++];
        if (script.result == StereoAudioMixResult::Mixed) {
            for (auto &sample : output_interleaved) {
                sample = script.sample;
            }

            read = script.read;
        }

        return script.result;
    }

    std::vector<ScriptedMix> scripts;
    std::vector<std::size_t> requested_frame_counts;
    std::size_t next = 0;
    std::size_t calls = 0;
};

struct SinkCall final {
    std::uint64_t start_frame_48k;
    std::vector<float> samples;
};

class RecordingEncoderSink final : public StereoAudioEncoderSink {
public:
    StereoAudioEncoderWrite WritePcm48k(
        std::uint64_t start_frame_48k,
        std::span<const float> interleaved_samples) noexcept override
    {
        calls.push_back({
            start_frame_48k,
            std::vector<float>(
                interleaved_samples.begin(),
                interleaved_samples.end()),
        });
        return next_write;
    }

    StereoAudioEncoderWrite next_write {VRREC_STATUS_OK, 0};
    std::vector<SinkCall> calls;
};

StereoAudioMixRead Read(
    std::uint64_t start_frame,
    std::size_t frame_count,
    bool desktop_available = true,
    bool microphone_available = true)
{
    return {
        start_frame,
        frame_count,
        desktop_available,
        microphone_available,
        !desktop_available,
        !microphone_available,
    };
}

void SubmitsTheExactMixedWindowAndCountsMuxedPackets()
{
    ScriptedMixSource source;
    source.scripts.push_back({
        StereoAudioMixResult::Mixed,
        Read(2'048, 1'024),
        0.25F,
    });
    RecordingEncoderSink sink;
    sink.next_write = {VRREC_STATUS_OK, 2};
    StereoAudioEncodingPump pump(source, sink);

    StereoAudioEncodingRead read {};
    CHECK(pump.PumpNext(1'024, read) ==
          StereoAudioEncodingResult::Submitted);
    CHECK(source.requested_frame_counts ==
          std::vector<std::size_t> {1'024});
    CHECK(sink.calls.size() == 1);
    CHECK(sink.calls[0].start_frame_48k == 2'048);
    CHECK(sink.calls[0].samples.size() == 2'048);
    for (const auto sample : sink.calls[0].samples) {
        CHECK(sample == 0.25F);
    }
    CHECK(read.mix.start_frame_48k == 2'048);
    CHECK(read.mix.frame_count_48k == 1'024);
    CHECK(read.muxed_packet_count == 2);
    CHECK(read.encoder_status == VRREC_STATUS_OK);
    CHECK(pump.SubmittedFrameCount() == 1'024);
    CHECK(pump.MuxedPacketCount() == 2);
}

void SubmitsSilenceEvenWhenTheEncoderBuffersTheFrame()
{
    ScriptedMixSource source;
    source.scripts.push_back({
        StereoAudioMixResult::Mixed,
        Read(0, 1'024, false, false),
        0.0F,
    });
    RecordingEncoderSink sink;
    sink.next_write = {VRREC_STATUS_OK, 0};
    StereoAudioEncodingPump pump(source, sink);

    StereoAudioEncodingRead read {};
    CHECK(pump.PumpNext(1'024, read) ==
          StereoAudioEncodingResult::Submitted);
    CHECK(sink.calls.size() == 1);
    for (const auto sample : sink.calls[0].samples) {
        CHECK(sample == 0.0F);
    }
    CHECK(!read.mix.desktop_available);
    CHECK(!read.mix.microphone_available);
    CHECK(read.muxed_packet_count == 0);
    CHECK(pump.SubmittedFrameCount() == 1'024);
    CHECK(pump.MuxedPacketCount() == 0);
}

void DoesNotCallTheEncoderAfterCaptureAbort()
{
    ScriptedMixSource source;
    source.scripts.push_back({StereoAudioMixResult::Aborted, {}, 0.0F});
    RecordingEncoderSink sink;
    StereoAudioEncodingPump pump(source, sink);

    StereoAudioEncodingRead read {};
    CHECK(pump.PumpNext(1'024, read) ==
          StereoAudioEncodingResult::Aborted);
    CHECK(sink.calls.empty());
    CHECK(pump.SubmittedFrameCount() == 0);
}

void ReportsEncoderFailureWithoutCountingTheWindow()
{
    ScriptedMixSource source;
    source.scripts.push_back({
        StereoAudioMixResult::Mixed,
        Read(0, 1'024),
        0.5F,
    });
    RecordingEncoderSink sink;
    sink.next_write = {VRREC_STATUS_INTERNAL_ERROR, 0};
    StereoAudioEncodingPump pump(source, sink);

    StereoAudioEncodingRead read {};
    CHECK(pump.PumpNext(1'024, read) ==
          StereoAudioEncodingResult::EncoderFailed);
    CHECK(read.encoder_status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(pump.SubmittedFrameCount() == 0);
    CHECK(pump.MuxedPacketCount() == 0);
}

}

int main()
{
    SubmitsTheExactMixedWindowAndCountsMuxedPackets();
    SubmitsSilenceEvenWhenTheEncoderBuffersTheFrame();
    DoesNotCallTheEncoderAfterCaptureAbort();
    ReportsEncoderFailureWithoutCountingTheWindow();
    return 0;
}
