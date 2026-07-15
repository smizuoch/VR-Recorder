#ifndef VRRECORDER_NATIVE_SPOUT2_SOURCE_BACKEND_CORE_HPP
#define VRRECORDER_NATIVE_SPOUT2_SOURCE_BACKEND_CORE_HPP

#include <memory>

#include "spout2_receiver_port.hpp"

namespace vrrecorder::native {

std::unique_ptr<SpoutSourceBackend> CreateSpout2SourceBackend(
    std::unique_ptr<Spout2ReceiverPort> port,
    vrrec_status_t &status) noexcept;

}

#endif
