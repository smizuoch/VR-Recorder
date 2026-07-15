#ifndef VRRECORDER_NATIVE_WINDOWS_SPOUT2_RECEIVER_PORT_HPP
#define VRRECORDER_NATIVE_WINDOWS_SPOUT2_RECEIVER_PORT_HPP

#include <memory>

#include "spout2_receiver_port.hpp"

namespace vrrecorder::native {

std::unique_ptr<Spout2ReceiverPort> CreateWindowsSpout2ReceiverPort(
    vrrec_status_t &status) noexcept;

}

#endif
