#ifndef VRRECORDER_NATIVE_WINDOWS_MEDIA_FOUNDATION_H264_DECODE_PORT_HPP
#define VRRECORDER_NATIVE_WINDOWS_MEDIA_FOUNDATION_H264_DECODE_PORT_HPP

#if !defined(_WIN32)
#error "The Media Foundation H.264 decode Port requires Windows"
#endif

#include <mftransform.h>

#include <cstddef>
#include <cstdint>
#include <span>

#include "annex_b_encoder_probe_decoder.hpp"

namespace vrrecorder::native {

class WindowsMediaFoundationH264DecodePort final
    : public AnnexBH264DecodePort {
public:
    WindowsMediaFoundationH264DecodePort() noexcept = default;
    ~WindowsMediaFoundationH264DecodePort() override;

    WindowsMediaFoundationH264DecodePort(
        const WindowsMediaFoundationH264DecodePort &) = delete;
    WindowsMediaFoundationH264DecodePort &operator=(
        const WindowsMediaFoundationH264DecodePort &) = delete;

    vrrec_status_t Begin(
        std::uint32_t width,
        std::uint32_t height) noexcept override;
    vrrec_status_t Submit(
        std::span<const std::byte> access_unit,
        std::int64_t pts_microseconds,
        std::int64_t duration_microseconds) noexcept override;
    EncoderProbeDecodeResult Finish() noexcept override;
    void Abort() noexcept override;

private:
    vrrec_status_t ConfigureOutputType() noexcept;
    vrrec_status_t ProcessAvailableOutput() noexcept;
    vrrec_status_t SubmitSample(IMFSample &sample) noexcept;
    void ReleaseResources(bool notify_transform) noexcept;

    IMFTransform *transform_ = nullptr;
    std::uint32_t width_ = 0;
    std::uint32_t height_ = 0;
    std::uint32_t decoded_frame_count_ = 0;
    std::int64_t presentation_start_microseconds_ = 0;
    bool has_presentation_start_ = false;
    bool com_uninitialize_required_ = false;
    bool media_foundation_started_ = false;
    bool active_ = false;
};

}

#endif
