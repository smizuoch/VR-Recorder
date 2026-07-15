#ifndef VRRECORDER_NATIVE_SPOUT2_RECEIVER_PORT_HPP
#define VRRECORDER_NATIVE_SPOUT2_RECEIVER_PORT_HPP

#include <chrono>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

#include "spout_source_backend.hpp"

namespace vrrecorder::native {

enum class Spout2ReceiverResult {
    FrameReady,
    Timeout,
    SenderLost,
    Aborted,
    OutOfMemory,
    Failed,
};

struct Spout2TextureMetadata final {
    std::string sender_id;
    std::uint64_t resource_identity;
    std::uint64_t receiver_epoch;
    std::uint64_t adapter_luid;
    std::string gpu_identity;
    vrrec_gpu_vendor_t gpu_vendor;
    std::uint32_t width;
    std::uint32_t height;
    vrrec_source_pixel_format_t pixel_format;
    double estimated_source_fps;
    std::uint64_t frame_sequence;
    std::int64_t monotonic_timestamp_microseconds;
};

class Spout2ReceiverPort {
public:
    virtual ~Spout2ReceiverPort() = default;

    virtual vrrec_status_t Snapshot(
        std::vector<SpoutSenderSnapshot> &senders) = 0;
    virtual Spout2ReceiverResult Receive(
        std::chrono::milliseconds timeout,
        Spout2TextureMetadata &metadata) noexcept = 0;
    virtual vrrec_status_t CopySurface(
        const Spout2TextureMetadata &metadata,
        std::uint64_t generation_id,
        std::shared_ptr<VideoSurface> &surface) noexcept = 0;
    virtual void Abort() noexcept = 0;
};

}

#endif
