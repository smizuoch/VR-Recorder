#ifndef VRRECORDER_NATIVE_STEAMVR_INPUT_BACKEND_HPP
#define VRRECORDER_NATIVE_STEAMVR_INPUT_BACKEND_HPP

#include <memory>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

class SteamVrInputBackend {
public:
    virtual ~SteamVrInputBackend() = default;

    virtual vrrec_status_t Poll(
        vrrec_steamvr_digital_state_v1 &state) noexcept = 0;
};

std::unique_ptr<SteamVrInputBackend> CreateSteamVrInputBackend(
    const vrrec_steamvr_input_config_v1 &config,
    vrrec_status_t &status);

}

#endif
