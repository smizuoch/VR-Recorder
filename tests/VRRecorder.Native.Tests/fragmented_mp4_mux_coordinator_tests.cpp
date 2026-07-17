#include "fragmented_mp4_mux_coordinator.hpp"

#include <chrono>
#include <cstddef>
#include <condition_variable>
#include <cstdlib>
#include <iostream>
#include <limits>
#include <mutex>
#include <thread>
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

class RecordingMuxer final : public FragmentedMp4Muxer {
public:
    vrrec_status_t WriteHeader(
        const FragmentedMp4StreamConfiguration &configuration)
        noexcept override
    {
        order.push_back(0);
        ++header_calls;
        configurations.push_back(configuration);
        return header_status;
    }

    vrrec_status_t WritePacket(
        const EncodedMediaPacket &packet) noexcept override
    {
        ++write_calls;
        order.push_back(packet.stream == MediaStreamKind::Video ? 1 : 2);
        packets.push_back(packet);
        if (fail_write_call != 0 && write_calls == fail_write_call) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        return write_status;
    }

    vrrec_status_t WriteTrailer() noexcept override
    {
        order.push_back(4);
        return trailer_status;
    }

    vrrec_status_t FlushFile() noexcept override
    {
        order.push_back(5);
        return flush_status;
    }

    void Abort() noexcept override
    {
        order.push_back(6);
        ++abort_calls;
    }

    std::vector<int> order;
    std::vector<EncodedMediaPacket> packets;
    std::vector<FragmentedMp4StreamConfiguration> configurations;
    vrrec_status_t header_status = VRREC_STATUS_OK;
    vrrec_status_t write_status = VRREC_STATUS_OK;
    vrrec_status_t trailer_status = VRREC_STATUS_OK;
    vrrec_status_t flush_status = VRREC_STATUS_OK;
    std::size_t header_calls = 0;
    std::size_t write_calls = 0;
    std::size_t fail_write_call = 0;
    std::size_t abort_calls = 0;
};

class RecordingPacketObserver final : public EncodedMediaPacketObserver {
public:
    vrrec_status_t Observe(
        const EncodedMediaPacket &packet) noexcept override
    {
        packets.push_back(packet);
        return VRREC_STATUS_OK;
    }

    std::vector<EncodedMediaPacket> packets;
};

class FailingPacketObserver final : public EncodedMediaPacketObserver {
public:
    vrrec_status_t Observe(const EncodedMediaPacket &) noexcept override
    {
        ++observe_calls;
        return VRREC_STATUS_INTERNAL_ERROR;
    }

    std::size_t observe_calls = 0;
};

class AbortOnPacketObserver final : public EncodedMediaPacketObserver {
public:
    vrrec_status_t Observe(const EncodedMediaPacket &) noexcept override
    {
        ++observe_calls;
        CHECK(coordinator != nullptr);
        coordinator->Abort();
        abort_returned = true;
        return VRREC_STATUS_OK;
    }

    FragmentedMp4MuxCoordinator *coordinator = nullptr;
    std::size_t observe_calls = 0;
    bool abort_returned = false;
};

FragmentedMp4StreamConfiguration Streams()
{
    return {
        {
            MicrosecondPacketTimeBase,
            1'920,
            1'080,
            H264Profile::High,
            H264PacketFormat::AnnexB,
            {std::byte{0x01}, std::byte{0x64}},
        },
        {
            MicrosecondPacketTimeBase,
            48'000,
            2,
            1'024,
            1'024,
            AacProfile::LowComplexity,
            AudioChannelLayout::Stereo,
            AacPacketFormat::RawAccessUnit,
            {std::byte{0x12}, std::byte{0x10}},
            192'000,
        },
        DefaultFragmentedMp4FragmentPolicy,
    };
}

void ComparesEveryMuxDescriptorFieldByValue()
{
    const auto streams = Streams();
    CHECK(streams == streams);
    CHECK(streams.video == streams.video);
    CHECK(streams.audio == streams.audio);
    CHECK(streams.fragment_policy == streams.fragment_policy);

    const auto different_video = [&](const auto &mutate) {
        auto changed = streams.video;
        mutate(changed);
        CHECK(!(streams.video == changed));
    };
    different_video([](auto &value) { value.packet_time_base.numerator = 2; });
    different_video([](auto &value) { value.packet_time_base.denominator = 1; });
    different_video([](auto &value) { ++value.width; });
    different_video([](auto &value) { ++value.height; });
    different_video([](auto &value) { value.profile = H264Profile::Main; });
    different_video([](auto &value) {
        value.packet_format = H264PacketFormat::AvccLengthPrefixed;
    });
    different_video([](auto &value) {
        value.codec_extradata.push_back(std::byte {0xff});
    });

    const auto different_audio = [&](const auto &mutate) {
        auto changed = streams.audio;
        mutate(changed);
        CHECK(!(streams.audio == changed));
    };
    different_audio([](auto &value) { value.packet_time_base.numerator = 2; });
    different_audio([](auto &value) { value.packet_time_base.denominator = 1; });
    different_audio([](auto &value) { --value.sample_rate; });
    different_audio([](auto &value) { --value.channel_count; });
    different_audio([](auto &value) { --value.frame_size; });
    different_audio([](auto &value) { --value.initial_padding_samples; });
    different_audio([](auto &value) {
        value.profile = static_cast<AacProfile>(99);
    });
    different_audio([](auto &value) {
        value.channel_layout = static_cast<AudioChannelLayout>(99);
    });
    different_audio([](auto &value) {
        value.packet_format = static_cast<AacPacketFormat>(99);
    });
    different_audio([](auto &value) {
        value.codec_extradata.push_back(std::byte {0xff});
    });
    different_audio([](auto &value) { --value.bitrate_bits_per_second; });

    const auto different_policy = [&](const auto &mutate) {
        auto changed = streams.fragment_policy;
        mutate(changed);
        CHECK(!(streams.fragment_policy == changed));
    };
    different_policy([](auto &value) { --value.minimum_duration_microseconds; });
    different_policy([](auto &value) { ++value.maximum_duration_microseconds; });
    different_policy([](auto &value) { value.prefer_video_key_frames = false; });

    for (const auto field : {0, 1, 2}) {
        auto changed = streams;
        if (field == 0) {
            ++changed.video.width;
        } else if (field == 1) {
            --changed.audio.sample_rate;
        } else {
            changed.fragment_policy.prefer_video_key_frames = false;
        }
        CHECK(!(streams == changed));
    }

    const auto side_data = EncodedPacketSideData {
        EncodedPacketSideDataKind::SkipSamples,
        std::vector<std::byte>(SkipSamplesSideDataSize, std::byte {0}),
    };
    CHECK(side_data == side_data);
    auto changed_side_data = side_data;
    changed_side_data.kind = static_cast<EncodedPacketSideDataKind>(99);
    CHECK(!(side_data == changed_side_data));
    changed_side_data = side_data;
    changed_side_data.payload[0] = std::byte {1};
    CHECK(!(side_data == changed_side_data));
}

void Begin(FragmentedMp4MuxCoordinator &coordinator)
{
    CHECK(coordinator.Begin(Streams()) == VRREC_STATUS_OK);
}

EncodedMediaPacket Video(
    std::int64_t timestamp_microseconds,
    bool key_frame = false)
{
    return {
        MediaStreamKind::Video,
        timestamp_microseconds,
        timestamp_microseconds,
        33'333,
        key_frame,
        std::vector<std::byte>(1'024, std::byte{0x01}),
    };
}

EncodedMediaPacket Audio(std::int64_t timestamp_microseconds)
{
    return {
        MediaStreamKind::Audio,
        timestamp_microseconds,
        timestamp_microseconds,
        21'333,
        false,
        std::vector<std::byte>(512, std::byte{0x02}),
    };
}

void RequiresOwnedStreamDescriptorsBeforeWritingPackets()
{
    RecordingMuxer muxer;
    FragmentedMp4MuxCoordinator coordinator(muxer);
    CHECK(coordinator.Submit(Video(0, true)) ==
          Mp4MuxResult::InvalidState);
    CHECK(coordinator.Finish() == VRREC_STATUS_INVALID_STATE);
    auto streams = Streams();
    CHECK(coordinator.Begin(streams) == VRREC_STATUS_OK);
    streams.video.codec_extradata[0] = std::byte{0xff};

    CHECK(muxer.header_calls == 1);
    CHECK(muxer.configurations.size() == 1);
    CHECK(muxer.configurations[0].video.codec_extradata[0] ==
          std::byte{0x01});
    CHECK(muxer.configurations[0].audio.frame_size == 1'024);
    CHECK(muxer.configurations[0].audio.initial_padding_samples == 1'024);
    CHECK(
        muxer.configurations[0].audio.bitrate_bits_per_second == 192'000);
    CHECK(coordinator.Begin(Streams()) == VRREC_STATUS_INVALID_STATE);
    CHECK(coordinator.Submit(Video(0, true)) == Mp4MuxResult::Written);
    CHECK(muxer.order == std::vector<int>({0, 1}));
}

void ZeroPacketFinishAndDestructorFollowHeaderLifecycle()
{
    {
        RecordingMuxer muxer;
        FragmentedMp4MuxCoordinator coordinator(muxer);
        Begin(coordinator);

        CHECK(coordinator.Finish() == VRREC_STATUS_OK);
        CHECK(muxer.order == std::vector<int>({0, 4, 5}));
        CHECK(muxer.abort_calls == 0);
    }

    RecordingMuxer muxer;
    {
        FragmentedMp4MuxCoordinator coordinator(muxer);
        Begin(coordinator);
    }
    CHECK(muxer.order == std::vector<int>({0, 6}));
    CHECK(muxer.abort_calls == 1);
}

void EmptyBatchChecksLifecycleWithoutWritingAPacket()
{
    RecordingMuxer muxer;
    FragmentedMp4MuxCoordinator coordinator(muxer);
    const std::span<const EncodedMediaPacket> empty;

    CHECK(coordinator.SubmitBatch(empty) == Mp4MuxResult::InvalidState);
    Begin(coordinator);
    CHECK(coordinator.SubmitBatch(empty) == Mp4MuxResult::Written);
    CHECK(muxer.packets.empty());
    CHECK(coordinator.Finish() == VRREC_STATUS_OK);
    CHECK(coordinator.SubmitBatch(empty) == Mp4MuxResult::InvalidState);
}

void RejectsInvalidStreamDescriptorsBeforeHeaderMutation()
{
    const auto rejects = [](auto invalidate) {
        RecordingMuxer muxer;
        FragmentedMp4MuxCoordinator coordinator(muxer);
        auto streams = Streams();
        invalidate(streams);

        CHECK(coordinator.Begin(streams) == VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(muxer.header_calls == 0);
        CHECK(muxer.order.empty());
        CHECK(coordinator.Submit(Audio(0)) == Mp4MuxResult::InvalidState);
    };

    rejects([](auto &streams) {
        streams.video.packet_time_base = {1, 90'000};
    });
    rejects([](auto &streams) {
        streams.audio.packet_time_base = {0, 1'000'000};
    });
    rejects([](auto &streams) { streams.video.width = 0; });
    rejects([](auto &streams) { streams.video.width = 1'919; });
    rejects([](auto &streams) { streams.video.width = 16'386; });
    rejects([](auto &streams) { streams.video.height = 0; });
    rejects([](auto &streams) { streams.video.height = 1'079; });
    rejects([](auto &streams) { streams.video.height = 16'386; });
    rejects([](auto &streams) {
        streams.video.profile = static_cast<H264Profile>(99);
    });
    rejects([](auto &streams) {
        streams.video.packet_format = static_cast<H264PacketFormat>(99);
    });
    rejects([](auto &streams) { streams.video.codec_extradata.clear(); });
    rejects([](auto &streams) { streams.audio.sample_rate = 44'100; });
    rejects([](auto &streams) { streams.audio.channel_count = 1; });
    rejects([](auto &streams) {
        streams.audio.bitrate_bits_per_second = 0;
    });
    rejects([](auto &streams) {
        streams.audio.bitrate_bits_per_second = 128'000;
    });
    rejects([](auto &streams) {
        streams.audio.bitrate_bits_per_second = 191'999;
    });
    rejects([](auto &streams) {
        streams.audio.bitrate_bits_per_second = 192'001;
    });
    rejects([](auto &streams) {
        streams.audio.bitrate_bits_per_second =
            std::numeric_limits<std::uint32_t>::max();
    });
    rejects([](auto &streams) { streams.audio.frame_size = 0; });
    rejects([](auto &streams) {
        streams.audio.frame_size =
            static_cast<std::uint32_t>(
                std::numeric_limits<std::int32_t>::max()) + 1U;
    });
    rejects([](auto &streams) {
        streams.audio.initial_padding_samples =
            static_cast<std::uint32_t>(
                std::numeric_limits<std::int32_t>::max()) + 1U;
    });
    rejects([](auto &streams) {
        streams.audio.profile = static_cast<AacProfile>(99);
    });
    rejects([](auto &streams) {
        streams.audio.channel_layout =
            static_cast<AudioChannelLayout>(99);
    });
    rejects([](auto &streams) {
        streams.audio.packet_format = static_cast<AacPacketFormat>(99);
    });
    rejects([](auto &streams) { streams.audio.codec_extradata.clear(); });
    rejects([](auto &streams) {
        streams.fragment_policy.minimum_duration_microseconds = 0;
    });
    rejects([](auto &streams) {
        ++streams.fragment_policy.maximum_duration_microseconds;
    });
    rejects([](auto &streams) {
        streams.fragment_policy.prefer_video_key_frames = false;
    });
}

void AcceptsEverySupportedH264DescriptorVariant()
{
    for (const auto profile : {H264Profile::Main, H264Profile::High}) {
        for (const auto packet_format : {
                 H264PacketFormat::AnnexB,
                 H264PacketFormat::AvccLengthPrefixed,
             }) {
            RecordingMuxer muxer;
            FragmentedMp4MuxCoordinator coordinator(muxer);
            auto streams = Streams();
            streams.video.profile = profile;
            streams.video.packet_format = packet_format;
            CHECK(coordinator.Begin(streams) == VRREC_STATUS_OK);
        }
    }
}

void CalculatesAacPrimingBoundsForMissingAndRoundedInputs()
{
    auto audio = Streams().audio;
    audio.sample_rate = 0;
    CHECK(AacPrimingLowerBoundMicroseconds(audio) == 0);
    audio.sample_rate = 48'000;
    audio.initial_padding_samples = 0;
    CHECK(AacPrimingLowerBoundMicroseconds(audio) == 0);
    audio.initial_padding_samples = 1;
    CHECK(AacPrimingLowerBoundMicroseconds(audio) == -21);
}

void HeaderFailureAbortsWithoutAcceptingPacketsOrTrailer()
{
    RecordingMuxer muxer;
    muxer.header_status = VRREC_STATUS_INTERNAL_ERROR;
    FragmentedMp4MuxCoordinator coordinator(muxer);

    CHECK(coordinator.Begin(Streams()) == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(muxer.order == std::vector<int>({0, 6}));
    CHECK(muxer.abort_calls == 1);
    CHECK(coordinator.Submit(Video(0, true)) ==
          Mp4MuxResult::InvalidState);
    CHECK(coordinator.Finish() == VRREC_STATUS_INVALID_STATE);
}

void DelegatesFragmentCutsToTheMuxerHeaderPolicy()
{
    RecordingMuxer muxer;
    FragmentedMp4MuxCoordinator coordinator(muxer);
    const auto streams = Streams();

    CHECK(streams.fragment_policy.minimum_duration_microseconds ==
          1'000'000);
    CHECK(streams.fragment_policy.maximum_duration_microseconds ==
          2'000'000);
    CHECK(streams.fragment_policy.prefer_video_key_frames);
    CHECK(coordinator.Begin(streams) == VRREC_STATUS_OK);
    CHECK(coordinator.Submit(Video(0, true)) == Mp4MuxResult::Written);
    CHECK(coordinator.Submit(Audio(2'000'000)) == Mp4MuxResult::Written);
    CHECK(coordinator.Submit(Video(33'333)) == Mp4MuxResult::Written);
    CHECK(muxer.order == std::vector<int>({0, 1, 2, 1}));
}

void SerializesAudioAndVideoPacketsWithoutChangingTimestamps()
{
    RecordingMuxer muxer;
    FragmentedMp4MuxCoordinator coordinator(muxer);
    Begin(coordinator);

    CHECK(coordinator.Submit(Video(0, true)) == Mp4MuxResult::Written);
    CHECK(coordinator.Submit(Audio(0)) == Mp4MuxResult::Written);
    CHECK(coordinator.Submit(Video(33'333)) == Mp4MuxResult::Written);
    CHECK(muxer.packets.size() == 3);
    CHECK(muxer.packets[0].pts_microseconds == 0);
    CHECK(muxer.packets[1].stream == MediaStreamKind::Audio);
    CHECK(muxer.packets[2].dts_microseconds == 33'333);
}

void PreservesTheBoundedNegativeAacPrimingEpochForTheMuxer()
{
    RecordingMuxer muxer;
    FragmentedMp4MuxCoordinator coordinator(muxer);
    Begin(coordinator);

    CHECK(coordinator.Submit(Audio(-21'333)) == Mp4MuxResult::Written);
    CHECK(coordinator.Submit(Audio(0)) == Mp4MuxResult::Written);
    CHECK(muxer.packets.size() == 2);
    CHECK(muxer.packets[0].pts_microseconds == -21'333);
    CHECK(muxer.packets[0].dts_microseconds == -21'333);

    RecordingMuxer too_early_muxer;
    FragmentedMp4MuxCoordinator too_early(too_early_muxer);
    Begin(too_early);
    CHECK(too_early.Submit(Audio(-21'335)) ==
        Mp4MuxResult::InvalidPacket);
    CHECK(too_early_muxer.packets.empty());

    RecordingMuxer no_padding_muxer;
    FragmentedMp4MuxCoordinator no_padding(no_padding_muxer);
    auto no_padding_streams = Streams();
    no_padding_streams.audio.initial_padding_samples = 0;
    CHECK(no_padding.Begin(no_padding_streams) == VRREC_STATUS_OK);
    CHECK(no_padding.Submit(Audio(-1)) == Mp4MuxResult::InvalidPacket);
    CHECK(no_padding_muxer.packets.empty());
}

void RejectsMalformedTimingAndSkipSamplesBeforeMuxMutation()
{
    std::vector<EncodedMediaPacket> invalid_packets;

    invalid_packets.push_back(Video(-1));

    auto missing_audio_timestamp = Audio(0);
    missing_audio_timestamp.pts_microseconds = UnknownMediaTimestamp;
    invalid_packets.push_back(missing_audio_timestamp);

    auto missing_audio_dts = Audio(0);
    missing_audio_dts.dts_microseconds = UnknownMediaTimestamp;
    invalid_packets.push_back(missing_audio_dts);

    auto invalid_stream = Video(0);
    invalid_stream.stream = static_cast<MediaStreamKind>(99);
    invalid_packets.push_back(invalid_stream);

    auto pts_before_dts = Video(1);
    pts_before_dts.pts_microseconds = 0;
    invalid_packets.push_back(pts_before_dts);

    auto negative_duration = Video(0);
    negative_duration.duration_microseconds = -1;
    invalid_packets.push_back(negative_duration);

    auto too_early_audio_dts = Audio(-21'333);
    too_early_audio_dts.pts_microseconds = -21'334;
    too_early_audio_dts.dts_microseconds = -21'335;
    invalid_packets.push_back(too_early_audio_dts);

    auto timestamp_end_overflow = Audio(
        std::numeric_limits<std::int64_t>::max());
    timestamp_end_overflow.duration_microseconds = 1;
    invalid_packets.push_back(timestamp_end_overflow);

    auto short_skip_samples = Audio(0);
    short_skip_samples.side_data.push_back({
        EncodedPacketSideDataKind::SkipSamples,
        std::vector<std::byte>(9, std::byte {0x04}),
    });
    invalid_packets.push_back(short_skip_samples);

    auto oversized_skip_samples = Audio(0);
    oversized_skip_samples.side_data.push_back({
        EncodedPacketSideDataKind::SkipSamples,
        std::vector<std::byte>(11, std::byte {0x04}),
    });
    invalid_packets.push_back(oversized_skip_samples);

    auto video_skip_samples = Video(0);
    video_skip_samples.side_data.push_back({
        EncodedPacketSideDataKind::SkipSamples,
        std::vector<std::byte>(
            SkipSamplesSideDataSize,
            std::byte {0x04}),
    });
    invalid_packets.push_back(video_skip_samples);

    auto duplicate_skip_samples = Audio(0);
    duplicate_skip_samples.side_data = {
        {
            EncodedPacketSideDataKind::SkipSamples,
            std::vector<std::byte>(
                SkipSamplesSideDataSize,
                std::byte {0x04}),
        },
        {
            EncodedPacketSideDataKind::SkipSamples,
            std::vector<std::byte>(
                SkipSamplesSideDataSize,
                std::byte {0x05}),
        },
    };
    invalid_packets.push_back(duplicate_skip_samples);

    auto unknown_side_data = Audio(0);
    unknown_side_data.side_data.push_back({
        static_cast<EncodedPacketSideDataKind>(99),
        std::vector<std::byte>(
            SkipSamplesSideDataSize,
            std::byte {0x04}),
    });
    invalid_packets.push_back(unknown_side_data);

    for (const auto &packet : invalid_packets) {
        RecordingMuxer muxer;
        FragmentedMp4MuxCoordinator coordinator(muxer);
        Begin(coordinator);

        CHECK(coordinator.Submit(packet) == Mp4MuxResult::InvalidPacket);
        CHECK(muxer.packets.empty());
    }
}

void OwnsPacketPayloadAndRejectsEmptyPayloadBeforeMuxMutation()
{
    RecordingMuxer muxer;
    FragmentedMp4MuxCoordinator coordinator(muxer);
    Begin(coordinator);
    auto packet = Video(0, true);
    std::vector<std::byte> source = {
        std::byte{0x01},
        std::byte{0x02},
        std::byte{0x03},
    };
    packet.payload = source;
    source[0] = std::byte{0xff};

    CHECK(coordinator.Submit(packet) == Mp4MuxResult::Written);
    CHECK(muxer.packets.size() == 1);
    CHECK(muxer.packets[0].payload == std::vector<std::byte>({
        std::byte{0x01},
        std::byte{0x02},
        std::byte{0x03},
    }));

    auto empty = Video(33'333);
    empty.payload.clear();
    CHECK(coordinator.Submit(empty) == Mp4MuxResult::InvalidPacket);
    CHECK(muxer.packets.size() == 1);
}

void RejectsNonMonotonicDtsPerStream()
{
    RecordingMuxer muxer;
    FragmentedMp4MuxCoordinator coordinator(muxer);
    Begin(coordinator);

    CHECK(coordinator.Submit(Video(100, true)) == Mp4MuxResult::Written);
    CHECK(coordinator.Submit(Video(99)) == Mp4MuxResult::InvalidPacket);
    CHECK(coordinator.Submit(Audio(100)) == Mp4MuxResult::Written);
    CHECK(coordinator.Submit(Audio(100)) == Mp4MuxResult::InvalidPacket);
    CHECK(muxer.packets.size() == 2);
}

void PreflightsAnEntireBatchBeforeWritingItsValidPrefix()
{
    RecordingMuxer muxer;
    RecordingPacketObserver observer;
    FragmentedMp4MuxCoordinator coordinator(muxer, &observer);
    Begin(coordinator);
    auto invalid = Video(33'333);
    invalid.duration_microseconds = 0;
    const std::vector<EncodedMediaPacket> batch {
        Video(0, true),
        invalid,
    };

    CHECK(coordinator.SubmitBatch(batch) == Mp4MuxResult::InvalidPacket);
    CHECK(muxer.packets.empty());
    CHECK(observer.packets.empty());
}

void AbortsAnNthWriteFailureWithoutObservingTheBatchPrefix()
{
    RecordingMuxer muxer;
    muxer.fail_write_call = 2;
    RecordingPacketObserver observer;
    FragmentedMp4MuxCoordinator coordinator(muxer, &observer);
    Begin(coordinator);
    const std::vector<EncodedMediaPacket> batch {
        Video(0, true),
        Video(33'333),
        Video(66'666),
    };

    CHECK(coordinator.SubmitBatch(batch) == Mp4MuxResult::MuxFailed);
    CHECK(muxer.write_calls == 2);
    CHECK(muxer.packets.size() == 2);
    CHECK(observer.packets.empty());
    CHECK(muxer.abort_calls == 1);
    CHECK(coordinator.Submit(Video(99'999)) ==
          Mp4MuxResult::InvalidState);
    CHECK(coordinator.Finish() == VRREC_STATUS_INVALID_STATE);
}

void FinalizesFragmentTrailerAndFileInOrder()
{
    RecordingMuxer muxer;
    FragmentedMp4MuxCoordinator coordinator(muxer);
    Begin(coordinator);
    CHECK(coordinator.Submit(Video(0, true)) == Mp4MuxResult::Written);

    CHECK(coordinator.Finish() == VRREC_STATUS_OK);
    CHECK(muxer.order == std::vector<int>({0, 1, 4, 5}));
    CHECK(coordinator.Submit(Audio(1)) == Mp4MuxResult::InvalidState);
}

void AbortNeverWritesATrailerAndIsIdempotent()
{
    RecordingMuxer muxer;
    FragmentedMp4MuxCoordinator coordinator(muxer);
    coordinator.Abort();
    coordinator.Abort();
    CHECK(muxer.order == std::vector<int>({6}));
    CHECK(muxer.abort_calls == 1);
}

void ObserverFailureAbortsTheIncompleteFile()
{
    RecordingMuxer muxer;
    FailingPacketObserver observer;
    FragmentedMp4MuxCoordinator coordinator(muxer, &observer);
    Begin(coordinator);

    CHECK(coordinator.Submit(Video(0, true)) == Mp4MuxResult::MuxFailed);
    CHECK(observer.observe_calls == 1);
    CHECK(muxer.packets.size() == 1);
    CHECK(muxer.abort_calls == 1);
    CHECK(coordinator.Finish() == VRREC_STATUS_INVALID_STATE);
    CHECK(coordinator.Submit(Audio(0)) == Mp4MuxResult::InvalidState);
}

void ObserverCanAbortTheCoordinatorWithoutDeadlocking()
{
    RecordingMuxer muxer;
    AbortOnPacketObserver observer;
    FragmentedMp4MuxCoordinator coordinator(muxer, &observer);
    observer.coordinator = &coordinator;
    Begin(coordinator);

    std::mutex watchdog_mutex;
    std::condition_variable watchdog_changed;
    bool submit_completed = false;
    std::thread watchdog([&] {
        std::unique_lock lock(watchdog_mutex);
        if (!watchdog_changed.wait_for(
                lock,
                std::chrono::seconds(2),
                [&] { return submit_completed; })) {
            std::cerr << __func__
                      << " timed out waiting for reentrant Abort\n";
            std::abort();
        }
    });

    const std::vector<EncodedMediaPacket> batch {
        Video(0, true),
        Video(33'333),
        Video(66'666),
    };
    const auto result = coordinator.SubmitBatch(batch);
    {
        const std::lock_guard lock(watchdog_mutex);
        submit_completed = true;
    }
    watchdog_changed.notify_all();
    watchdog.join();

    CHECK(result == Mp4MuxResult::MuxFailed);
    CHECK(observer.observe_calls == 1);
    CHECK(observer.abort_returned);
    CHECK(muxer.packets.size() == 3);
    CHECK(muxer.abort_calls == 1);
    CHECK(coordinator.Submit(Audio(0)) == Mp4MuxResult::InvalidState);
    CHECK(coordinator.Finish() == VRREC_STATUS_INVALID_STATE);
}

void FinalizationFailureStopsAtTheExactFailedStageAndAbortsOnce()
{
    {
        RecordingMuxer muxer;
        muxer.trailer_status = VRREC_STATUS_INTERNAL_ERROR;
        FragmentedMp4MuxCoordinator coordinator(muxer);
        Begin(coordinator);
        CHECK(coordinator.Submit(Video(0, true)) == Mp4MuxResult::Written);
        CHECK(coordinator.Finish() == VRREC_STATUS_INTERNAL_ERROR);
        CHECK(muxer.order == std::vector<int>({0, 1, 4, 6}));
        CHECK(muxer.abort_calls == 1);
        CHECK(coordinator.Submit(Audio(0)) == Mp4MuxResult::InvalidState);
    }

    {
        RecordingMuxer muxer;
        muxer.flush_status = VRREC_STATUS_INTERNAL_ERROR;
        FragmentedMp4MuxCoordinator coordinator(muxer);
        Begin(coordinator);
        CHECK(coordinator.Submit(Video(0, true)) == Mp4MuxResult::Written);
        CHECK(coordinator.Finish() == VRREC_STATUS_INTERNAL_ERROR);
        CHECK(muxer.order == std::vector<int>({0, 1, 4, 5, 6}));
        CHECK(muxer.abort_calls == 1);
        CHECK(coordinator.Submit(Audio(0)) == Mp4MuxResult::InvalidState);
    }
}

}

int main()
{
    ComparesEveryMuxDescriptorFieldByValue();
    RequiresOwnedStreamDescriptorsBeforeWritingPackets();
    ZeroPacketFinishAndDestructorFollowHeaderLifecycle();
    EmptyBatchChecksLifecycleWithoutWritingAPacket();
    RejectsInvalidStreamDescriptorsBeforeHeaderMutation();
    AcceptsEverySupportedH264DescriptorVariant();
    CalculatesAacPrimingBoundsForMissingAndRoundedInputs();
    HeaderFailureAbortsWithoutAcceptingPacketsOrTrailer();
    DelegatesFragmentCutsToTheMuxerHeaderPolicy();
    SerializesAudioAndVideoPacketsWithoutChangingTimestamps();
    PreservesTheBoundedNegativeAacPrimingEpochForTheMuxer();
    RejectsMalformedTimingAndSkipSamplesBeforeMuxMutation();
    OwnsPacketPayloadAndRejectsEmptyPayloadBeforeMuxMutation();
    RejectsNonMonotonicDtsPerStream();
    PreflightsAnEntireBatchBeforeWritingItsValidPrefix();
    AbortsAnNthWriteFailureWithoutObservingTheBatchPrefix();
    FinalizesFragmentTrailerAndFileInOrder();
    AbortNeverWritesATrailerAndIsIdempotent();
    ObserverFailureAbortsTheIncompleteFile();
    ObserverCanAbortTheCoordinatorWithoutDeadlocking();
    FinalizationFailureStopsAtTheExactFailedStageAndAbortsOnce();
    return 0;
}
