#ifndef VRRECORDER_NATIVE_OPENVR_INPUT_PORT_HPP
#define VRRECORDER_NATIVE_OPENVR_INPUT_PORT_HPP

#include <cstdint>
#include <string_view>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

struct OpenVrDigitalActionData final {
    bool is_active;
    bool state;
    bool changed;
};

class OpenVrInputPort {
public:
    virtual ~OpenVrInputPort() = default;

    virtual vrrec_status_t Initialize() noexcept = 0;
    virtual vrrec_status_t AddApplicationManifest(
        std::string_view absolute_path,
        bool temporary) noexcept = 0;
    virtual vrrec_status_t SetActionManifestPath(
        std::string_view absolute_path) noexcept = 0;
    virtual vrrec_status_t GetActionSetHandle(
        std::string_view action_set_path,
        std::uint64_t &handle) noexcept = 0;
    virtual vrrec_status_t GetDigitalActionHandle(
        std::string_view action_path,
        std::uint64_t &handle) noexcept = 0;
    virtual vrrec_status_t UpdateActionState(
        std::uint64_t action_set_handle) noexcept = 0;
    virtual vrrec_status_t GetDigitalActionData(
        std::uint64_t action_handle,
        OpenVrDigitalActionData &data) noexcept = 0;
    virtual void Shutdown() noexcept = 0;
};

}

#endif
