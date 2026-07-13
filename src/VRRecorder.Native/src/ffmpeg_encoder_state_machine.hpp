#ifndef VRRECORDER_NATIVE_FFMPEG_ENCODER_STATE_MACHINE_HPP
#define VRRECORDER_NATIVE_FFMPEG_ENCODER_STATE_MACHINE_HPP

#include <cstddef>
#include <cstdint>
#include <mutex>
#include <vector>

#include "fragmented_mp4_mux_coordinator.hpp"
#include "vrrecorder_native.h"

namespace vrrecorder::native {

enum class FfmpegCodecIoState {
    Ok,
    Again,
    EndOfStream,
    Failed,
};

struct FfmpegCodecIoResult final {
    FfmpegCodecIoState state;
    vrrec_status_t failure_status;
};

enum class FfmpegReceivedPacketSideDataKind {
    SkipSamples,
    Unsupported,
};

struct FfmpegReceivedPacketSideDataView final {
    FfmpegReceivedPacketSideDataKind kind =
        FfmpegReceivedPacketSideDataKind::Unsupported;
    const std::byte *data = nullptr;
    int size = 0;
};

struct FfmpegReceivedPacketView final {
    const std::byte *data = nullptr;
    int size = 0;
    std::int64_t pts_microseconds = UnknownMediaTimestamp;
    std::int64_t dts_microseconds = UnknownMediaTimestamp;
    std::int64_t duration_microseconds = 0;
    bool key_frame = false;
    bool corrupt = false;
    const FfmpegReceivedPacketSideDataView *side_data = nullptr;
    std::size_t side_data_count = 0;
};

class FfmpegEncoderPort {
public:
    virtual ~FfmpegEncoderPort() = default;

    virtual FfmpegCodecIoResult SendPreparedFrame() noexcept = 0;
    virtual FfmpegCodecIoResult SendDrain() noexcept = 0;
    virtual FfmpegCodecIoResult ReceivePacket(
        FfmpegReceivedPacketView &packet) noexcept = 0;
    virtual void UnrefReceivedPacket() noexcept = 0;
    virtual void Abort() noexcept = 0;
};

struct FfmpegEncodeBatch final {
    vrrec_status_t status = VRREC_STATUS_OK;
    std::vector<EncodedMediaPacket> packets;
};

class FfmpegEncoderStateMachine final {
public:
    FfmpegEncoderStateMachine(
        FfmpegEncoderPort &port,
        MediaStreamKind stream) noexcept;
    ~FfmpegEncoderStateMachine();

    FfmpegEncoderStateMachine(
        const FfmpegEncoderStateMachine &) = delete;
    FfmpegEncoderStateMachine &operator=(
        const FfmpegEncoderStateMachine &) = delete;

    FfmpegEncodeBatch EncodePreparedFrame() noexcept;
    FfmpegEncodeBatch Finish() noexcept;
    void Abort() noexcept;

private:
    enum class State {
        Active,
        Draining,
        Finished,
        Aborted,
    };

    enum class ReceiveBoundary {
        Again,
        EndOfStream,
        Failed,
    };

    struct ReceiveResult final {
        ReceiveBoundary boundary;
        vrrec_status_t status;
        std::size_t received_packet_count;
    };

    ReceiveResult ReceiveAvailable(
        std::vector<EncodedMediaPacket> &packets) noexcept;
    vrrec_status_t AppendPacket(
        const FfmpegReceivedPacketView &packet,
        std::vector<EncodedMediaPacket> &packets) noexcept;
    FfmpegEncodeBatch FailLocked(
        vrrec_status_t status,
        std::vector<EncodedMediaPacket> &packets) noexcept;
    static vrrec_status_t FailureStatus(
        const FfmpegCodecIoResult &result) noexcept;
    void AbortLocked() noexcept;

    FfmpegEncoderPort &port_;
    MediaStreamKind stream_;
    std::mutex mutex_;
    State state_ = State::Active;
    bool port_aborted_ = false;
};

}

#endif
