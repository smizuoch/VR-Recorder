#ifndef VRRECORDER_NATIVE_AUDIO_CAPTURE_SOURCE_HPP
#define VRRECORDER_NATIVE_AUDIO_CAPTURE_SOURCE_HPP

#include <cstddef>
#include <cstdint>
#include <span>
#include <string>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

enum class AudioCaptureRole {
    DesktopLoopback,
    Microphone,
};

struct AudioCaptureSourceConfig final {
    AudioCaptureRole role;
    std::string endpoint_id_utf8;
    std::int64_t session_start_qpc_100ns;
};

struct CapturedStereoPacket48k final {
    std::uint64_t start_frame_48k = 0;
    std::uint64_t device_position = 0;
    std::int64_t qpc_100ns = 0;
    std::size_t frame_count_48k = 0;
    std::span<const float> interleaved_samples {};
    bool silent = false;
    bool discontinuity = false;
};

enum class AudioCaptureReadResult {
    Packet,
    DeviceLost,
    Aborted,
    Failed,
};

struct AudioCaptureRead final {
    AudioCaptureReadResult result = AudioCaptureReadResult::Failed;
    CapturedStereoPacket48k packet {};
    std::uint64_t effective_frame_48k = 0;
};

class AudioCaptureSource {
public:
    virtual ~AudioCaptureSource() = default;

    virtual vrrec_status_t Start(
        const AudioCaptureSourceConfig &config) noexcept = 0;
    virtual AudioCaptureRead Read() noexcept = 0;
    virtual void Abort() noexcept = 0;
};

}

#endif
