#ifndef VRRECORDER_NATIVE_ENCODED_MEDIA_PACKET_SUBMISSION_PORT_HPP
#define VRRECORDER_NATIVE_ENCODED_MEDIA_PACKET_SUBMISSION_PORT_HPP

#include <span>

#include "fragmented_mp4_mux_coordinator.hpp"

namespace vrrecorder::native {

class EncodedMediaPacketSubmissionPort {
public:
    virtual ~EncodedMediaPacketSubmissionPort() = default;

    // Submission is synchronous. Implementations must not retain packet or
    // payload references after this call returns. EncoderFailed may be called
    // reentrantly from a synchronous media-event callback; recursive batch
    // submission and completion are not part of this Port's contract.
    virtual Mp4MuxResult SubmitBatch(
        MediaStreamKind producer,
        std::span<const EncodedMediaPacket> packets) noexcept = 0;
    virtual vrrec_status_t EncoderFinished(
        MediaStreamKind stream) noexcept = 0;
    virtual void EncoderFailed(MediaStreamKind stream) noexcept = 0;
};

}

#endif
