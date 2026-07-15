#ifndef VRRECORDER_NATIVE_OPENVR_PROCESS_RUNTIME_HPP
#define VRRECORDER_NATIVE_OPENVR_PROCESS_RUNTIME_HPP

#include <memory>

#include "openvr_input_port.hpp"

namespace vrrecorder::native {

class OpenVrProcessRuntime;
class NativeThreadFactoryPort;

std::shared_ptr<OpenVrProcessRuntime> CreateOpenVrProcessRuntime(
    std::unique_ptr<OpenVrInputPort> api,
    vrrec_status_t &status,
    NativeThreadFactoryPort *thread_factory = nullptr) noexcept;

std::unique_ptr<OpenVrInputPort> CreateOpenVrProcessInputPort(
    std::shared_ptr<OpenVrProcessRuntime> runtime,
    vrrec_status_t &status) noexcept;

}

#endif
