#ifndef VRRECORDER_NATIVE_FFMPEG_LIBAVFORMAT_FRAGMENTED_MP4_MUXER_PORT_HPP
#define VRRECORDER_NATIVE_FFMPEG_LIBAVFORMAT_FRAGMENTED_MP4_MUXER_PORT_HPP

#include <memory>

#include "ffmpeg_fragmented_mp4_muxer.hpp"

namespace vrrecorder::native {

struct LibavformatFragmentedMp4MuxerPortCreateResult;

#if defined(VRRECORDER_NATIVE_TESTING)
enum class LibavformatMuxerFailurePoint {
    None,
    WriteHeader,
    WritePacket,
    WriteTrailer,
    FlushFile,
    CloseFile,
    FlushAndCloseFile,
};

struct LibavformatMuxerOperationCounts final {
    unsigned int write_packet_calls = 0;
    unsigned int flush_file_calls = 0;
    unsigned int close_file_calls = 0;
};
#endif

class LibavformatFragmentedMp4MuxerPort final : public FfmpegMuxerPort {
public:
    ~LibavformatFragmentedMp4MuxerPort() override;

    LibavformatFragmentedMp4MuxerPort(
        const LibavformatFragmentedMp4MuxerPort &) = delete;
    LibavformatFragmentedMp4MuxerPort &operator=(
        const LibavformatFragmentedMp4MuxerPort &) = delete;
    LibavformatFragmentedMp4MuxerPort(
        LibavformatFragmentedMp4MuxerPort &&) = delete;
    LibavformatFragmentedMp4MuxerPort &operator=(
        LibavformatFragmentedMp4MuxerPort &&) = delete;

    // The port owns the output AVIO handle from Create until FlushFile or
    // Abort. The path is opened only after the exact runtime identity passes.
    static LibavformatFragmentedMp4MuxerPortCreateResult Create(
        const char *output_path_utf8) noexcept;

#if defined(VRRECORDER_NATIVE_TESTING)
    static LibavformatFragmentedMp4MuxerPortCreateResult CreateForTesting(
        const char *output_path_utf8,
        unsigned int avformat_version,
        unsigned int avcodec_version,
        unsigned int avutil_version,
        const char *release_version,
        LibavformatMuxerFailurePoint failure_point =
            LibavformatMuxerFailurePoint::None,
        LibavformatMuxerOperationCounts *operation_counts = nullptr)
        noexcept;
#endif

    vrrec_status_t WriteHeader(
        const FragmentedMp4StreamConfiguration &configuration)
        noexcept override;
    vrrec_status_t GetActualStreamTimeBase(
        MediaStreamKind stream,
        MediaTimeBase &time_base) noexcept override;
    void RescalePacketTimestamps(
        const FfmpegPacketTimestamps &source,
        MediaTimeBase source_time_base,
        MediaTimeBase destination_time_base,
        FfmpegPacketTimestamps &destination) noexcept override;
    vrrec_status_t WriteInterleavedPacket(
        const EncodedMediaPacket &canonical_packet,
        const FfmpegPacketTimestamps &stream_timestamps)
        noexcept override;
    vrrec_status_t WriteTrailer() noexcept override;
    vrrec_status_t FlushFile() noexcept override;
    void Abort() noexcept override;

private:
    class Impl;
    struct RuntimeIdentity;

    explicit LibavformatFragmentedMp4MuxerPort(
        std::unique_ptr<Impl> impl) noexcept;
    static LibavformatFragmentedMp4MuxerPortCreateResult
        CreateWithRuntimeIdentity(
            const char *output_path_utf8,
            const RuntimeIdentity &runtime_identity
#if defined(VRRECORDER_NATIVE_TESTING)
            , LibavformatMuxerFailurePoint failure_point,
            LibavformatMuxerOperationCounts *operation_counts
#endif
            ) noexcept;

    std::unique_ptr<Impl> impl_;
};

struct LibavformatFragmentedMp4MuxerPortCreateResult final {
    vrrec_status_t status = VRREC_STATUS_INTERNAL_ERROR;
    std::unique_ptr<LibavformatFragmentedMp4MuxerPort> port;
};

}

#endif
