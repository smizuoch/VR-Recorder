#ifndef VRRECORDER_NATIVE_OPENVR_STEAMVR_INPUT_BACKEND_CORE_HPP
#define VRRECORDER_NATIVE_OPENVR_STEAMVR_INPUT_BACKEND_CORE_HPP

#include <memory>

#include "openvr_input_port.hpp"
#include "steamvr_input_backend.hpp"

namespace vrrecorder::native {

std::unique_ptr<SteamVrInputBackend> CreateOpenVrSteamVrInputBackend(
    const vrrec_steamvr_input_config_v1 &config,
    std::unique_ptr<OpenVrInputPort> port,
    vrrec_status_t &status) noexcept;

}

#endif
