#ifndef VRRECORDER_NATIVE_MEDIA_BACKEND_HPP
#define VRRECORDER_NATIVE_MEDIA_BACKEND_HPP

#include <cstdint>
#include <memory>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

enum class AudioEndpointRole {
    Desktop,
    Microphone,
};

class MediaEventSink {
public:
    virtual ~MediaEventSink() = default;

    virtual void FirstVideoPacketMuxed() noexcept = 0;
    virtual void Stopped(
        std::uint64_t video_packet_count,
        std::uint64_t audio_packet_count) noexcept = 0;
    virtual void Faulted(
        vrrec_status_t status,
        const char *message_utf8) noexcept = 0;
    virtual void AudioEndpointAvailabilityChanged(
        AudioEndpointRole role,
        bool available,
        std::uint64_t frame_position) noexcept = 0;
};

class MediaBackend {
public:
    virtual ~MediaBackend() = default;

    virtual vrrec_status_t Start() noexcept = 0;
    virtual vrrec_status_t UpdateVideoLayout(
        const vrrec_video_layout_v1 &layout) noexcept = 0;
    virtual vrrec_status_t GetStatistics(
        vrrec_session_statistics_v1 &statistics) noexcept = 0;
    virtual vrrec_status_t RequestStop() noexcept = 0;
    virtual void Abort() noexcept = 0;
};

std::unique_ptr<MediaBackend> CreateMediaBackend(
    const vrrec_session_config_v1 &config,
    MediaEventSink &events,
    vrrec_status_t &status);

}

#endif
