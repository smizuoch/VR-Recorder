#include <cstdint>
#include <cstdlib>
#include <algorithm>
#include <fstream>
#include <iostream>
#include <iterator>
#include <vector>
#include <limits>
#include <string>

extern "C" {
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavutil/channel_layout.h>
#include <libavutil/error.h>
#include <libavutil/frame.h>
}

namespace {

[[noreturn]] void Fail(const std::string &message)
{
    std::cerr << "oracle error: " << message << '\n';
    std::exit(1);
}


std::vector<unsigned char> ReadAllBytes(const char *path)
{
    std::ifstream input(path, std::ios::binary);
    if (!input.good()) {
        Fail("cannot read media bytes");
    }
    return {
        std::istreambuf_iterator<char>(input),
        std::istreambuf_iterator<char>(),
    };
}

std::uint32_t ReadBigEndian32(
    const std::vector<unsigned char> &bytes,
    std::size_t offset)
{
    if (offset > bytes.size() || bytes.size() - offset < 4U) {
        Fail("truncated MP4 metadata");
    }
    return
        (static_cast<std::uint32_t>(bytes[offset]) << 24U) |
        (static_cast<std::uint32_t>(bytes[offset + 1U]) << 16U) |
        (static_cast<std::uint32_t>(bytes[offset + 2U]) << 8U) |
        static_cast<std::uint32_t>(bytes[offset + 3U]);
}

template <std::size_t Size>
std::size_t FindAscii(
    const std::vector<unsigned char> &bytes,
    const char (&text)[Size])
{
    static_assert(Size == 5U);
    const unsigned char needle[] {
        static_cast<unsigned char>(text[0]),
        static_cast<unsigned char>(text[1]),
        static_cast<unsigned char>(text[2]),
        static_cast<unsigned char>(text[3]),
    };
    const auto found = std::search(
        bytes.begin(),
        bytes.end(),
        std::begin(needle),
        std::end(needle));
    if (found == bytes.end()) {
        return std::numeric_limits<std::size_t>::max();
    }
    return static_cast<std::size_t>(found - bytes.begin());
}

std::uint32_t ReadAacBitrateMetadata(const char *path)
{
    const auto bytes = ReadAllBytes(path);
    const auto esds = FindAscii(bytes, "esds");
    const auto btrt = FindAscii(bytes, "btrt");
    if (esds == std::numeric_limits<std::size_t>::max() ||
        btrt == std::numeric_limits<std::size_t>::max()) {
        Fail("missing AAC bitrate metadata boxes");
    }
    if (bytes.size() - esds < 34U ||
        bytes[esds + 8U] != 0x03U ||
        bytes[esds + 16U] != 0x04U ||
        bytes[esds + 21U] != 0x40U ||
        bytes[esds + 22U] != 0x15U) {
        Fail("AAC esds metadata shape mismatch");
    }
    const auto esds_maximum = ReadBigEndian32(bytes, esds + 26U);
    const auto esds_average = ReadBigEndian32(bytes, esds + 30U);
    if (bytes.size() - btrt < 16U || ReadBigEndian32(bytes, btrt + 4U) != 0U) {
        Fail("AAC btrt metadata shape mismatch");
    }
    const auto btrt_maximum = ReadBigEndian32(bytes, btrt + 8U);
    const auto btrt_average = ReadBigEndian32(bytes, btrt + 12U);
    if (esds_maximum != esds_average ||
        esds_maximum != btrt_maximum ||
        esds_maximum != btrt_average) {
        Fail("AAC bitrate metadata disagrees across boxes");
    }
    return esds_maximum;
}

void Check(int result, const char *operation)
{
    if (result < 0) {
        char buffer[AV_ERROR_MAX_STRING_SIZE] {};
        av_strerror(result, buffer, sizeof(buffer));
        Fail(std::string(operation) + " failed: " + buffer);
    }
}

std::int64_t ToMicroseconds(std::int64_t value, AVRational time_base)
{
    if (value == AV_NOPTS_VALUE) {
        return 0;
    }
    return av_rescale_q(value, time_base, AVRational {1, 1'000'000});
}

std::uint64_t ChannelCount(const AVCodecParameters &parameters)
{
#if LIBAVUTIL_VERSION_MAJOR >= 57
    return static_cast<std::uint64_t>(parameters.ch_layout.nb_channels);
#else
    return static_cast<std::uint64_t>(parameters.channels);
#endif
}

} // namespace

int main(int argc, char **argv)
{
    if (argc != 2) {
        Fail("usage: ffmpeg_aac_decode_oracle <mp4-path>");
    }

    AVFormatContext *format = nullptr;
    Check(avformat_open_input(&format, argv[1], nullptr, nullptr), "open input");
    Check(avformat_find_stream_info(format, nullptr), "find stream info");

    int audio_stream_index = -1;
    for (unsigned int index = 0; index < format->nb_streams; ++index) {
        const AVStream *stream = format->streams[index];
        if (stream->codecpar->codec_type == AVMEDIA_TYPE_AUDIO) {
            if (audio_stream_index >= 0) {
                Fail("multiple audio streams");
            }
            audio_stream_index = static_cast<int>(index);
        } else if (stream->codecpar->codec_type != AVMEDIA_TYPE_VIDEO) {
            Fail("unexpected non audio/video stream");
        }
    }
    if (audio_stream_index < 0) {
        Fail("missing audio stream");
    }

    AVStream *audio_stream = format->streams[audio_stream_index];
    const AVCodecParameters *parameters = audio_stream->codecpar;
    if (parameters->codec_id != AV_CODEC_ID_AAC) {
        Fail("audio stream is not AAC");
    }
    const AVCodec *codec = avcodec_find_decoder(parameters->codec_id);
    if (codec == nullptr) {
        Fail("AAC decoder not found");
    }
    AVCodecContext *decoder = avcodec_alloc_context3(codec);
    if (decoder == nullptr) {
        Fail("decoder allocation failed");
    }
    Check(avcodec_parameters_to_context(decoder, parameters), "copy codec parameters");
    Check(avcodec_open2(decoder, codec, nullptr), "open decoder");

    AVPacket *packet = av_packet_alloc();
    AVFrame *frame = av_frame_alloc();
    if (packet == nullptr || frame == nullptr) {
        Fail("packet/frame allocation failed");
    }

    std::uint64_t packet_count = 0;
    std::uint64_t decoded_frame_count = 0;
    std::int64_t first_pts_us = 0;
    std::int64_t first_dts_us = 0;
    bool saw_first_packet = false;

    while (true) {
        const int read_result = av_read_frame(format, packet);
        if (read_result == AVERROR_EOF) {
            break;
        }
        Check(read_result, "read frame");
        if (packet->stream_index == audio_stream_index) {
            if (!saw_first_packet) {
                first_pts_us = ToMicroseconds(packet->pts, audio_stream->time_base);
                first_dts_us = ToMicroseconds(packet->dts, audio_stream->time_base);
                saw_first_packet = true;
            }
            ++packet_count;
            Check(avcodec_send_packet(decoder, packet), "send packet");
            while (true) {
                const int receive_result = avcodec_receive_frame(decoder, frame);
                if (receive_result == AVERROR(EAGAIN) ||
                    receive_result == AVERROR_EOF) {
                    break;
                }
                Check(receive_result, "receive frame");
                decoded_frame_count += static_cast<std::uint64_t>(frame->nb_samples);
                av_frame_unref(frame);
            }
        }
        av_packet_unref(packet);
    }

    Check(avcodec_send_packet(decoder, nullptr), "send drain");
    while (true) {
        const int receive_result = avcodec_receive_frame(decoder, frame);
        if (receive_result == AVERROR_EOF) {
            break;
        }
        if (receive_result == AVERROR(EAGAIN)) {
            continue;
        }
        Check(receive_result, "receive drain frame");
        decoded_frame_count += static_cast<std::uint64_t>(frame->nb_samples);
        av_frame_unref(frame);
    }

    if (!saw_first_packet) {
        Fail("no audio packets");
    }

    std::cout << "codec_name=aac\n";
    std::cout << "profile=LC\n";
    std::cout << "sample_rate=" << parameters->sample_rate << '\n';
    std::cout << "channel_count=" << ChannelCount(*parameters) << '\n';
    std::cout << "packet_count=" << packet_count << '\n';
    std::cout << "bitrate_metadata_bits_per_second="
              << ReadAacBitrateMetadata(argv[1]) << '\n';
    std::cout << "first_pts_microseconds=" << first_pts_us << '\n';
    std::cout << "first_dts_microseconds=" << first_dts_us << '\n';
    std::cout << "decoded_frame_count=" << decoded_frame_count << '\n';

    av_frame_free(&frame);
    av_packet_free(&packet);
    avcodec_free_context(&decoder);
    avformat_close_input(&format);
    return 0;
}
