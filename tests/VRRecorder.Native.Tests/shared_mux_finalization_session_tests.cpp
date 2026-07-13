#include "shared_mux_finalization_session.hpp"

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
        const EncodedMediaPacket &) noexcept override
    {
        order.push_back(1);
        return write_status;
    }

    vrrec_status_t EndFragment() noexcept override
    {
        order.push_back(2);
        return VRREC_STATUS_OK;
    }

    vrrec_status_t WriteTrailer() noexcept override
    {
        order.push_back(3);
        return VRREC_STATUS_OK;
    }

    vrrec_status_t FlushFile() noexcept override
    {
        order.push_back(4);
        return VRREC_STATUS_OK;
    }

    void Abort() noexcept override
    {
        order.push_back(5);
        ++abort_calls;
    }

    std::vector<int> order;
    vrrec_status_t write_status = VRREC_STATUS_OK;
    std::size_t abort_calls = 0;
};

EncodedMediaPacket VideoPacket()
{
    return {MediaStreamKind::Video, 0, 0, 33'333, true, 100};
}

void FinalizesOnlyAfterBothEncodersFlushSuccessfully()
{
    RecordingMuxer backend;
    FragmentedMp4MuxCoordinator mux(backend);
    SharedMuxFinalizationSession session(mux);
    CHECK(session.Submit(VideoPacket()) == Mp4MuxResult::Written);

    CHECK(session.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(backend.order == std::vector<int>({1}));
    CHECK(session.EncoderFinished(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    CHECK(backend.order == std::vector<int>({1, 2, 3, 4}));
}

void SupportsAudioFinishingBeforeVideo()
{
    RecordingMuxer backend;
    FragmentedMp4MuxCoordinator mux(backend);
    SharedMuxFinalizationSession session(mux);

    CHECK(session.EncoderFinished(MediaStreamKind::Audio) ==
          VRREC_STATUS_OK);
    CHECK(backend.order.empty());
    CHECK(session.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(backend.order == std::vector<int>({3, 4}));
}

void RejectsPacketsAfterTheirEncoderHasFinished()
{
    RecordingMuxer backend;
    FragmentedMp4MuxCoordinator mux(backend);
    SharedMuxFinalizationSession session(mux);

    CHECK(session.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_OK);
    CHECK(session.Submit(VideoPacket()) == Mp4MuxResult::InvalidState);
    CHECK(session.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_INVALID_STATE);
}

void EncoderFailureAbortsWithoutWritingATrailer()
{
    RecordingMuxer backend;
    FragmentedMp4MuxCoordinator mux(backend);
    SharedMuxFinalizationSession session(mux);
    CHECK(session.Submit(VideoPacket()) == Mp4MuxResult::Written);

    session.EncoderFailed(MediaStreamKind::Audio);
    session.EncoderFailed(MediaStreamKind::Audio);
    CHECK(backend.order == std::vector<int>({1, 5}));
    CHECK(backend.abort_calls == 1);
    CHECK(session.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_INVALID_STATE);
}

void PacketMuxFailureImmediatelyTerminalizesTheSharedSession()
{
    RecordingMuxer backend;
    backend.write_status = VRREC_STATUS_INTERNAL_ERROR;
    FragmentedMp4MuxCoordinator mux(backend);
    SharedMuxFinalizationSession session(mux);

    CHECK(session.Submit(VideoPacket()) == Mp4MuxResult::MuxFailed);
    CHECK(backend.abort_calls == 1);
    CHECK(session.EncoderFinished(MediaStreamKind::Video) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(session.EncoderFinished(MediaStreamKind::Audio) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(session.Submit(VideoPacket()) == Mp4MuxResult::InvalidState);
}

}

int main()
{
    FinalizesOnlyAfterBothEncodersFlushSuccessfully();
    SupportsAudioFinishingBeforeVideo();
    RejectsPacketsAfterTheirEncoderHasFinished();
    EncoderFailureAbortsWithoutWritingATrailer();
    PacketMuxFailureImmediatelyTerminalizesTheSharedSession();
    return 0;
}
