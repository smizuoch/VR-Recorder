#include "ffmpeg_fragmented_mp4_muxer.hpp"
#include "fragmented_mp4_test_support.hpp"

#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <limits>
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
using namespace vrrecorder::native::test;

EncodedMediaPacket Packet(
    MediaStreamKind stream,
    std::int64_t timestamp_microseconds)
{
    return {
        stream,
        timestamp_microseconds,
        timestamp_microseconds,
        stream == MediaStreamKind::Video ? 16'667 : 21'333,
        stream == MediaStreamKind::Video && timestamp_microseconds == 0,
        std::vector<std::byte>(8, std::byte {0x2a}),
    };
}

struct RescaleCall final {
    FfmpegPacketTimestamps source;
    MediaTimeBase source_time_base;
    MediaTimeBase destination_time_base;
};

struct WrittenPacket final {
    EncodedMediaPacket canonical_packet;
    FfmpegPacketTimestamps stream_timestamps;
};

class RecordingFfmpegMuxerPort final : public FfmpegMuxerPort {
public:
    vrrec_status_t WriteHeader(
        const FragmentedMp4StreamConfiguration &configuration)
        noexcept override
    {
        ++write_header_calls;
        header_configuration = configuration;
        return write_header_status;
    }

    vrrec_status_t GetActualStreamTimeBase(
        MediaStreamKind stream,
        MediaTimeBase &time_base) noexcept override
    {
        readback_streams.push_back(stream);
        const auto status = stream == MediaStreamKind::Video
            ? video_readback_status
            : audio_readback_status;
        if (status != VRREC_STATUS_OK) {
            return status;
        }
        time_base = stream == MediaStreamKind::Video
            ? actual_video_time_base
            : actual_audio_time_base;
        return VRREC_STATUS_OK;
    }

    void RescalePacketTimestamps(
        const FfmpegPacketTimestamps &source,
        MediaTimeBase source_time_base,
        MediaTimeBase destination_time_base,
        FfmpegPacketTimestamps &destination) noexcept override
    {
        rescale_calls.push_back(
            {source, source_time_base, destination_time_base});
        if (rescale_index < scripted_rescale_results.size()) {
            destination = scripted_rescale_results[rescale_index++];
        } else {
            destination = source;
        }
    }

    vrrec_status_t WriteInterleavedPacket(
        const EncodedMediaPacket &canonical_packet,
        const FfmpegPacketTimestamps &stream_timestamps) noexcept override
    {
        written_packets.push_back({canonical_packet, stream_timestamps});
        return write_packet_status;
    }

    vrrec_status_t WriteTrailer() noexcept override
    {
        ++write_trailer_calls;
        return write_trailer_status;
    }

    vrrec_status_t FlushFile() noexcept override
    {
        ++flush_calls;
        return flush_status;
    }

    void Abort() noexcept override
    {
        ++abort_calls;
    }

    FragmentedMp4StreamConfiguration header_configuration = TestMp4Streams();
    MediaTimeBase actual_video_time_base {1, 90'000};
    MediaTimeBase actual_audio_time_base {1, 48'000};
    vrrec_status_t write_header_status = VRREC_STATUS_OK;
    vrrec_status_t video_readback_status = VRREC_STATUS_OK;
    vrrec_status_t audio_readback_status = VRREC_STATUS_OK;
    vrrec_status_t write_packet_status = VRREC_STATUS_OK;
    vrrec_status_t write_trailer_status = VRREC_STATUS_OK;
    vrrec_status_t flush_status = VRREC_STATUS_OK;
    std::vector<MediaStreamKind> readback_streams;
    std::vector<FfmpegPacketTimestamps> scripted_rescale_results;
    std::vector<RescaleCall> rescale_calls;
    std::vector<WrittenPacket> written_packets;
    std::size_t rescale_index = 0;
    std::size_t write_header_calls = 0;
    std::size_t write_trailer_calls = 0;
    std::size_t flush_calls = 0;
    std::size_t abort_calls = 0;
};

void SnapshotsBothActualStreamTimeBasesOnlyAfterHeaderSuccess()
{
    RecordingFfmpegMuxerPort port;
    FfmpegFragmentedMp4Muxer muxer(port);

    CHECK(muxer.WriteHeader(TestMp4Streams()) == VRREC_STATUS_OK);
    CHECK(port.write_header_calls == 1);
    CHECK(port.readback_streams.size() == 2);
    CHECK(port.readback_streams[0] == MediaStreamKind::Video);
    CHECK(port.readback_streams[1] == MediaStreamKind::Audio);
    CHECK(port.header_configuration.audio.frame_size == 1'024);
    CHECK(port.header_configuration.audio.initial_padding_samples == 1'024);

    port.scripted_rescale_results = {{90'000, 90'000, 1'500}};
    CHECK(muxer.WritePacket(Packet(MediaStreamKind::Video, 1'000'000)) ==
        VRREC_STATUS_OK);
    CHECK(port.rescale_calls.size() == 1);
    CHECK(port.rescale_calls[0].source_time_base ==
        MicrosecondPacketTimeBase);
    CHECK(port.rescale_calls[0].destination_time_base ==
        (MediaTimeBase {1, 90'000}));
}

void HeaderFailureDoesNotReadTimeBasesAndTerminalizesExactlyOnce()
{
    RecordingFfmpegMuxerPort port;
    port.write_header_status = VRREC_STATUS_OUT_OF_MEMORY;
    FfmpegFragmentedMp4Muxer muxer(port);

    CHECK(muxer.WriteHeader(TestMp4Streams()) ==
        VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(port.readback_streams.empty());
    CHECK(port.abort_calls == 1);
    CHECK(muxer.WriteHeader(TestMp4Streams()) ==
        VRREC_STATUS_INVALID_STATE);
    CHECK(muxer.WritePacket(Packet(MediaStreamKind::Video, 0)) ==
        VRREC_STATUS_INVALID_STATE);
    muxer.Abort();
    CHECK(port.abort_calls == 1);
}

void RejectsInvalidOrUnavailablePostHeaderTimeBasesBeforePacketWrites()
{
    const std::vector<MediaTimeBase> invalid_time_bases {
        {0, 90'000},
        {-1, 90'000},
        {1, 0},
        {1, -90'000},
    };
    for (const auto invalid_time_base : invalid_time_bases) {
        RecordingFfmpegMuxerPort port;
        port.actual_video_time_base = invalid_time_base;
        FfmpegFragmentedMp4Muxer muxer(port);

        CHECK(muxer.WriteHeader(TestMp4Streams()) ==
            VRREC_STATUS_INTERNAL_ERROR);
        CHECK(port.rescale_calls.empty());
        CHECK(port.written_packets.empty());
        CHECK(port.abort_calls == 1);
    }

    RecordingFfmpegMuxerPort port;
    port.video_readback_status = VRREC_STATUS_INTERNAL_ERROR;
    FfmpegFragmentedMp4Muxer muxer(port);
    CHECK(muxer.WriteHeader(TestMp4Streams()) ==
        VRREC_STATUS_INTERNAL_ERROR);
    CHECK(port.abort_calls == 1);

    RecordingFfmpegMuxerPort audio_port;
    audio_port.audio_readback_status = VRREC_STATUS_OUT_OF_MEMORY;
    FfmpegFragmentedMp4Muxer audio_muxer(audio_port);
    CHECK(audio_muxer.WriteHeader(TestMp4Streams()) ==
        VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(audio_port.readback_streams.size() == 2);
    CHECK(audio_port.rescale_calls.empty());
    CHECK(audio_port.abort_calls == 1);

    RecordingFfmpegMuxerPort invalid_audio_port;
    invalid_audio_port.actual_audio_time_base = {0, 48'000};
    FfmpegFragmentedMp4Muxer invalid_audio_muxer(invalid_audio_port);
    CHECK(invalid_audio_muxer.WriteHeader(TestMp4Streams()) ==
        VRREC_STATUS_INTERNAL_ERROR);
    CHECK(invalid_audio_port.readback_streams.size() == 2);
    CHECK(invalid_audio_port.abort_calls == 1);
}

void UsesDifferentActualTimeBasesForVideoAndAudioWithoutMutatingCallerPackets()
{
    RecordingFfmpegMuxerPort port;
    port.scripted_rescale_results = {
        {3'000, 3'000, 1'500},
        {1'024, 1'024, 1'024},
    };
    FfmpegFragmentedMp4Muxer muxer(port);
    CHECK(muxer.WriteHeader(TestMp4Streams()) == VRREC_STATUS_OK);
    const auto video = Packet(MediaStreamKind::Video, 33'333);
    const auto audio = Packet(MediaStreamKind::Audio, 21'333);
    const auto video_before = video;
    const auto audio_before = audio;

    CHECK(muxer.WritePacket(video) == VRREC_STATUS_OK);
    CHECK(muxer.WritePacket(audio) == VRREC_STATUS_OK);

    CHECK(port.rescale_calls.size() == 2);
    CHECK(port.rescale_calls[0].destination_time_base ==
        (MediaTimeBase {1, 90'000}));
    CHECK(port.rescale_calls[1].destination_time_base ==
        (MediaTimeBase {1, 48'000}));
    CHECK(port.written_packets[0].stream_timestamps ==
        (FfmpegPacketTimestamps {3'000, 3'000, 1'500}));
    CHECK(port.written_packets[1].stream_timestamps ==
        (FfmpegPacketTimestamps {1'024, 1'024, 1'024}));
    CHECK(video.pts_microseconds == video_before.pts_microseconds);
    CHECK(video.payload == video_before.payload);
    CHECK(audio.dts_microseconds == audio_before.dts_microseconds);
    CHECK(audio.payload == audio_before.payload);
}

void PreservesOwnedSkipSamplesSideDataThroughTheInterleavedWriteSeam()
{
    RecordingFfmpegMuxerPort port;
    port.scripted_rescale_results = {{1'024, 1'024, 1'024}};
    FfmpegFragmentedMp4Muxer muxer(port);
    CHECK(muxer.WriteHeader(TestMp4Streams()) == VRREC_STATUS_OK);
    auto audio = Packet(MediaStreamKind::Audio, 21'333);
    audio.side_data.push_back({
        EncodedPacketSideDataKind::SkipSamples,
        std::vector<std::byte>(10, std::byte {0x04}),
    });

    CHECK(muxer.WritePacket(audio) == VRREC_STATUS_OK);

    CHECK(port.written_packets.size() == 1);
    CHECK(port.written_packets[0].canonical_packet.side_data ==
        audio.side_data);
}

void PreservesNegativeAacPrimingTimestampsThroughRescaleAndWrite()
{
    RecordingFfmpegMuxerPort port;
    port.scripted_rescale_results = {{-1'024, -1'024, 1'024}};
    FfmpegFragmentedMp4Muxer muxer(port);
    CHECK(muxer.WriteHeader(TestMp4Streams()) == VRREC_STATUS_OK);
    const auto priming = Packet(MediaStreamKind::Audio, -21'333);

    CHECK(muxer.WritePacket(priming) == VRREC_STATUS_OK);

    CHECK(port.rescale_calls.size() == 1);
    CHECK(port.rescale_calls[0].source ==
        (FfmpegPacketTimestamps {-21'333, -21'333, 21'333}));
    CHECK(port.written_packets.size() == 1);
    CHECK(port.written_packets[0].stream_timestamps ==
        (FfmpegPacketTimestamps {-1'024, -1'024, 1'024}));
}

void RejectsPacketsBeforeHeaderAndMalformedPacketsBeforeRescale()
{
    RecordingFfmpegMuxerPort port;
    FfmpegFragmentedMp4Muxer muxer(port);
    CHECK(muxer.WritePacket(Packet(MediaStreamKind::Video, 0)) ==
        VRREC_STATUS_INVALID_STATE);
    CHECK(port.rescale_calls.empty());
    CHECK(port.abort_calls == 0);

    CHECK(muxer.WriteHeader(TestMp4Streams()) == VRREC_STATUS_OK);
    auto malformed = Packet(MediaStreamKind::Video, 0);
    malformed.payload.clear();
    CHECK(muxer.WritePacket(malformed) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(port.rescale_calls.empty());
    CHECK(port.written_packets.empty());
    CHECK(port.abort_calls == 1);
}

void RejectsEveryMalformedCanonicalPacketBeforeRescale()
{
    std::vector<EncodedMediaPacket> invalid_packets;

    auto invalid_stream = Packet(MediaStreamKind::Video, 0);
    invalid_stream.stream = static_cast<MediaStreamKind>(99);
    invalid_packets.push_back(invalid_stream);

    auto negative_pts = Packet(MediaStreamKind::Video, 0);
    negative_pts.pts_microseconds = -1;
    invalid_packets.push_back(negative_pts);

    auto negative_dts = Packet(MediaStreamKind::Video, 0);
    negative_dts.dts_microseconds = -1;
    invalid_packets.push_back(negative_dts);

    invalid_packets.push_back(Packet(MediaStreamKind::Audio, -21'335));

    auto pts_before_dts = Packet(MediaStreamKind::Video, 10);
    pts_before_dts.dts_microseconds = 11;
    invalid_packets.push_back(pts_before_dts);

    auto zero_duration = Packet(MediaStreamKind::Video, 0);
    zero_duration.duration_microseconds = 0;
    invalid_packets.push_back(zero_duration);

    auto empty_payload = Packet(MediaStreamKind::Video, 0);
    empty_payload.payload.clear();
    invalid_packets.push_back(empty_payload);

    auto timestamp_end_overflow = Packet(
        MediaStreamKind::Audio,
        std::numeric_limits<std::int64_t>::max());
    timestamp_end_overflow.duration_microseconds = 1;
    invalid_packets.push_back(timestamp_end_overflow);

    auto empty_skip_samples = Packet(MediaStreamKind::Audio, 0);
    empty_skip_samples.side_data.push_back({
        EncodedPacketSideDataKind::SkipSamples,
        {},
    });
    invalid_packets.push_back(empty_skip_samples);

    auto short_skip_samples = Packet(MediaStreamKind::Audio, 0);
    short_skip_samples.side_data.push_back({
        EncodedPacketSideDataKind::SkipSamples,
        std::vector<std::byte>(9, std::byte {0x04}),
    });
    invalid_packets.push_back(short_skip_samples);

    auto oversized_skip_samples = Packet(MediaStreamKind::Audio, 0);
    oversized_skip_samples.side_data.push_back({
        EncodedPacketSideDataKind::SkipSamples,
        std::vector<std::byte>(11, std::byte {0x04}),
    });
    invalid_packets.push_back(oversized_skip_samples);

    auto video_skip_samples = Packet(MediaStreamKind::Video, 0);
    video_skip_samples.side_data.push_back({
        EncodedPacketSideDataKind::SkipSamples,
        std::vector<std::byte>(
            SkipSamplesSideDataSize,
            std::byte {0x04}),
    });
    invalid_packets.push_back(video_skip_samples);

    auto duplicate_skip_samples = Packet(MediaStreamKind::Audio, 0);
    duplicate_skip_samples.side_data = {
        {
            EncodedPacketSideDataKind::SkipSamples,
            std::vector<std::byte>(10, std::byte {0x04}),
        },
        {
            EncodedPacketSideDataKind::SkipSamples,
            std::vector<std::byte>(10, std::byte {0x05}),
        },
    };
    invalid_packets.push_back(duplicate_skip_samples);

    for (const auto &packet : invalid_packets) {
        RecordingFfmpegMuxerPort port;
        FfmpegFragmentedMp4Muxer muxer(port);
        CHECK(muxer.WriteHeader(TestMp4Streams()) == VRREC_STATUS_OK);

        CHECK(muxer.WritePacket(packet) == VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(port.rescale_calls.empty());
        CHECK(port.written_packets.empty());
        CHECK(port.abort_calls == 1);
    }
}

void RejectsInvalidRescaledTimestampsBeforeWrite()
{
    const std::vector<FfmpegPacketTimestamps> invalid_results {
        {std::numeric_limits<std::int64_t>::min(), 0, 1},
        {0, std::numeric_limits<std::int64_t>::min(), 1},
        {0, 0, std::numeric_limits<std::int64_t>::min()},
        {-1, 0, 1},
        {0, -1, 1},
        {0, 0, 0},
        {0, 1, 1},
        {-1, -1, 1},
        {
            std::numeric_limits<std::int64_t>::max(),
            std::numeric_limits<std::int64_t>::max(),
            1,
        },
    };
    for (const auto invalid_result : invalid_results) {
        RecordingFfmpegMuxerPort port;
        port.scripted_rescale_results = {invalid_result};
        FfmpegFragmentedMp4Muxer muxer(port);
        CHECK(muxer.WriteHeader(TestMp4Streams()) == VRREC_STATUS_OK);
        CHECK(muxer.WritePacket(Packet(MediaStreamKind::Video, 0)) ==
            VRREC_STATUS_INTERNAL_ERROR);
        CHECK(port.written_packets.empty());
        CHECK(port.abort_calls == 1);
    }
}

void RejectsPerStreamDtsCollisionsCreatedByRounding()
{
    RecordingFfmpegMuxerPort port;
    port.scripted_rescale_results = {
        {0, 0, 1},
        {0, 0, 1},
    };
    FfmpegFragmentedMp4Muxer muxer(port);
    CHECK(muxer.WriteHeader(TestMp4Streams()) == VRREC_STATUS_OK);

    CHECK(muxer.WritePacket(Packet(MediaStreamKind::Video, 0)) ==
        VRREC_STATUS_OK);
    CHECK(muxer.WritePacket(Packet(MediaStreamKind::Video, 1)) ==
        VRREC_STATUS_INTERNAL_ERROR);
    CHECK(port.rescale_calls.size() == 2);
    CHECK(port.written_packets.size() == 1);
    CHECK(port.abort_calls == 1);
}

void TracksRoundedDtsIndependentlyForEachStream()
{
    RecordingFfmpegMuxerPort port;
    port.scripted_rescale_results = {
        {0, 0, 1},
        {0, 0, 1},
    };
    FfmpegFragmentedMp4Muxer muxer(port);
    CHECK(muxer.WriteHeader(TestMp4Streams()) == VRREC_STATUS_OK);

    CHECK(muxer.WritePacket(Packet(MediaStreamKind::Video, 0)) ==
        VRREC_STATUS_OK);
    CHECK(muxer.WritePacket(Packet(MediaStreamKind::Audio, 0)) ==
        VRREC_STATUS_OK);
    CHECK(port.written_packets.size() == 2);
    CHECK(port.abort_calls == 0);
}

void WriteFailureAbortsAndRejectsFurtherMutation()
{
    RecordingFfmpegMuxerPort port;
    port.write_packet_status = VRREC_STATUS_INTERNAL_ERROR;
    FfmpegFragmentedMp4Muxer muxer(port);
    CHECK(muxer.WriteHeader(TestMp4Streams()) == VRREC_STATUS_OK);

    CHECK(muxer.WritePacket(Packet(MediaStreamKind::Video, 0)) ==
        VRREC_STATUS_INTERNAL_ERROR);
    CHECK(port.abort_calls == 1);
    const auto write_count = port.written_packets.size();
    CHECK(muxer.WritePacket(Packet(MediaStreamKind::Video, 33'333)) ==
        VRREC_STATUS_INVALID_STATE);
    CHECK(port.written_packets.size() == write_count);
}

void RequiresTrailerBeforeFlushAndTerminalizesEveryFailure()
{
    RecordingFfmpegMuxerPort port;
    FfmpegFragmentedMp4Muxer muxer(port);
    CHECK(muxer.FlushFile() == VRREC_STATUS_INVALID_STATE);
    CHECK(muxer.WriteTrailer() == VRREC_STATUS_INVALID_STATE);
    CHECK(muxer.WriteHeader(TestMp4Streams()) == VRREC_STATUS_OK);
    CHECK(muxer.FlushFile() == VRREC_STATUS_INVALID_STATE);
    CHECK(muxer.WriteTrailer() == VRREC_STATUS_OK);
    CHECK(muxer.WritePacket(Packet(MediaStreamKind::Video, 0)) ==
        VRREC_STATUS_INVALID_STATE);
    CHECK(muxer.FlushFile() == VRREC_STATUS_OK);
    CHECK(muxer.FlushFile() == VRREC_STATUS_INVALID_STATE);

    RecordingFfmpegMuxerPort trailer_failure_port;
    trailer_failure_port.write_trailer_status =
        VRREC_STATUS_INTERNAL_ERROR;
    FfmpegFragmentedMp4Muxer trailer_failure(trailer_failure_port);
    CHECK(trailer_failure.WriteHeader(TestMp4Streams()) ==
        VRREC_STATUS_OK);
    CHECK(trailer_failure.WriteTrailer() == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(trailer_failure_port.flush_calls == 0);
    CHECK(trailer_failure_port.abort_calls == 1);

    RecordingFfmpegMuxerPort flush_failure_port;
    flush_failure_port.flush_status = VRREC_STATUS_INTERNAL_ERROR;
    FfmpegFragmentedMp4Muxer flush_failure(flush_failure_port);
    CHECK(flush_failure.WriteHeader(TestMp4Streams()) ==
        VRREC_STATUS_OK);
    CHECK(flush_failure.WriteTrailer() == VRREC_STATUS_OK);
    CHECK(flush_failure.FlushFile() == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(flush_failure_port.abort_calls == 1);
}

void DestructorAbortsAnUnfinishedMuxerExactlyOnce()
{
    RecordingFfmpegMuxerPort port;
    {
        FfmpegFragmentedMp4Muxer muxer(port);
        CHECK(muxer.WriteHeader(TestMp4Streams()) == VRREC_STATUS_OK);
    }
    CHECK(port.abort_calls == 1);
}

void DestructorDoesNotReportAbortAfterDurableFinish()
{
    RecordingFfmpegMuxerPort port;
    {
        FfmpegFragmentedMp4Muxer muxer(port);
        CHECK(muxer.WriteHeader(TestMp4Streams()) == VRREC_STATUS_OK);
        CHECK(muxer.WriteTrailer() == VRREC_STATUS_OK);
        CHECK(muxer.FlushFile() == VRREC_STATUS_OK);
    }
    CHECK(port.abort_calls == 0);
}

void AbortAfterDurableFinishDoesNotInvalidateCommittedOutput()
{
    RecordingFfmpegMuxerPort port;
    FfmpegFragmentedMp4Muxer muxer(port);
    CHECK(muxer.WriteHeader(TestMp4Streams()) == VRREC_STATUS_OK);
    CHECK(muxer.WriteTrailer() == VRREC_STATUS_OK);
    CHECK(muxer.FlushFile() == VRREC_STATUS_OK);

    muxer.Abort();
    muxer.Abort();

    CHECK(port.abort_calls == 0);
}

}

int main()
{
    SnapshotsBothActualStreamTimeBasesOnlyAfterHeaderSuccess();
    HeaderFailureDoesNotReadTimeBasesAndTerminalizesExactlyOnce();
    RejectsInvalidOrUnavailablePostHeaderTimeBasesBeforePacketWrites();
    UsesDifferentActualTimeBasesForVideoAndAudioWithoutMutatingCallerPackets();
    PreservesOwnedSkipSamplesSideDataThroughTheInterleavedWriteSeam();
    PreservesNegativeAacPrimingTimestampsThroughRescaleAndWrite();
    RejectsPacketsBeforeHeaderAndMalformedPacketsBeforeRescale();
    RejectsEveryMalformedCanonicalPacketBeforeRescale();
    RejectsInvalidRescaledTimestampsBeforeWrite();
    RejectsPerStreamDtsCollisionsCreatedByRounding();
    TracksRoundedDtsIndependentlyForEachStream();
    WriteFailureAbortsAndRejectsFurtherMutation();
    RequiresTrailerBeforeFlushAndTerminalizesEveryFailure();
    DestructorAbortsAnUnfinishedMuxerExactlyOnce();
    DestructorDoesNotReportAbortAfterDurableFinish();
    AbortAfterDurableFinishDoesNotInvalidateCommittedOutput();
    return 0;
}
