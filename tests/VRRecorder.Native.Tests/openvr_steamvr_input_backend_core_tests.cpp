#include "openvr_steamvr_input_backend_core.hpp"
#include "openvr_steamvr_haptic_backend_core.hpp"
#include "steamvr_manifest_paths.hpp"

#include "allocation_failure_test_support.hpp"

#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <memory>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace {

#define CHECK(condition)                                                        \
    do {                                                                        \
        if (!(condition)) {                                                     \
            std::cerr << "check failed at " << __FILE__ << ':' << __LINE__      \
                      << ": " #condition << '\n';                              \
            std::abort();                                                       \
        }                                                                       \
    } while (false)

using namespace vrrecorder::native;

struct FakePortState final {
    std::vector<std::string> calls;
    std::size_t shutdown_calls = 0;
};

class FakePort final : public OpenVrInputPort {
public:
    explicit FakePort(std::shared_ptr<FakePortState> state)
        : state_(std::move(state))
    {
    }

    vrrec_status_t Initialize() noexcept override
    {
        state_->calls.emplace_back("initialize");
        return initialize_status;
    }

    vrrec_status_t AddApplicationManifest(
        std::string_view absolute_path,
        bool temporary) noexcept override
    {
        state_->calls.emplace_back(
            "application:" + std::string(absolute_path) +
            (temporary ? ":temporary" : ":persistent"));
        return application_manifest_status;
    }

    vrrec_status_t SetActionManifestPath(
        std::string_view absolute_path) noexcept override
    {
        state_->calls.emplace_back("manifest:" + std::string(absolute_path));
        return manifest_status;
    }

    vrrec_status_t GetActionSetHandle(
        std::string_view action_set_path,
        std::uint64_t &handle) noexcept override
    {
        state_->calls.emplace_back("set:" + std::string(action_set_path));
        handle = action_set_handle;
        return action_set_status;
    }

    vrrec_status_t GetDigitalActionHandle(
        std::string_view action_path,
        std::uint64_t &handle) noexcept override
    {
        state_->calls.emplace_back("action:" + std::string(action_path));
        handle = action_handle;
        return action_status;
    }

    vrrec_status_t UpdateActionState(
        std::uint64_t handle) noexcept override
    {
        state_->calls.emplace_back("update:" + std::to_string(handle));
        return update_status;
    }

    vrrec_status_t GetDigitalActionData(
        std::uint64_t handle,
        OpenVrDigitalActionData &data) noexcept override
    {
        state_->calls.emplace_back("digital:" + std::to_string(handle));
        data = digital_data;
        return digital_status;
    }

    void Shutdown() noexcept override
    {
        state_->calls.emplace_back("shutdown");
        ++state_->shutdown_calls;
    }

    vrrec_status_t initialize_status = VRREC_STATUS_OK;
    vrrec_status_t application_manifest_status = VRREC_STATUS_OK;
    vrrec_status_t manifest_status = VRREC_STATUS_OK;
    vrrec_status_t action_set_status = VRREC_STATUS_OK;
    vrrec_status_t action_status = VRREC_STATUS_OK;
    vrrec_status_t update_status = VRREC_STATUS_OK;
    vrrec_status_t digital_status = VRREC_STATUS_OK;
    std::uint64_t action_set_handle = 101;
    std::uint64_t action_handle = 202;
    OpenVrDigitalActionData digital_data {true, true, false};

private:
    std::shared_ptr<FakePortState> state_;
};

class FakeHapticPort final : public OpenVrHapticPort {
public:
    explicit FakeHapticPort(std::shared_ptr<FakePortState> state)
        : state_(std::move(state))
    {
    }

    vrrec_status_t Initialize() noexcept override
    {
        state_->calls.emplace_back("haptic-initialize");
        return initialize_status;
    }

    vrrec_status_t AddApplicationManifest(
        std::string_view absolute_path,
        bool temporary) noexcept override
    {
        state_->calls.emplace_back(
            "haptic-application:" + std::string(absolute_path) +
            (temporary ? ":temporary" : ":persistent"));
        return application_manifest_status;
    }

    vrrec_status_t SetActionManifestPath(
        std::string_view absolute_path) noexcept override
    {
        state_->calls.emplace_back(
            "haptic-manifest:" + std::string(absolute_path));
        return manifest_status;
    }

    vrrec_status_t GetHapticActionHandle(
        std::string_view action_path,
        std::uint64_t &handle) noexcept override
    {
        state_->calls.emplace_back(
            "haptic-action:" + std::string(action_path));
        handle = action_handle;
        return action_status;
    }

    vrrec_status_t GetInputSourceHandle(
        std::string_view input_source_path,
        std::uint64_t &handle) noexcept override
    {
        state_->calls.emplace_back(
            "haptic-source:" + std::string(input_source_path));
        handle = source_handle;
        return source_status;
    }

    vrrec_status_t TriggerHapticVibrationAction(
        std::uint64_t action,
        std::uint64_t source,
        const OpenVrHapticPulse &pulse) noexcept override
    {
        state_->calls.emplace_back(
            "haptic-trigger:" + std::to_string(action) + ':' +
            std::to_string(source) + ':' +
            std::to_string(pulse.duration_seconds));
        return trigger_status;
    }

    void Shutdown() noexcept override
    {
        state_->calls.emplace_back("haptic-shutdown");
        ++state_->shutdown_calls;
    }

    vrrec_status_t initialize_status = VRREC_STATUS_OK;
    vrrec_status_t application_manifest_status = VRREC_STATUS_OK;
    vrrec_status_t manifest_status = VRREC_STATUS_OK;
    vrrec_status_t action_status = VRREC_STATUS_OK;
    vrrec_status_t source_status = VRREC_STATUS_OK;
    vrrec_status_t trigger_status = VRREC_STATUS_OK;
    std::uint64_t action_handle = 303;
    std::uint64_t source_handle = 404;

private:
    std::shared_ptr<FakePortState> state_;
};

vrrec_steamvr_input_config_v1 Config()
{
    return vrrec_steamvr_input_config_v1 {
        sizeof(vrrec_steamvr_input_config_v1),
        VRREC_ABI_V1,
        "/install/OpenVr/actions.json",
        "/actions/main",
        "/actions/main/in/toggle_recording",
    };
}

std::unique_ptr<SteamVrInputBackend> CreateBackend(
    std::unique_ptr<FakePort> port,
    vrrec_status_t &status)
{
    return CreateOpenVrSteamVrInputBackend(
        Config(),
        std::move(port),
        status);
}

SteamVrHapticConfig HapticConfig()
{
    return SteamVrHapticConfig {
        "/install/OpenVr/actions.json",
        "/actions/vrrecorder/out/haptic",
        "/user/hand/right",
    };
}

void InitializesHapticBackendAndRoutesPulse()
{
    auto state = std::make_shared<FakePortState>();
    auto port = std::make_unique<FakeHapticPort>(state);
    auto *borrowed = port.get();
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto backend = CreateOpenVrSteamVrHapticBackend(
        HapticConfig(),
        std::move(port),
        status);

    CHECK(status == VRREC_STATUS_OK);
    CHECK(backend != nullptr);
    CHECK((state->calls == std::vector<std::string> {
        "haptic-initialize",
        "haptic-application:/install/OpenVr/steamvr.vrmanifest:temporary",
        "haptic-manifest:/install/OpenVr/actions.json",
        "haptic-action:/actions/vrrecorder/out/haptic",
        "haptic-source:/user/hand/right",
    }));
    CHECK(backend->Trigger(OpenVrHapticPulse {0.03F, 120.0F, 0.65F}) ==
          VRREC_STATUS_OK);
    CHECK(state->calls.back() == "haptic-trigger:303:404:0.030000");

    borrowed->trigger_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    CHECK(backend->Trigger(OpenVrHapticPulse {0.08F, 120.0F, 0.65F}) ==
          VRREC_STATUS_BACKEND_UNAVAILABLE);
    backend.reset();
    CHECK(state->shutdown_calls == 1);
    CHECK(state->calls.back() == "haptic-shutdown");
}

void HapticInitializationFailureReleasesAcquiredRuntime()
{
    for (std::size_t failure = 0; failure != 5; ++failure) {
        auto state = std::make_shared<FakePortState>();
        auto port = std::make_unique<FakeHapticPort>(state);
        if (failure == 0) {
            port->initialize_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
        } else if (failure == 1) {
            port->application_manifest_status = VRREC_STATUS_INTERNAL_ERROR;
        } else if (failure == 2) {
            port->manifest_status = VRREC_STATUS_INTERNAL_ERROR;
        } else if (failure == 3) {
            port->action_status = VRREC_STATUS_INTERNAL_ERROR;
        } else {
            port->source_status = VRREC_STATUS_INTERNAL_ERROR;
        }
        auto status = VRREC_STATUS_OK;
        auto backend = CreateOpenVrSteamVrHapticBackend(
            HapticConfig(),
            std::move(port),
            status);

        CHECK(backend == nullptr);
        CHECK(status == (failure == 0
            ? VRREC_STATUS_BACKEND_UNAVAILABLE
            : VRREC_STATUS_INTERNAL_ERROR));
        CHECK(state->shutdown_calls == (failure == 0 ? 0 : 1));
    }
}

void RejectsInvalidHapticCompositionInputs()
{
    auto state = std::make_shared<FakePortState>();
    auto invalid = HapticConfig();
    invalid.action_manifest_path = "/install/OpenVr/not-actions.json";
    auto status = VRREC_STATUS_OK;
    auto backend = CreateOpenVrSteamVrHapticBackend(
        invalid,
        std::make_unique<FakeHapticPort>(state),
        status);
    CHECK(backend == nullptr);
    CHECK(status == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(state->calls.empty());

    invalid = HapticConfig();
    invalid.input_source_path = {};
    backend = CreateOpenVrSteamVrHapticBackend(
        invalid,
        std::make_unique<FakeHapticPort>(state),
        status);
    CHECK(backend == nullptr);
    CHECK(status == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(state->calls.empty());

    backend = CreateOpenVrSteamVrHapticBackend(
        HapticConfig(),
        nullptr,
        status);
    CHECK(backend == nullptr);
    CHECK(status == VRREC_STATUS_INVALID_ARGUMENT);
}

void ResolvesSteamVrApplicationManifestPathBoundaries()
{
    std::string output;
    CHECK(ResolveSteamVrApplicationManifestPath("actions.json", output) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(ResolveSteamVrApplicationManifestPath(
              "C:\\app\\actions.json",
              output) == VRREC_STATUS_OK);
    CHECK(output == "C:\\app\\steamvr.vrmanifest");

    const std::string long_path =
        "C:\\" + std::string(1'024, 'a') + "\\actions.json";
    allocation_failure::fail_on_allocation = 1;
    const auto status =
        ResolveSteamVrApplicationManifestPath(long_path, output);
    allocation_failure::fail_on_allocation = 0;
    CHECK(status == VRREC_STATUS_OUT_OF_MEMORY);
}

void InitializesInRequiredOrderAndCanonicalizesDigitalState()
{
    auto state = std::make_shared<FakePortState>();
    auto port = std::make_unique<FakePort>(state);
    auto *borrowed = port.get();
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto backend = CreateBackend(std::move(port), status);

    CHECK(status == VRREC_STATUS_OK);
    CHECK(backend != nullptr);
    CHECK((state->calls == std::vector<std::string> {
        "initialize",
        "application:/install/OpenVr/steamvr.vrmanifest:temporary",
        "manifest:/install/OpenVr/actions.json",
        "set:/actions/main",
        "action:/actions/main/in/toggle_recording",
    }));

    auto output = vrrec_steamvr_digital_state_v1 {
        sizeof(vrrec_steamvr_digital_state_v1),
        VRREC_ABI_V1,
        9,
        9,
        9,
        9,
    };
    CHECK(backend->Poll(output) == VRREC_STATUS_OK);
    CHECK(output.is_active == 1);
    CHECK(output.state == 1);
    CHECK(output.changed == 0);
    CHECK(output.reserved == 0);
    CHECK(state->calls[5] == "update:101");
    CHECK(state->calls[6] == "digital:202");

    borrowed->digital_data = {false, false, true};
    CHECK(backend->Poll(output) == VRREC_STATUS_OK);
    CHECK(output.is_active == 0);
    CHECK(output.state == 0);
    CHECK(output.changed == 1);

    backend.reset();
    CHECK(state->shutdown_calls == 1);
    CHECK(state->calls.back() == "shutdown");
}

void FailsClosedAtEachInitializationBoundary()
{
    for (std::size_t failure = 0; failure != 4; ++failure) {
        auto state = std::make_shared<FakePortState>();
        auto port = std::make_unique<FakePort>(state);
        if (failure == 0) {
            port->initialize_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
        } else if (failure == 1) {
            port->application_manifest_status = VRREC_STATUS_INTERNAL_ERROR;
        } else if (failure == 2) {
            port->manifest_status = VRREC_STATUS_INTERNAL_ERROR;
        } else {
            port->action_set_status = VRREC_STATUS_INTERNAL_ERROR;
        }
        auto status = VRREC_STATUS_OK;
        auto backend = CreateBackend(std::move(port), status);

        CHECK(backend == nullptr);
        CHECK(status == (failure == 0
            ? VRREC_STATUS_BACKEND_UNAVAILABLE
            : VRREC_STATUS_INTERNAL_ERROR));
        CHECK(state->shutdown_calls == (failure == 0 ? 0 : 1));
    }

    auto state = std::make_shared<FakePortState>();
    auto port = std::make_unique<FakePort>(state);
    port->action_status = VRREC_STATUS_INTERNAL_ERROR;
    auto status = VRREC_STATUS_OK;
    auto backend = CreateBackend(std::move(port), status);
    CHECK(backend == nullptr);
    CHECK(status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(state->shutdown_calls == 1);
}

void RejectsZeroHandlesAndInvalidCompositionInputs()
{
    auto state = std::make_shared<FakePortState>();
    auto port = std::make_unique<FakePort>(state);
    port->action_set_handle = 0;
    auto status = VRREC_STATUS_OK;
    auto backend = CreateBackend(std::move(port), status);
    CHECK(backend == nullptr);
    CHECK(status == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(state->shutdown_calls == 1);

    auto invalid = Config();
    invalid.digital_action_path_utf8 = nullptr;
    state = std::make_shared<FakePortState>();
    backend = CreateOpenVrSteamVrInputBackend(
        invalid,
        std::make_unique<FakePort>(state),
        status);
    CHECK(backend == nullptr);
    CHECK(status == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(state->calls.empty());

    invalid = Config();
    invalid.action_manifest_path_utf8 = "/install/OpenVr/not-actions.json";
    state = std::make_shared<FakePortState>();
    backend = CreateOpenVrSteamVrInputBackend(
        invalid,
        std::make_unique<FakePort>(state),
        status);
    CHECK(backend == nullptr);
    CHECK(status == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(state->calls.empty());

    backend = CreateOpenVrSteamVrInputBackend(Config(), nullptr, status);
    CHECK(backend == nullptr);
    CHECK(status == VRREC_STATUS_INVALID_ARGUMENT);
}

void PollFailurePublishesOnlyAZeroState()
{
    auto state = std::make_shared<FakePortState>();
    auto port = std::make_unique<FakePort>(state);
    auto *borrowed = port.get();
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto backend = CreateBackend(std::move(port), status);
    auto output = vrrec_steamvr_digital_state_v1 {
        sizeof(vrrec_steamvr_digital_state_v1),
        VRREC_ABI_V1,
        1,
        1,
        1,
        1,
    };

    borrowed->update_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    CHECK(backend->Poll(output) == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(output.is_active == 0);
    CHECK(output.state == 0);
    CHECK(output.changed == 0);
    CHECK(output.reserved == 0);
    CHECK(state->calls.back() == "update:101");

    borrowed->update_status = VRREC_STATUS_OK;
    borrowed->digital_status = VRREC_STATUS_INTERNAL_ERROR;
    output.is_active = 1;
    output.state = 1;
    output.changed = 1;
    CHECK(backend->Poll(output) == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(output.is_active == 0);
    CHECK(output.state == 0);
    CHECK(output.changed == 0);
    CHECK(state->calls.back() == "digital:202");
}

}

int main()
{
    InitializesHapticBackendAndRoutesPulse();
    HapticInitializationFailureReleasesAcquiredRuntime();
    RejectsInvalidHapticCompositionInputs();
    ResolvesSteamVrApplicationManifestPathBoundaries();
    InitializesInRequiredOrderAndCanonicalizesDigitalState();
    FailsClosedAtEachInitializationBoundary();
    RejectsZeroHandlesAndInvalidCompositionInputs();
    PollFailurePublishesOnlyAZeroState();
    return 0;
}
