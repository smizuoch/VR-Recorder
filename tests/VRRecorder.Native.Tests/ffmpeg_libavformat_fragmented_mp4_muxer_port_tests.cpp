#include "ffmpeg_libavformat_fragmented_mp4_muxer_port.hpp"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iterator>
#include <limits>
#include <string>
#include <vector>

extern "C" {
#include <libavcodec/version.h>
#include <libavformat/avformat.h>
#include <libavformat/version.h>
#include <libavutil/avutil.h>
#include <libavutil/mem.h>
#include <libavutil/version.h>
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

class ScratchOutput final {
public:
    explicit ScratchOutput(const char *label)
    {
        static std::atomic<unsigned long long> sequence {0};
        const auto nonce = static_cast<unsigned long long>(
            std::chrono::steady_clock::now().time_since_epoch().count());
        path_ = std::filesystem::temp_directory_path() /
            (std::string("vrrecorder-") + label + '-' +
                std::to_string(nonce) + '-' +
                std::to_string(sequence.fetch_add(1)) + ".mp4");
        std::error_code error;
        std::filesystem::remove(path_, error);
    }

    ~ScratchOutput()
    {
        std::error_code error;
        std::filesystem::remove(path_, error);
    }

    const std::string String() const
    {
        return path_.string();
    }

    const std::filesystem::path &Path() const noexcept
    {
        return path_;
    }

private:
    std::filesystem::path path_;
};

class PendingPublicationOutput final {
public:
    PendingPublicationOutput()
    {
        static std::atomic<unsigned long long> sequence {0};
        const auto nonce = static_cast<unsigned long long>(
            std::chrono::steady_clock::now().time_since_epoch().count());
        const auto base = std::string("vrrecorder-publication-") +
            std::to_string(nonce) + '-' +
            std::to_string(sequence.fetch_add(1));
        pending_path_ = std::filesystem::temp_directory_path() /
            (base + ".recording.mp4");
        final_path_ = std::filesystem::temp_directory_path() /
            (base + ".mp4");
        std::error_code error;
        std::filesystem::remove(pending_path_, error);
        error.clear();
        std::filesystem::remove(final_path_, error);
    }

    ~PendingPublicationOutput()
    {
        std::error_code error;
        std::filesystem::remove(pending_path_, error);
        error.clear();
        std::filesystem::remove(final_path_, error);
    }

    const std::filesystem::path &PendingPath() const noexcept
    {
        return pending_path_;
    }

    const std::filesystem::path &FinalPath() const noexcept
    {
        return final_path_;
    }

private:
    std::filesystem::path pending_path_;
    std::filesystem::path final_path_;
};

FragmentedMp4StreamConfiguration Streams()
{
    // Coherent 16x16 High-profile fixture generated as a two-frame closed-GOP
    // H.264 stream. Its IDR and P samples below were host-decoded together.
    const std::array<std::uint8_t, 44> avcc {
        0x01, 0x64, 0x00, 0x0a, 0xff, 0xe1, 0x00, 0x17,
        0x67, 0x64, 0x00, 0x0a, 0xac, 0xb2, 0x3d, 0x80,
        0x88, 0x00, 0x00, 0x03, 0x00, 0x08, 0x00, 0x00,
        0x03, 0x01, 0xe0, 0x78, 0x91, 0x32, 0x40, 0x01,
        0x00, 0x06, 0x68, 0xeb, 0xc3, 0xcb, 0x22, 0xc0,
        0xfd, 0xf8, 0xf8, 0x00,
    };
    std::vector<std::byte> video_extradata(avcc.size());
    std::transform(
        avcc.begin(),
        avcc.end(),
        video_extradata.begin(),
        [](std::uint8_t value) { return static_cast<std::byte>(value); });

    return {
        {
            MicrosecondPacketTimeBase,
            16,
            16,
            H264Profile::High,
            H264PacketFormat::AvccLengthPrefixed,
            std::move(video_extradata),
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
            {
                std::byte {0x11}, std::byte {0x90}, std::byte {0x56},
                std::byte {0xe5}, std::byte {0x00},
            },
        },
        DefaultFragmentedMp4FragmentPolicy,
    };
}

EncodedMediaPacket VideoPacket(
    std::int64_t timestamp_microseconds,
    bool key_frame = true)
{
    return {
        MediaStreamKind::Video,
        timestamp_microseconds,
        timestamp_microseconds,
        33'333,
        key_frame,
        key_frame
            ? std::vector<std::byte> {
                std::byte {0x00}, std::byte {0x00}, std::byte {0x00},
                std::byte {0x0e}, std::byte {0x65}, std::byte {0x88},
                std::byte {0x84}, std::byte {0x0a}, std::byte {0xff},
                std::byte {0xfe}, std::byte {0xf6}, std::byte {0x73},
                std::byte {0x7c}, std::byte {0x0a}, std::byte {0x6b},
                std::byte {0x6d}, std::byte {0xb1}, std::byte {0x81},
            }
            : std::vector<std::byte> {
                std::byte {0x00}, std::byte {0x00}, std::byte {0x00},
                std::byte {0x07}, std::byte {0x41}, std::byte {0x9a},
                std::byte {0x3b}, std::byte {0x10}, std::byte {0x9f},
                std::byte {0xfe}, std::byte {0xc0},
            },
    };
}

EncodedMediaPacket AudioPacket(std::int64_t timestamp_microseconds)
{
    return {
        MediaStreamKind::Audio,
        timestamp_microseconds,
        timestamp_microseconds,
        21'333,
        false,
        timestamp_microseconds < 0
            ? std::vector<std::byte> {
                std::byte {0xde}, std::byte {0x02}, std::byte {0x00},
                std::byte {0x4c}, std::byte {0x61}, std::byte {0x76},
                std::byte {0x63}, std::byte {0x36}, std::byte {0x32},
                std::byte {0x2e}, std::byte {0x32}, std::byte {0x38},
                std::byte {0x2e}, std::byte {0x31}, std::byte {0x30},
                std::byte {0x32}, std::byte {0x00}, std::byte {0x42},
                std::byte {0x20}, std::byte {0x08}, std::byte {0xc1},
                std::byte {0x18}, std::byte {0x38},
            }
            : std::vector<std::byte> {
                std::byte {0x21}, std::byte {0x10},
                std::byte {0x04}, std::byte {0x60},
                std::byte {0x8c}, std::byte {0x1c},
            },
    };
}

std::vector<std::byte> ReadAll(const std::filesystem::path &path)
{
    std::ifstream input(path, std::ios::binary);
    CHECK(input.good());
    const std::vector<char> characters {
        std::istreambuf_iterator<char>(input),
        std::istreambuf_iterator<char>(),
    };
    std::vector<std::byte> bytes(characters.size());
    std::transform(
        characters.begin(),
        characters.end(),
        bytes.begin(),
        [](char value) {
            return static_cast<std::byte>(
                static_cast<unsigned char>(value));
        });
    return bytes;
}

std::size_t FindAscii(
    const std::vector<std::byte> &bytes,
    const char (&text)[5])
{
    const std::array<std::byte, 4> needle {
        static_cast<std::byte>(text[0]),
        static_cast<std::byte>(text[1]),
        static_cast<std::byte>(text[2]),
        static_cast<std::byte>(text[3]),
    };
    const auto found = std::search(
        bytes.begin(), bytes.end(), needle.begin(), needle.end());
    return found == bytes.end()
        ? std::numeric_limits<std::size_t>::max()
        : static_cast<std::size_t>(found - bytes.begin());
}

std::uint32_t ReadBigEndian32(
    const std::vector<std::byte> &bytes,
    std::size_t offset)
{
    CHECK(offset <= bytes.size());
    CHECK(bytes.size() - offset >= 4);
    return
        (std::to_integer<std::uint32_t>(bytes[offset]) << 24U) |
        (std::to_integer<std::uint32_t>(bytes[offset + 1]) << 16U) |
        (std::to_integer<std::uint32_t>(bytes[offset + 2]) << 8U) |
        std::to_integer<std::uint32_t>(bytes[offset + 3]);
}

std::uint64_t ReadBigEndian64(
    const std::vector<std::byte> &bytes,
    std::size_t offset)
{
    return
        (static_cast<std::uint64_t>(ReadBigEndian32(bytes, offset)) << 32U) |
        ReadBigEndian32(bytes, offset + 4);
}

struct BoxView final {
    std::size_t offset;
    std::size_t size;
    std::size_t header_size;
};

bool ReadBox(
    const std::vector<std::byte> &bytes,
    std::size_t offset,
    std::size_t limit,
    BoxView &box)
{
    if (limit > bytes.size() || offset > limit || limit - offset < 8) {
        return false;
    }
    const auto short_size = ReadBigEndian32(bytes, offset);
    std::uint64_t size = short_size;
    std::size_t header_size = 8;
    if (short_size == 1) {
        if (limit - offset < 16) {
            return false;
        }
        size = ReadBigEndian64(bytes, offset + 8);
        header_size = 16;
    } else if (short_size == 0) {
        size = limit - offset;
    }
    if (size < header_size || size > limit - offset) {
        return false;
    }
    box = {
        offset,
        static_cast<std::size_t>(size),
        header_size,
    };
    return true;
}

bool BoxTypeEquals(
    const std::vector<std::byte> &bytes,
    const BoxView &box,
    const char (&type)[5])
{
    const auto type_offset = box.offset + 4;
    return bytes[type_offset] == static_cast<std::byte>(type[0]) &&
        bytes[type_offset + 1] == static_cast<std::byte>(type[1]) &&
        bytes[type_offset + 2] == static_cast<std::byte>(type[2]) &&
        bytes[type_offset + 3] == static_cast<std::byte>(type[3]);
}

std::size_t CountTopLevelBox(
    const std::vector<std::byte> &bytes,
    const char (&type)[5])
{
    std::size_t count = 0;
    std::size_t cursor = 0;
    while (cursor < bytes.size()) {
        BoxView box {};
        CHECK(ReadBox(bytes, cursor, bytes.size(), box));
        if (BoxTypeEquals(bytes, box, type)) {
            ++count;
        }
        cursor += box.size;
    }
    return count;
}

bool FindTopLevelBox(
    const std::vector<std::byte> &bytes,
    const char (&type)[5],
    BoxView &found)
{
    std::size_t cursor = 0;
    while (cursor < bytes.size()) {
        BoxView box {};
        if (!ReadBox(bytes, cursor, bytes.size(), box)) {
            return false;
        }
        if (BoxTypeEquals(bytes, box, type)) {
            found = box;
            return true;
        }
        cursor += box.size;
    }
    return false;
}

bool FindChildBox(
    const std::vector<std::byte> &bytes,
    const BoxView &parent,
    const char (&type)[5],
    BoxView &found)
{
    auto cursor = parent.offset + parent.header_size;
    const auto limit = parent.offset + parent.size;
    while (cursor < limit) {
        BoxView box {};
        if (!ReadBox(bytes, cursor, limit, box)) {
            return false;
        }
        if (BoxTypeEquals(bytes, box, type)) {
            found = box;
            return true;
        }
        cursor += box.size;
    }
    return false;
}

bool HasElstMediaTime(
    const std::vector<std::byte> &bytes,
    std::int64_t expected_media_time)
{
    const std::array<std::byte, 4> needle {
        std::byte {'e'}, std::byte {'l'}, std::byte {'s'}, std::byte {'t'},
    };
    auto search_start = bytes.begin();
    while (search_start != bytes.end()) {
        const auto found = std::search(
            search_start,
            bytes.end(),
            needle.begin(),
            needle.end());
        if (found == bytes.end()) {
            return false;
        }
        const auto type_offset =
            static_cast<std::size_t>(found - bytes.begin());
        if (type_offset >= 4 && bytes.size() - type_offset >= 12) {
            const auto box_start = type_offset - 4;
            const auto box_size = ReadBigEndian32(bytes, box_start);
            const auto version =
                std::to_integer<unsigned int>(bytes[type_offset + 4]);
            const auto entry_count =
                ReadBigEndian32(bytes, type_offset + 8);
            const auto entry_size = version == 1U ? 20U : 12U;
            const auto entries_offset = type_offset + 12;
            if ((version == 0U || version == 1U) &&
                box_size >= 16 &&
                box_size <= bytes.size() - box_start &&
                entry_count <=
                    (box_size - 16U) / entry_size) {
                for (std::uint32_t index = 0;
                     index < entry_count;
                     ++index) {
                    const auto entry_offset = entries_offset +
                        static_cast<std::size_t>(index) * entry_size;
                    const auto raw_media_time = version == 1U
                        ? ReadBigEndian64(bytes, entry_offset + 8)
                        : ReadBigEndian32(bytes, entry_offset + 4);
                    const auto media_time = version == 1U
                        ? static_cast<std::int64_t>(raw_media_time)
                        : static_cast<std::int64_t>(
                            static_cast<std::int32_t>(raw_media_time));
                    if (media_time == expected_media_time) {
                        return true;
                    }
                }
            }
        }
        search_start = found +
            static_cast<std::ptrdiff_t>(needle.size());
    }
    return false;
}

void RejectsEveryRuntimeIdentityMismatchBeforeOpeningTheOutput()
{
    struct Identity final {
        unsigned int avformat;
        unsigned int avcodec;
        unsigned int avutil;
        const char *release;
    };
    const std::array<Identity, 5> mismatches {{
        {
            LIBAVFORMAT_VERSION_INT ^ 1U,
            LIBAVCODEC_VERSION_INT,
            LIBAVUTIL_VERSION_INT,
            "8.1.2",
        },
        {
            LIBAVFORMAT_VERSION_INT,
            LIBAVCODEC_VERSION_INT ^ 1U,
            LIBAVUTIL_VERSION_INT,
            "8.1.2",
        },
        {
            LIBAVFORMAT_VERSION_INT,
            LIBAVCODEC_VERSION_INT,
            LIBAVUTIL_VERSION_INT ^ 1U,
            "8.1.2",
        },
        {
            LIBAVFORMAT_VERSION_INT,
            LIBAVCODEC_VERSION_INT,
            LIBAVUTIL_VERSION_INT,
            "8.1.1",
        },
        {
            LIBAVFORMAT_VERSION_INT,
            LIBAVCODEC_VERSION_INT,
            LIBAVUTIL_VERSION_INT,
            nullptr,
        },
    }};

    for (const auto &identity : mismatches) {
        ScratchOutput output("runtime-mismatch");
        const auto path = output.String();
        auto result =
            LibavformatFragmentedMp4MuxerPort::CreateForTesting(
                path.c_str(),
                identity.avformat,
                identity.avcodec,
                identity.avutil,
                identity.release);
        CHECK(result.status == VRREC_STATUS_BACKEND_UNAVAILABLE);
        CHECK(result.port == nullptr);
        CHECK(!std::filesystem::exists(output.Path()));
    }
}

void RejectsInvalidPathsAndMapsRealAllocationFailure()
{
    CHECK(LibavformatFragmentedMp4MuxerPort::Create(nullptr).status ==
        VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(LibavformatFragmentedMp4MuxerPort::Create("").status ==
        VRREC_STATUS_INVALID_ARGUMENT);

    ScratchOutput missing_parent("missing-parent");
    const auto invalid = missing_parent.Path().parent_path() /
        "vrrecorder-directory-that-must-not-exist" /
        "output.mp4";
    std::error_code error;
    std::filesystem::remove_all(invalid.parent_path(), error);
    auto open_failure =
        LibavformatFragmentedMp4MuxerPort::Create(
            invalid.string().c_str());
    CHECK(open_failure.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(open_failure.port == nullptr);

    ScratchOutput output("create-oom");
    const auto path = output.String();
    av_max_alloc(1);
    auto out_of_memory =
        LibavformatFragmentedMp4MuxerPort::Create(path.c_str());
    av_max_alloc(static_cast<std::size_t>(
        std::numeric_limits<int>::max()));
    CHECK(out_of_memory.status == VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(out_of_memory.port == nullptr);
}

void CreatesH264AndAacStreamsAndReadsBackPostHeaderTimeBases()
{
    ScratchOutput output("stream-contract");
    const auto path = output.String();
    auto result = LibavformatFragmentedMp4MuxerPort::Create(path.c_str());
    CHECK(result.status == VRREC_STATUS_OK);
    CHECK(result.port != nullptr);

    CHECK(result.port->WriteHeader(Streams()) == VRREC_STATUS_OK);
    MediaTimeBase video {};
    MediaTimeBase audio {};
    CHECK(result.port->GetActualStreamTimeBase(
              MediaStreamKind::Video, video) == VRREC_STATUS_OK);
    CHECK(result.port->GetActualStreamTimeBase(
              MediaStreamKind::Audio, audio) == VRREC_STATUS_OK);
    CHECK(video == (MediaTimeBase {1, 1'000'000}));
    CHECK(audio == (MediaTimeBase {1, 48'000}));
    CHECK(result.port->WriteTrailer() == VRREC_STATUS_OK);
    CHECK(result.port->FlushFile() == VRREC_STATUS_OK);

    const auto bytes = ReadAll(output.Path());
    BoxView ftyp {};
    BoxView moov {};
    CHECK(FindTopLevelBox(bytes, "ftyp", ftyp));
    CHECK(ftyp.offset == 0);
    CHECK(FindAscii(bytes, "iso5") == 8);
    CHECK(FindTopLevelBox(bytes, "moov", moov));
    CHECK(FindAscii(bytes, "avc1") !=
        std::numeric_limits<std::size_t>::max());
    CHECK(FindAscii(bytes, "avcC") !=
        std::numeric_limits<std::size_t>::max());
    CHECK(FindAscii(bytes, "mp4a") !=
        std::numeric_limits<std::size_t>::max());
    CHECK(FindAscii(bytes, "esds") !=
        std::numeric_limits<std::size_t>::max());
}

void UsesLibavutilPacketRescalingIncludingNegativeAacPriming()
{
    ScratchOutput output("rescale");
    const auto path = output.String();
    auto result = LibavformatFragmentedMp4MuxerPort::Create(path.c_str());
    CHECK(result.status == VRREC_STATUS_OK);

    FfmpegPacketTimestamps destination {};
    result.port->RescalePacketTimestamps(
        {-21'333, -21'333, 21'333},
        MicrosecondPacketTimeBase,
        {1, 48'000},
        destination);
    CHECK(destination == (FfmpegPacketTimestamps {-1'024, -1'024, 1'024}));

    result.port->RescalePacketTimestamps(
        {UnknownMediaTimestamp, UnknownMediaTimestamp, 1},
        MicrosecondPacketTimeBase,
        {1, 90'000},
        destination);
    CHECK(destination.pts == UnknownMediaTimestamp);
    CHECK(destination.dts == UnknownMediaTimestamp);

    result.port->RescalePacketTimestamps(
        {0, 0, 1},
        {0, 1},
        {1, 90'000},
        destination);
    CHECK(destination == (FfmpegPacketTimestamps {
        UnknownMediaTimestamp, UnknownMediaTimestamp, 0}));
}

void ClonesCanonicalPacketsIntoAnActualFragmentedMp4()
{
    ScratchOutput output("actual-fragment");
    const auto path = output.String();
    LibavformatMuxerOperationCounts operation_counts;
    auto result =
        LibavformatFragmentedMp4MuxerPort::CreateForTesting(
            path.c_str(),
            LIBAVFORMAT_VERSION_INT,
            LIBAVCODEC_VERSION_INT,
            LIBAVUTIL_VERSION_INT,
            "8.1.2",
            LibavformatMuxerFailurePoint::None,
            &operation_counts);
    CHECK(result.status == VRREC_STATUS_OK);
    FfmpegFragmentedMp4Muxer muxer(*result.port);
    CHECK(muxer.WriteHeader(Streams()) == VRREC_STATUS_OK);

    auto video = VideoPacket(0);
    auto audio = AudioPacket(-21'333);
    const auto video_before = video;
    const auto audio_before = audio;

    CHECK(muxer.WritePacket(video) == VRREC_STATUS_OK);
    CHECK(muxer.WritePacket(audio) == VRREC_STATUS_OK);
    CHECK(operation_counts.write_packet_calls == 2);
    CHECK(video.payload == video_before.payload);
    CHECK(audio.payload == audio_before.payload);
    CHECK(audio.side_data == audio_before.side_data);
    CHECK(muxer.WriteTrailer() == VRREC_STATUS_OK);
    CHECK(muxer.FlushFile() == VRREC_STATUS_OK);

    const auto bytes = ReadAll(output.Path());
    BoxView ftyp {};
    BoxView moov {};
    BoxView moof {};
    BoxView mdat {};
    BoxView mfra {};
    BoxView mvex {};
    BoxView trex {};
    CHECK(FindTopLevelBox(bytes, "ftyp", ftyp));
    CHECK(FindTopLevelBox(bytes, "moov", moov));
    CHECK(FindTopLevelBox(bytes, "moof", moof));
    CHECK(FindTopLevelBox(bytes, "mdat", mdat));
    CHECK(FindTopLevelBox(bytes, "mfra", mfra));
    CHECK(ftyp.offset < moov.offset);
    CHECK(moov.offset < moof.offset);
    CHECK(moof.offset < mdat.offset);
    CHECK(mdat.offset < mfra.offset);
    CHECK(FindChildBox(bytes, moov, "mvex", mvex));
    CHECK(FindChildBox(bytes, mvex, "trex", trex));
    CHECK(CountTopLevelBox(bytes, "moof") == 1);
    CHECK(FindAscii(bytes, "edts") !=
        std::numeric_limits<std::size_t>::max());
    CHECK(FindAscii(bytes, "elst") !=
        std::numeric_limits<std::size_t>::max());
    CHECK(HasElstMediaTime(bytes, 1'024));
}

void EnforcesMinimumAndMaximumFragmentDurationWithRealMovflags()
{
    ScratchOutput output("fragment-policy");
    const auto path = output.String();
    auto result = LibavformatFragmentedMp4MuxerPort::Create(path.c_str());
    CHECK(result.status == VRREC_STATUS_OK);
    FfmpegFragmentedMp4Muxer muxer(*result.port);
    CHECK(muxer.WriteHeader(Streams()) == VRREC_STATUS_OK);

    CHECK(muxer.WritePacket(VideoPacket(0, true)) == VRREC_STATUS_OK);
    CHECK(muxer.WritePacket(AudioPacket(0)) == VRREC_STATUS_OK);
    CHECK(muxer.WritePacket(VideoPacket(500'000, true)) ==
        VRREC_STATUS_OK);
    CHECK(muxer.WritePacket(AudioPacket(500'000)) == VRREC_STATUS_OK);
    CHECK(muxer.WritePacket(VideoPacket(1'100'000, true)) ==
        VRREC_STATUS_OK);
    CHECK(muxer.WritePacket(AudioPacket(1'100'000)) == VRREC_STATUS_OK);
    CHECK(muxer.WritePacket(VideoPacket(3'200'000, false)) ==
        VRREC_STATUS_OK);
    CHECK(muxer.WritePacket(AudioPacket(3'200'000)) == VRREC_STATUS_OK);
    CHECK(muxer.WriteTrailer() == VRREC_STATUS_OK);
    CHECK(muxer.FlushFile() == VRREC_STATUS_OK);

    const auto bytes = ReadAll(output.Path());
    // The 0.5 s keyframe is suppressed by min_frag_duration; the 1.1 s
    // keyframe cuts one fragment and frag_duration cuts the next at > 2 s.
    CHECK(CountTopLevelBox(bytes, "moof") == 3);
}

void RejectsInvalidConfigurationAndRealPacketAllocationFailure()
{
    std::vector<FragmentedMp4StreamConfiguration> invalid_configurations;

    auto wrong_avcc_version = Streams();
    wrong_avcc_version.video.codec_extradata[0] = std::byte {0x02};
    invalid_configurations.push_back(wrong_avcc_version);

    auto truncated_avcc = Streams();
    truncated_avcc.video.codec_extradata.pop_back();
    invalid_configurations.push_back(truncated_avcc);

    auto wrong_length_size = Streams();
    wrong_length_size.video.codec_extradata[4] = std::byte {0xfe};
    invalid_configurations.push_back(wrong_length_size);

    auto no_sps = Streams();
    no_sps.video.codec_extradata[5] = std::byte {0xe0};
    invalid_configurations.push_back(no_sps);

    auto overflowing_sps = Streams();
    overflowing_sps.video.codec_extradata[6] = std::byte {0xff};
    overflowing_sps.video.codec_extradata[7] = std::byte {0xff};
    invalid_configurations.push_back(overflowing_sps);

    auto wrong_sps_type = Streams();
    wrong_sps_type.video.codec_extradata[8] = std::byte {0x68};
    invalid_configurations.push_back(wrong_sps_type);

    auto forbidden_sps_header = Streams();
    forbidden_sps_header.video.codec_extradata[8] = std::byte {0xe7};
    invalid_configurations.push_back(forbidden_sps_header);

    auto undersized_sps = Streams();
    undersized_sps.video.codec_extradata[6] = std::byte {0x00};
    undersized_sps.video.codec_extradata[7] = std::byte {0x02};
    invalid_configurations.push_back(undersized_sps);

    auto mismatched_sps_compatibility = Streams();
    mismatched_sps_compatibility.video.codec_extradata[2] =
        std::byte {0x01};
    invalid_configurations.push_back(mismatched_sps_compatibility);

    auto mismatched_sps_level = Streams();
    mismatched_sps_level.video.codec_extradata[3] = std::byte {0x0e};
    invalid_configurations.push_back(mismatched_sps_level);

    auto wrong_pps_type = Streams();
    wrong_pps_type.video.codec_extradata[34] = std::byte {0x67};
    invalid_configurations.push_back(wrong_pps_type);

    auto forbidden_pps_header = Streams();
    forbidden_pps_header.video.codec_extradata[34] = std::byte {0xe8};
    invalid_configurations.push_back(forbidden_pps_header);

    auto undersized_pps = Streams();
    undersized_pps.video.codec_extradata[32] = std::byte {0x00};
    undersized_pps.video.codec_extradata[33] = std::byte {0x01};
    invalid_configurations.push_back(undersized_pps);

    auto trailing_avcc_garbage = Streams();
    trailing_avcc_garbage.video.codec_extradata.push_back(
        std::byte {0x00});
    invalid_configurations.push_back(trailing_avcc_garbage);

    auto wrong_h264_profile = Streams();
    wrong_h264_profile.video.codec_extradata[1] = std::byte {0x4d};
    invalid_configurations.push_back(wrong_h264_profile);

    auto wrong_aac_rate = Streams();
    wrong_aac_rate.audio.codec_extradata = {
        std::byte {0x12}, std::byte {0x10},
    };
    invalid_configurations.push_back(wrong_aac_rate);

    auto wrong_aac_object_type = Streams();
    wrong_aac_object_type.audio.codec_extradata[0] = std::byte {0x09};
    invalid_configurations.push_back(wrong_aac_object_type);

    auto wrong_aac_channels = Streams();
    wrong_aac_channels.audio.codec_extradata[1] = std::byte {0x88};
    invalid_configurations.push_back(wrong_aac_channels);

    auto wrong_aac_frame_length = Streams();
    wrong_aac_frame_length.audio.codec_extradata[1] = std::byte {0x94};
    invalid_configurations.push_back(wrong_aac_frame_length);

    auto truncated_aac_core_dependency = Streams();
    truncated_aac_core_dependency.audio.codec_extradata[1] =
        std::byte {0x92};
    invalid_configurations.push_back(truncated_aac_core_dependency);

    auto trailing_aac_garbage = Streams();
    trailing_aac_garbage.audio.codec_extradata = {
        std::byte {0x11}, std::byte {0x90}, std::byte {0x00},
    };
    invalid_configurations.push_back(trailing_aac_garbage);

    auto malformed_aac_sync_extension = Streams();
    malformed_aac_sync_extension.audio.codec_extradata[2] =
        std::byte {0x57};
    invalid_configurations.push_back(malformed_aac_sync_extension);

    for (const auto &configuration : invalid_configurations) {
        ScratchOutput invalid_output("invalid-header");
        const auto invalid_path = invalid_output.String();
        auto invalid =
            LibavformatFragmentedMp4MuxerPort::Create(
                invalid_path.c_str());
        CHECK(invalid.status == VRREC_STATUS_OK);
        CHECK(invalid.port->WriteHeader(configuration) ==
            VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(invalid.port->WriteHeader(Streams()) ==
            VRREC_STATUS_INVALID_STATE);
        invalid.port->Abort();
        invalid.port->Abort();
        CHECK(std::filesystem::exists(invalid_output.Path()));
        CHECK(std::filesystem::file_size(invalid_output.Path()) == 0);
    }

    ScratchOutput header_oom_output("header-oom");
    const auto header_oom_path = header_oom_output.String();
    auto header_oom =
        LibavformatFragmentedMp4MuxerPort::Create(
            header_oom_path.c_str());
    CHECK(header_oom.status == VRREC_STATUS_OK);
    av_max_alloc(1);
    const auto header_status = header_oom.port->WriteHeader(Streams());
    av_max_alloc(static_cast<std::size_t>(
        std::numeric_limits<int>::max()));
    CHECK(header_status == VRREC_STATUS_OUT_OF_MEMORY);
    header_oom.port->Abort();

    ScratchOutput packet_oom_output("packet-oom");
    const auto packet_oom_path = packet_oom_output.String();
    auto packet_oom =
        LibavformatFragmentedMp4MuxerPort::Create(
            packet_oom_path.c_str());
    CHECK(packet_oom.status == VRREC_STATUS_OK);
    CHECK(packet_oom.port->WriteHeader(Streams()) == VRREC_STATUS_OK);
    FfmpegPacketTimestamps timestamps {};
    packet_oom.port->RescalePacketTimestamps(
        {0, 0, 33'333},
        MicrosecondPacketTimeBase,
        {1, 1'000'000},
        timestamps);
    av_max_alloc(1);
    const auto packet_status = packet_oom.port->WriteInterleavedPacket(
        VideoPacket(0), timestamps);
    av_max_alloc(static_cast<std::size_t>(
        std::numeric_limits<int>::max()));
    CHECK(packet_status == VRREC_STATUS_OUT_OF_MEMORY);
    packet_oom.port->Abort();
}

void RejectsSideDataAdtsAndEveryMalformedH264PacketBeforeMuxing()
{
    std::vector<EncodedMediaPacket> malformed_packets;

    auto annex_b_in_avcc_stream = VideoPacket(0);
    annex_b_in_avcc_stream.payload = {
        std::byte {0x00}, std::byte {0x00}, std::byte {0x00},
        std::byte {0x01}, std::byte {0x65}, std::byte {0x80},
    };
    malformed_packets.push_back(annex_b_in_avcc_stream);

    auto truncated_nal = VideoPacket(0);
    truncated_nal.payload = {
        std::byte {0x00}, std::byte {0x00}, std::byte {0x00},
        std::byte {0x03}, std::byte {0x65}, std::byte {0x80},
    };
    malformed_packets.push_back(truncated_nal);

    auto zero_length_nal = VideoPacket(0);
    zero_length_nal.payload = {
        std::byte {0x00}, std::byte {0x00}, std::byte {0x00},
        std::byte {0x00},
    };
    malformed_packets.push_back(zero_length_nal);

    auto invalid_nal_type = VideoPacket(0);
    invalid_nal_type.payload[4] = std::byte {0x00};
    malformed_packets.push_back(invalid_nal_type);

    auto forbidden_nal_header = VideoPacket(0);
    forbidden_nal_header.payload[4] = std::byte {0xe5};
    malformed_packets.push_back(forbidden_nal_header);

    auto header_only_nal = VideoPacket(0);
    header_only_nal.payload[4] = std::byte {0x67};
    malformed_packets.push_back(header_only_nal);

    auto header_only_sample = VideoPacket(0);
    header_only_sample.payload = {
        std::byte {0x00}, std::byte {0x00}, std::byte {0x00},
        std::byte {0x01}, std::byte {0x65},
    };
    malformed_packets.push_back(header_only_sample);

    auto non_idr_marked_key = VideoPacket(0, true);
    non_idr_marked_key.payload[4] = std::byte {0x41};
    malformed_packets.push_back(non_idr_marked_key);

    auto idr_not_marked_key = VideoPacket(0, false);
    idr_not_marked_key.payload[4] = std::byte {0x65};
    malformed_packets.push_back(idr_not_marked_key);

    auto mixed_idr_and_non_idr = VideoPacket(0, true);
    const auto non_idr = VideoPacket(0, false);
    mixed_idr_and_non_idr.payload.insert(
        mixed_idr_and_non_idr.payload.end(),
        non_idr.payload.begin(),
        non_idr.payload.end());
    malformed_packets.push_back(mixed_idr_and_non_idr);

    auto mixed_idr_and_partitioned_non_idr = VideoPacket(0, true);
    auto partitioned_non_idr = VideoPacket(0, false);
    partitioned_non_idr.payload[4] = std::byte {0x42};
    mixed_idr_and_partitioned_non_idr.payload.insert(
        mixed_idr_and_partitioned_non_idr.payload.end(),
        partitioned_non_idr.payload.begin(),
        partitioned_non_idr.payload.end());
    malformed_packets.push_back(mixed_idr_and_partitioned_non_idr);

    auto incomplete_second_length = VideoPacket(0);
    incomplete_second_length.payload.push_back(std::byte {0x00});
    malformed_packets.push_back(incomplete_second_length);

    auto unsupported_side_data = AudioPacket(0);
    unsupported_side_data.side_data.push_back({
        EncodedPacketSideDataKind::SkipSamples,
        std::vector<std::byte>(
            SkipSamplesSideDataSize,
            std::byte {0x00}),
    });
    malformed_packets.push_back(unsupported_side_data);

    auto adts = AudioPacket(0);
    adts.payload = {
        std::byte {0xff}, std::byte {0xf1}, std::byte {0x4c},
        std::byte {0x80}, std::byte {0x00}, std::byte {0xff},
        std::byte {0xfc},
    };
    malformed_packets.push_back(adts);

    auto adts_wrong_rate = adts;
    adts_wrong_rate.payload[2] = std::byte {0x50};
    malformed_packets.push_back(adts_wrong_rate);

    auto adts_different_profile = adts;
    adts_different_profile.payload[2] = std::byte {0x0c};
    malformed_packets.push_back(adts_different_profile);

    auto adts_different_channels = adts;
    adts_different_channels.payload[3] = std::byte {0x40};
    malformed_packets.push_back(adts_different_channels);

    auto concatenated_adts_frames = adts;
    concatenated_adts_frames.payload.insert(
        concatenated_adts_frames.payload.end(),
        adts.payload.begin(),
        adts.payload.end());
    malformed_packets.push_back(concatenated_adts_frames);

    for (const auto &packet : malformed_packets) {
        ScratchOutput output("invalid-packet");
        const auto path = output.String();
        auto result =
            LibavformatFragmentedMp4MuxerPort::Create(path.c_str());
        CHECK(result.status == VRREC_STATUS_OK);
        CHECK(result.port->WriteHeader(Streams()) == VRREC_STATUS_OK);
        MediaTimeBase time_base {};
        CHECK(result.port->GetActualStreamTimeBase(
                  packet.stream, time_base) == VRREC_STATUS_OK);
        FfmpegPacketTimestamps timestamps {};
        result.port->RescalePacketTimestamps(
            {
                packet.pts_microseconds,
                packet.dts_microseconds,
                packet.duration_microseconds,
            },
            MicrosecondPacketTimeBase,
            time_base,
            timestamps);
        CHECK(result.port->WriteInterleavedPacket(packet, timestamps) ==
            VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(result.port->WriteTrailer() == VRREC_STATUS_INVALID_STATE);
        result.port->Abort();
        CHECK(std::filesystem::exists(output.Path()));
    }
}

void DoesNotMisclassifyATruncatedAdtsLikePrefixAsAnAdtsFrame()
{
    ScratchOutput output("adts-like-raw-prefix");
    const auto path = output.String();
    auto result = LibavformatFragmentedMp4MuxerPort::Create(path.c_str());
    CHECK(result.status == VRREC_STATUS_OK);
    CHECK(result.port->WriteHeader(Streams()) == VRREC_STATUS_OK);
    auto packet = AudioPacket(0);
    packet.payload = {std::byte {0xff}, std::byte {0xf1}};
    MediaTimeBase time_base {};
    CHECK(result.port->GetActualStreamTimeBase(
              MediaStreamKind::Audio, time_base) == VRREC_STATUS_OK);
    FfmpegPacketTimestamps timestamps {};
    result.port->RescalePacketTimestamps(
        {
            packet.pts_microseconds,
            packet.dts_microseconds,
            packet.duration_microseconds,
        },
        MicrosecondPacketTimeBase,
        time_base,
        timestamps);
    CHECK(result.port->WriteInterleavedPacket(packet, timestamps) ==
        VRREC_STATUS_OK);
    result.port->Abort();
}

void RejectsAnnexBBeforeHeaderMutationUntilAConverterIsImplemented()
{
    auto annex_b_streams = Streams();
    annex_b_streams.video.packet_format = H264PacketFormat::AnnexB;
    annex_b_streams.video.codec_extradata = {
        std::byte {0x00}, std::byte {0x00}, std::byte {0x00},
        std::byte {0x01}, std::byte {0x67}, std::byte {0x64},
        std::byte {0x00}, std::byte {0x0d}, std::byte {0xac},
        std::byte {0xd9}, std::byte {0x41}, std::byte {0x41},
        std::byte {0xfb}, std::byte {0x01}, std::byte {0x10},
        std::byte {0x00}, std::byte {0x00}, std::byte {0x03},
        std::byte {0x00}, std::byte {0x10}, std::byte {0x00},
        std::byte {0x00}, std::byte {0x03}, std::byte {0x03},
        std::byte {0xc0}, std::byte {0xf1}, std::byte {0x42},
        std::byte {0x99}, std::byte {0x60},
        std::byte {0x00}, std::byte {0x00}, std::byte {0x00},
        std::byte {0x01}, std::byte {0x68}, std::byte {0xeb},
        std::byte {0xe3}, std::byte {0xcb}, std::byte {0x22},
        std::byte {0xc0},
    };

    ScratchOutput output("annex-b");
    const auto path = output.String();
    auto result = LibavformatFragmentedMp4MuxerPort::Create(path.c_str());
    CHECK(result.status == VRREC_STATUS_OK);
    CHECK(result.port->WriteHeader(annex_b_streams) ==
        VRREC_STATUS_INVALID_ARGUMENT);
    result.port->Abort();
    CHECK(std::filesystem::exists(output.Path()));
    CHECK(std::filesystem::file_size(output.Path()) == 0);
}

void InjectedInternalFailuresAreTerminalAndReleaseEveryOwnedResource()
{
    {
        ScratchOutput output("injected-header");
        const auto path = output.String();
        auto result =
            LibavformatFragmentedMp4MuxerPort::CreateForTesting(
                path.c_str(),
                LIBAVFORMAT_VERSION_INT,
                LIBAVCODEC_VERSION_INT,
                LIBAVUTIL_VERSION_INT,
                "8.1.2",
                LibavformatMuxerFailurePoint::WriteHeader);
        CHECK(result.status == VRREC_STATUS_OK);
        CHECK(result.port->WriteHeader(Streams()) ==
            VRREC_STATUS_INTERNAL_ERROR);
        CHECK(result.port->WriteHeader(Streams()) ==
            VRREC_STATUS_INVALID_STATE);
        result.port->Abort();
    }
    {
        ScratchOutput output("injected-packet");
        const auto path = output.String();
        LibavformatMuxerOperationCounts operation_counts;
        auto result =
            LibavformatFragmentedMp4MuxerPort::CreateForTesting(
                path.c_str(),
                LIBAVFORMAT_VERSION_INT,
                LIBAVCODEC_VERSION_INT,
                LIBAVUTIL_VERSION_INT,
                "8.1.2",
                LibavformatMuxerFailurePoint::WritePacket,
                &operation_counts);
        CHECK(result.status == VRREC_STATUS_OK);
        CHECK(result.port->WriteHeader(Streams()) == VRREC_STATUS_OK);
        FfmpegPacketTimestamps timestamps {};
        result.port->RescalePacketTimestamps(
            {0, 0, 33'333},
            MicrosecondPacketTimeBase,
            {1, 1'000'000},
            timestamps);
        const auto packet = VideoPacket(0);
        const auto packet_before = packet;
        CHECK(result.port->WriteInterleavedPacket(packet, timestamps) ==
            VRREC_STATUS_INTERNAL_ERROR);
        CHECK(packet.payload == packet_before.payload);
        CHECK(packet.pts_microseconds == packet_before.pts_microseconds);
        CHECK(result.port->WriteInterleavedPacket(packet, timestamps) ==
            VRREC_STATUS_INVALID_STATE);
        CHECK(operation_counts.write_packet_calls == 1);
        CHECK(result.port->WriteTrailer() == VRREC_STATUS_INVALID_STATE);
        result.port->Abort();
    }
    {
        ScratchOutput output("injected-trailer");
        const auto path = output.String();
        auto result =
            LibavformatFragmentedMp4MuxerPort::CreateForTesting(
                path.c_str(),
                LIBAVFORMAT_VERSION_INT,
                LIBAVCODEC_VERSION_INT,
                LIBAVUTIL_VERSION_INT,
                "8.1.2",
                LibavformatMuxerFailurePoint::WriteTrailer);
        CHECK(result.status == VRREC_STATUS_OK);
        CHECK(result.port->WriteHeader(Streams()) == VRREC_STATUS_OK);
        CHECK(result.port->WriteTrailer() == VRREC_STATUS_INTERNAL_ERROR);
        CHECK(result.port->FlushFile() == VRREC_STATUS_INVALID_STATE);
        result.port->Abort();
    }
    for (const auto point : {
             LibavformatMuxerFailurePoint::FlushFile,
             LibavformatMuxerFailurePoint::CloseFile,
             LibavformatMuxerFailurePoint::FlushAndCloseFile,
         }) {
        ScratchOutput output("injected-finalize");
        const auto path = output.String();
        LibavformatMuxerOperationCounts operation_counts;
        auto result =
            LibavformatFragmentedMp4MuxerPort::CreateForTesting(
                path.c_str(),
                LIBAVFORMAT_VERSION_INT,
                LIBAVCODEC_VERSION_INT,
                LIBAVUTIL_VERSION_INT,
                "8.1.2",
                point,
                &operation_counts);
        CHECK(result.status == VRREC_STATUS_OK);
        CHECK(result.port->WriteHeader(Streams()) == VRREC_STATUS_OK);
        CHECK(result.port->WriteTrailer() == VRREC_STATUS_OK);
        CHECK(result.port->FlushFile() == VRREC_STATUS_INTERNAL_ERROR);
        CHECK(result.port->FlushFile() == VRREC_STATUS_INVALID_STATE);
        result.port->Abort();
        CHECK(operation_counts.flush_file_calls == 1);
        CHECK(operation_counts.close_file_calls == 1);

        // avio_closep must have run even when final durability reports an
        // error. Reopening for truncation is also a Windows handle check.
        std::ofstream reopened(output.Path(), std::ios::binary | std::ios::trunc);
        CHECK(reopened.good());
    }
}

#if defined(__linux__)
void PropagatesARealAvioDeviceFailureAndStillClosesExactlyOnce()
{
    LibavformatMuxerOperationCounts operation_counts;
    auto result =
        LibavformatFragmentedMp4MuxerPort::CreateForTesting(
            "/dev/full",
            LIBAVFORMAT_VERSION_INT,
            LIBAVCODEC_VERSION_INT,
            LIBAVUTIL_VERSION_INT,
            "8.1.2",
            LibavformatMuxerFailurePoint::None,
            &operation_counts);
    CHECK(result.status == VRREC_STATUS_OK);
    FfmpegFragmentedMp4Muxer muxer(*result.port);
    CHECK(muxer.WriteHeader(Streams()) == VRREC_STATUS_OK);
    CHECK(muxer.WritePacket(VideoPacket(0)) == VRREC_STATUS_OK);
    CHECK(muxer.WritePacket(AudioPacket(-21'333)) == VRREC_STATUS_OK);

    const auto trailer_status = muxer.WriteTrailer();
    const auto flush_status = trailer_status == VRREC_STATUS_OK
        ? muxer.FlushFile()
        : VRREC_STATUS_INVALID_STATE;
    CHECK(trailer_status == VRREC_STATUS_INTERNAL_ERROR ||
        flush_status == VRREC_STATUS_INTERNAL_ERROR);
    muxer.Abort();
    result.port->Abort();
    CHECK(operation_counts.close_file_calls == 1);
}
#endif

void FailureClosesButPreservesThePendingRecordingWithoutPublishing()
{
    PendingPublicationOutput output;
    const auto pending = output.PendingPath().string();
    LibavformatMuxerOperationCounts operation_counts;
    auto result =
        LibavformatFragmentedMp4MuxerPort::CreateForTesting(
            pending.c_str(),
            LIBAVFORMAT_VERSION_INT,
            LIBAVCODEC_VERSION_INT,
            LIBAVUTIL_VERSION_INT,
            "8.1.2",
            LibavformatMuxerFailurePoint::WritePacket,
            &operation_counts);
    CHECK(result.status == VRREC_STATUS_OK);
    CHECK(result.port->WriteHeader(Streams()) == VRREC_STATUS_OK);
    MediaTimeBase time_base {};
    CHECK(result.port->GetActualStreamTimeBase(
              MediaStreamKind::Video, time_base) == VRREC_STATUS_OK);
    FfmpegPacketTimestamps timestamps {};
    result.port->RescalePacketTimestamps(
        {0, 0, 33'333},
        MicrosecondPacketTimeBase,
        time_base,
        timestamps);
    CHECK(result.port->WriteInterleavedPacket(
              VideoPacket(0), timestamps) ==
        VRREC_STATUS_INTERNAL_ERROR);
    result.port->Abort();
    result.port->Abort();

    CHECK(operation_counts.write_packet_calls == 1);
    CHECK(operation_counts.close_file_calls == 1);
    CHECK(std::filesystem::exists(output.PendingPath()));
    CHECK(!std::filesystem::exists(output.FinalPath()));
    std::ofstream reopened(
        output.PendingPath(),
        std::ios::binary | std::ios::app);
    CHECK(reopened.good());
}

void AbortAndDestructionCloseUnfinishedOutputsIdempotently()
{
    ScratchOutput output("abort-close");
    const auto path = output.String();
    LibavformatMuxerOperationCounts operation_counts;
    {
        auto result =
            LibavformatFragmentedMp4MuxerPort::CreateForTesting(
                path.c_str(),
                LIBAVFORMAT_VERSION_INT,
                LIBAVCODEC_VERSION_INT,
                LIBAVUTIL_VERSION_INT,
                "8.1.2",
                LibavformatMuxerFailurePoint::None,
                &operation_counts);
        CHECK(result.status == VRREC_STATUS_OK);
        CHECK(result.port->WriteHeader(Streams()) == VRREC_STATUS_OK);
        result.port->Abort();
        result.port->Abort();
        CHECK(result.port->WriteTrailer() == VRREC_STATUS_INVALID_STATE);
    }
    CHECK(operation_counts.flush_file_calls == 0);
    CHECK(operation_counts.close_file_calls == 1);
    std::ofstream reopened(output.Path(), std::ios::binary | std::ios::trunc);
    CHECK(reopened.good());
}

}

int main()
{
    RejectsEveryRuntimeIdentityMismatchBeforeOpeningTheOutput();
    RejectsInvalidPathsAndMapsRealAllocationFailure();
    CreatesH264AndAacStreamsAndReadsBackPostHeaderTimeBases();
    UsesLibavutilPacketRescalingIncludingNegativeAacPriming();
    ClonesCanonicalPacketsIntoAnActualFragmentedMp4();
    EnforcesMinimumAndMaximumFragmentDurationWithRealMovflags();
    RejectsInvalidConfigurationAndRealPacketAllocationFailure();
    RejectsSideDataAdtsAndEveryMalformedH264PacketBeforeMuxing();
    DoesNotMisclassifyATruncatedAdtsLikePrefixAsAnAdtsFrame();
    RejectsAnnexBBeforeHeaderMutationUntilAConverterIsImplemented();
    InjectedInternalFailuresAreTerminalAndReleaseEveryOwnedResource();
#if defined(__linux__)
    PropagatesARealAvioDeviceFailureAndStillClosesExactlyOnce();
#endif
    FailureClosesButPreservesThePendingRecordingWithoutPublishing();
    AbortAndDestructionCloseUnfinishedOutputsIdempotently();
    return 0;
}
