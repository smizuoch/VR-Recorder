#include "ffmpeg_libavformat_fragmented_mp4_muxer_port.hpp"

#include <cerrno>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <limits>
#include <new>
#include <utility>

extern "C" {
#include <libavcodec/avcodec.h>
#include <libavcodec/codec_par.h>
#include <libavcodec/defs.h>
#include <libavcodec/packet.h>
#include <libavcodec/version.h>
#include <libavformat/avformat.h>
#include <libavformat/avio.h>
#include <libavformat/version.h>
#include <libavutil/avutil.h>
#include <libavutil/channel_layout.h>
#include <libavutil/dict.h>
#include <libavutil/error.h>
#include <libavutil/mem.h>
#include <libavutil/version.h>
}

namespace vrrecorder::native {

namespace {

constexpr char PinnedFfmpegVersion[] = "8.1.2";

vrrec_status_t ErrorStatus(int error) noexcept
{
    return error == AVERROR(ENOMEM)
        ? VRREC_STATUS_OUT_OF_MEMORY
        : VRREC_STATUS_INTERNAL_ERROR;
}

bool IsTimeBaseValid(MediaTimeBase time_base) noexcept
{
    return time_base.numerator > 0 && time_base.denominator > 0;
}

AVRational Rational(MediaTimeBase time_base) noexcept
{
    return {time_base.numerator, time_base.denominator};
}

std::int64_t AvTimestamp(std::int64_t timestamp) noexcept
{
    return timestamp == UnknownMediaTimestamp
        ? AV_NOPTS_VALUE
        : timestamp;
}

std::int64_t MediaTimestamp(std::int64_t timestamp) noexcept
{
    return timestamp == AV_NOPTS_VALUE
        ? UnknownMediaTimestamp
        : timestamp;
}

bool IsH264NalTypeSupported(std::byte header) noexcept
{
    const auto value = std::to_integer<unsigned int>(header);
    const auto type = value & 0x1fU;
    return (value & 0x80U) == 0 &&
        ((type >= 1U && type <= 16U) ||
            (type >= 19U && type <= 21U));
}

bool ReadBigEndian16(
    const std::vector<std::byte> &bytes,
    std::size_t &cursor,
    std::size_t &value) noexcept
{
    if (cursor > bytes.size() || bytes.size() - cursor < 2) {
        return false;
    }
    value =
        (std::to_integer<std::size_t>(bytes[cursor]) << 8U) |
        std::to_integer<std::size_t>(bytes[cursor + 1]);
    cursor += 2;
    return true;
}

bool ConsumeAvccNal(
    const std::vector<std::byte> &bytes,
    std::size_t &cursor,
    unsigned int expected_nal_type,
    std::size_t minimum_length,
    std::size_t *nal_offset = nullptr) noexcept
{
    std::size_t length = 0;
    if (!ReadBigEndian16(bytes, cursor, length) ||
        length < minimum_length ||
        cursor > bytes.size() || length > bytes.size() - cursor) {
        return false;
    }
    const auto nal_type =
        std::to_integer<unsigned int>(bytes[cursor]) & 0x1fU;
    if (!IsH264NalTypeSupported(bytes[cursor]) ||
        nal_type != expected_nal_type) {
        return false;
    }
    if (nal_offset != nullptr) {
        *nal_offset = cursor;
    }
    cursor += length;
    return true;
}

bool IsAvccExtradata(
    const std::vector<std::byte> &extradata,
    H264Profile expected_profile) noexcept
{
    if (extradata.size() < 7 || extradata[0] != std::byte {1}) {
        return false;
    }

    const auto expected_profile_idc = expected_profile == H264Profile::High
        ? static_cast<unsigned int>(AV_PROFILE_H264_HIGH)
        : static_cast<unsigned int>(AV_PROFILE_H264_MAIN);
    if (std::to_integer<unsigned int>(extradata[1]) !=
            expected_profile_idc ||
        (std::to_integer<unsigned int>(extradata[4]) & 0xfcU) != 0xfcU ||
        (std::to_integer<unsigned int>(extradata[4]) & 0x03U) != 3U ||
        (std::to_integer<unsigned int>(extradata[5]) & 0xe0U) != 0xe0U) {
        return false;
    }

    std::size_t cursor = 6;
    const auto sps_count =
        std::to_integer<unsigned int>(extradata[5]) & 0x1fU;
    if (sps_count == 0) {
        return false;
    }
    for (unsigned int index = 0; index < sps_count; ++index) {
        std::size_t sps_offset = 0;
        if (!ConsumeAvccNal(
                extradata, cursor, 7U, 4, &sps_offset) ||
            std::to_integer<unsigned int>(extradata[sps_offset + 1]) !=
                expected_profile_idc ||
            (index == 0 &&
                (extradata[sps_offset + 1] != extradata[1] ||
                    extradata[sps_offset + 2] != extradata[2] ||
                    extradata[sps_offset + 3] != extradata[3]))) {
            return false;
        }
    }
    if (cursor >= extradata.size()) {
        return false;
    }

    const auto pps_count =
        std::to_integer<unsigned int>(extradata[cursor++]);
    if (pps_count == 0) {
        return false;
    }
    for (unsigned int index = 0; index < pps_count; ++index) {
        if (!ConsumeAvccNal(extradata, cursor, 8U, 2)) {
            return false;
        }
    }

    // High-profile AVCDecoderConfigurationRecord optionally carries the
    // chroma/bit-depth extension. If present, validate the entire extension
    // and reject trailing or truncated bytes.
    if (cursor < extradata.size()) {
        if (expected_profile != H264Profile::High ||
            extradata.size() - cursor < 4 ||
            (std::to_integer<unsigned int>(extradata[cursor]) & 0xfcU) !=
                0xfcU ||
            (std::to_integer<unsigned int>(extradata[cursor + 1]) & 0xf8U) !=
                0xf8U ||
            (std::to_integer<unsigned int>(extradata[cursor + 2]) & 0xf8U) !=
                0xf8U) {
            return false;
        }
        cursor += 3;
        const auto extension_count =
            std::to_integer<unsigned int>(extradata[cursor++]);
        for (unsigned int index = 0; index < extension_count; ++index) {
            if (!ConsumeAvccNal(extradata, cursor, 13U, 2)) {
                return false;
            }
        }
    }
    return cursor == extradata.size();
}

bool IsAvccPacket(
    const std::vector<std::byte> &payload,
    bool key_frame) noexcept
{
    std::size_t cursor = 0;
    bool found_idr = false;
    bool found_non_idr_vcl = false;
    while (cursor < payload.size()) {
        if (payload.size() - cursor < 4) {
            return false;
        }
        const auto length =
            (std::to_integer<std::size_t>(payload[cursor]) << 24U) |
            (std::to_integer<std::size_t>(payload[cursor + 1]) << 16U) |
            (std::to_integer<std::size_t>(payload[cursor + 2]) << 8U) |
            std::to_integer<std::size_t>(payload[cursor + 3]);
        cursor += 4;
        if (length < 2 || length > payload.size() - cursor ||
            !IsH264NalTypeSupported(payload[cursor])) {
            return false;
        }
        const auto nal_type =
            std::to_integer<unsigned int>(payload[cursor]) & 0x1fU;
        found_idr = found_idr || nal_type == 5U;
        found_non_idr_vcl = found_non_idr_vcl ||
            (nal_type >= 1U && nal_type <= 4U);
        cursor += length;
    }
    return key_frame
        ? found_idr && !found_non_idr_vcl
        : found_non_idr_vcl && !found_idr;
}

bool IsAacAudioSpecificConfig(
    const std::vector<std::byte> &extradata) noexcept
{
    if (extradata.size() < 2) {
        return false;
    }
    const auto first = std::to_integer<unsigned int>(extradata[0]);
    const auto second = std::to_integer<unsigned int>(extradata[1]);
    const auto audio_object_type = first >> 3U;
    const auto sampling_frequency_index =
        ((first & 0x07U) << 1U) | (second >> 7U);
    const auto channel_configuration = (second >> 3U) & 0x0fU;
    const auto core_configuration_valid = audio_object_type == 2U &&
        sampling_frequency_index == 3U &&
        channel_configuration == 2U && (second & 0x07U) == 0;
    if (!core_configuration_valid || extradata.size() == 3 ||
        extradata.size() == 4 || extradata.size() > 5) {
        return false;
    }
    if (extradata.size() == 2) {
        return true;
    }
    // Pinned libavcodec AAC emits the GASpecificConfig followed by this
    // three-byte sync extension. Accept the short ASC too, but do not silently
    // accept arbitrary trailing extension syntax that this port does not parse.
    return extradata[2] == std::byte {0x56} &&
        extradata[3] == std::byte {0xe5} &&
        extradata[4] == std::byte {0x00};
}

bool IsAdtsTransportPayload(
    const std::vector<std::byte> &payload) noexcept
{
    std::size_t cursor = 0;
    bool found_frame = false;
    while (cursor < payload.size()) {
        if (payload.size() - cursor < 7) {
            return false;
        }
        const auto byte0 =
            std::to_integer<unsigned int>(payload[cursor]);
        const auto byte1 =
            std::to_integer<unsigned int>(payload[cursor + 1]);
        const auto byte2 =
            std::to_integer<unsigned int>(payload[cursor + 2]);
        const auto byte3 =
            std::to_integer<unsigned int>(payload[cursor + 3]);
        const auto byte4 =
            std::to_integer<unsigned int>(payload[cursor + 4]);
        const auto byte5 =
            std::to_integer<unsigned int>(payload[cursor + 5]);
        const auto protection_absent = (byte1 & 0x01U) != 0;
        const std::size_t header_size = protection_absent ? 7U : 9U;
        const auto frame_length =
            ((byte3 & 0x03U) << 11U) | (byte4 << 3U) | (byte5 >> 5U);
        const auto sampling_frequency_index = (byte2 >> 2U) & 0x0fU;
        if (byte0 != 0xffU || (byte1 & 0xf6U) != 0xf0U ||
            sampling_frequency_index >= 13U ||
            frame_length < header_size ||
            frame_length > payload.size() - cursor) {
            return false;
        }
        cursor += frame_length;
        found_frame = true;
    }
    if (!found_frame) {
        return false;
    }
    return true;
}

bool IsConfigurationValid(
    const FragmentedMp4StreamConfiguration &configuration) noexcept
{
    constexpr std::uint32_t MaximumDimension = 16'384;
    const auto video_profile_valid =
        configuration.video.profile == H264Profile::Main ||
        configuration.video.profile == H264Profile::High;
    const auto video_extradata_valid = video_profile_valid &&
        configuration.video.packet_format ==
            H264PacketFormat::AvccLengthPrefixed &&
        IsAvccExtradata(
            configuration.video.codec_extradata,
            configuration.video.profile);
    const auto &policy = configuration.fragment_policy;

    return configuration.video.packet_time_base ==
            MicrosecondPacketTimeBase &&
        configuration.audio.packet_time_base ==
            MicrosecondPacketTimeBase &&
        configuration.video.width > 0 &&
        configuration.video.height > 0 &&
        configuration.video.width <= MaximumDimension &&
        configuration.video.height <= MaximumDimension &&
        configuration.video.width % 2U == 0 &&
        configuration.video.height % 2U == 0 &&
        video_extradata_valid &&
        configuration.audio.sample_rate == 48'000 &&
        configuration.audio.channel_count == 2 &&
        configuration.audio.bitrate_bits_per_second ==
            AacTargetBitrateBitsPerSecond &&
        configuration.audio.frame_size == 1'024 &&
        configuration.audio.initial_padding_samples <=
            static_cast<std::uint32_t>(
                std::numeric_limits<std::int32_t>::max()) &&
        configuration.audio.profile == AacProfile::LowComplexity &&
        configuration.audio.channel_layout == AudioChannelLayout::Stereo &&
        configuration.audio.packet_format ==
            AacPacketFormat::RawAccessUnit &&
        IsAacAudioSpecificConfig(configuration.audio.codec_extradata) &&
        policy.minimum_duration_microseconds > 0 &&
        policy.maximum_duration_microseconds >=
            policy.minimum_duration_microseconds &&
        policy.maximum_duration_microseconds <=
            std::numeric_limits<int>::max();
}

vrrec_status_t CopyExtradata(
    const std::vector<std::byte> &source,
    AVCodecParameters &destination) noexcept
{
    if (source.empty() ||
        source.size() >
            static_cast<std::size_t>(std::numeric_limits<int>::max()) ||
        source.size() > std::numeric_limits<std::size_t>::max() -
            AV_INPUT_BUFFER_PADDING_SIZE) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    auto *copy = static_cast<std::uint8_t *>(av_mallocz(
        source.size() + AV_INPUT_BUFFER_PADDING_SIZE));
    if (copy == nullptr) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    }
    std::memcpy(copy, source.data(), source.size());
    destination.extradata = copy;
    destination.extradata_size = static_cast<int>(source.size());
    return VRREC_STATUS_OK;
}

bool IsPacketTimingValid(
    const EncodedMediaPacket &packet,
    std::int64_t minimum_audio_timestamp) noexcept
{
    if ((packet.stream != MediaStreamKind::Video &&
            packet.stream != MediaStreamKind::Audio) ||
        packet.pts_microseconds == UnknownMediaTimestamp ||
        packet.dts_microseconds == UnknownMediaTimestamp ||
        packet.pts_microseconds < packet.dts_microseconds ||
        packet.duration_microseconds <= 0 || packet.payload.empty() ||
        !packet.side_data.empty()) {
        return false;
    }
    const auto minimum = packet.stream == MediaStreamKind::Video
        ? 0
        : minimum_audio_timestamp;
    return packet.pts_microseconds >= minimum &&
        packet.dts_microseconds >= minimum &&
        packet.pts_microseconds <=
            std::numeric_limits<std::int64_t>::max() -
                packet.duration_microseconds &&
        packet.dts_microseconds <=
            std::numeric_limits<std::int64_t>::max() -
                packet.duration_microseconds;
}

}

struct LibavformatFragmentedMp4MuxerPort::RuntimeIdentity final {
    unsigned int avformat_version;
    unsigned int avcodec_version;
    unsigned int avutil_version;
    const char *release_version;
};

class LibavformatFragmentedMp4MuxerPort::Impl final {
public:
    enum class State {
        Created,
        Ready,
        TrailerWritten,
        Finished,
        Failed,
        Aborted,
    };

    explicit Impl(
        AVFormatContext *context_value
#if defined(VRRECORDER_NATIVE_TESTING)
        , LibavformatMuxerFailurePoint failure_point_value,
        LibavformatMuxerOperationCounts *operation_counts_value
#endif
        ) noexcept
        : context(context_value)
#if defined(VRRECORDER_NATIVE_TESTING)
        , failure_point(failure_point_value),
          operation_counts(operation_counts_value)
#endif
    {
    }

    ~Impl()
    {
        Release();
    }

    Impl(const Impl &) = delete;
    Impl &operator=(const Impl &) = delete;

    void Release() noexcept
    {
        video_stream = nullptr;
        audio_stream = nullptr;
        if (context != nullptr && context->pb != nullptr) {
            static_cast<void>(CloseOutput());
        }
        avformat_free_context(context);
        context = nullptr;
    }

    int CloseOutput() noexcept
    {
        if (context == nullptr || context->pb == nullptr) {
            return 0;
        }
#if defined(VRRECORDER_NATIVE_TESTING)
        if (operation_counts != nullptr) {
            ++operation_counts->close_file_calls;
        }
#endif
        return avio_closep(&context->pb);
    }

    bool FailWriteHeader() const noexcept
    {
#if defined(VRRECORDER_NATIVE_TESTING)
        return failure_point == LibavformatMuxerFailurePoint::WriteHeader;
#else
        return false;
#endif
    }

    bool FailWritePacket() const noexcept
    {
#if defined(VRRECORDER_NATIVE_TESTING)
        return failure_point == LibavformatMuxerFailurePoint::WritePacket;
#else
        return false;
#endif
    }

    bool FailWriteTrailer() const noexcept
    {
#if defined(VRRECORDER_NATIVE_TESTING)
        return failure_point == LibavformatMuxerFailurePoint::WriteTrailer;
#else
        return false;
#endif
    }

    bool FailFlushFile() const noexcept
    {
#if defined(VRRECORDER_NATIVE_TESTING)
        return failure_point == LibavformatMuxerFailurePoint::FlushFile ||
            failure_point ==
                LibavformatMuxerFailurePoint::FlushAndCloseFile;
#else
        return false;
#endif
    }

    bool FailCloseFile() const noexcept
    {
#if defined(VRRECORDER_NATIVE_TESTING)
        return failure_point == LibavformatMuxerFailurePoint::CloseFile ||
            failure_point ==
                LibavformatMuxerFailurePoint::FlushAndCloseFile;
#else
        return false;
#endif
    }

    AVFormatContext *context = nullptr;
    AVStream *video_stream = nullptr;
    AVStream *audio_stream = nullptr;
    State state = State::Created;
    H264PacketFormat h264_packet_format =
        H264PacketFormat::AvccLengthPrefixed;
    std::int64_t minimum_audio_timestamp_microseconds = 0;
#if defined(VRRECORDER_NATIVE_TESTING)
    LibavformatMuxerFailurePoint failure_point =
        LibavformatMuxerFailurePoint::None;
    LibavformatMuxerOperationCounts *operation_counts = nullptr;
#endif
};

LibavformatFragmentedMp4MuxerPort::
    LibavformatFragmentedMp4MuxerPort(std::unique_ptr<Impl> impl) noexcept
    : impl_(std::move(impl))
{
}

LibavformatFragmentedMp4MuxerPort::
    ~LibavformatFragmentedMp4MuxerPort()
{
    Abort();
}

LibavformatFragmentedMp4MuxerPortCreateResult
LibavformatFragmentedMp4MuxerPort::Create(
    const char *output_path_utf8) noexcept
{
    const RuntimeIdentity identity {
        avformat_version(),
        avcodec_version(),
        avutil_version(),
        av_version_info(),
    };
    return CreateWithRuntimeIdentity(
        output_path_utf8,
        identity
#if defined(VRRECORDER_NATIVE_TESTING)
        , LibavformatMuxerFailurePoint::None,
        nullptr
#endif
        );
}

#if defined(VRRECORDER_NATIVE_TESTING)
LibavformatFragmentedMp4MuxerPortCreateResult
LibavformatFragmentedMp4MuxerPort::CreateForTesting(
    const char *output_path_utf8,
    unsigned int avformat_version_value,
    unsigned int avcodec_version_value,
    unsigned int avutil_version_value,
    const char *release_version,
    LibavformatMuxerFailurePoint failure_point,
    LibavformatMuxerOperationCounts *operation_counts) noexcept
{
    const RuntimeIdentity identity {
        avformat_version_value,
        avcodec_version_value,
        avutil_version_value,
        release_version,
    };
    return CreateWithRuntimeIdentity(
        output_path_utf8,
        identity,
        failure_point,
        operation_counts);
}
#endif

LibavformatFragmentedMp4MuxerPortCreateResult
LibavformatFragmentedMp4MuxerPort::CreateWithRuntimeIdentity(
    const char *output_path_utf8,
    const RuntimeIdentity &runtime_identity
#if defined(VRRECORDER_NATIVE_TESTING)
    , LibavformatMuxerFailurePoint failure_point,
    LibavformatMuxerOperationCounts *operation_counts
#endif
    ) noexcept
{
    if (output_path_utf8 == nullptr || output_path_utf8[0] == '\0') {
        return {VRREC_STATUS_INVALID_ARGUMENT, nullptr};
    }
    if (runtime_identity.avformat_version != LIBAVFORMAT_VERSION_INT ||
        runtime_identity.avcodec_version != LIBAVCODEC_VERSION_INT ||
        runtime_identity.avutil_version != LIBAVUTIL_VERSION_INT ||
        runtime_identity.release_version == nullptr ||
        std::strcmp(
            runtime_identity.release_version,
            PinnedFfmpegVersion) != 0) {
        return {VRREC_STATUS_BACKEND_UNAVAILABLE, nullptr};
    }

    AVFormatContext *context = nullptr;
    const auto allocation_result = avformat_alloc_output_context2(
        &context,
        nullptr,
        "mp4",
        output_path_utf8);
    if (allocation_result < 0 || context == nullptr) {
        avformat_free_context(context);
        return {
            allocation_result < 0
                ? ErrorStatus(allocation_result)
                : VRREC_STATUS_OUT_OF_MEMORY,
            nullptr,
        };
    }
    if (context->oformat == nullptr || context->oformat->name == nullptr ||
        std::strcmp(context->oformat->name, "mp4") != 0 ||
        (context->oformat->flags & AVFMT_NOFILE) != 0) {
        avformat_free_context(context);
        return {VRREC_STATUS_BACKEND_UNAVAILABLE, nullptr};
    }

    const auto open_result = avio_open(
        &context->pb,
        output_path_utf8,
        AVIO_FLAG_WRITE);
    if (open_result < 0) {
        avformat_free_context(context);
        return {ErrorStatus(open_result), nullptr};
    }

    std::unique_ptr<Impl> impl(new (std::nothrow) Impl(
        context
#if defined(VRRECORDER_NATIVE_TESTING)
        , failure_point,
        operation_counts
#endif
        ));
    if (impl == nullptr) {
        static_cast<void>(avio_closep(&context->pb));
        avformat_free_context(context);
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr};
    }

    std::unique_ptr<LibavformatFragmentedMp4MuxerPort> port(
        new (std::nothrow) LibavformatFragmentedMp4MuxerPort(
            std::move(impl)));
    if (port == nullptr) {
        return {VRREC_STATUS_OUT_OF_MEMORY, nullptr};
    }
    return {VRREC_STATUS_OK, std::move(port)};
}

vrrec_status_t LibavformatFragmentedMp4MuxerPort::WriteHeader(
    const FragmentedMp4StreamConfiguration &configuration) noexcept
{
    if (impl_ == nullptr || impl_->state != Impl::State::Created ||
        impl_->context == nullptr || impl_->context->pb == nullptr) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (!IsConfigurationValid(configuration)) {
        impl_->state = Impl::State::Failed;
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    auto *video_stream = avformat_new_stream(impl_->context, nullptr);
    if (video_stream == nullptr || video_stream->codecpar == nullptr) {
        impl_->state = Impl::State::Failed;
        return VRREC_STATUS_OUT_OF_MEMORY;
    }
    video_stream->time_base = Rational(configuration.video.packet_time_base);
    video_stream->codecpar->codec_type = AVMEDIA_TYPE_VIDEO;
    video_stream->codecpar->codec_id = AV_CODEC_ID_H264;
    video_stream->codecpar->codec_tag = 0;
    video_stream->codecpar->width =
        static_cast<int>(configuration.video.width);
    video_stream->codecpar->height =
        static_cast<int>(configuration.video.height);
    video_stream->codecpar->profile =
        configuration.video.profile == H264Profile::High
        ? AV_PROFILE_H264_HIGH
        : AV_PROFILE_H264_MAIN;
    auto status = CopyExtradata(
        configuration.video.codec_extradata,
        *video_stream->codecpar);
    if (status != VRREC_STATUS_OK) {
        impl_->state = Impl::State::Failed;
        return status;
    }

    auto *audio_stream = avformat_new_stream(impl_->context, nullptr);
    if (audio_stream == nullptr || audio_stream->codecpar == nullptr) {
        impl_->state = Impl::State::Failed;
        return VRREC_STATUS_OUT_OF_MEMORY;
    }
    audio_stream->time_base = Rational(configuration.audio.packet_time_base);
    audio_stream->codecpar->codec_type = AVMEDIA_TYPE_AUDIO;
    audio_stream->codecpar->codec_id = AV_CODEC_ID_AAC;
    audio_stream->codecpar->codec_tag = 0;
    audio_stream->codecpar->profile = AV_PROFILE_AAC_LOW;
    audio_stream->codecpar->bit_rate = static_cast<std::int64_t>(
        configuration.audio.bitrate_bits_per_second);
    audio_stream->codecpar->sample_rate =
        static_cast<int>(configuration.audio.sample_rate);
    audio_stream->codecpar->frame_size =
        static_cast<int>(configuration.audio.frame_size);
    audio_stream->codecpar->initial_padding =
        static_cast<int>(configuration.audio.initial_padding_samples);
    const AVChannelLayout stereo = AV_CHANNEL_LAYOUT_STEREO;
    const auto layout_result = av_channel_layout_copy(
        &audio_stream->codecpar->ch_layout,
        &stereo);
    if (layout_result < 0) {
        impl_->state = Impl::State::Failed;
        return ErrorStatus(layout_result);
    }
    status = CopyExtradata(
        configuration.audio.codec_extradata,
        *audio_stream->codecpar);
    if (status != VRREC_STATUS_OK) {
        impl_->state = Impl::State::Failed;
        return status;
    }

    AVDictionary *options = nullptr;
    const char *movflags = configuration.fragment_policy.prefer_video_key_frames
        ? "+frag_keyframe+empty_moov+delay_moov+default_base_moof"
        : "+empty_moov+delay_moov+default_base_moof";
    auto option_result = av_dict_set(&options, "movflags", movflags, 0);
    if (option_result >= 0) {
        option_result = av_dict_set_int(
            &options,
            "frag_duration",
            configuration.fragment_policy.maximum_duration_microseconds,
            0);
    }
    if (option_result >= 0) {
        option_result = av_dict_set_int(
            &options,
            "min_frag_duration",
            configuration.fragment_policy.minimum_duration_microseconds,
            0);
    }
    if (option_result >= 0) {
        option_result = av_dict_set_int(&options, "use_editlist", 1, 0);
    }
    if (option_result >= 0) {
        option_result = av_dict_set(
            &options,
            "avoid_negative_ts",
            "disabled",
            0);
    }
    if (option_result < 0) {
        av_dict_free(&options);
        impl_->state = Impl::State::Failed;
        return ErrorStatus(option_result);
    }

    const auto header_result = impl_->FailWriteHeader()
        ? AVERROR(EIO)
        : avformat_write_header(impl_->context, &options);
    const auto unused_option_count = av_dict_count(options);
    av_dict_free(&options);
    if (header_result < 0 || unused_option_count != 0) {
        impl_->state = Impl::State::Failed;
        return header_result < 0
            ? ErrorStatus(header_result)
            : VRREC_STATUS_BACKEND_UNAVAILABLE;
    }

    if (impl_->context->nb_streams != 2 ||
        impl_->context->streams == nullptr ||
        impl_->context->streams[0] != video_stream ||
        impl_->context->streams[1] != audio_stream ||
        video_stream->index != 0 || audio_stream->index != 1 ||
        video_stream->codecpar == nullptr ||
        audio_stream->codecpar == nullptr ||
        video_stream->codecpar->codec_id != AV_CODEC_ID_H264 ||
        audio_stream->codecpar->codec_id != AV_CODEC_ID_AAC ||
        audio_stream->codecpar->bit_rate !=
            static_cast<std::int64_t>(
                configuration.audio.bitrate_bits_per_second) ||
        video_stream->time_base.num <= 0 ||
        video_stream->time_base.den <= 0 ||
        audio_stream->time_base.num <= 0 ||
        audio_stream->time_base.den <= 0) {
        impl_->state = Impl::State::Failed;
        return VRREC_STATUS_INTERNAL_ERROR;
    }

    impl_->video_stream = video_stream;
    impl_->audio_stream = audio_stream;
    impl_->h264_packet_format = configuration.video.packet_format;
    impl_->minimum_audio_timestamp_microseconds =
        AacPrimingLowerBoundMicroseconds(configuration.audio);
    impl_->state = Impl::State::Ready;
    return VRREC_STATUS_OK;
}

vrrec_status_t
LibavformatFragmentedMp4MuxerPort::GetActualStreamTimeBase(
    MediaStreamKind stream,
    MediaTimeBase &time_base) noexcept
{
    if (impl_ == nullptr || impl_->state != Impl::State::Ready) {
        return VRREC_STATUS_INVALID_STATE;
    }
    AVStream *selected = nullptr;
    if (stream == MediaStreamKind::Video) {
        selected = impl_->video_stream;
    } else if (stream == MediaStreamKind::Audio) {
        selected = impl_->audio_stream;
    } else {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }
    if (selected == nullptr || selected->time_base.num <= 0 ||
        selected->time_base.den <= 0) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
    time_base = {selected->time_base.num, selected->time_base.den};
    return VRREC_STATUS_OK;
}

void LibavformatFragmentedMp4MuxerPort::RescalePacketTimestamps(
    const FfmpegPacketTimestamps &source,
    MediaTimeBase source_time_base,
    MediaTimeBase destination_time_base,
    FfmpegPacketTimestamps &destination) noexcept
{
    destination = {
        UnknownMediaTimestamp,
        UnknownMediaTimestamp,
        0,
    };
    if (!IsTimeBaseValid(source_time_base) ||
        !IsTimeBaseValid(destination_time_base)) {
        return;
    }

    AVPacket packet {};
    packet.pts = AvTimestamp(source.pts);
    packet.dts = AvTimestamp(source.dts);
    packet.duration = source.duration;
    av_packet_rescale_ts(
        &packet,
        Rational(source_time_base),
        Rational(destination_time_base));
    destination = {
        MediaTimestamp(packet.pts),
        MediaTimestamp(packet.dts),
        packet.duration,
    };
}

vrrec_status_t
LibavformatFragmentedMp4MuxerPort::WriteInterleavedPacket(
    const EncodedMediaPacket &canonical_packet,
    const FfmpegPacketTimestamps &stream_timestamps) noexcept
{
    if (impl_ == nullptr || impl_->state != Impl::State::Ready ||
        impl_->context == nullptr) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (!IsPacketTimingValid(
            canonical_packet,
            impl_->minimum_audio_timestamp_microseconds) ||
        canonical_packet.payload.size() >
            static_cast<std::size_t>(std::numeric_limits<int>::max())) {
        impl_->state = Impl::State::Failed;
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    AVStream *stream = nullptr;
    if (canonical_packet.stream == MediaStreamKind::Video) {
        stream = impl_->video_stream;
        if (impl_->h264_packet_format !=
                H264PacketFormat::AvccLengthPrefixed ||
            !IsAvccPacket(
                canonical_packet.payload,
                canonical_packet.key_frame)) {
            impl_->state = Impl::State::Failed;
            return VRREC_STATUS_INVALID_ARGUMENT;
        }
    } else {
        stream = impl_->audio_stream;
        if (IsAdtsTransportPayload(canonical_packet.payload)) {
            impl_->state = Impl::State::Failed;
            return VRREC_STATUS_INVALID_ARGUMENT;
        }
    }
    if (stream == nullptr || stream->time_base.num <= 0 ||
        stream->time_base.den <= 0) {
        impl_->state = Impl::State::Failed;
        return VRREC_STATUS_INTERNAL_ERROR;
    }

    FfmpegPacketTimestamps expected_timestamps {};
    RescalePacketTimestamps(
        {
            canonical_packet.pts_microseconds,
            canonical_packet.dts_microseconds,
            canonical_packet.duration_microseconds,
        },
        MicrosecondPacketTimeBase,
        {stream->time_base.num, stream->time_base.den},
        expected_timestamps);
    if (expected_timestamps != stream_timestamps ||
        stream_timestamps.pts == UnknownMediaTimestamp ||
        stream_timestamps.dts == UnknownMediaTimestamp ||
        stream_timestamps.pts < stream_timestamps.dts ||
        stream_timestamps.duration <= 0) {
        impl_->state = Impl::State::Failed;
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    AVPacket *packet = av_packet_alloc();
    if (packet == nullptr) {
        impl_->state = Impl::State::Failed;
        return VRREC_STATUS_OUT_OF_MEMORY;
    }
    const auto allocation_result = av_new_packet(
        packet,
        static_cast<int>(canonical_packet.payload.size()));
    if (allocation_result < 0) {
        av_packet_free(&packet);
        impl_->state = Impl::State::Failed;
        return ErrorStatus(allocation_result);
    }
    std::memcpy(
        packet->data,
        canonical_packet.payload.data(),
        canonical_packet.payload.size());
    packet->pts = AvTimestamp(canonical_packet.pts_microseconds);
    packet->dts = AvTimestamp(canonical_packet.dts_microseconds);
    packet->duration = canonical_packet.duration_microseconds;
    packet->stream_index = stream->index;
    packet->pos = -1;
    if (canonical_packet.key_frame) {
        packet->flags |= AV_PKT_FLAG_KEY;
    }
    av_packet_rescale_ts(
        packet,
        AV_TIME_BASE_Q,
        stream->time_base);

    if (impl_->FailWritePacket()) {
        packet->stream_index =
            static_cast<int>(impl_->context->nb_streams);
    }
#if defined(VRRECORDER_NATIVE_TESTING)
    if (impl_->operation_counts != nullptr) {
        ++impl_->operation_counts->write_packet_calls;
    }
#endif
    auto write_result =
        av_interleaved_write_frame(impl_->context, packet);
    if (impl_->FailWritePacket() && write_result >= 0) {
        write_result = AVERROR(EIO);
    }
    const auto packet_was_consumed = packet->buf == nullptr &&
        packet->data == nullptr && packet->size == 0 &&
        packet->side_data == nullptr && packet->side_data_elems == 0 &&
        packet->pts == AV_NOPTS_VALUE && packet->dts == AV_NOPTS_VALUE &&
        packet->duration == 0;
    av_packet_free(&packet);
    if (write_result < 0 || !packet_was_consumed) {
        impl_->state = Impl::State::Failed;
        return write_result < 0
            ? ErrorStatus(write_result)
            : VRREC_STATUS_INTERNAL_ERROR;
    }
    return VRREC_STATUS_OK;
}

vrrec_status_t LibavformatFragmentedMp4MuxerPort::WriteTrailer() noexcept
{
    if (impl_ == nullptr || impl_->state != Impl::State::Ready ||
        impl_->context == nullptr) {
        return VRREC_STATUS_INVALID_STATE;
    }
    const auto trailer_result = impl_->FailWriteTrailer()
        ? AVERROR(EIO)
        : av_write_trailer(impl_->context);
    if (trailer_result < 0) {
        impl_->state = Impl::State::Failed;
        return ErrorStatus(trailer_result);
    }
    impl_->state = Impl::State::TrailerWritten;
    return VRREC_STATUS_OK;
}

vrrec_status_t LibavformatFragmentedMp4MuxerPort::FlushFile() noexcept
{
    if (impl_ == nullptr || impl_->state != Impl::State::TrailerWritten ||
        impl_->context == nullptr || impl_->context->pb == nullptr) {
        return VRREC_STATUS_INVALID_STATE;
    }

    avio_flush(impl_->context->pb);
#if defined(VRRECORDER_NATIVE_TESTING)
    if (impl_->operation_counts != nullptr) {
        ++impl_->operation_counts->flush_file_calls;
    }
#endif
    auto flush_result = impl_->context->pb->error;
    if (impl_->FailFlushFile()) {
        flush_result = AVERROR(EIO);
    }
    auto close_result = impl_->CloseOutput();
    if (impl_->FailCloseFile()) {
        close_result = AVERROR(EIO);
    }
    avformat_free_context(impl_->context);
    impl_->context = nullptr;
    impl_->video_stream = nullptr;
    impl_->audio_stream = nullptr;

    if (flush_result < 0 || close_result < 0) {
        impl_->state = Impl::State::Failed;
        return flush_result < 0
            ? ErrorStatus(flush_result)
            : ErrorStatus(close_result);
    }
    impl_->state = Impl::State::Finished;
    return VRREC_STATUS_OK;
}

void LibavformatFragmentedMp4MuxerPort::Abort() noexcept
{
    if (impl_ == nullptr || impl_->state == Impl::State::Finished ||
        impl_->state == Impl::State::Aborted) {
        return;
    }
    impl_->Release();
    impl_->state = Impl::State::Aborted;
}

}
