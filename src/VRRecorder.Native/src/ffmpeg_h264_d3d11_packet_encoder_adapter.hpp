#ifndef VRRECORDER_NATIVE_FFMPEG_H264_D3D11_PACKET_ENCODER_ADAPTER_HPP
#define VRRECORDER_NATIVE_FFMPEG_H264_D3D11_PACKET_ENCODER_ADAPTER_HPP

#include <atomic>
#include <memory>
#include <mutex>

#include "ffmpeg_h264_packet_encoder.hpp"
#include "muxing_video_encoder_sink.hpp"
#include "production_video_encoder_route.hpp"

namespace vrrecorder::native {

class FfmpegH264D3d11PacketEncoderAdapter final
    : public PacketVideoEncoder {
public:
    FfmpegH264D3d11PacketEncoderAdapter(
        ProductionVideoEncoderRoute route,
        H264VideoEncoderConfig config) noexcept;

    PacketVideoEncoderWrite Encode(
        const ScheduledVideoFrame &frame) noexcept override;
    PacketVideoEncoderWrite Finish() noexcept override;
    void Abort() noexcept override;

private:
    vrrec_status_t EnsureEncoder(
        const std::shared_ptr<VideoSurface> &surface) noexcept;
    PacketVideoEncoderWrite Complete(
        FfmpegH264PacketEncoderWrite encoded,
        std::uint64_t latency) noexcept;

    ProductionVideoEncoderRoute route_;
    H264VideoEncoderConfig config_;
    std::unique_ptr<FfmpegH264PacketEncoder> encoder_;
    std::mutex mutex_;
    bool descriptor_published_ = false;
    bool finished_ = false;
    std::atomic_bool aborted_ = false;
};

}

#endif
