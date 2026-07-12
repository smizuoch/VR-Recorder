#ifndef VRRECORDER_NATIVE_FRAGMENTED_MP4_MUX_COORDINATOR_HPP
#define VRRECORDER_NATIVE_FRAGMENTED_MP4_MUX_COORDINATOR_HPP

#include <cstddef>
#include <cstdint>
#include <mutex>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

enum class MediaStreamKind {
    Video,
    Audio,
};

struct EncodedMediaPacket final {
    MediaStreamKind stream;
    std::int64_t pts_microseconds;
    std::int64_t dts_microseconds;
    std::int64_t duration_microseconds;
    bool key_frame;
    std::size_t payload_size;
};

class FragmentedMp4Muxer {
public:
    virtual ~FragmentedMp4Muxer() = default;

    virtual vrrec_status_t WritePacket(
        const EncodedMediaPacket &packet) noexcept = 0;
    virtual vrrec_status_t EndFragment() noexcept = 0;
    virtual vrrec_status_t WriteTrailer() noexcept = 0;
    virtual vrrec_status_t FlushFile() noexcept = 0;
    virtual void Abort() noexcept = 0;
};

enum class Mp4MuxResult {
    Written,
    InvalidPacket,
    InvalidState,
    MuxFailed,
};

class FragmentedMp4MuxCoordinator final {
public:
    explicit FragmentedMp4MuxCoordinator(
        FragmentedMp4Muxer &muxer) noexcept;
    ~FragmentedMp4MuxCoordinator();

    FragmentedMp4MuxCoordinator(
        const FragmentedMp4MuxCoordinator &) = delete;
    FragmentedMp4MuxCoordinator &operator=(
        const FragmentedMp4MuxCoordinator &) = delete;

    Mp4MuxResult Submit(const EncodedMediaPacket &packet) noexcept;
    vrrec_status_t Finish() noexcept;
    void Abort() noexcept;

private:
    bool IsPacketValid(const EncodedMediaPacket &packet) const noexcept;
    void AbortLocked() noexcept;

    FragmentedMp4Muxer &muxer_;
    std::mutex mutex_;
    std::int64_t last_video_dts_ = 0;
    std::int64_t last_audio_dts_ = 0;
    std::int64_t fragment_start_dts_ = 0;
    bool has_video_dts_ = false;
    bool has_audio_dts_ = false;
    bool has_fragment_ = false;
    bool terminal_ = false;
    bool aborted_ = false;
};

}

#endif
