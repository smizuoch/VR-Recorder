#include "ffmpeg_aac_packet_encoder.hpp"
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
#include <utility>
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

H264StreamDescriptor VideoDescriptor()
{
    const std::array<std::uint8_t, 44> avcc {
        0x01, 0x64, 0x00, 0x0a, 0xff, 0xe1, 0x00, 0x17,
        0x67, 0x64, 0x00, 0x0a, 0xac, 0xb2, 0x3d, 0x80,
        0x88, 0x00, 0x00, 0x03, 0x00, 0x08, 0x00, 0x00,
        0x03, 0x01, 0xe0, 0x78, 0x91, 0x32, 0x40, 0x01,
        0x00, 0x06, 0x68, 0xeb, 0xc3, 0xcb, 0x22, 0xc0,
        0xfd, 0xf8, 0xf8, 0x00,
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

}

int main()
{
    CarriesTheOpenedAacBitrateIntoTheRealFragmentedMp4Header();
    return 0;
}
