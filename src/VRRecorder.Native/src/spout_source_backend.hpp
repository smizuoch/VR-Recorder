#ifndef VRRECORDER_NATIVE_SPOUT_SOURCE_BACKEND_HPP
#define VRRECORDER_NATIVE_SPOUT_SOURCE_BACKEND_HPP

#include <chrono>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

struct SpoutSenderSnapshot {
    std::string sender_id;
    std::uint64_t latest_frame_generation;
};

struct SpoutFrame {
    std::string sender_id;
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

class SpoutSourceBackend {
public:
    virtual ~SpoutSourceBackend() = default;

    virtual vrrec_status_t Snapshot(
        std::vector<SpoutSenderSnapshot> &senders) = 0;
    virtual vrrec_status_t Poll(
        std::chrono::milliseconds timeout,
        SpoutFrame &frame) = 0;
    virtual void Abort() noexcept
    {
    }
};

std::unique_ptr<SpoutSourceBackend> CreateSpoutSourceBackend(
    const vrrec_spout_source_config_v1 &config,
    vrrec_status_t &status);

}

#endif
