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

template <std::size_t Size>
std::size_t FindAsciiInRange(
    const std::vector<unsigned char> &bytes,
    const char (&text)[Size],
    std::size_t begin,
    std::size_t end)
{
    static_assert(Size == 5U);
    if (begin > end || end > bytes.size()) {
        Fail("invalid MP4 search range");
    }
    const unsigned char needle[] {
        static_cast<unsigned char>(text[0]),
        static_cast<unsigned char>(text[1]),
        static_cast<unsigned char>(text[2]),
        static_cast<unsigned char>(text[3]),
    };
    const auto found = std::search(
        bytes.begin() + static_cast<std::ptrdiff_t>(begin),
        bytes.begin() + static_cast<std::ptrdiff_t>(end),
        std::begin(needle),
        std::end(needle));
    return found == bytes.begin() + static_cast<std::ptrdiff_t>(end)
        ? std::numeric_limits<std::size_t>::max()
        : static_cast<std::size_t>(found - bytes.begin());
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


std::uint64_t ReadAudioPresentedSampleCount(
    const char *path,
    std::uint64_t audio_duration_samples,
    std::uint32_t audio_track_id)
{
    const auto bytes = ReadAllBytes(path);
    if (audio_duration_samples == 0U || audio_track_id == 0U) {
        Fail("missing audio duration or track identity");
    }

    std::uint64_t edit_media_time = 0;
    bool found_audio_track = false;
    std::size_t search_start = 0;
    while (search_start < bytes.size()) {
        const auto trak = FindAsciiInRange(
            bytes,
            "trak",
            search_start,
            bytes.size());
        if (trak == std::numeric_limits<std::size_t>::max()) {
            break;
        }
        if (trak < 4U) {
            Fail("invalid trak box");
        }
        const auto track_box_start = trak - 4U;
        const auto track_box_size = ReadBigEndian32(bytes, track_box_start);
        if (track_box_size < 8U ||
            track_box_size > bytes.size() - track_box_start) {
            Fail("invalid trak box size");
        }
        const auto track_box_end = track_box_start + track_box_size;
        const auto tkhd = FindAsciiInRange(bytes, "tkhd", trak + 4U, track_box_end);
        if (tkhd == std::numeric_limits<std::size_t>::max() ||
            track_box_end - tkhd < 20U) {
            Fail("missing or truncated tkhd box");
        }
        const auto tkhd_version = bytes[tkhd + 4U];
        const auto track_id_offset = tkhd_version == 0U
            ? tkhd + 16U
            : tkhd_version == 1U
                ? tkhd + 24U
                : std::numeric_limits<std::size_t>::max();
        if (track_id_offset == std::numeric_limits<std::size_t>::max() ||
            track_id_offset > track_box_end ||
            track_box_end - track_id_offset < 4U) {
            Fail("unsupported or truncated tkhd box");
        }
        if (ReadBigEndian32(bytes, track_id_offset) != audio_track_id) {
            search_start = track_box_end;
            continue;
        }
        if (found_audio_track) {
            Fail("duplicate audio track identity");
        }
        found_audio_track = true;
        const auto elst = FindAsciiInRange(bytes, "elst", trak + 4U, track_box_end);
        if (elst == std::numeric_limits<std::size_t>::max()) {
            break;
        }
        if (elst < 4U || bytes.size() - elst < 24U) {
            Fail("truncated elst box");
        }
        const auto version = bytes[elst + 4U];
        const auto entry_count = ReadBigEndian32(bytes, elst + 8U);
        if (entry_count != 1U) {
            Fail("unexpected audio edit list entry count");
        }
        if (version == 0U) {
            const auto raw_media_time = ReadBigEndian32(bytes, elst + 16U);
            edit_media_time = static_cast<std::uint64_t>(raw_media_time);
        } else if (version == 1U) {
            if (bytes.size() - elst < 32U) {
                Fail("truncated version-1 elst box");
            }
            edit_media_time =
                (static_cast<std::uint64_t>(ReadBigEndian32(bytes, elst + 20U)) << 32U) |
                ReadBigEndian32(bytes, elst + 24U);
        } else {
            Fail("unsupported elst version");
        }
        break;
    }
    if (!found_audio_track) {
        Fail("audio track identity not found in MP4 metadata");
    }
    if (edit_media_time > audio_duration_samples) {
        Fail("audio edit list exceeds sample duration");
    }
    return audio_duration_samples - edit_media_time;
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
    int video_stream_index = -1;
    for (unsigned int index = 0; index < format->nb_streams; ++index) {
        const AVStream *stream = format->streams[index];
        if (stream->codecpar->codec_type == AVMEDIA_TYPE_AUDIO) {
            if (audio_stream_index >= 0) {
                Fail("multiple audio streams");
            }
            audio_stream_index = static_cast<int>(index);
        } else if (stream->codecpar->codec_type == AVMEDIA_TYPE_VIDEO) {
            if (video_stream_index >= 0) {
                Fail("multiple video streams");
            }
            video_stream_index = static_cast<int>(index);
        } else {
            Fail("unexpected non audio/video stream");
        }
    }
    if (audio_stream_index < 0) {
        Fail("missing audio stream");
    }
    if (video_stream_index < 0) {
        Fail("missing video stream");
    }

    AVStream *audio_stream = format->streams[audio_stream_index];
    const AVCodecParameters *audio_parameters = audio_stream->codecpar;
    if (audio_parameters->codec_id != AV_CODEC_ID_AAC) {
        Fail("audio stream is not AAC");
    }
    const AVCodec *audio_codec = avcodec_find_decoder(audio_parameters->codec_id);
    if (audio_codec == nullptr) {
        Fail("AAC decoder not found");
    }
    AVCodecContext *audio_decoder = avcodec_alloc_context3(audio_codec);
    if (audio_decoder == nullptr) {
        Fail("audio decoder allocation failed");
    }
    Check(avcodec_parameters_to_context(audio_decoder, audio_parameters),
        "copy audio codec parameters");
    Check(avcodec_open2(audio_decoder, audio_codec, nullptr),
        "open audio decoder");

    AVStream *video_stream = format->streams[video_stream_index];
    const AVCodecParameters *video_parameters = video_stream->codecpar;
    if (video_parameters->codec_id != AV_CODEC_ID_H264) {
        Fail("video stream is not H.264");
    }
    const AVCodec *video_codec = avcodec_find_decoder(video_parameters->codec_id);
    if (video_codec == nullptr) {
        Fail("H.264 decoder not found");
    }
    AVCodecContext *video_decoder = avcodec_alloc_context3(video_codec);
    if (video_decoder == nullptr) {
        Fail("video decoder allocation failed");
    }
    Check(avcodec_parameters_to_context(video_decoder, video_parameters),
        "copy video codec parameters");
    Check(avcodec_open2(video_decoder, video_codec, nullptr),
        "open video decoder");

    AVPacket *packet = av_packet_alloc();
    AVFrame *audio_frame = av_frame_alloc();
    AVFrame *video_frame = av_frame_alloc();
    if (packet == nullptr || audio_frame == nullptr || video_frame == nullptr) {
        Fail("packet/frames allocation failed");
    }

    std::uint64_t packet_count = 0;
    std::uint64_t decoded_frame_count = 0;
    std::uint64_t audio_duration_ticks = 0;
    std::uint64_t video_packet_count = 0;
    std::uint64_t video_decoded_frame_count = 0;
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
            if (packet->duration <= 0 ||
                static_cast<std::uint64_t>(packet->duration) >
                    std::numeric_limits<std::uint64_t>::max() -
                        audio_duration_ticks) {
                Fail("invalid audio packet duration");
            }
            audio_duration_ticks +=
                static_cast<std::uint64_t>(packet->duration);
            Check(avcodec_send_packet(audio_decoder, packet),
                "send audio packet");
            while (true) {
                const int receive_result =
                    avcodec_receive_frame(audio_decoder, audio_frame);
                if (receive_result == AVERROR(EAGAIN) ||
                    receive_result == AVERROR_EOF) {
                    break;
                }
                Check(receive_result, "receive audio frame");
                const auto decoded_samples =
                    static_cast<std::uint64_t>(audio_frame->nb_samples);
                decoded_frame_count += decoded_samples;
                av_frame_unref(audio_frame);
            }
        } else if (packet->stream_index == video_stream_index) {
            ++video_packet_count;
            Check(avcodec_send_packet(video_decoder, packet),
                "send video packet");
            while (true) {
                const int receive_result =
                    avcodec_receive_frame(video_decoder, video_frame);
                if (receive_result == AVERROR(EAGAIN) ||
                    receive_result == AVERROR_EOF) {
                    break;
                }
                Check(receive_result, "receive video frame");
                ++video_decoded_frame_count;
                av_frame_unref(video_frame);
            }
        }
        av_packet_unref(packet);
    }

    Check(avcodec_send_packet(audio_decoder, nullptr), "send audio drain");
    while (true) {
        const int receive_result =
            avcodec_receive_frame(audio_decoder, audio_frame);
        if (receive_result == AVERROR_EOF) {
            break;
        }
        if (receive_result == AVERROR(EAGAIN)) {
            continue;
        }
        Check(receive_result, "receive audio drain frame");
        const auto decoded_samples =
            static_cast<std::uint64_t>(audio_frame->nb_samples);
        decoded_frame_count += decoded_samples;
        av_frame_unref(audio_frame);
    }
    Check(avcodec_send_packet(video_decoder, nullptr), "send video drain");
    while (true) {
        const int receive_result =
            avcodec_receive_frame(video_decoder, video_frame);
        if (receive_result == AVERROR_EOF) {
            break;
        }
        if (receive_result == AVERROR(EAGAIN)) {
            continue;
        }
        Check(receive_result, "receive video drain frame");
        ++video_decoded_frame_count;
        av_frame_unref(video_frame);
    }

    if (!saw_first_packet) {
        Fail("no audio packets");
    }
    if (audio_stream->id <= 0) {
        Fail("invalid audio track identity");
    }
    const auto audio_duration_samples = av_rescale_q(
        static_cast<std::int64_t>(audio_duration_ticks),
        audio_stream->time_base,
        AVRational {1, audio_parameters->sample_rate});
    if (audio_duration_samples <= 0) {
        Fail("invalid audio duration");
    }

    std::cout << "codec_name=aac\n";
    std::cout << "profile=LC\n";
    std::cout << "sample_rate=" << audio_parameters->sample_rate << '\n';
    std::cout << "channel_count=" << ChannelCount(*audio_parameters) << '\n';
    std::cout << "packet_count=" << packet_count << '\n';
    std::cout << "bitrate_metadata_bits_per_second="
              << ReadAacBitrateMetadata(argv[1]) << '\n';
    std::cout << "first_pts_microseconds=" << first_pts_us << '\n';
    std::cout << "first_dts_microseconds=" << first_dts_us << '\n';
    std::cout << "decoded_frame_count=" << decoded_frame_count << '\n';
    std::cout << "presented_decoded_frame_count="
              << ReadAudioPresentedSampleCount(
                     argv[1],
                     static_cast<std::uint64_t>(audio_duration_samples),
                     static_cast<std::uint32_t>(audio_stream->id))
              << '\n';
    std::cout << "video_codec_name=h264\n";
    std::cout << "video_width=" << video_parameters->width << '\n';
    std::cout << "video_height=" << video_parameters->height << '\n';
    std::cout << "video_packet_count=" << video_packet_count << '\n';
    std::cout << "video_decoded_frame_count="
              << video_decoded_frame_count << '\n';

    av_frame_free(&video_frame);
    av_frame_free(&audio_frame);
    av_packet_free(&packet);
    avcodec_free_context(&video_decoder);
    avcodec_free_context(&audio_decoder);
    avformat_close_input(&format);
    return 0;
}
