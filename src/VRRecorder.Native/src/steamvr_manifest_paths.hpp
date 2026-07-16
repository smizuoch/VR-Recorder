#ifndef VRRECORDER_NATIVE_STEAMVR_MANIFEST_PATHS_HPP
#define VRRECORDER_NATIVE_STEAMVR_MANIFEST_PATHS_HPP

#include <new>
#include <string>
#include <string_view>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

inline vrrec_status_t ResolveSteamVrApplicationManifestPath(
    std::string_view action_manifest_path,
    std::string &application_manifest_path) noexcept
{
    const auto separator = action_manifest_path.find_last_of("/\\");
    if (separator == std::string_view::npos ||
        action_manifest_path.substr(separator + 1) != "actions.json") {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }
    try {
        application_manifest_path.assign(
            action_manifest_path.substr(0, separator + 1));
        application_manifest_path.append("steamvr.vrmanifest");
        return VRREC_STATUS_OK;
    } catch (const std::bad_alloc &) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

}

#endif
