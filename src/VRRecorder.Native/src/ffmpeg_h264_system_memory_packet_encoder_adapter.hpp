#ifndef VRRECORDER_NATIVE_FFMPEG_H264_SYSTEM_MEMORY_PACKET_ENCODER_ADAPTER_HPP
#define VRRECORDER_NATIVE_FFMPEG_H264_SYSTEM_MEMORY_PACKET_ENCODER_ADAPTER_HPP

#include <atomic>
#include <cstdint>
#include <memory>

#include "ffmpeg_h264_packet_encoder.hpp"
#include "muxing_video_encoder_sink.hpp"

namespace vrrecorder::native {

class SystemMemoryNv12FrameMapping {
public:
    virtual ~SystemMemoryNv12FrameMapping() = default;
    virtual SystemMemoryNv12FrameView View() const noexcept = 0;
};

struct SystemMemoryNv12FrameMapResult final {
    vrrec_status_t status = VRREC_STATUS_INTERNAL_ERROR;
    std::unique_ptr<SystemMemoryNv12FrameMapping> mapping;
};

class SystemMemoryNv12FrameMapper {
public:
    virtual ~SystemMemoryNv12FrameMapper() = default;
    virtual SystemMemoryNv12FrameMapResult Map(
        const ScheduledVideoFrame &frame) noexcept = 0;
    virtual void Abort() noexcept = 0;
};

class FfmpegH264SystemMemoryPacketEncoderAdapter final
    : public PacketVideoEncoder {
public:
    FfmpegH264SystemMemoryPacketEncoderAdapter(
        FfmpegH264PacketEncoder &encoder,
        SystemMemoryNv12FrameMapper &mapper,
        std::uint32_t frames_per_second) noexcept;

    PacketVideoEncoderWrite Encode(
        const ScheduledVideoFrame &frame) noexcept override;
    PacketVideoEncoderWrite Finish() noexcept override;
    void Abort() noexcept override;

private:
    FfmpegH264PacketEncoder &encoder_;
    SystemMemoryNv12FrameMapper &mapper_;
    std::uint32_t frames_per_second_;
    std::atomic_bool aborted_ = false;
    std::atomic_bool finished_ = false;
};

}

#endif
