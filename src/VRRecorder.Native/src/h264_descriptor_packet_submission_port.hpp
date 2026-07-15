#ifndef VRRECORDER_NATIVE_H264_DESCRIPTOR_PACKET_SUBMISSION_PORT_HPP
#define VRRECORDER_NATIVE_H264_DESCRIPTOR_PACKET_SUBMISSION_PORT_HPP

#include <span>

#include "fragmented_mp4_mux_coordinator.hpp"

namespace vrrecorder::native {

class H264DescriptorPacketSubmissionPort {
public:
    virtual ~H264DescriptorPacketSubmissionPort() = default;

    virtual Mp4MuxResult SubmitVideoDescriptorBatch(
        const void *encoder_identity,
        const H264StreamDescriptor &descriptor,
        std::span<const EncodedMediaPacket> packets) noexcept = 0;
};

}

#endif
