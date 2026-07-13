#ifndef VRRECORDER_NATIVE_FFMPEG_LIBAVCODEC_ENCODER_PORT_HPP
#define VRRECORDER_NATIVE_FFMPEG_LIBAVCODEC_ENCODER_PORT_HPP

#include <memory>

#include "ffmpeg_encoder_state_machine.hpp"

struct AVCodecContext;
struct AVFrame;

namespace vrrecorder::native {

struct LibavcodecEncoderPortCreateResult;

class LibavcodecEncoderPort final : public FfmpegEncoderPort {
public:
    ~LibavcodecEncoderPort() override;

    LibavcodecEncoderPort(const LibavcodecEncoderPort &) = delete;
    LibavcodecEncoderPort &operator=(const LibavcodecEncoderPort &) = delete;
    LibavcodecEncoderPort(LibavcodecEncoderPort &&) = delete;
    LibavcodecEncoderPort &operator=(LibavcodecEncoderPort &&) = delete;

    // The context must already be open for encoding. Create consumes the
    // context on every path, including validation or allocation failure.
    static LibavcodecEncoderPortCreateResult Create(
        AVCodecContext *opened_context) noexcept;

#if defined(VRRECORDER_NATIVE_TESTING)
    static LibavcodecEncoderPortCreateResult CreateForTesting(
        AVCodecContext *opened_context,
        unsigned int avcodec_version,
        unsigned int avutil_version,
        const char *release_version) noexcept;
#endif

    // Takes a reference to the frame. The caller may immediately unref or free
    // its AVFrame after this call succeeds.
    vrrec_status_t PrepareFrame(const AVFrame &frame) noexcept;

    FfmpegCodecIoResult SendPreparedFrame() noexcept override;
    FfmpegCodecIoResult SendDrain() noexcept override;
    FfmpegCodecIoResult ReceivePacket(
        FfmpegReceivedPacketView &packet) noexcept override;
    void UnrefReceivedPacket() noexcept override;
    void Abort() noexcept override;

private:
    class Impl;
    struct RuntimeIdentity;

    explicit LibavcodecEncoderPort(std::unique_ptr<Impl> impl) noexcept;
    static LibavcodecEncoderPortCreateResult CreateWithRuntimeIdentity(
        AVCodecContext *opened_context,
        const RuntimeIdentity &runtime_identity) noexcept;

    std::unique_ptr<Impl> impl_;
};

struct LibavcodecEncoderPortCreateResult final {
    vrrec_status_t status = VRREC_STATUS_INTERNAL_ERROR;
    std::unique_ptr<LibavcodecEncoderPort> port;
};

}

#endif
