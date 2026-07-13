#ifndef VRRECORDER_NATIVE_TEST_FRAGMENTED_MP4_TEST_SUPPORT_HPP
#define VRRECORDER_NATIVE_TEST_FRAGMENTED_MP4_TEST_SUPPORT_HPP

#include "fragmented_mp4_mux_coordinator.hpp"

namespace vrrecorder::native::test {

inline FragmentedMp4StreamConfiguration TestMp4Streams()
{
    return {
        {
            MicrosecondPacketTimeBase,
            1'920,
            1'080,
            H264Profile::High,
            H264PacketFormat::AnnexB,
            {std::byte{0x01}, std::byte{0x64}},
        },
        {
            MicrosecondPacketTimeBase,
            48'000,
            2,
            1'024,
            1'024,
            AacProfile::LowComplexity,
            AudioChannelLayout::Stereo,
            AacPacketFormat::RawAccessUnit,
            {std::byte{0x12}, std::byte{0x10}},
            192'000,
        },
        DefaultFragmentedMp4FragmentPolicy,
    };
}

}

#endif
