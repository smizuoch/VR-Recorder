#include "fragmented_mp4_mux_coordinator.hpp"

#include <cstddef>
#include <cstdlib>
#include <iostream>
#include <vector>

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

class RecordingMuxer final : public FragmentedMp4Muxer {
public:
    vrrec_status_t WritePacket(
        const EncodedMediaPacket &packet) noexcept override
    {
        order.push_back(packet.stream == MediaStreamKind::Video ? 1 : 2);
        packets.push_back(packet);
        return write_status;
    }

    vrrec_status_t EndFragment() noexcept override
    {
        order.push_back(3);
        ++fragment_calls;
        return fragment_status;
    }

    vrrec_status_t WriteTrailer() noexcept override
    {
        order.push_back(4);
        return trailer_status;
    }

    vrrec_status_t FlushFile() noexcept override
    {
        order.push_back(5);
        return flush_status;
    }

    void Abort() noexcept override
    {
        order.push_back(6);
        ++abort_calls;
    }

    std::vector<int> order;
    std::vector<EncodedMediaPacket> packets;
    vrrec_status_t write_status = VRREC_STATUS_OK;
    vrrec_status_t fragment_status = VRREC_STATUS_OK;
    vrrec_status_t trailer_status = VRREC_STATUS_OK;
    vrrec_status_t flush_status = VRREC_STATUS_OK;
    std::size_t fragment_calls = 0;
    std::size_t abort_calls = 0;
};

class FailingPacketObserver final : public EncodedMediaPacketObserver {
public:
    vrrec_status_t Observe(const EncodedMediaPacket &) noexcept override
    {
        ++observe_calls;
        return VRREC_STATUS_INTERNAL_ERROR;
    }

    std::size_t observe_calls = 0;
};

EncodedMediaPacket Video(
    std::int64_t timestamp_microseconds,
    bool key_frame = false)
{
    return {
        MediaStreamKind::Video,
        timestamp_microseconds,
        timestamp_microseconds,
        33'333,
        key_frame,
        1'024,
    };
}

EncodedMediaPacket Audio(std::int64_t timestamp_microseconds)
{
    return {
        MediaStreamKind::Audio,
        timestamp_microseconds,
        timestamp_microseconds,
        21'333,
        false,
        512,
    };
}

void SerializesAudioAndVideoPacketsWithoutChangingTimestamps()
{
    RecordingMuxer muxer;
    FragmentedMp4MuxCoordinator coordinator(muxer);

    CHECK(coordinator.Submit(Video(0, true)) == Mp4MuxResult::Written);
    CHECK(coordinator.Submit(Audio(0)) == Mp4MuxResult::Written);
    CHECK(coordinator.Submit(Video(33'333)) == Mp4MuxResult::Written);
    CHECK(muxer.packets.size() == 3);
    CHECK(muxer.packets[0].pts_microseconds == 0);
    CHECK(muxer.packets[1].stream == MediaStreamKind::Audio);
    CHECK(muxer.packets[2].dts_microseconds == 33'333);
}

void EndsAFragmentBeforeTheNextKeyFrameAtTwoSeconds()
{
    RecordingMuxer muxer;
    FragmentedMp4MuxCoordinator coordinator(muxer);

    CHECK(coordinator.Submit(Video(0, true)) == Mp4MuxResult::Written);
    CHECK(coordinator.Submit(Audio(1'000'000)) == Mp4MuxResult::Written);
    CHECK(coordinator.Submit(Video(2'000'000, true)) ==
          Mp4MuxResult::Written);
    CHECK(muxer.order == std::vector<int>({1, 2, 3, 1}));
    CHECK(muxer.fragment_calls == 1);
}

void ForcesAFragmentBoundaryAtTheTwoSecondLimit()
{
    RecordingMuxer muxer;
    FragmentedMp4MuxCoordinator coordinator(muxer);

    CHECK(coordinator.Submit(Video(0, true)) == Mp4MuxResult::Written);
    CHECK(coordinator.Submit(Audio(2'000'000)) == Mp4MuxResult::Written);
    CHECK(muxer.order == std::vector<int>({1, 3, 2}));
    CHECK(muxer.fragment_calls == 1);
}

void RejectsNonMonotonicDtsPerStream()
{
    RecordingMuxer muxer;
    FragmentedMp4MuxCoordinator coordinator(muxer);

    CHECK(coordinator.Submit(Video(100, true)) == Mp4MuxResult::Written);
    CHECK(coordinator.Submit(Video(99)) == Mp4MuxResult::InvalidPacket);
    CHECK(muxer.packets.size() == 1);
}

void FinalizesFragmentTrailerAndFileInOrder()
{
    RecordingMuxer muxer;
    FragmentedMp4MuxCoordinator coordinator(muxer);
    CHECK(coordinator.Submit(Video(0, true)) == Mp4MuxResult::Written);

    CHECK(coordinator.Finish() == VRREC_STATUS_OK);
    CHECK(muxer.order == std::vector<int>({1, 3, 4, 5}));
    CHECK(coordinator.Submit(Audio(1)) == Mp4MuxResult::InvalidState);
}

void AbortNeverWritesATrailerAndIsIdempotent()
{
    RecordingMuxer muxer;
    FragmentedMp4MuxCoordinator coordinator(muxer);
    coordinator.Abort();
    coordinator.Abort();
    CHECK(muxer.order == std::vector<int>({6}));
    CHECK(muxer.abort_calls == 1);
}

void ObserverFailureAbortsTheIncompleteFile()
{
    RecordingMuxer muxer;
    FailingPacketObserver observer;
    FragmentedMp4MuxCoordinator coordinator(muxer, &observer);

    CHECK(coordinator.Submit(Video(0, true)) == Mp4MuxResult::MuxFailed);
    CHECK(observer.observe_calls == 1);
    CHECK(muxer.packets.size() == 1);
    CHECK(muxer.abort_calls == 1);
    CHECK(coordinator.Finish() == VRREC_STATUS_INVALID_STATE);
    CHECK(coordinator.Submit(Audio(0)) == Mp4MuxResult::InvalidState);
}

}

int main()
{
    SerializesAudioAndVideoPacketsWithoutChangingTimestamps();
    EndsAFragmentBeforeTheNextKeyFrameAtTwoSeconds();
    ForcesAFragmentBoundaryAtTheTwoSecondLimit();
    RejectsNonMonotonicDtsPerStream();
    FinalizesFragmentTrailerAndFileInOrder();
    AbortNeverWritesATrailerAndIsIdempotent();
    ObserverFailureAbortsTheIncompleteFile();
    return 0;
}
