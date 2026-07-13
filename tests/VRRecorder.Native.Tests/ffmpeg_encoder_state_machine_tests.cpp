#include "ffmpeg_encoder_state_machine.hpp"

#include <array>
#include <atomic>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <limits>
#include <mutex>
#include <new>
#include <thread>
#include <vector>

namespace allocation_failure {

std::atomic<std::size_t> fail_on_allocation {0};

bool ShouldFail() noexcept
{
    auto remaining = fail_on_allocation.load();
    while (remaining != 0) {
        if (fail_on_allocation.compare_exchange_weak(
                remaining,
                remaining - 1)) {
            return remaining == 1;
        }
    }
    return false;
}

}

void *operator new(std::size_t size)
{
    if (allocation_failure::ShouldFail()) {
        throw std::bad_alloc {};
    }
    if (auto *allocation = std::malloc(size); allocation != nullptr) {
        return allocation;
    }
    throw std::bad_alloc {};
}

void *operator new[](std::size_t size)
{
    return ::operator new(size);
}

void operator delete(void *allocation) noexcept
{
    std::free(allocation);
}

void operator delete[](void *allocation) noexcept
{
    ::operator delete(allocation);
}

void operator delete(void *allocation, std::size_t) noexcept
{
    ::operator delete(allocation);
}

void operator delete[](void *allocation, std::size_t) noexcept
{
    ::operator delete[](allocation);
}

namespace {

#define CHECK(condition)                                                        \
    do {                                                                        \
        if (!(condition)) {                                                     \
            std::cerr << "check failed at " << __FILE__ << ':' << __LINE__      \
                      << ": " #condition << '\n';                              \
            std::abort();                                                       \
        }                                                                       \
    } while (false)

using namespace vrrecorder::native;

FfmpegCodecIoResult Ok()
{
    return {FfmpegCodecIoState::Ok, VRREC_STATUS_OK};
}

FfmpegCodecIoResult Again()
{
    return {FfmpegCodecIoState::Again, VRREC_STATUS_OK};
}

FfmpegCodecIoResult EndOfStream()
{
    return {FfmpegCodecIoState::EndOfStream, VRREC_STATUS_OK};
}

FfmpegCodecIoResult Failure(
    vrrec_status_t status = VRREC_STATUS_INTERNAL_ERROR)
{
    return {FfmpegCodecIoState::Failed, status};
}

struct ReceiveStep final {
    FfmpegCodecIoResult result;
    std::vector<std::byte> payload;
    int reported_size = std::numeric_limits<int>::min();
    bool report_null_data = false;
    std::int64_t pts_microseconds = 0;
    std::int64_t dts_microseconds = 0;
    std::int64_t duration_microseconds = 33'333;
    bool key_frame = false;
    bool corrupt = false;
    bool has_side_data = false;
    FfmpegReceivedPacketSideDataKind side_data_kind =
        FfmpegReceivedPacketSideDataKind::SkipSamples;
    std::vector<std::byte> side_data_payload;
    int reported_side_data_size = std::numeric_limits<int>::min();
    bool report_null_side_data = false;
    bool report_null_side_data_array = false;
    std::size_t side_data_count = 1;
};

ReceiveStep Packet(
    std::byte value,
    std::int64_t timestamp,
    bool key_frame = false)
{
    ReceiveStep step {};
    step.result = Ok();
    step.payload = std::vector<std::byte>(4, value);
    step.pts_microseconds = timestamp;
    step.dts_microseconds = timestamp;
    step.key_frame = key_frame;
    return step;
}

ReceiveStep ReceiveResult(FfmpegCodecIoResult result)
{
    ReceiveStep step {};
    step.result = result;
    return step;
}

class ScriptedEncoderPort final : public FfmpegEncoderPort {
public:
    FfmpegCodecIoResult SendPreparedFrame() noexcept override
    {
        ++send_frame_calls;
        if (send_frame_index >= send_frame_results.size()) {
            return Failure();
        }
        return send_frame_results[send_frame_index++];
    }

    FfmpegCodecIoResult SendDrain() noexcept override
    {
        ++send_drain_calls;
        if (send_drain_index >= send_drain_results.size()) {
            return Failure();
        }
        return send_drain_results[send_drain_index++];
    }

    FfmpegCodecIoResult ReceivePacket(
        FfmpegReceivedPacketView &packet) noexcept override
    {
        ++receive_calls;
        packet = {};
        if (receive_index >= receive_steps.size()) {
            return Failure();
        }

        auto &step = receive_steps[receive_index++];
        if (step.result.state != FfmpegCodecIoState::Ok) {
            return step.result;
        }
        packet.data = step.report_null_data ? nullptr : step.payload.data();
        packet.size = step.reported_size != std::numeric_limits<int>::min()
            ? step.reported_size
            : static_cast<int>(step.payload.size());
        packet.pts_microseconds = step.pts_microseconds;
        packet.dts_microseconds = step.dts_microseconds;
        packet.duration_microseconds = step.duration_microseconds;
        packet.key_frame = step.key_frame;
        packet.corrupt = step.corrupt;
        if (step.has_side_data) {
            CHECK(step.side_data_count <= current_side_data.size());
            for (std::size_t index = 0;
                 index < step.side_data_count;
                 ++index) {
                current_side_data[index].kind = step.side_data_kind;
                current_side_data[index].data = step.report_null_side_data
                    ? nullptr
                    : step.side_data_payload.data();
                current_side_data[index].size =
                    step.reported_side_data_size !=
                        std::numeric_limits<int>::min()
                    ? step.reported_side_data_size
                    : static_cast<int>(step.side_data_payload.size());
            }
            packet.side_data = step.report_null_side_data_array
                ? nullptr
                : current_side_data.data();
            packet.side_data_count = step.side_data_count;
        }
        return step.result;
    }

    void UnrefReceivedPacket() noexcept override
    {
        ++unref_calls;
    }

    void Abort() noexcept override
    {
        ++abort_calls;
    }

    std::vector<FfmpegCodecIoResult> send_frame_results;
    std::vector<FfmpegCodecIoResult> send_drain_results;
    std::vector<ReceiveStep> receive_steps;
    std::size_t send_frame_index = 0;
    std::size_t send_drain_index = 0;
    std::size_t receive_index = 0;
    std::size_t send_frame_calls = 0;
    std::size_t send_drain_calls = 0;
    std::size_t receive_calls = 0;
    std::size_t unref_calls = 0;
    std::size_t abort_calls = 0;

private:
    std::array<FfmpegReceivedPacketSideDataView, 2> current_side_data {};
};

void AcceptsAFrameThatProducesNoPacketYet()
{
    ScriptedEncoderPort port;
    port.send_frame_results = {Ok()};
    port.receive_steps = {ReceiveResult(Again())};
    FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Video);

    const auto result = encoder.EncodePreparedFrame();

    CHECK(result.status == VRREC_STATUS_OK);
    CHECK(result.packets.empty());
    CHECK(port.send_frame_calls == 1);
    CHECK(port.receive_calls == 1);
    CHECK(port.unref_calls == 0);
}

void CopiesEveryPacketInOrderAndUnrefsEachSuccessfulReceive()
{
    ScriptedEncoderPort port;
    port.send_frame_results = {Ok()};
    port.receive_steps = {
        Packet(std::byte {0x11}, 0, true),
        Packet(std::byte {0x22}, 33'333),
        ReceiveResult(Again()),
    };
    FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Video);

    auto result = encoder.EncodePreparedFrame();
    CHECK(result.status == VRREC_STATUS_OK);
    CHECK(result.packets.size() == 2);
    CHECK(result.packets[0].key_frame);
    CHECK(result.packets[0].pts_microseconds == 0);
    CHECK(result.packets[1].pts_microseconds == 33'333);
    CHECK(result.packets[0].payload.front() == std::byte {0x11});
    CHECK(result.packets[1].payload.front() == std::byte {0x22});
    CHECK(port.unref_calls == 2);

    port.receive_steps[0].payload[0] = std::byte {0x7f};
    port.receive_steps[1].payload[0] = std::byte {0x7f};
    CHECK(result.packets[0].payload.front() == std::byte {0x11});
    CHECK(result.packets[1].payload.front() == std::byte {0x22});
}

void CopiesSupportedSkipSamplesSideDataAndOwnsItsPayload()
{
    ScriptedEncoderPort port;
    port.send_frame_results = {Ok()};
    auto packet = Packet(std::byte {0x11}, 0);
    packet.has_side_data = true;
    packet.side_data_payload =
        std::vector<std::byte>(10, std::byte {0x04});
    port.receive_steps = {
        packet,
        ReceiveResult(Again()),
    };
    FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Audio);

    auto result = encoder.EncodePreparedFrame();

    CHECK(result.status == VRREC_STATUS_OK);
    CHECK(result.packets.size() == 1);
    CHECK(result.packets[0].side_data.size() == 1);
    CHECK(result.packets[0].side_data[0].kind ==
        EncodedPacketSideDataKind::SkipSamples);
    CHECK(result.packets[0].side_data[0].payload.size() == 10);
    CHECK(result.packets[0].side_data[0].payload.front() ==
        std::byte {0x04});
    CHECK(port.unref_calls == 1);

    port.receive_steps[0].side_data_payload[0] = std::byte {0x7f};
    CHECK(result.packets[0].side_data[0].payload.front() ==
        std::byte {0x04});
}

void PreservesNegativeAudioPrimingTimestampsForTheMp4EditList()
{
    ScriptedEncoderPort port;
    port.send_frame_results = {Ok()};
    auto priming = Packet(std::byte {0x11}, -21'333);
    priming.duration_microseconds = 21'333;
    port.receive_steps = {
        priming,
        ReceiveResult(Again()),
    };
    FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Audio);

    const auto result = encoder.EncodePreparedFrame();

    CHECK(result.status == VRREC_STATUS_OK);
    CHECK(result.packets.size() == 1);
    CHECK(result.packets[0].pts_microseconds == -21'333);
    CHECK(result.packets[0].dts_microseconds == -21'333);
    CHECK(port.unref_calls == 1);
    CHECK(port.abort_calls == 0);
}

void DrainsPendingOutputAndRetriesTheSamePreparedFrameAfterSendAgain()
{
    ScriptedEncoderPort port;
    port.send_frame_results = {Again(), Ok()};
    port.receive_steps = {
        Packet(std::byte {0x10}, 0),
        ReceiveResult(Again()),
        Packet(std::byte {0x20}, 33'333),
        Packet(std::byte {0x30}, 66'666),
        ReceiveResult(Again()),
    };
    FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Video);

    const auto result = encoder.EncodePreparedFrame();

    CHECK(result.status == VRREC_STATUS_OK);
    CHECK(result.packets.size() == 3);
    CHECK(port.send_frame_calls == 2);
    CHECK(port.receive_calls == 5);
    CHECK(port.unref_calls == 3);
}

void RejectsSimultaneousSendAndReceiveAgainWithoutBusyLooping()
{
    ScriptedEncoderPort port;
    port.send_frame_results = {Again()};
    port.receive_steps = {ReceiveResult(Again())};
    FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Video);

    const auto result = encoder.EncodePreparedFrame();

    CHECK(result.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(result.packets.empty());
    CHECK(port.send_frame_calls == 1);
    CHECK(port.receive_calls == 1);
    CHECK(port.abort_calls == 1);
    CHECK(encoder.EncodePreparedFrame().status == VRREC_STATUS_INVALID_STATE);
    CHECK(port.send_frame_calls == 1);
}

void DiscardsAPartialBatchWhenAReceiveFails()
{
    ScriptedEncoderPort port;
    port.send_frame_results = {Ok()};
    port.receive_steps = {
        Packet(std::byte {0x10}, 0),
        ReceiveResult(Failure()),
    };
    FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Video);

    const auto result = encoder.EncodePreparedFrame();

    CHECK(result.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(result.packets.empty());
    CHECK(port.unref_calls == 1);
    CHECK(port.abort_calls == 1);
}

void RejectsMalformedAndUnsupportedPacketsAfterUnref()
{
    std::vector<ReceiveStep> invalid_packets;

    auto null_payload = Packet(std::byte {0x10}, 0);
    null_payload.report_null_data = true;
    invalid_packets.push_back(null_payload);

    auto negative_size = Packet(std::byte {0x10}, 0);
    negative_size.reported_size = -2;
    invalid_packets.push_back(negative_size);

    auto empty_side_data_only = Packet(std::byte {0x10}, 0);
    empty_side_data_only.payload.clear();
    empty_side_data_only.reported_size = 0;
    empty_side_data_only.has_side_data = true;
    empty_side_data_only.side_data_payload =
        std::vector<std::byte>(10, std::byte {0x04});
    invalid_packets.push_back(empty_side_data_only);

    auto payload_with_unmodeled_side_data = Packet(std::byte {0x10}, 0);
    payload_with_unmodeled_side_data.has_side_data = true;
    payload_with_unmodeled_side_data.side_data_kind =
        FfmpegReceivedPacketSideDataKind::Unsupported;
    payload_with_unmodeled_side_data.side_data_payload =
        std::vector<std::byte>(4, std::byte {0x01});
    invalid_packets.push_back(payload_with_unmodeled_side_data);

    auto null_side_data = Packet(std::byte {0x10}, 0);
    null_side_data.has_side_data = true;
    null_side_data.report_null_side_data = true;
    null_side_data.reported_side_data_size = 10;
    invalid_packets.push_back(null_side_data);

    auto null_side_data_array = Packet(std::byte {0x10}, 0);
    null_side_data_array.has_side_data = true;
    null_side_data_array.report_null_side_data_array = true;
    invalid_packets.push_back(null_side_data_array);

    auto short_skip_samples = Packet(std::byte {0x10}, 0);
    short_skip_samples.has_side_data = true;
    short_skip_samples.side_data_payload =
        std::vector<std::byte>(9, std::byte {0x04});
    invalid_packets.push_back(short_skip_samples);

    auto oversized_skip_samples = Packet(std::byte {0x10}, 0);
    oversized_skip_samples.has_side_data = true;
    oversized_skip_samples.side_data_payload =
        std::vector<std::byte>(11, std::byte {0x04});
    invalid_packets.push_back(oversized_skip_samples);

    auto duplicate_skip_samples = Packet(std::byte {0x10}, 0);
    duplicate_skip_samples.has_side_data = true;
    duplicate_skip_samples.side_data_count = 2;
    duplicate_skip_samples.side_data_payload =
        std::vector<std::byte>(10, std::byte {0x04});
    invalid_packets.push_back(duplicate_skip_samples);

    auto corrupt = Packet(std::byte {0x10}, 0);
    corrupt.corrupt = true;
    invalid_packets.push_back(corrupt);

    auto missing_pts = Packet(std::byte {0x10}, 0);
    missing_pts.pts_microseconds = UnknownMediaTimestamp;
    invalid_packets.push_back(missing_pts);

    auto missing_dts = Packet(std::byte {0x10}, 0);
    missing_dts.dts_microseconds = UnknownMediaTimestamp;
    invalid_packets.push_back(missing_dts);

    auto pts_before_dts = Packet(std::byte {0x10}, 10);
    pts_before_dts.dts_microseconds = 11;
    invalid_packets.push_back(pts_before_dts);

    auto zero_duration = Packet(std::byte {0x10}, 0);
    zero_duration.duration_microseconds = 0;
    invalid_packets.push_back(zero_duration);

    auto timestamp_end_overflow = Packet(
        std::byte {0x10},
        std::numeric_limits<std::int64_t>::max());
    timestamp_end_overflow.duration_microseconds = 1;
    invalid_packets.push_back(timestamp_end_overflow);

    for (auto &invalid_packet : invalid_packets) {
        ScriptedEncoderPort port;
        port.send_frame_results = {Ok()};
        port.receive_steps = {
            invalid_packet,
            ReceiveResult(Again()),
        };
        FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Audio);

        const auto result = encoder.EncodePreparedFrame();
        CHECK(result.status == VRREC_STATUS_INTERNAL_ERROR);
        CHECK(result.packets.empty());
        CHECK(port.unref_calls == 1);
        CHECK(port.abort_calls == 1);
    }

    auto video_side_data = Packet(std::byte {0x10}, 0);
    video_side_data.has_side_data = true;
    video_side_data.side_data_payload =
        std::vector<std::byte>(SkipSamplesSideDataSize, std::byte {0x04});
    auto negative_video_pts = Packet(std::byte {0x10}, -1);
    auto negative_video_dts = Packet(std::byte {0x10}, 0);
    negative_video_dts.dts_microseconds = -1;
    const std::vector<ReceiveStep> invalid_video_packets {
        video_side_data,
        negative_video_pts,
        negative_video_dts,
    };
    for (const auto &invalid_video_packet : invalid_video_packets) {
        ScriptedEncoderPort video_port;
        video_port.send_frame_results = {Ok()};
        video_port.receive_steps = {invalid_video_packet};
        FfmpegEncoderStateMachine video_encoder(
            video_port,
            MediaStreamKind::Video);
        CHECK(video_encoder.EncodePreparedFrame().status ==
            VRREC_STATUS_INTERNAL_ERROR);
        CHECK(video_port.unref_calls == 1);
        CHECK(video_port.abort_calls == 1);
    }
}

void PropagatesAnOutOfMemoryPortFailureAndTerminalizes()
{
    ScriptedEncoderPort port;
    port.send_frame_results = {Failure(VRREC_STATUS_OUT_OF_MEMORY)};
    FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Video);

    const auto result = encoder.EncodePreparedFrame();

    CHECK(result.status == VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(result.packets.empty());
    CHECK(port.abort_calls == 1);
}

void RejectsUnexpectedEncodeProtocolBoundariesWithoutReturningPartialOutput()
{
    {
        ScriptedEncoderPort port;
        port.send_frame_results = {EndOfStream()};
        FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Video);
        CHECK(encoder.EncodePreparedFrame().status ==
            VRREC_STATUS_INTERNAL_ERROR);
        CHECK(port.receive_calls == 0);
        CHECK(port.abort_calls == 1);
    }

    {
        ScriptedEncoderPort port;
        port.send_frame_results = {
            Again(),
            Again(),
        };
        port.receive_steps = {
            Packet(std::byte {0x10}, 0),
            ReceiveResult(Again()),
        };
        FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Video);
        const auto result = encoder.EncodePreparedFrame();
        CHECK(result.status == VRREC_STATUS_INTERNAL_ERROR);
        CHECK(result.packets.empty());
        CHECK(port.send_frame_calls == 2);
        CHECK(port.unref_calls == 1);
        CHECK(port.abort_calls == 1);
    }

    {
        ScriptedEncoderPort port;
        port.send_frame_results = {Ok()};
        port.receive_steps = {ReceiveResult(EndOfStream())};
        FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Video);
        CHECK(encoder.EncodePreparedFrame().status ==
            VRREC_STATUS_INTERNAL_ERROR);
        CHECK(port.abort_calls == 1);
    }

    {
        ScriptedEncoderPort port;
        port.send_frame_results = {Ok()};
        port.receive_steps = {
            Packet(std::byte {0x10}, 0),
            ReceiveResult(Failure(VRREC_STATUS_OUT_OF_MEMORY)),
        };
        FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Video);
        const auto result = encoder.EncodePreparedFrame();
        CHECK(result.status == VRREC_STATUS_OUT_OF_MEMORY);
        CHECK(result.packets.empty());
        CHECK(port.unref_calls == 1);
        CHECK(port.abort_calls == 1);
    }
}

void ConvertsEveryPacketOwnershipAllocationFailureWithoutTerminatingOrLeaking()
{
    for (std::size_t failing_allocation = 1;
         failing_allocation <= 4;
         ++failing_allocation) {
        ScriptedEncoderPort port;
        port.send_frame_results = {Ok()};
        auto packet = Packet(std::byte {0x10}, 0);
        packet.has_side_data = true;
        packet.side_data_payload =
            std::vector<std::byte>(10, std::byte {0x04});
        port.receive_steps = {packet};
        FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Audio);

        allocation_failure::fail_on_allocation = failing_allocation;
        const auto result = encoder.EncodePreparedFrame();
        allocation_failure::fail_on_allocation = 0;

        CHECK(result.status == VRREC_STATUS_OUT_OF_MEMORY);
        CHECK(result.packets.empty());
        CHECK(port.unref_calls == 1);
        CHECK(port.abort_calls == 1);
    }
}

void DiscardsAPartialBatchWhenASecondPacketAllocationFails()
{
    ScriptedEncoderPort port;
    port.send_frame_results = {Ok()};
    port.receive_steps = {
        Packet(std::byte {0x10}, 0),
        Packet(std::byte {0x20}, 33'333),
    };
    FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Video);

    allocation_failure::fail_on_allocation = 3;
    const auto result = encoder.EncodePreparedFrame();
    allocation_failure::fail_on_allocation = 0;

    CHECK(result.status == VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(result.packets.empty());
    CHECK(port.unref_calls == 2);
    CHECK(port.abort_calls == 1);
}

void DiscardsDelayedPartialBatchWhenAllocationFailsDuringDrain()
{
    ScriptedEncoderPort port;
    port.send_drain_results = {Ok()};
    port.receive_steps = {
        Packet(std::byte {0x10}, 0),
        Packet(std::byte {0x20}, 33'333),
    };
    FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Audio);

    allocation_failure::fail_on_allocation = 3;
    const auto result = encoder.Finish();
    allocation_failure::fail_on_allocation = 0;

    CHECK(result.status == VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(result.packets.empty());
    CHECK(port.unref_calls == 2);
    CHECK(port.abort_calls == 1);
}

void DrainsAllDelayedPacketsUntilEndOfStream()
{
    ScriptedEncoderPort port;
    port.send_drain_results = {Ok()};
    port.receive_steps = {
        Packet(std::byte {0x10}, 0),
        Packet(std::byte {0x20}, 33'333),
        ReceiveResult(EndOfStream()),
    };
    FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Video);

    const auto result = encoder.Finish();

    CHECK(result.status == VRREC_STATUS_OK);
    CHECK(result.packets.size() == 2);
    CHECK(port.send_drain_calls == 1);
    CHECK(port.receive_calls == 3);
    CHECK(port.unref_calls == 2);
    CHECK(encoder.Finish().status == VRREC_STATUS_INVALID_STATE);
    CHECK(encoder.EncodePreparedFrame().status == VRREC_STATUS_INVALID_STATE);
    CHECK(port.send_drain_calls == 1);
    CHECK(port.send_frame_calls == 0);
}

void DrainsPendingOutputBeforeRetryingTheNullFrame()
{
    ScriptedEncoderPort port;
    port.send_drain_results = {Again(), Ok()};
    port.receive_steps = {
        Packet(std::byte {0x10}, 0),
        ReceiveResult(Again()),
        Packet(std::byte {0x20}, 33'333),
        ReceiveResult(EndOfStream()),
    };
    FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Audio);

    const auto result = encoder.Finish();

    CHECK(result.status == VRREC_STATUS_OK);
    CHECK(result.packets.size() == 2);
    CHECK(port.send_drain_calls == 2);
    CHECK(port.receive_calls == 4);
}

void RejectsAgainAfterTheDrainFrameWasAccepted()
{
    ScriptedEncoderPort port;
    port.send_drain_results = {Ok()};
    port.receive_steps = {ReceiveResult(Again())};
    FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Audio);

    const auto result = encoder.Finish();

    CHECK(result.status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(result.packets.empty());
    CHECK(port.send_drain_calls == 1);
    CHECK(port.receive_calls == 1);
    CHECK(port.abort_calls == 1);
}

void RejectsInvalidDrainBoundariesAndDiscardsDelayedPartialOutput()
{
    {
        ScriptedEncoderPort port;
        port.send_drain_results = {Again()};
        port.receive_steps = {ReceiveResult(Again())};
        FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Audio);
        CHECK(encoder.Finish().status == VRREC_STATUS_INTERNAL_ERROR);
        CHECK(port.send_drain_calls == 1);
        CHECK(port.receive_calls == 1);
        CHECK(port.abort_calls == 1);
    }

    {
        ScriptedEncoderPort port;
        port.send_drain_results = {Again()};
        port.receive_steps = {ReceiveResult(EndOfStream())};
        FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Audio);
        CHECK(encoder.Finish().status == VRREC_STATUS_INTERNAL_ERROR);
        CHECK(port.abort_calls == 1);
    }

    {
        ScriptedEncoderPort port;
        port.send_drain_results = {Ok()};
        port.receive_steps = {
            Packet(std::byte {0x10}, 0),
            ReceiveResult(Failure()),
        };
        FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Audio);
        const auto result = encoder.Finish();
        CHECK(result.status == VRREC_STATUS_INTERNAL_ERROR);
        CHECK(result.packets.empty());
        CHECK(port.unref_calls == 1);
        CHECK(port.abort_calls == 1);
    }

    {
        ScriptedEncoderPort port;
        port.send_drain_results = {Ok()};
        auto malformed = Packet(std::byte {0x10}, 0);
        malformed.duration_microseconds = 0;
        port.receive_steps = {malformed};
        FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Audio);
        const auto result = encoder.Finish();
        CHECK(result.status == VRREC_STATUS_INTERNAL_ERROR);
        CHECK(result.packets.empty());
        CHECK(port.unref_calls == 1);
        CHECK(port.abort_calls == 1);
    }
}

void RejectsAnInvalidOutputStreamBeforeCallingTheCodec()
{
    ScriptedEncoderPort port;
    port.send_frame_results = {Ok()};
    port.receive_steps = {
        Packet(std::byte {0x10}, 0),
        ReceiveResult(Again()),
    };
    FfmpegEncoderStateMachine encoder(
        port,
        static_cast<MediaStreamKind>(99));

    const auto result = encoder.EncodePreparedFrame();

    CHECK(result.status == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(result.packets.empty());
    CHECK(port.send_frame_calls == 0);
    CHECK(port.receive_calls == 0);
    CHECK(port.abort_calls == 1);
}

void RejectsUnknownCodecIoStates()
{
    {
        ScriptedEncoderPort port;
        port.send_frame_results = {{
            static_cast<FfmpegCodecIoState>(99),
            VRREC_STATUS_OK,
        }};
        FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Video);
        CHECK(encoder.EncodePreparedFrame().status ==
            VRREC_STATUS_INTERNAL_ERROR);
        CHECK(port.abort_calls == 1);
    }

    {
        ScriptedEncoderPort port;
        port.send_frame_results = {Ok()};
        port.receive_steps = {ReceiveResult({
            static_cast<FfmpegCodecIoState>(99),
            VRREC_STATUS_OK,
        })};
        FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Video);
        CHECK(encoder.EncodePreparedFrame().status ==
            VRREC_STATUS_INTERNAL_ERROR);
        CHECK(port.abort_calls == 1);
    }
}

void AbortIsIdempotentAndNeverAttemptsToDrain()
{
    ScriptedEncoderPort port;
    FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Video);

    encoder.Abort();
    encoder.Abort();

    CHECK(port.abort_calls == 1);
    CHECK(port.send_drain_calls == 0);
    CHECK(encoder.EncodePreparedFrame().status == VRREC_STATUS_INVALID_STATE);
    CHECK(encoder.Finish().status == VRREC_STATUS_INVALID_STATE);
}

void AbortAfterFinishCleansUpWithoutDrainingAgain()
{
    ScriptedEncoderPort port;
    port.send_drain_results = {Ok()};
    port.receive_steps = {ReceiveResult(EndOfStream())};
    FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Video);

    CHECK(encoder.Finish().status == VRREC_STATUS_OK);
    encoder.Abort();
    encoder.Abort();

    CHECK(port.send_drain_calls == 1);
    CHECK(port.abort_calls == 1);
}

void DestructorDoesNotReportAbortAfterGracefulFinish()
{
    ScriptedEncoderPort port;
    port.send_drain_results = {Ok()};
    port.receive_steps = {ReceiveResult(EndOfStream())};
    {
        FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Video);
        CHECK(encoder.Finish().status == VRREC_STATUS_OK);
    }
    CHECK(port.abort_calls == 0);
}

void DestructorAbortsAnActiveEncoderExactlyOnce()
{
    ScriptedEncoderPort port;
    {
        FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Video);
    }
    CHECK(port.abort_calls == 1);
    CHECK(port.send_drain_calls == 0);
}

class GatedEncoderPort final : public FfmpegEncoderPort {
public:
    FfmpegCodecIoResult SendPreparedFrame() noexcept override
    {
        {
            const std::lock_guard lock(gate_mutex);
            send_entered = true;
            api_call_active = true;
        }
        gate_changed.notify_all();

        {
            std::unique_lock lock(gate_mutex);
            gate_changed.wait(lock, [this] { return release_send; });
            api_call_active = false;
        }
        return Ok();
    }

    FfmpegCodecIoResult SendDrain() noexcept override
    {
        return Failure();
    }

    FfmpegCodecIoResult ReceivePacket(
        FfmpegReceivedPacketView &) noexcept override
    {
        return Again();
    }

    void UnrefReceivedPacket() noexcept override
    {
    }

    void Abort() noexcept override
    {
        const std::lock_guard lock(gate_mutex);
        abort_was_concurrent = api_call_active;
        ++abort_calls;
    }

    void WaitForSendEntry()
    {
        std::unique_lock lock(gate_mutex);
        gate_changed.wait(lock, [this] { return send_entered; });
    }

    void ReleaseSend()
    {
        {
            const std::lock_guard lock(gate_mutex);
            release_send = true;
        }
        gate_changed.notify_all();
    }

    std::mutex gate_mutex;
    std::condition_variable gate_changed;
    bool send_entered = false;
    bool release_send = false;
    bool api_call_active = false;
    bool abort_was_concurrent = false;
    std::size_t abort_calls = 0;
};

void SerializesAbortAgainstAnInFlightCodecCall()
{
    GatedEncoderPort port;
    FfmpegEncoderStateMachine encoder(port, MediaStreamKind::Video);
    FfmpegEncodeBatch encode_result;
    std::atomic<bool> abort_thread_started {false};

    std::thread encode_thread([&] {
        encode_result = encoder.EncodePreparedFrame();
    });
    port.WaitForSendEntry();

    std::thread abort_thread([&] {
        abort_thread_started = true;
        encoder.Abort();
    });
    while (!abort_thread_started.load()) {
        std::this_thread::yield();
    }
    CHECK(port.abort_calls == 0);

    port.ReleaseSend();
    encode_thread.join();
    abort_thread.join();

    CHECK(encode_result.status == VRREC_STATUS_OK);
    CHECK(!port.abort_was_concurrent);
    CHECK(port.abort_calls == 1);
}

}

int main()
{
    AcceptsAFrameThatProducesNoPacketYet();
    CopiesEveryPacketInOrderAndUnrefsEachSuccessfulReceive();
    CopiesSupportedSkipSamplesSideDataAndOwnsItsPayload();
    PreservesNegativeAudioPrimingTimestampsForTheMp4EditList();
    DrainsPendingOutputAndRetriesTheSamePreparedFrameAfterSendAgain();
    RejectsSimultaneousSendAndReceiveAgainWithoutBusyLooping();
    DiscardsAPartialBatchWhenAReceiveFails();
    RejectsMalformedAndUnsupportedPacketsAfterUnref();
    PropagatesAnOutOfMemoryPortFailureAndTerminalizes();
    RejectsUnexpectedEncodeProtocolBoundariesWithoutReturningPartialOutput();
    ConvertsEveryPacketOwnershipAllocationFailureWithoutTerminatingOrLeaking();
    DiscardsAPartialBatchWhenASecondPacketAllocationFails();
    DiscardsDelayedPartialBatchWhenAllocationFailsDuringDrain();
    DrainsAllDelayedPacketsUntilEndOfStream();
    DrainsPendingOutputBeforeRetryingTheNullFrame();
    RejectsAgainAfterTheDrainFrameWasAccepted();
    RejectsInvalidDrainBoundariesAndDiscardsDelayedPartialOutput();
    RejectsAnInvalidOutputStreamBeforeCallingTheCodec();
    RejectsUnknownCodecIoStates();
    AbortIsIdempotentAndNeverAttemptsToDrain();
    AbortAfterFinishCleansUpWithoutDrainingAgain();
    DestructorDoesNotReportAbortAfterGracefulFinish();
    DestructorAbortsAnActiveEncoderExactlyOnce();
    SerializesAbortAgainstAnInFlightCodecCall();
    return 0;
}
