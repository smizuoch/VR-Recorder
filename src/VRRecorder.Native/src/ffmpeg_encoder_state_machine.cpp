#include "ffmpeg_encoder_state_machine.hpp"

#include <limits>
#include <new>
#include <utility>

namespace vrrecorder::native {

namespace {

class ReceivedPacketUnrefGuard final {
public:
    explicit ReceivedPacketUnrefGuard(FfmpegEncoderPort &port) noexcept
        : port_(port)
    {
    }

    ~ReceivedPacketUnrefGuard()
    {
        port_.UnrefReceivedPacket();
    }

    ReceivedPacketUnrefGuard(const ReceivedPacketUnrefGuard &) = delete;
    ReceivedPacketUnrefGuard &operator=(
        const ReceivedPacketUnrefGuard &) = delete;

private:
    FfmpegEncoderPort &port_;
};

}

FfmpegEncoderStateMachine::FfmpegEncoderStateMachine(
    FfmpegEncoderPort &port,
    MediaStreamKind stream) noexcept
    : port_(port),
      stream_(stream)
{
}

FfmpegEncoderStateMachine::~FfmpegEncoderStateMachine()
{
    const std::lock_guard lock(mutex_);
    if (state_ != State::Finished) {
        AbortLocked();
    }
}

FfmpegEncodeBatch FfmpegEncoderStateMachine::EncodePreparedFrame() noexcept
{
    const std::lock_guard lock(mutex_);
    if (state_ != State::Active) {
        return {VRREC_STATUS_INVALID_STATE, {}};
    }

    std::vector<EncodedMediaPacket> packets;
    if (stream_ != MediaStreamKind::Video &&
        stream_ != MediaStreamKind::Audio) {
        return FailLocked(VRREC_STATUS_INVALID_ARGUMENT, packets);
    }
    auto send = port_.SendPreparedFrame();
    if (send.state == FfmpegCodecIoState::Again) {
        const auto pending = ReceiveAvailable(packets);
        if (pending.boundary != ReceiveBoundary::Again ||
            pending.received_packet_count == 0) {
            const auto status = pending.boundary == ReceiveBoundary::Failed
                ? pending.status
                : VRREC_STATUS_INTERNAL_ERROR;
            return FailLocked(status, packets);
        }

        send = port_.SendPreparedFrame();
        if (send.state != FfmpegCodecIoState::Ok) {
            const auto status = send.state == FfmpegCodecIoState::Failed
                ? FailureStatus(send)
                : VRREC_STATUS_INTERNAL_ERROR;
            return FailLocked(status, packets);
        }
    } else if (send.state != FfmpegCodecIoState::Ok) {
        const auto status = send.state == FfmpegCodecIoState::Failed
            ? FailureStatus(send)
            : VRREC_STATUS_INTERNAL_ERROR;
        return FailLocked(status, packets);
    }

    const auto output = ReceiveAvailable(packets);
    if (output.boundary != ReceiveBoundary::Again) {
        const auto status = output.boundary == ReceiveBoundary::Failed
            ? output.status
            : VRREC_STATUS_INTERNAL_ERROR;
        return FailLocked(status, packets);
    }

    return {VRREC_STATUS_OK, std::move(packets)};
}

FfmpegEncodeBatch FfmpegEncoderStateMachine::Finish() noexcept
{
    const std::lock_guard lock(mutex_);
    if (state_ != State::Active) {
        return {VRREC_STATUS_INVALID_STATE, {}};
    }

    std::vector<EncodedMediaPacket> packets;
    if (stream_ != MediaStreamKind::Video &&
        stream_ != MediaStreamKind::Audio) {
        return FailLocked(VRREC_STATUS_INVALID_ARGUMENT, packets);
    }
    auto send = port_.SendDrain();
    if (send.state == FfmpegCodecIoState::Again) {
        const auto pending = ReceiveAvailable(packets);
        if (pending.boundary != ReceiveBoundary::Again ||
            pending.received_packet_count == 0) {
            const auto status = pending.boundary == ReceiveBoundary::Failed
                ? pending.status
                : VRREC_STATUS_INTERNAL_ERROR;
            return FailLocked(status, packets);
        }

        send = port_.SendDrain();
        if (send.state != FfmpegCodecIoState::Ok) {
            const auto status = send.state == FfmpegCodecIoState::Failed
                ? FailureStatus(send)
                : VRREC_STATUS_INTERNAL_ERROR;
            return FailLocked(status, packets);
        }
    } else if (send.state != FfmpegCodecIoState::Ok) {
        const auto status = send.state == FfmpegCodecIoState::Failed
            ? FailureStatus(send)
            : VRREC_STATUS_INTERNAL_ERROR;
        return FailLocked(status, packets);
    }

    state_ = State::Draining;
    const auto output = ReceiveAvailable(packets);
    if (output.boundary != ReceiveBoundary::EndOfStream) {
        const auto status = output.boundary == ReceiveBoundary::Failed
            ? output.status
            : VRREC_STATUS_INTERNAL_ERROR;
        return FailLocked(status, packets);
    }

    state_ = State::Finished;
    return {VRREC_STATUS_OK, std::move(packets)};
}

void FfmpegEncoderStateMachine::Abort() noexcept
{
    const std::lock_guard lock(mutex_);
    AbortLocked();
}

FfmpegEncoderStateMachine::ReceiveResult
FfmpegEncoderStateMachine::ReceiveAvailable(
    std::vector<EncodedMediaPacket> &packets) noexcept
{
    std::size_t received_packet_count = 0;
    for (;;) {
        FfmpegReceivedPacketView packet {};
        const auto receive = port_.ReceivePacket(packet);
        switch (receive.state) {
        case FfmpegCodecIoState::Ok: {
            const auto status = AppendPacket(packet, packets);
            if (status != VRREC_STATUS_OK) {
                return {
                    ReceiveBoundary::Failed,
                    status,
                    received_packet_count,
                };
            }
            ++received_packet_count;
            break;
        }
        case FfmpegCodecIoState::Again:
            return {
                ReceiveBoundary::Again,
                VRREC_STATUS_OK,
                received_packet_count,
            };
        case FfmpegCodecIoState::EndOfStream:
            return {
                ReceiveBoundary::EndOfStream,
                VRREC_STATUS_OK,
                received_packet_count,
            };
        case FfmpegCodecIoState::Failed:
            return {
                ReceiveBoundary::Failed,
                FailureStatus(receive),
                received_packet_count,
            };
        default:
            return {
                ReceiveBoundary::Failed,
                VRREC_STATUS_INTERNAL_ERROR,
                received_packet_count,
            };
        }
    }
}

vrrec_status_t FfmpegEncoderStateMachine::AppendPacket(
    const FfmpegReceivedPacketView &packet,
    std::vector<EncodedMediaPacket> &packets) noexcept
{
    const ReceivedPacketUnrefGuard unref(port_);
    if (packet.data == nullptr || packet.size <= 0 || packet.corrupt ||
        packet.pts_microseconds == UnknownMediaTimestamp ||
        packet.dts_microseconds == UnknownMediaTimestamp ||
        (stream_ == MediaStreamKind::Video &&
            (packet.pts_microseconds < 0 ||
                packet.dts_microseconds < 0)) ||
        packet.pts_microseconds < packet.dts_microseconds ||
        packet.duration_microseconds <= 0 ||
        packet.pts_microseconds >
            std::numeric_limits<std::int64_t>::max() -
                packet.duration_microseconds ||
        packet.dts_microseconds >
            std::numeric_limits<std::int64_t>::max() -
                packet.duration_microseconds ||
        (packet.side_data_count != 0 && packet.side_data == nullptr)) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }

    try {
        EncodedMediaPacket owned {
            stream_,
            packet.pts_microseconds,
            packet.dts_microseconds,
            packet.duration_microseconds,
            packet.key_frame,
            {},
            {},
        };
        owned.payload.assign(
            packet.data,
            packet.data + static_cast<std::size_t>(packet.size));
        owned.side_data.reserve(packet.side_data_count);
        bool has_skip_samples = false;
        for (std::size_t index = 0; index < packet.side_data_count; ++index) {
            const auto &side_data = packet.side_data[index];
            if (side_data.kind ==
                    FfmpegReceivedPacketSideDataKind::QualityStats) {
                constexpr int quality_stats_header_size = 8;
                if (stream_ != MediaStreamKind::Video ||
                    side_data.data == nullptr ||
                    side_data.size < quality_stats_header_size) {
                    return VRREC_STATUS_INTERNAL_ERROR;
                }
                const auto error_count = std::to_integer<unsigned int>(
                    side_data.data[5]);
                const auto expected_size = quality_stats_header_size +
                    static_cast<int>(error_count * 8U);
                if (side_data.size != expected_size) {
                    return VRREC_STATUS_INTERNAL_ERROR;
                }
                continue;
            }
            if (side_data.kind !=
                    FfmpegReceivedPacketSideDataKind::SkipSamples ||
                stream_ != MediaStreamKind::Audio ||
                side_data.data == nullptr ||
                side_data.size !=
                    static_cast<int>(SkipSamplesSideDataSize) ||
                has_skip_samples) {
                return VRREC_STATUS_INTERNAL_ERROR;
            }
            has_skip_samples = true;
            EncodedPacketSideData owned_side_data {
                EncodedPacketSideDataKind::SkipSamples,
                {},
            };
            owned_side_data.payload.assign(
                side_data.data,
                side_data.data +
                    static_cast<std::size_t>(side_data.size));
            owned.side_data.push_back(std::move(owned_side_data));
        }
        packets.push_back(std::move(owned));
        return VRREC_STATUS_OK;
    } catch (const std::bad_alloc &) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

FfmpegEncodeBatch FfmpegEncoderStateMachine::FailLocked(
    vrrec_status_t status,
    std::vector<EncodedMediaPacket> &packets) noexcept
{
    packets.clear();
    AbortLocked();
    return {status, {}};
}

vrrec_status_t FfmpegEncoderStateMachine::FailureStatus(
    const FfmpegCodecIoResult &result) noexcept
{
    return result.failure_status == VRREC_STATUS_OUT_OF_MEMORY
        ? VRREC_STATUS_OUT_OF_MEMORY
        : VRREC_STATUS_INTERNAL_ERROR;
}

void FfmpegEncoderStateMachine::AbortLocked() noexcept
{
    if (port_aborted_) {
        return;
    }
    port_aborted_ = true;
    state_ = State::Aborted;
    port_.Abort();
}

}
