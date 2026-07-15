#ifndef VRRECORDER_NATIVE_WASAPI_AUDIO_CAPTURE_PORT_HPP
#define VRRECORDER_NATIVE_WASAPI_AUDIO_CAPTURE_PORT_HPP

#include <cstddef>
#include <cstdint>
#include <span>

#include "audio_capture_normalizer.hpp"

namespace vrrecorder::native {

enum class WasapiCapturePortResult {
    Ok,
    Empty,
    DeviceLost,
    Aborted,
    BackendUnavailable,
    OutOfMemory,
    InvalidArgument,
    Failed,
};

struct WasapiCapturePacket final {
    std::uint64_t device_position = 0;
    std::uint64_t qpc_100ns = 0;
    std::uint32_t frame_count = 0;
    std::span<const std::byte> bytes {};
    bool silent = false;
    bool discontinuity = false;
    bool timestamp_error = false;
};

class WasapiCapturePort {
public:
    virtual ~WasapiCapturePort() = default;

    virtual WasapiCapturePortResult Start(
        const AudioCaptureSourceConfig &config,
        CapturePcmFormat &format) noexcept = 0;
    virtual WasapiCapturePortResult Acquire(
        WasapiCapturePacket &packet) noexcept = 0;
    virtual WasapiCapturePortResult Release(
        std::uint32_t frame_count) noexcept = 0;
    virtual void Abort() noexcept = 0;
    virtual void Close() noexcept = 0;
};

}

#endif
