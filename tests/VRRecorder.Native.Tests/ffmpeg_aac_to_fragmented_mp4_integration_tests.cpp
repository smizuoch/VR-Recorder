#include "ffmpeg_aac_audio_pipeline.hpp"
#include "ffmpeg_aac_packet_encoder.hpp"
#include "ffmpeg_fragmented_mp4_muxer.hpp"
#include "ffmpeg_libavformat_fragmented_mp4_muxer_port.hpp"
#include "media_mux_pipeline.hpp"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <cstdio>
#include <iostream>
#include <iterator>
#include <limits>
#include <mutex>
#include <span>
#include <string>
#include <sstream>
#include <utility>
#include <vector>

extern "C" {
#include <libavcodec/version.h>
#include <libavformat/version.h>
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
    ScratchOutput()
    {
        static std::atomic_uint64_t sequence {0};
        const auto suffix = std::to_string(
            std::chrono::steady_clock::now().time_since_epoch().count()) +
            "-" + std::to_string(sequence.fetch_add(1));
        path_ = std::filesystem::temp_directory_path() /
            ("vrrecorder-aac-to-mux-" + suffix + ".mp4");
        std::error_code error;
        std::filesystem::remove(path_, error);
    }

    ~ScratchOutput()
    {
        std::error_code error;
        std::filesystem::remove(path_, error);
    }

    const std::filesystem::path &Path() const noexcept
    {
        return path_;
    }

private:
    std::filesystem::path path_;
};

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

bool HasAacEsdsBitRates(
    const std::vector<std::byte> &bytes,
    std::uint32_t expected)
{
    const auto type_offset = FindAscii(bytes, "esds");
    return type_offset != std::numeric_limits<std::size_t>::max() &&
        type_offset <= bytes.size() && bytes.size() - type_offset >= 34 &&
        bytes[type_offset + 8] == std::byte {0x03} &&
        bytes[type_offset + 16] == std::byte {0x04} &&
        bytes[type_offset + 21] == std::byte {0x40} &&
        bytes[type_offset + 22] == std::byte {0x15} &&
        ReadBigEndian32(bytes, type_offset + 26) == expected &&
        ReadBigEndian32(bytes, type_offset + 30) == expected;
}

bool HasAacBtrtBitRates(
    const std::vector<std::byte> &bytes,
    std::uint32_t expected)
{
    const auto type_offset = FindAscii(bytes, "btrt");
    return type_offset != std::numeric_limits<std::size_t>::max() &&
        type_offset <= bytes.size() && bytes.size() - type_offset >= 16 &&
        ReadBigEndian32(bytes, type_offset + 4) == 0 &&
        ReadBigEndian32(bytes, type_offset + 8) == expected &&
        ReadBigEndian32(bytes, type_offset + 12) == expected;
}

struct FragmentedFileSummary final {
    std::size_t ftyp_count = 0;
    std::size_t moov_count = 0;
    std::size_t moof_count = 0;
    std::size_t mdat_count = 0;
    std::size_t mfra_count = 0;
    std::size_t mdat_payload_bytes = 0;
    std::vector<std::byte> mdat_payload;
};

bool HasBoxType(
    const std::vector<std::byte> &bytes,
    std::size_t type_offset,
    const char (&type)[5])
{
    return type_offset <= bytes.size() &&
        bytes.size() - type_offset >= 4 &&
        bytes[type_offset] == static_cast<std::byte>(type[0]) &&
        bytes[type_offset + 1] == static_cast<std::byte>(type[1]) &&
        bytes[type_offset + 2] == static_cast<std::byte>(type[2]) &&
        bytes[type_offset + 3] == static_cast<std::byte>(type[3]);
}

FragmentedFileSummary SummarizeTopLevelBoxes(
    const std::vector<std::byte> &bytes)
{
    FragmentedFileSummary summary;
    std::size_t cursor = 0;
    while (cursor < bytes.size()) {
        CHECK(bytes.size() - cursor >= 8);
        const auto box_size = static_cast<std::size_t>(
            ReadBigEndian32(bytes, cursor));
        CHECK(box_size >= 8);
        CHECK(box_size <= bytes.size() - cursor);
        const auto type_offset = cursor + 4;
        if (HasBoxType(bytes, type_offset, "ftyp")) {
            ++summary.ftyp_count;
        } else if (HasBoxType(bytes, type_offset, "moov")) {
            ++summary.moov_count;
        } else if (HasBoxType(bytes, type_offset, "moof")) {
            ++summary.moof_count;
        } else if (HasBoxType(bytes, type_offset, "mdat")) {
            ++summary.mdat_count;
            summary.mdat_payload_bytes += box_size - 8;
            summary.mdat_payload.insert(
                summary.mdat_payload.end(),
                bytes.begin() + static_cast<std::ptrdiff_t>(cursor + 8),
                bytes.begin() + static_cast<std::ptrdiff_t>(
                    cursor + box_size));
        } else if (HasBoxType(bytes, type_offset, "mfra")) {
            ++summary.mfra_count;
        }
        cursor += box_size;
    }
    CHECK(cursor == bytes.size());
    return summary;
}

bool HasElstMediaTime(
    const std::vector<std::byte> &bytes,
    std::int64_t expected_media_time)
{
    const std::array<std::byte, 4> needle {
        static_cast<std::byte>('e'),
        static_cast<std::byte>('l'),
        static_cast<std::byte>('s'),
        static_cast<std::byte>('t'),
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
                entry_count <= (box_size - 16U) / entry_size) {
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


#ifndef VRRECORDER_AAC_DECODE_ORACLE_EXECUTABLE
#define VRRECORDER_AAC_DECODE_ORACLE_EXECUTABLE ""
#endif

struct AacDecodeOracleSummary final {
    std::string codec_name;
    std::string profile;
    std::uint64_t sample_rate = 0;
    std::uint64_t channel_count = 0;
    std::uint64_t packet_count = 0;
    std::uint64_t bitrate_metadata_bits_per_second = 0;
    std::int64_t first_pts_microseconds = 0;
    std::int64_t first_dts_microseconds = 0;
    std::uint64_t decoded_frame_count = 0;
    std::uint64_t presented_decoded_frame_count = 0;
    std::string video_codec_name;
    std::uint64_t video_width = 0;
    std::uint64_t video_height = 0;
    std::uint64_t video_packet_count = 0;
    std::uint64_t video_decoded_frame_count = 0;
};

std::string ShellQuote(const std::filesystem::path &path)
{
    std::string text = path.string();
#if defined(_WIN32)
    CHECK(text.find('"') == std::string::npos);
    return '"' + text + '"';
#else
    std::string quoted;
    quoted.reserve(text.size() + 2U);
    quoted.push_back('\'');
    for (const char value : text) {
        if (value == '\'') {
            quoted += "'\\''";
        } else {
            quoted.push_back(value);
        }
    }
    quoted.push_back('\'');
    return quoted;
#endif
}

FILE *OpenProcessPipe(const std::string &command)
{
#if defined(_WIN32)
    return _popen(command.c_str(), "r");
#else
    return popen(command.c_str(), "r");
#endif
}

int CloseProcessPipe(FILE *pipe)
{
#if defined(_WIN32)
    return _pclose(pipe);
#else
    return pclose(pipe);
#endif
}

AacDecodeOracleSummary RunAacDecodeOracle(
    const std::filesystem::path &media_path)
{
    const std::filesystem::path oracle_path {
        VRRECORDER_AAC_DECODE_ORACLE_EXECUTABLE};
    CHECK(!oracle_path.empty());
    CHECK(std::filesystem::exists(oracle_path));
    CHECK(std::filesystem::is_regular_file(oracle_path));

    const auto command = ShellQuote(oracle_path) + " " + ShellQuote(media_path);
    std::array<char, 4096> buffer {};
    std::string output;
    FILE *pipe = OpenProcessPipe(command);
    CHECK(pipe != nullptr);
    while (fgets(buffer.data(), static_cast<int>(buffer.size()), pipe) != nullptr) {
        output += buffer.data();
    }
    const int status = CloseProcessPipe(pipe);
    CHECK(status == 0);

    AacDecodeOracleSummary summary;
    std::istringstream lines(output);
    std::string line;
    while (std::getline(lines, line)) {
        const auto separator = line.find('=');
        CHECK(separator != std::string::npos);
        const auto key = line.substr(0, separator);
        const auto value = line.substr(separator + 1U);
        CHECK(!key.empty());
        CHECK(!value.empty());
        if (key == "codec_name") {
            summary.codec_name = value;
        } else if (key == "profile") {
            summary.profile = value;
        } else if (key == "sample_rate") {
            summary.sample_rate = std::stoull(value);
        } else if (key == "channel_count") {
            summary.channel_count = std::stoull(value);
        } else if (key == "packet_count") {
            summary.packet_count = std::stoull(value);
        } else if (key == "bitrate_metadata_bits_per_second") {
            summary.bitrate_metadata_bits_per_second = std::stoull(value);
        } else if (key == "first_pts_microseconds") {
            summary.first_pts_microseconds = std::stoll(value);
        } else if (key == "first_dts_microseconds") {
            summary.first_dts_microseconds = std::stoll(value);
        } else if (key == "decoded_frame_count") {
            summary.decoded_frame_count = std::stoull(value);
        } else if (key == "presented_decoded_frame_count") {
            summary.presented_decoded_frame_count = std::stoull(value);
        } else if (key == "video_codec_name") {
            summary.video_codec_name = value;
        } else if (key == "video_width") {
            summary.video_width = std::stoull(value);
        } else if (key == "video_height") {
            summary.video_height = std::stoull(value);
        } else if (key == "video_packet_count") {
            summary.video_packet_count = std::stoull(value);
        } else if (key == "video_decoded_frame_count") {
            summary.video_decoded_frame_count = std::stoull(value);
        } else {
            CHECK(false);
        }
    }
    CHECK(summary.codec_name == "aac");
    CHECK(summary.profile == "LC");
    CHECK(summary.sample_rate == 48'000);
    CHECK(summary.channel_count == 2);
    CHECK(summary.packet_count > 0);
    CHECK(summary.bitrate_metadata_bits_per_second ==
        AacTargetBitrateBitsPerSecond);
    return summary;
}

std::vector<float> StereoFrames(
    std::uint64_t start_frame,
    std::size_t frame_count)
{
    std::vector<float> samples(frame_count * 2U);
    for (std::size_t index = 0; index < frame_count; ++index) {
        const auto frame = start_frame + index;
        const auto value =
            static_cast<float>(frame % 97U) / 485.0F - 0.1F;
        samples[index * 2U] = value;
        samples[index * 2U + 1U] = -value;
    }
    return samples;
}

constexpr auto WaitTimeout = std::chrono::seconds(2);

class OneWindowCapture final : public StereoAudioCaptureSessionPort {
public:
    vrrec_status_t Start(
        const StereoAudioCaptureSessionConfig &) noexcept override
    {
        const std::lock_guard lock(mutex_);
        ++start_calls_;
        return VRREC_STATUS_OK;
    }

    StereoAudioMixResult MixNext(
        std::size_t frame_count_48k,
        std::span<float> output_interleaved,
        StereoAudioMixRead &read) noexcept override
    {
        std::unique_lock lock(mutex_);
        ++mix_calls_;
        changed_.notify_all();
        if (mix_calls_ == 1) {
            if (frame_count_48k != 1'024 ||
                output_interleaved.size() != frame_count_48k * 2U) {
                return StereoAudioMixResult::InvalidArgument;
            }
            for (std::size_t index = 0; index < frame_count_48k; ++index) {
                const auto value =
                    static_cast<float>(index % 97U) / 485.0F - 0.1F;
                output_interleaved[index * 2U] = value;
                output_interleaved[index * 2U + 1U] = -value;
            }
            read = {0, frame_count_48k, true, true, false, false};
            return StereoAudioMixResult::Mixed;
        }

        changed_.wait(lock, [this] { return aborted_; });
        return StereoAudioMixResult::Aborted;
    }

    vrrec_status_t SetRouting(vrrec_audio_routing_t) noexcept override
    {
        return VRREC_STATUS_OK;
    }

    void Abort() noexcept override
    {
        {
            const std::lock_guard lock(mutex_);
            if (aborted_) {
                return;
            }
            aborted_ = true;
            ++abort_calls_;
        }
        changed_.notify_all();
    }

    bool WaitForBlockedSecondMix()
    {
        std::unique_lock lock(mutex_);
        return changed_.wait_for(
            lock,
            WaitTimeout,
            [this] { return mix_calls_ >= 2; });
    }

    std::size_t StartCalls() const
    {
        const std::lock_guard lock(mutex_);
        return start_calls_;
    }

    std::size_t AbortCalls() const
    {
        const std::lock_guard lock(mutex_);
        return abort_calls_;
    }

private:
    mutable std::mutex mutex_;
    std::condition_variable changed_;
    std::size_t start_calls_ = 0;
    std::size_t abort_calls_ = 0;
    std::size_t mix_calls_ = 0;
    bool aborted_ = false;
};

class SilentMediaEvents final : public MediaEventSink {
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
};

StereoAudioCaptureSessionConfig CaptureConfig()
{
    return {"desktop-id", "microphone-id", 1'234'567};
}

H264StreamDescriptor VideoDescriptor()
{
    const std::array<std::uint8_t, 35> avcc {
        0x01, 0x64, 0x10, 0x0a, 0xff, 0xe1, 0x00, 0x13,
        0x67, 0x64, 0x10, 0x0a, 0xac, 0xbb, 0xd8, 0x08,
        0x80, 0x00, 0x00, 0x03, 0x00, 0x80, 0x00, 0x00,
        0x03, 0x01, 0x42, 0x01, 0x00, 0x05, 0x68, 0xee,
        0x0f, 0x2c, 0x8b,
    };
    std::vector<std::byte> extradata(avcc.size());
    std::transform(
        avcc.begin(),
        avcc.end(),
        extradata.begin(),
        [](std::uint8_t value) { return static_cast<std::byte>(value); });
    return {
        MicrosecondPacketTimeBase,
        16,
        16,
        H264Profile::High,
        H264PacketFormat::AvccLengthPrefixed,
        std::move(extradata),
    };
}

EncodedMediaPacket DeterministicH264Packet(
    std::span<const std::uint8_t> idr,
    std::int64_t timestamp_microseconds)
{
    CHECK(idr.size() <= 0xffU);
    std::vector<std::byte> payload {
        std::byte {0},
        std::byte {0},
        std::byte {0},
        static_cast<std::byte>(idr.size()),
    };
    std::transform(
        idr.begin(),
        idr.end(),
        std::back_inserter(payload),
        [](std::uint8_t value) { return static_cast<std::byte>(value); });
    return {
        MediaStreamKind::Video,
        timestamp_microseconds,
        timestamp_microseconds,
        1'000'000,
        true,
        std::move(payload),
        {},
    };
}

void WriteDeterministicH264Packets(FfmpegFragmentedMp4Muxer &mux)
{
    const std::array<std::uint8_t, 14> first {
        0x65, 0x88, 0x84, 0x04, 0xbf, 0xfe, 0xf7,
        0xad, 0xdf, 0x81, 0x4d, 0xc3, 0x2b, 0x3d,
    };
    const std::array<std::uint8_t, 15> second {
        0x65, 0x88, 0x82, 0x01, 0x7f, 0xfe, 0xf7, 0xd4,
        0xb7, 0xcc, 0xb2, 0xee, 0x07, 0x23, 0x80,
    };
    const std::array<std::uint8_t, 15> third {
        0x65, 0x88, 0x84, 0x05, 0xff, 0xfe, 0xf7, 0xd4,
        0xb7, 0xcc, 0xb2, 0xee, 0x07, 0x23, 0x81,
    };
    CHECK(mux.WritePacket(DeterministicH264Packet(first, 0)) ==
          VRREC_STATUS_OK);
    CHECK(mux.WritePacket(DeterministicH264Packet(second, 1'000'000)) ==
          VRREC_STATUS_OK);
    CHECK(mux.WritePacket(DeterministicH264Packet(third, 2'000'000)) ==
          VRREC_STATUS_OK);
}

void RequiresThreeDecodedH264FramesAlongsideThreeSecondsOfRealAac()
{
    constexpr std::uint64_t SampleRate = 48'000;
    constexpr std::uint64_t InputFrameCount = SampleRate * 3U;
    constexpr std::size_t WindowFrameCount = 1'024;

    AacAudioEncoderConfig config {};
    CHECK(CreateAacAudioEncoderConfig(config) == VRREC_STATUS_OK);
    auto encoder_result = FfmpegAacPacketEncoder::Create(config);
    CHECK(encoder_result.status == VRREC_STATUS_OK);
    CHECK(encoder_result.encoder != nullptr);
    CHECK(encoder_result.descriptor.has_value());

    ScratchOutput output;
    const auto path = output.Path().string();
    auto mux_result = LibavformatFragmentedMp4MuxerPort::Create(path.c_str());
    CHECK(mux_result.status == VRREC_STATUS_OK);
    CHECK(mux_result.port != nullptr);
    FfmpegFragmentedMp4Muxer mux(*mux_result.port);
    const FragmentedMp4StreamConfiguration streams {
        VideoDescriptor(),
        std::move(*encoder_result.descriptor),
        DefaultFragmentedMp4FragmentPolicy,
    };
    encoder_result.descriptor.reset();
    CHECK(mux.WriteHeader(streams) == VRREC_STATUS_OK);
    WriteDeterministicH264Packets(mux);

    std::uint64_t frame = 0;
    while (frame < InputFrameCount) {
        const auto frame_count = static_cast<std::size_t>(std::min(
            static_cast<std::uint64_t>(WindowFrameCount),
            InputFrameCount - frame));
        auto samples = StereoFrames(frame, frame_count);
        const auto encoded =
            encoder_result.encoder->EncodePcm48k(frame, samples);
        CHECK(encoded.status == VRREC_STATUS_OK);
        for (const auto &packet : encoded.packets) {
            CHECK(mux.WritePacket(packet) == VRREC_STATUS_OK);
        }
        frame += frame_count;
    }
    const auto finished = encoder_result.encoder->Finish();
    CHECK(finished.status == VRREC_STATUS_OK);
    for (const auto &packet : finished.packets) {
        CHECK(mux.WritePacket(packet) == VRREC_STATUS_OK);
    }
    CHECK(mux.WriteTrailer() == VRREC_STATUS_OK);
    CHECK(mux.FlushFile() == VRREC_STATUS_OK);

    const auto oracle = RunAacDecodeOracle(output.Path());
    CHECK(oracle.presented_decoded_frame_count == InputFrameCount);
    CHECK(oracle.video_packet_count == 3);
    CHECK(oracle.video_decoded_frame_count == 3);
}

void CarriesTheOpenedAacBitrateIntoTheRealFragmentedMp4Header()
{
    AacAudioEncoderConfig config {};
    CHECK(CreateAacAudioEncoderConfig(config) == VRREC_STATUS_OK);
    auto encoder_result = FfmpegAacPacketEncoder::Create(config);
    CHECK(encoder_result.status == VRREC_STATUS_OK);
    CHECK(encoder_result.encoder != nullptr);
    CHECK(encoder_result.descriptor.has_value());

    auto audio = std::move(*encoder_result.descriptor);
    encoder_result.descriptor.reset();
    encoder_result.encoder.reset();
    CHECK(audio.bitrate_bits_per_second ==
        AacTargetBitrateBitsPerSecond);

    ScratchOutput output;
    const auto path = output.Path().string();
    auto mux_result = LibavformatFragmentedMp4MuxerPort::Create(
        path.c_str());
    CHECK(mux_result.status == VRREC_STATUS_OK);
    CHECK(mux_result.port != nullptr);
    const FragmentedMp4StreamConfiguration streams {
        VideoDescriptor(),
        std::move(audio),
        DefaultFragmentedMp4FragmentPolicy,
    };

    CHECK(mux_result.port->WriteHeader(streams) == VRREC_STATUS_OK);
    CHECK(mux_result.port->WriteTrailer() == VRREC_STATUS_OK);
    CHECK(mux_result.port->FlushFile() == VRREC_STATUS_OK);

    const auto bytes = ReadAll(output.Path());
    CHECK(HasAacEsdsBitRates(bytes, AacTargetBitrateBitsPerSecond));
    CHECK(HasAacBtrtBitRates(bytes, AacTargetBitrateBitsPerSecond));
}

void WritesThreeSecondsOfRealAacPacketsIntoFragmentedMp4()
{
    constexpr std::uint64_t SampleRate = 48'000;
    constexpr std::uint64_t InputFrameCount = SampleRate * 3U;
    constexpr std::size_t WindowFrameCount = 1'024;

    AacAudioEncoderConfig config {};
    CHECK(CreateAacAudioEncoderConfig(config) == VRREC_STATUS_OK);
    auto encoder_result = FfmpegAacPacketEncoder::Create(config);
    CHECK(encoder_result.status == VRREC_STATUS_OK);
    CHECK(encoder_result.encoder != nullptr);
    CHECK(encoder_result.descriptor.has_value());

    ScratchOutput output;
    const auto path = output.Path().string();
    auto mux_result = LibavformatFragmentedMp4MuxerPort::Create(
        path.c_str());
    CHECK(mux_result.status == VRREC_STATUS_OK);
    CHECK(mux_result.port != nullptr);
    FfmpegFragmentedMp4Muxer mux(*mux_result.port);
    const FragmentedMp4StreamConfiguration streams {
        VideoDescriptor(),
        std::move(*encoder_result.descriptor),
        DefaultFragmentedMp4FragmentPolicy,
    };
    encoder_result.descriptor.reset();
    CHECK(mux.WriteHeader(streams) == VRREC_STATUS_OK);

    std::vector<std::byte> encoded_payload;
    std::size_t encoded_packet_count = 0;
    std::int64_t first_packet_pts_microseconds = 0;
    std::int64_t first_packet_dts_microseconds = 0;
    std::int64_t final_packet_end_microseconds = 0;
    const auto write_batch = [&](PacketAudioEncoderWrite batch) {
        CHECK(batch.status == VRREC_STATUS_OK);
        for (const auto &packet : batch.packets) {
            CHECK(packet.stream == MediaStreamKind::Audio);
            CHECK(packet.side_data.empty());
            if (encoded_packet_count == 0) {
                first_packet_pts_microseconds = packet.pts_microseconds;
                first_packet_dts_microseconds = packet.dts_microseconds;
            }
            ++encoded_packet_count;
            CHECK(mux.WritePacket(packet) == VRREC_STATUS_OK);
            encoded_payload.insert(
                encoded_payload.end(),
                packet.payload.begin(),
                packet.payload.end());
            final_packet_end_microseconds =
                packet.pts_microseconds + packet.duration_microseconds;
        }
    };

    std::uint64_t frame = 0;
    while (frame < InputFrameCount) {
        const auto frame_count = static_cast<std::size_t>(std::min(
            static_cast<std::uint64_t>(WindowFrameCount),
            InputFrameCount - frame));
        auto samples = StereoFrames(frame, frame_count);
        write_batch(encoder_result.encoder->EncodePcm48k(frame, samples));
        frame += frame_count;
    }
    write_batch(encoder_result.encoder->Finish());

    CHECK(!encoded_payload.empty());
    CHECK(final_packet_end_microseconds == 3'000'000);
    CHECK(mux.WriteTrailer() == VRREC_STATUS_OK);
    CHECK(mux.FlushFile() == VRREC_STATUS_OK);

    const auto bytes = ReadAll(output.Path());
    const auto boxes = SummarizeTopLevelBoxes(bytes);
    CHECK(boxes.ftyp_count == 1);
    CHECK(boxes.moov_count == 1);
    CHECK(boxes.moof_count > 0);
    CHECK(boxes.mdat_count > 0);
    CHECK(boxes.mdat_payload_bytes == encoded_payload.size());
    CHECK(boxes.mdat_payload == encoded_payload);
    CHECK(HasAacEsdsBitRates(bytes, AacTargetBitrateBitsPerSecond));
    CHECK(HasAacBtrtBitRates(bytes, AacTargetBitrateBitsPerSecond));

    const auto oracle = RunAacDecodeOracle(output.Path());
    CHECK(oracle.packet_count == encoded_packet_count);
    CHECK(oracle.first_pts_microseconds == first_packet_pts_microseconds);
    CHECK(oracle.first_dts_microseconds == first_packet_dts_microseconds);
    CHECK(oracle.presented_decoded_frame_count == InputFrameCount);
    CHECK(oracle.video_codec_name == "h264");
    CHECK(oracle.video_width == 16);
    CHECK(oracle.video_height == 16);
    CHECK(oracle.video_packet_count == 0);
    CHECK(oracle.video_decoded_frame_count == 0);
}

void FlushesTheOwnedAacPipelineThroughTheRealMuxGraph()
{
    ScratchOutput output;
    LibavformatMuxerOperationCounts operation_counts {};
    const auto path = output.Path().string();
    auto port_result =
        LibavformatFragmentedMp4MuxerPort::CreateForTesting(
            path.c_str(),
            LIBAVFORMAT_VERSION_INT,
            LIBAVCODEC_VERSION_INT,
            LIBAVUTIL_VERSION_INT,
            "8.1.2",
            LibavformatMuxerFailurePoint::None,
            &operation_counts);
    CHECK(port_result.status == VRREC_STATUS_OK);
    CHECK(port_result.port != nullptr);

    FfmpegFragmentedMp4Muxer mux(*port_result.port);
    SilentMediaEvents events;
    MediaMuxPipeline submission(mux, events);
    OneWindowCapture capture;
    auto creation = CreateFfmpegAacAudioPipeline(capture, submission);
    CHECK(creation.status == VRREC_STATUS_OK);
    CHECK(creation.pipeline != nullptr);
    CHECK(creation.descriptor.has_value());
    CHECK(creation.descriptor->sample_rate == 48'000);
    CHECK(creation.descriptor->channel_count == 2);
    CHECK(creation.descriptor->frame_size == 1'024);
    CHECK(creation.descriptor->initial_padding_samples == 1'024);
    CHECK(
        creation.descriptor->bitrate_bits_per_second ==
        AacTargetBitrateBitsPerSecond);
    CHECK(creation.descriptor->profile == AacProfile::LowComplexity);
    CHECK(
        creation.descriptor->packet_format ==
        AacPacketFormat::RawAccessUnit);
    const auto frame_size = creation.descriptor->frame_size;
    const FragmentedMp4StreamConfiguration streams {
        VideoDescriptor(),
        std::move(*creation.descriptor),
        DefaultFragmentedMp4FragmentPolicy,
    };
    creation.descriptor.reset();

    CHECK(submission.Start(streams) == VRREC_STATUS_OK);
    CHECK(
        submission.EncoderFinished(MediaStreamKind::Video) ==
        VRREC_STATUS_OK);
    CHECK(
        creation.pipeline->Session().Start(CaptureConfig(), frame_size) ==
        VRREC_STATUS_OK);
    CHECK(capture.WaitForBlockedSecondMix());
    CHECK(operation_counts.write_packet_calls == 0);
    CHECK(operation_counts.flush_file_calls == 0);
    CHECK(operation_counts.close_file_calls == 0);

    CHECK(
        creation.pipeline->Session().RequestStop() ==
        VRREC_STATUS_OK);
    CHECK(
        creation.pipeline->Session().Join() ==
        StereoAudioEncodingWorkerResult::Stopped);
    const auto statistics = creation.pipeline->Session().Statistics();
    CHECK(statistics.submitted_frame_count == 1'024);
    CHECK(statistics.muxed_packet_count == 2);
    CHECK(operation_counts.write_packet_calls == 2);
    CHECK(operation_counts.flush_file_calls == 1);
    CHECK(operation_counts.close_file_calls == 1);
    CHECK(capture.StartCalls() == 1);
    CHECK(capture.AbortCalls() == 1);

    const auto bytes = ReadAll(output.Path());
    const auto boxes = SummarizeTopLevelBoxes(bytes);
    CHECK(boxes.ftyp_count == 1);
    CHECK(boxes.moov_count == 1);
    CHECK(boxes.moof_count == 1);
    CHECK(boxes.mdat_count == 1);
    CHECK(boxes.mdat_payload_bytes > 0);
    CHECK(boxes.mfra_count == 1);
    CHECK(HasElstMediaTime(bytes, 1'024));
    CHECK(HasAacEsdsBitRates(bytes, AacTargetBitrateBitsPerSecond));
    CHECK(HasAacBtrtBitRates(bytes, AacTargetBitrateBitsPerSecond));
}

}

int main()
{
    CarriesTheOpenedAacBitrateIntoTheRealFragmentedMp4Header();
    WritesThreeSecondsOfRealAacPacketsIntoFragmentedMp4();
    RequiresThreeDecodedH264FramesAlongsideThreeSecondsOfRealAac();
    FlushesTheOwnedAacPipelineThroughTheRealMuxGraph();
    return 0;
}
