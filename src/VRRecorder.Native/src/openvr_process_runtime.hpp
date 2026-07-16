#ifndef VRRECORDER_NATIVE_OPENVR_PROCESS_RUNTIME_HPP
#define VRRECORDER_NATIVE_OPENVR_PROCESS_RUNTIME_HPP

#include <memory>
#include <string_view>

#include "openvr_overlay_lifecycle.hpp"
#include "openvr_runtime_port.hpp"

namespace vrrecorder::native {

class OpenVrProcessRuntime;
class NativeThreadFactoryPort;

std::shared_ptr<OpenVrProcessRuntime> CreateOpenVrProcessRuntime(
    std::unique_ptr<OpenVrRuntimePort> api,
    vrrec_status_t &status,
    NativeThreadFactoryPort *thread_factory = nullptr) noexcept;

std::unique_ptr<OpenVrInputPort> CreateOpenVrProcessInputPort(
    std::shared_ptr<OpenVrProcessRuntime> runtime,
    vrrec_status_t &status) noexcept;

std::unique_ptr<OpenVrHapticPort> CreateOpenVrProcessHapticPort(
    std::shared_ptr<OpenVrProcessRuntime> runtime,
    vrrec_status_t &status) noexcept;

std::unique_ptr<OpenVrOverlayLifecyclePort>
CreateOpenVrProcessOverlayLifecyclePort(
    std::shared_ptr<OpenVrProcessRuntime> runtime,
    vrrec_status_t &status) noexcept;

std::unique_ptr<OpenVrOverlayLifecycle>
CreateOpenVrProcessOverlayLifecycle(
    std::shared_ptr<OpenVrProcessRuntime> runtime,
    std::string_view application_manifest_path,
    const OpenVrOverlayLifecycleConfig &config,
    vrrec_status_t &status) noexcept;

}

#endif
