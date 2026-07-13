#include "ffmpeg_aac_packet_encoder.hpp"

#include <array>
#include <chrono>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <condition_variable>
#include <iostream>
#include <limits>
#include <mutex>
#include <span>
#include <thread>
#include <utility>
#include <vector>

extern "C" {
#include <libavcodec/avcodec.h>
#include <libavcodec/version.h>
#include <libavutil/avutil.h>
#include <libavutil/log.h>
#include <libavutil/mem.h>
#include <libavutil/version.h>
#include <libswresample/swresample.h>
#include <libswresample/version.h>
}

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

constexpr std::uint32_t SampleRate = 48'000;
constexpr std::size_t ChannelCount = 2;
constexpr std::array<std::byte, 5> ExpectedAudioSpecificConfig {
    std::byte {0x11},
    std::byte {0x90},
    std::byte {0x56},
    std::byte {0xe5},
    std::byte {0x00},
};

AacAudioEncoderConfig ExactConfig()
{
    AacAudioEncoderConfig config {};
    CHECK(CreateAacAudioEncoderConfig(config) == VRREC_STATUS_OK);
    return config;
}

FfmpegAacPacketEncoderCreateResult CreateForTest(
    FfmpegAacPacketEncoderFailurePoint failure_point =
        FfmpegAacPacketEncoderFailurePoint::None,
    FfmpegAacPreparedFrameObserver *observer = nullptr,
    std::size_t fail_on_occurrence = 1,
    FfmpegAacSerializationObserver *serialization_observer = nullptr)
{
    return FfmpegAacPacketEncoder::CreateForTesting(
        ExactConfig(),
        LIBAVCODEC_VERSION_INT,
        LIBAVUTIL_VERSION_INT,
        LIBSWRESAMPLE_VERSION_INT,
        "8.1.2",
        failure_point,
        observer,
        fail_on_occurrence,
        serialization_observer);
}

std::vector<float> StereoFrames(std::size_t frame_count)
{
    std::vector<float> samples(frame_count * ChannelCount);
    for (std::size_t index = 0; index < frame_count; ++index) {
        samples[index * ChannelCount] =
            static_cast<float>(index) + 0.25F;
        samples[index * ChannelCount + 1] =
            -static_cast<float>(index) - 0.5F;
    }
    return samples;
}

void AppendPackets(
    std::vector<EncodedMediaPacket> &destination,
    PacketAudioEncoderWrite source)
{
    CHECK(source.status == VRREC_STATUS_OK);
    for (auto &packet : source.packets) {
        destination.push_back(std::move(packet));
    }
}

struct ExpectedPacket final {
    std::int64_t pts_microseconds;
    std::int64_t duration_microseconds;
};

void CheckPackets(
    const std::vector<EncodedMediaPacket> &packets,
    std::span<const ExpectedPacket> expected)
{
    CHECK(packets.size() == expected.size());
    for (std::size_t index = 0; index < packets.size(); ++index) {
        const auto &packet = packets[index];
        CHECK(packet.stream == MediaStreamKind::Audio);
        CHECK(packet.pts_microseconds == expected[index].pts_microseconds);
        CHECK(packet.dts_microseconds == expected[index].pts_microseconds);
        CHECK(
            packet.duration_microseconds ==
            expected[index].duration_microseconds);
        CHECK(packet.key_frame);
        CHECK(!packet.payload.empty());
        CHECK(packet.side_data.empty());
        CHECK(
            packet.payload.size() < 2 ||
            !(packet.payload[0] == std::byte {0xff} &&
                (packet.payload[1] & std::byte {0xf0}) ==
                    std::byte {0xf0}));
    }
}

std::vector<EncodedMediaPacket> EncodeAndFinish(
    std::size_t frame_count,
    std::uint64_t start_frame_48k = 0)
{
    auto creation = CreateForTest();
    CHECK(creation.status == VRREC_STATUS_OK);
    CHECK(creation.encoder != nullptr);
    std::vector<EncodedMediaPacket> packets;
    if (frame_count != 0) {
        auto samples = StereoFrames(frame_count);
        AppendPackets(
            packets,
            creation.encoder->EncodePcm48k(start_frame_48k, samples));
    }
    AppendPackets(packets, creation.encoder->Finish());
    return packets;
}

void CreatesTheExactPinnedDescriptorAndOwnsItsAsc()
{
    CHECK(avcodec_version() == LIBAVCODEC_VERSION_INT);
    CHECK(avutil_version() == LIBAVUTIL_VERSION_INT);
    CHECK(swresample_version() == LIBSWRESAMPLE_VERSION_INT);
    CHECK(std::strcmp(av_version_info(), "8.1.2") == 0);

    auto creation = FfmpegAacPacketEncoder::Create(ExactConfig());
    CHECK(creation.status == VRREC_STATUS_OK);
    CHECK(creation.encoder != nullptr);
    CHECK(creation.descriptor.has_value());
    auto descriptor = std::move(*creation.descriptor);
    creation.descriptor.reset();
    creation.encoder.reset();

    CHECK(descriptor.packet_time_base == MicrosecondPacketTimeBase);
    CHECK(descriptor.sample_rate == SampleRate);
    CHECK(descriptor.channel_count == ChannelCount);
    CHECK(descriptor.frame_size == 1'024);
    CHECK(descriptor.initial_padding_samples == 1'024);
    CHECK(descriptor.profile == AacProfile::LowComplexity);
    CHECK(descriptor.channel_layout == AudioChannelLayout::Stereo);
    CHECK(descriptor.packet_format == AacPacketFormat::RawAccessUnit);
    CHECK(
        descriptor.codec_extradata ==
        std::vector<std::byte>(
            ExpectedAudioSpecificConfig.begin(),
            ExpectedAudioSpecificConfig.end()));
}

void EncodesEverySmallLastFrameBoundaryExactly()
{
    CheckPackets(EncodeAndFinish(0), {});

    constexpr std::array one {
        ExpectedPacket {-21'333, 21'333},
        ExpectedPacket {0, 21},
    };
    CheckPackets(EncodeAndFinish(1), one);

    constexpr std::array one_thousand_twenty_three {
        ExpectedPacket {-21'333, 21'333},
        ExpectedPacket {0, 21'313},
    };
    CheckPackets(EncodeAndFinish(1'023), one_thousand_twenty_three);

    constexpr std::array one_thousand_twenty_four {
        ExpectedPacket {-21'333, 21'333},
        ExpectedPacket {0, 21'333},
    };
    CheckPackets(EncodeAndFinish(1'024), one_thousand_twenty_four);

    constexpr std::array one_thousand_twenty_five {
        ExpectedPacket {-21'333, 21'333},
        ExpectedPacket {0, 21'333},
        ExpectedPacket {21'333, 21},
    };
    CheckPackets(EncodeAndFinish(1'025), one_thousand_twenty_five);
}

struct ObservedFrame final {
    std::int64_t pts_48k;
    std::vector<float> left;
    std::vector<float> right;
};

class PreparedFrameObserver final : public FfmpegAacPreparedFrameObserver {
public:
    void Observe(
        std::int64_t pts_48k,
        std::span<const float> left,
        std::span<const float> right) noexcept override
    {
        try {
            frames.push_back({
                pts_48k,
                {left.begin(), left.end()},
                {right.begin(), right.end()},
            });
        } catch (...) {
            failed = true;
        }
    }

    std::vector<ObservedFrame> frames;
    bool failed = false;
};

void PreservesPackedChannelsAcrossArbitraryChunkBoundaries()
{
    PreparedFrameObserver observer;
    auto creation = CreateForTest(
        FfmpegAacPacketEncoderFailurePoint::None,
        &observer);
    CHECK(creation.status == VRREC_STATUS_OK);

    auto first = StereoFrames(600);
    auto second = StereoFrames(600);
    for (std::size_t index = 0; index < 600; ++index) {
        second[index * ChannelCount] += 600.0F;
        second[index * ChannelCount + 1] -= 600.0F;
    }
    std::vector<float> complete = first;
    complete.insert(complete.end(), second.begin(), second.end());

    std::vector<EncodedMediaPacket> packets;
    auto first_write = creation.encoder->EncodePcm48k(0, first);
    CHECK(first_write.status == VRREC_STATUS_OK);
    CHECK(first_write.packets.empty());
    AppendPackets(packets, std::move(first_write));
    AppendPackets(
        packets,
        creation.encoder->EncodePcm48k(600, second));
    AppendPackets(packets, creation.encoder->Finish());

    constexpr std::array expected_packets {
        ExpectedPacket {-21'333, 21'333},
        ExpectedPacket {0, 21'333},
        ExpectedPacket {21'333, 3'667},
    };
    CheckPackets(packets, expected_packets);
    CHECK(!observer.failed);
    CHECK(observer.frames.size() == 2);
    CHECK(observer.frames[0].pts_48k == 0);
    CHECK(observer.frames[0].left.size() == 1'024);
    CHECK(observer.frames[1].pts_48k == 1'024);
    CHECK(observer.frames[1].left.size() == 176);
    for (std::size_t index = 0; index < 1'200; ++index) {
        const auto frame_index = index < 1'024 ? 0U : 1U;
        const auto plane_index = index < 1'024 ? index : index - 1'024;
        CHECK(
            observer.frames[frame_index].left[plane_index] ==
            complete[index * ChannelCount]);
        CHECK(
            observer.frames[frame_index].right[plane_index] ==
            complete[index * ChannelCount + 1]);
    }
}

void ReusesWritableFramesForTwoFullAacFrames()
{
    PreparedFrameObserver observer;
    auto creation = CreateForTest(
        FfmpegAacPacketEncoderFailurePoint::None,
        &observer);
    CHECK(creation.status == VRREC_STATUS_OK);
    auto samples = StereoFrames(2'048);
    std::vector<EncodedMediaPacket> packets;
    AppendPackets(packets, creation.encoder->EncodePcm48k(0, samples));
    AppendPackets(packets, creation.encoder->Finish());

    constexpr std::array expected_packets {
        ExpectedPacket {-21'333, 21'333},
        ExpectedPacket {0, 21'333},
        ExpectedPacket {21'333, 21'333},
    };
    CheckPackets(packets, expected_packets);
    CHECK(!observer.failed);
    CHECK(observer.frames.size() == 2);
    CHECK(observer.frames[0].pts_48k == 0);
    CHECK(observer.frames[1].pts_48k == 1'024);
    CHECK(observer.frames[0].left.size() == 1'024);
    CHECK(observer.frames[1].left.size() == 1'024);
}

void AllowsANonzeroFirstFrameAndPreservesItsPts()
{
    PreparedFrameObserver observer;
    auto creation = CreateForTest(
        FfmpegAacPacketEncoderFailurePoint::None,
        &observer);
    CHECK(creation.status == VRREC_STATUS_OK);
    auto samples = StereoFrames(1'024);
    std::vector<EncodedMediaPacket> packets;
    AppendPackets(
        packets,
        creation.encoder->EncodePcm48k(480, samples));
    AppendPackets(packets, creation.encoder->Finish());

    constexpr std::array expected_packets {
        ExpectedPacket {-11'333, 21'333},
        ExpectedPacket {10'000, 21'333},
    };
    CheckPackets(packets, expected_packets);
    CHECK(observer.frames.size() == 1);
    CHECK(observer.frames.front().pts_48k == 480);
}

template<typename InvalidWrite>
void CheckInvalidWriteIsTerminal(InvalidWrite invalid_write)
{
    auto creation = CreateForTest();
    CHECK(creation.status == VRREC_STATUS_OK);
    auto failure = invalid_write(*creation.encoder);
    CHECK(failure.status == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(failure.packets.empty());
    auto valid = StereoFrames(1);
    CHECK(
        creation.encoder->EncodePcm48k(0, valid).status ==
        VRREC_STATUS_INVALID_STATE);
    CHECK(creation.encoder->Finish().status == VRREC_STATUS_INVALID_STATE);
    creation.encoder->Abort();
    creation.encoder->Abort();
}

void RejectsMalformedSamplesAndTimelineDiscontinuitiesTerminally()
{
    CheckInvalidWriteIsTerminal([](FfmpegAacPacketEncoder &encoder) {
        return encoder.EncodePcm48k(0, {});
    });
    CheckInvalidWriteIsTerminal([](FfmpegAacPacketEncoder &encoder) {
        const std::array odd {0.0F};
        return encoder.EncodePcm48k(0, odd);
    });
    CheckInvalidWriteIsTerminal([](FfmpegAacPacketEncoder &encoder) {
        const std::array samples {
            std::numeric_limits<float>::quiet_NaN(),
            0.0F,
        };
        return encoder.EncodePcm48k(0, samples);
    });
    CheckInvalidWriteIsTerminal([](FfmpegAacPacketEncoder &encoder) {
        const std::array samples {
            0.0F,
            std::numeric_limits<float>::infinity(),
        };
        return encoder.EncodePcm48k(0, samples);
    });
    CheckInvalidWriteIsTerminal([](FfmpegAacPacketEncoder &encoder) {
        const std::array samples {0.0F, 0.0F};
        return encoder.EncodePcm48k(
            static_cast<std::uint64_t>(
                std::numeric_limits<std::int64_t>::max()),
            samples);
    });
    CheckInvalidWriteIsTerminal([](FfmpegAacPacketEncoder &encoder) {
        constexpr auto unsafe_seconds =
            static_cast<std::uint64_t>(
                std::numeric_limits<std::int64_t>::max()) /
                1'000'000U +
            1U;
        constexpr auto unsafe_frame_48k = unsafe_seconds * 48'000U;
        const std::array samples {0.0F, 0.0F};
        return encoder.EncodePcm48k(unsafe_frame_48k, samples);
    });
    CheckInvalidWriteIsTerminal([](FfmpegAacPacketEncoder &encoder) {
        auto samples = StereoFrames(1);
        CHECK(encoder.EncodePcm48k(10, samples).status == VRREC_STATUS_OK);
        return encoder.EncodePcm48k(12, samples);
    });
    CheckInvalidWriteIsTerminal([](FfmpegAacPacketEncoder &encoder) {
        auto samples = StereoFrames(2);
        CHECK(encoder.EncodePcm48k(10, samples).status == VRREC_STATUS_OK);
        return encoder.EncodePcm48k(11, samples);
    });
}

void FinishAndAbortAreTerminalAndIdempotent()
{
    auto finished = CreateForTest();
    CHECK(finished.status == VRREC_STATUS_OK);
    auto first_finish = finished.encoder->Finish();
    CHECK(first_finish.status == VRREC_STATUS_OK);
    CHECK(first_finish.packets.empty());
    CHECK(finished.encoder->Finish().status == VRREC_STATUS_INVALID_STATE);
    auto samples = StereoFrames(1);
    CHECK(
        finished.encoder->EncodePcm48k(0, samples).status ==
        VRREC_STATUS_INVALID_STATE);
    finished.encoder->Abort();
    finished.encoder->Abort();

    auto aborted = CreateForTest();
    CHECK(aborted.status == VRREC_STATUS_OK);
    aborted.encoder->Abort();
    aborted.encoder->Abort();
    CHECK(aborted.encoder->Finish().status == VRREC_STATUS_INVALID_STATE);
    CHECK(
        aborted.encoder->EncodePcm48k(0, samples).status ==
        VRREC_STATUS_INVALID_STATE);
}

void RejectsEveryNonExactConfiguration()
{
    const auto reject = [](const AacAudioEncoderConfig &config) {
        auto creation = FfmpegAacPacketEncoder::Create(config);
        CHECK(creation.status == VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(creation.encoder == nullptr);
        CHECK(!creation.descriptor.has_value());
    };

    auto config = ExactConfig();
    config.profile = static_cast<AacProfile>(99);
    reject(config);
    config = ExactConfig();
    config.sample_rate = 44'100;
    reject(config);
    config = ExactConfig();
    config.channel_count = 1;
    reject(config);
    config = ExactConfig();
    config.channel_layout = static_cast<AudioChannelLayout>(99);
    reject(config);
    config = ExactConfig();
    config.bitrate_bits_per_second = 128'000;
    reject(config);
    config = ExactConfig();
    config.source_sample_format = static_cast<AudioSampleFormat>(99);
    reject(config);

    auto invalid_occurrence = FfmpegAacPacketEncoder::CreateForTesting(
        ExactConfig(),
        LIBAVCODEC_VERSION_INT,
        LIBAVUTIL_VERSION_INT,
        LIBSWRESAMPLE_VERSION_INT,
        "8.1.2",
        FfmpegAacPacketEncoderFailurePoint::None,
        nullptr,
        0);
    CHECK(invalid_occurrence.status == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(invalid_occurrence.encoder == nullptr);
    CHECK(!invalid_occurrence.descriptor.has_value());
}

void RejectsEveryMismatchedRuntimeIdentity()
{
    const auto reject = [](
                            unsigned int avcodec_version_value,
                            unsigned int avutil_version_value,
                            unsigned int swresample_version_value,
                            const char *release_version) {
        auto creation = FfmpegAacPacketEncoder::CreateForTesting(
            ExactConfig(),
            avcodec_version_value,
            avutil_version_value,
            swresample_version_value,
            release_version);
        CHECK(creation.status == VRREC_STATUS_BACKEND_UNAVAILABLE);
        CHECK(creation.encoder == nullptr);
        CHECK(!creation.descriptor.has_value());
    };

    reject(
        LIBAVCODEC_VERSION_INT - 1U,
        LIBAVUTIL_VERSION_INT,
        LIBSWRESAMPLE_VERSION_INT,
        "8.1.2");
    reject(
        LIBAVCODEC_VERSION_INT,
        LIBAVUTIL_VERSION_INT - 1U,
        LIBSWRESAMPLE_VERSION_INT,
        "8.1.2");
    reject(
        LIBAVCODEC_VERSION_INT,
        LIBAVUTIL_VERSION_INT,
        LIBSWRESAMPLE_VERSION_INT - 1U,
        "8.1.2");
    reject(
        LIBAVCODEC_VERSION_INT,
        LIBAVUTIL_VERSION_INT,
        LIBSWRESAMPLE_VERSION_INT,
        "8.1.1");
    reject(
        LIBAVCODEC_VERSION_INT,
        LIBAVUTIL_VERSION_INT,
        LIBSWRESAMPLE_VERSION_INT,
        nullptr);
}

void MapsEveryInjectedCreateFailure()
{
    struct Case final {
        FfmpegAacPacketEncoderFailurePoint point;
        vrrec_status_t status;
    };
    constexpr std::array cases {
        Case {
            FfmpegAacPacketEncoderFailurePoint::FindEncoder,
            VRREC_STATUS_BACKEND_UNAVAILABLE,
        },
        Case {
            FfmpegAacPacketEncoderFailurePoint::AllocateContext,
            VRREC_STATUS_OUT_OF_MEMORY,
        },
        Case {
            FfmpegAacPacketEncoderFailurePoint::CopyChannelLayout,
            VRREC_STATUS_OUT_OF_MEMORY,
        },
        Case {
            FfmpegAacPacketEncoderFailurePoint::OpenCodecOutOfMemory,
            VRREC_STATUS_OUT_OF_MEMORY,
        },
        Case {
            FfmpegAacPacketEncoderFailurePoint::OpenCodecFailure,
            VRREC_STATUS_INTERNAL_ERROR,
        },
        Case {
            FfmpegAacPacketEncoderFailurePoint::CopyDescriptorOutOfMemory,
            VRREC_STATUS_OUT_OF_MEMORY,
        },
        Case {
            FfmpegAacPacketEncoderFailurePoint::AllocateResampler,
            VRREC_STATUS_OUT_OF_MEMORY,
        },
        Case {
            FfmpegAacPacketEncoderFailurePoint::InitializeResampler,
            VRREC_STATUS_INTERNAL_ERROR,
        },
        Case {
            FfmpegAacPacketEncoderFailurePoint::AllocateFifo,
            VRREC_STATUS_OUT_OF_MEMORY,
        },
        Case {
            FfmpegAacPacketEncoderFailurePoint::AllocateFrame,
            VRREC_STATUS_OUT_OF_MEMORY,
        },
        Case {
            FfmpegAacPacketEncoderFailurePoint::CopyFrameChannelLayout,
            VRREC_STATUS_OUT_OF_MEMORY,
        },
        Case {
            FfmpegAacPacketEncoderFailurePoint::AllocateFrameBuffer,
            VRREC_STATUS_OUT_OF_MEMORY,
        },
        Case {
            FfmpegAacPacketEncoderFailurePoint::AllocateImplementation,
            VRREC_STATUS_OUT_OF_MEMORY,
        },
        Case {
            FfmpegAacPacketEncoderFailurePoint::AllocateEncoder,
            VRREC_STATUS_OUT_OF_MEMORY,
        },
    };

    for (const auto &test_case : cases) {
        auto creation = CreateForTest(test_case.point);
        CHECK(creation.status == test_case.status);
        CHECK(creation.encoder == nullptr);
        CHECK(!creation.descriptor.has_value());
    }
}

void MapsARealLibavAllocationFailure()
{
    av_max_alloc(1);
    auto creation = FfmpegAacPacketEncoder::Create(ExactConfig());
    av_max_alloc(static_cast<std::size_t>(
        std::numeric_limits<int>::max()));
    CHECK(creation.status == VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(creation.encoder == nullptr);
    CHECK(!creation.descriptor.has_value());
}

void OperationalFailuresPublishNoPacketsAndAreTerminal()
{
    const auto check_encode_failure = [](
                                          FfmpegAacPacketEncoderFailurePoint
                                              point,
                                          std::size_t frame_count,
                                          vrrec_status_t expected_status) {
        auto creation = CreateForTest(point);
        CHECK(creation.status == VRREC_STATUS_OK);
        auto samples = StereoFrames(frame_count);
        auto failure = creation.encoder->EncodePcm48k(0, samples);
        CHECK(failure.status == expected_status);
        CHECK(failure.packets.empty());
        CHECK(creation.encoder->Finish().status == VRREC_STATUS_INVALID_STATE);
    };

    check_encode_failure(
        FfmpegAacPacketEncoderFailurePoint::AllocateConvertedFrame,
        1,
        VRREC_STATUS_OUT_OF_MEMORY);
    check_encode_failure(
        FfmpegAacPacketEncoderFailurePoint::CopyConvertedChannelLayout,
        1,
        VRREC_STATUS_OUT_OF_MEMORY);
    check_encode_failure(
        FfmpegAacPacketEncoderFailurePoint::AllocateConvertedFrameBuffer,
        1,
        VRREC_STATUS_OUT_OF_MEMORY);
    check_encode_failure(
        FfmpegAacPacketEncoderFailurePoint::QueryResamplerOutput,
        1,
        VRREC_STATUS_INTERNAL_ERROR);
    check_encode_failure(
        FfmpegAacPacketEncoderFailurePoint::ConvertSamples,
        1,
        VRREC_STATUS_INTERNAL_ERROR);
    check_encode_failure(
        FfmpegAacPacketEncoderFailurePoint::WriteFifo,
        1,
        VRREC_STATUS_INTERNAL_ERROR);
    check_encode_failure(
        FfmpegAacPacketEncoderFailurePoint::MakeFrameWritable,
        1'024,
        VRREC_STATUS_OUT_OF_MEMORY);
    check_encode_failure(
        FfmpegAacPacketEncoderFailurePoint::ReadFifo,
        1'024,
        VRREC_STATUS_INTERNAL_ERROR);
    check_encode_failure(
        FfmpegAacPacketEncoderFailurePoint::PrepareFrameOutOfMemory,
        1'024,
        VRREC_STATUS_OUT_OF_MEMORY);
}

void AFailureAfterAnEarlierPacketPublishesNoPartialBatch()
{
    auto creation = CreateForTest(
        FfmpegAacPacketEncoderFailurePoint::AppendPacketsOutOfMemory,
        nullptr,
        2);
    CHECK(creation.status == VRREC_STATUS_OK);
    auto samples = StereoFrames(4'096);
    auto failure = creation.encoder->EncodePcm48k(0, samples);
    CHECK(failure.status == VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(failure.packets.empty());
    CHECK(creation.encoder->Finish().status == VRREC_STATUS_INVALID_STATE);

    auto frame_failure = CreateForTest(
        FfmpegAacPacketEncoderFailurePoint::MakeFrameWritable,
        nullptr,
        3);
    CHECK(frame_failure.status == VRREC_STATUS_OK);
    failure = frame_failure.encoder->EncodePcm48k(0, samples);
    CHECK(failure.status == VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(failure.packets.empty());
    CHECK(
        frame_failure.encoder->Finish().status ==
        VRREC_STATUS_INVALID_STATE);

    auto finish_failure = CreateForTest(
        FfmpegAacPacketEncoderFailurePoint::AppendPacketsOutOfMemory);
    CHECK(finish_failure.status == VRREC_STATUS_OK);
    auto one_frame = StereoFrames(1);
    auto write = finish_failure.encoder->EncodePcm48k(0, one_frame);
    CHECK(write.status == VRREC_STATUS_OK);
    CHECK(write.packets.empty());
    failure = finish_failure.encoder->Finish();
    CHECK(failure.status == VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(failure.packets.empty());
    CHECK(
        finish_failure.encoder->Finish().status ==
        VRREC_STATUS_INVALID_STATE);

    auto drain_failure = CreateForTest(
        FfmpegAacPacketEncoderFailurePoint::DrainCodecFailure);
    CHECK(drain_failure.status == VRREC_STATUS_OK);
    auto tail = StereoFrames(1'025);
    write = drain_failure.encoder->EncodePcm48k(0, tail);
    CHECK(write.status == VRREC_STATUS_OK);
    CHECK(write.packets.empty());
    failure = drain_failure.encoder->Finish();
    CHECK(failure.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(failure.packets.empty());
    CHECK(
        drain_failure.encoder->Finish().status ==
        VRREC_STATUS_INVALID_STATE);
}

void ResamplerFlushFailureIsObservableAndTerminal()
{
    auto creation = CreateForTest(
        FfmpegAacPacketEncoderFailurePoint::FlushResampler);
    CHECK(creation.status == VRREC_STATUS_OK);
    auto samples = StereoFrames(1);
    auto write = creation.encoder->EncodePcm48k(0, samples);
    CHECK(write.status == VRREC_STATUS_OK);
    CHECK(write.packets.empty());
    auto failure = creation.encoder->Finish();
    CHECK(failure.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(failure.packets.empty());
    CHECK(creation.encoder->Finish().status == VRREC_STATUS_INVALID_STATE);

    auto query_failure = CreateForTest(
        FfmpegAacPacketEncoderFailurePoint::QueryResamplerOutput,
        nullptr,
        2);
    CHECK(query_failure.status == VRREC_STATUS_OK);
    write = query_failure.encoder->EncodePcm48k(0, samples);
    CHECK(write.status == VRREC_STATUS_OK);
    CHECK(write.packets.empty());
    failure = query_failure.encoder->Finish();
    CHECK(failure.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(failure.packets.empty());
}

class BlockingPreparedFrameObserver final
    : public FfmpegAacPreparedFrameObserver {
public:
    void Observe(
        std::int64_t,
        std::span<const float>,
        std::span<const float>) noexcept override
    {
        std::unique_lock lock(mutex_);
        entered_ = true;
        entered_condition_.notify_one();
        release_condition_.wait(lock, [this] { return released_; });
    }

    bool WaitUntilEntered()
    {
        std::unique_lock lock(mutex_);
        return entered_condition_.wait_for(
            lock,
            std::chrono::seconds(5),
            [this] { return entered_; });
    }

    void Release()
    {
        const std::lock_guard lock(mutex_);
        released_ = true;
        release_condition_.notify_one();
    }

private:
    std::mutex mutex_;
    std::condition_variable entered_condition_;
    std::condition_variable release_condition_;
    bool entered_ = false;
    bool released_ = false;
};

class ContentionObserver final : public FfmpegAacSerializationObserver {
public:
    void ObserveContention(
        FfmpegAacPacketEncoderOperation operation) noexcept override
    {
        const std::lock_guard lock(mutex_);
        observed_[static_cast<std::size_t>(operation)] = true;
        condition_.notify_one();
    }

    bool WaitUntilObserved(FfmpegAacPacketEncoderOperation operation)
    {
        std::unique_lock lock(mutex_);
        return condition_.wait_for(
            lock,
            std::chrono::seconds(5),
            [&] {
                return observed_[static_cast<std::size_t>(operation)];
            });
    }

private:
    std::mutex mutex_;
    std::condition_variable condition_;
    std::array<bool, 3> observed_ {};
};

void SerializesEncodeFinishAndAbortThroughOneMutex()
{
    {
        BlockingPreparedFrameObserver observer;
        ContentionObserver contention;
        auto creation = CreateForTest(
            FfmpegAacPacketEncoderFailurePoint::None,
            &observer,
            1,
            &contention);
        CHECK(creation.status == VRREC_STATUS_OK);
        auto samples = StereoFrames(1'024);
        PacketAudioEncoderWrite encode_result {
            VRREC_STATUS_INTERNAL_ERROR,
            {},
        };
        std::thread encode_thread([&] {
            encode_result = creation.encoder->EncodePcm48k(0, samples);
        });
        CHECK(observer.WaitUntilEntered());

        std::thread abort_thread([&] {
            creation.encoder->Abort();
        });
        CHECK(contention.WaitUntilObserved(
            FfmpegAacPacketEncoderOperation::Abort));
        observer.Release();
        encode_thread.join();
        abort_thread.join();
        CHECK(encode_result.status == VRREC_STATUS_OK);
        CHECK(
            creation.encoder->Finish().status ==
            VRREC_STATUS_INVALID_STATE);
    }

    {
        BlockingPreparedFrameObserver observer;
        ContentionObserver contention;
        auto creation = CreateForTest(
            FfmpegAacPacketEncoderFailurePoint::None,
            &observer,
            1,
            &contention);
        CHECK(creation.status == VRREC_STATUS_OK);
        auto samples = StereoFrames(1);
        CHECK(
            creation.encoder->EncodePcm48k(0, samples).status ==
            VRREC_STATUS_OK);
        PacketAudioEncoderWrite finish_result {
            VRREC_STATUS_INTERNAL_ERROR,
            {},
        };
        std::thread finish_thread([&] {
            finish_result = creation.encoder->Finish();
        });
        CHECK(observer.WaitUntilEntered());

        PacketAudioEncoderWrite write_result {
            VRREC_STATUS_INTERNAL_ERROR,
            {},
        };
        std::thread write_thread([&] {
            write_result = creation.encoder->EncodePcm48k(1, samples);
        });
        CHECK(contention.WaitUntilObserved(
            FfmpegAacPacketEncoderOperation::Encode));
        observer.Release();
        finish_thread.join();
        write_thread.join();
        CHECK(finish_result.status == VRREC_STATUS_OK);
        CHECK(write_result.status == VRREC_STATUS_INVALID_STATE);
        CHECK(write_result.packets.empty());
    }

    {
        BlockingPreparedFrameObserver observer;
        ContentionObserver contention;
        auto creation = CreateForTest(
            FfmpegAacPacketEncoderFailurePoint::None,
            &observer,
            1,
            &contention);
        CHECK(creation.status == VRREC_STATUS_OK);
        auto samples = StereoFrames(1);
        CHECK(
            creation.encoder->EncodePcm48k(0, samples).status ==
            VRREC_STATUS_OK);
        PacketAudioEncoderWrite finish_result {
            VRREC_STATUS_INTERNAL_ERROR,
            {},
        };
        std::thread finish_thread([&] {
            finish_result = creation.encoder->Finish();
        });
        CHECK(observer.WaitUntilEntered());

        std::thread abort_thread([&] {
            creation.encoder->Abort();
        });
        CHECK(contention.WaitUntilObserved(
            FfmpegAacPacketEncoderOperation::Abort));
        observer.Release();
        finish_thread.join();
        abort_thread.join();
        CHECK(finish_result.status == VRREC_STATUS_OK);
        CHECK(
            creation.encoder->Finish().status ==
            VRREC_STATUS_INVALID_STATE);
    }
}

}

int main()
{
    av_log_set_level(AV_LOG_QUIET);
    CreatesTheExactPinnedDescriptorAndOwnsItsAsc();
    EncodesEverySmallLastFrameBoundaryExactly();
    PreservesPackedChannelsAcrossArbitraryChunkBoundaries();
    ReusesWritableFramesForTwoFullAacFrames();
    AllowsANonzeroFirstFrameAndPreservesItsPts();
    RejectsMalformedSamplesAndTimelineDiscontinuitiesTerminally();
    FinishAndAbortAreTerminalAndIdempotent();
    RejectsEveryNonExactConfiguration();
    RejectsEveryMismatchedRuntimeIdentity();
    MapsEveryInjectedCreateFailure();
    MapsARealLibavAllocationFailure();
    OperationalFailuresPublishNoPacketsAndAreTerminal();
    AFailureAfterAnEarlierPacketPublishesNoPartialBatch();
    ResamplerFlushFailureIsObservableAndTerminal();
    SerializesEncodeFinishAndAbortThroughOneMutex();
    return 0;
}
