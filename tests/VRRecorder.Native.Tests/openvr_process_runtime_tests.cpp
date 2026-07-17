#include "openvr_process_runtime.hpp"
#include "native_thread_factory.hpp"
#include "openvr_overlay_lifecycle_port.hpp"
#include "openvr_overlay_lifecycle.hpp"

#include <atomic>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <memory>
#include <new>
#include <string>
#include <string_view>
#include <thread>
#include <utility>
#include <vector>

namespace allocation_failure {

std::atomic<std::size_t> fail_on_allocation {0};

bool ShouldFail() noexcept
{
    auto remaining = fail_on_allocation.load();
    while (remaining != 0) {
        if (fail_on_allocation.compare_exchange_weak(
                remaining,
                remaining - 1)) {
            return remaining == 1;
        }
    }
    return false;
}

}

void *operator new(std::size_t size)
{
    if (allocation_failure::ShouldFail()) {
        throw std::bad_alloc {};
    }
    if (auto *allocation = std::malloc(size); allocation != nullptr) {
        return allocation;
    }
    throw std::bad_alloc {};
}

void *operator new(std::size_t size, const std::nothrow_t &) noexcept
{
    if (allocation_failure::ShouldFail()) {
        return nullptr;
    }
    return std::malloc(size);
}

void *operator new[](std::size_t size)
{
    return ::operator new(size);
}

void operator delete(void *allocation) noexcept
{
    std::free(allocation);
}

void operator delete[](void *allocation) noexcept
{
    ::operator delete(allocation);
}

void operator delete(void *allocation, std::size_t) noexcept
{
    ::operator delete(allocation);
}

void operator delete[](void *allocation, std::size_t) noexcept
{
    ::operator delete[](allocation);
}

void operator delete(
    void *allocation,
    const std::nothrow_t &) noexcept
{
    ::operator delete(allocation);
}

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

struct RawState final {
    std::vector<std::string> calls;
    std::size_t initialize_calls = 0;
    std::size_t application_manifest_calls = 0;
    std::size_t manifest_calls = 0;
    std::size_t update_calls = 0;
    std::size_t digital_calls = 0;
    std::size_t shutdown_calls = 0;
    std::size_t overlay_destroy_calls = 0;
    std::uint64_t generation = 0;
    std::uint64_t failing_digital_handle = 0;
    vrrec_status_t update_status = VRREC_STATUS_OK;
    std::vector<std::thread::id> polling_threads;
    OpenVrOverlayPointerEvent overlay_pointer_event {};
    bool has_overlay_pointer_event = false;
    OpenVrOverlayPose overlay_pose {};
    OpenVrDeviceProfile device_profile {
        "lighthouse",
        "Valve Index",
        "{indexcontroller}/input/index_controller_profile.json",
    };
};

class FakeRawApi final : public OpenVrRuntimePort {
public:
    explicit FakeRawApi(std::shared_ptr<RawState> state)
        : state_(std::move(state))
    {
    }

    vrrec_status_t Initialize() noexcept override
    {
        state_->calls.emplace_back("initialize");
        ++state_->initialize_calls;
        return initialize_status;
    }

    vrrec_status_t AddApplicationManifest(
        std::string_view path,
        bool temporary) noexcept override
    {
        state_->calls.emplace_back(
            "application:" + std::string(path) +
            (temporary ? ":temporary" : ":persistent"));
        ++state_->application_manifest_calls;
        return application_manifest_status;
    }

    vrrec_status_t CreateOverlay(
        std::string_view key,
        std::string_view name,
        std::uint64_t &handle) noexcept override
    {
        state_->calls.emplace_back(
            "overlay-create:" + std::string(key) + ':' + std::string(name));
        handle = 91;
        return overlay_create_status;
    }

    vrrec_status_t SetOverlayWidthInMeters(
        std::uint64_t handle,
        float width) noexcept override
    {
        state_->calls.emplace_back(
            "overlay-width:" + std::to_string(handle) + ':' +
            std::to_string(width));
        return overlay_width_status;
    }

    vrrec_status_t ShowOverlay(std::uint64_t handle) noexcept override
    {
        state_->calls.emplace_back(
            "overlay-show:" + std::to_string(handle));
        return VRREC_STATUS_OK;
    }

    vrrec_status_t HideOverlay(std::uint64_t handle) noexcept override
    {
        state_->calls.emplace_back(
            "overlay-hide:" + std::to_string(handle));
        return VRREC_STATUS_OK;
    }

    vrrec_status_t DestroyOverlay(std::uint64_t handle) noexcept override
    {
        state_->calls.emplace_back(
            "overlay-destroy:" + std::to_string(handle));
        ++state_->overlay_destroy_calls;
        return VRREC_STATUS_OK;
    }

    vrrec_status_t SetOverlayBgraTexture(
        std::uint64_t handle,
        const OpenVrBgraTextureFrame &frame) noexcept override
    {
        state_->calls.emplace_back(
            "overlay-texture:" + std::to_string(handle) + ':' +
            std::to_string(frame.width) + 'x' +
            std::to_string(frame.height) + ':' +
            std::to_string(frame.stride_bytes) + ':' +
            std::to_string(frame.pixel_bytes_size));
        return VRREC_STATUS_OK;
    }

    vrrec_status_t ClearOverlayTexture(
        std::uint64_t handle) noexcept override
    {
        state_->calls.emplace_back(
            "overlay-clear:" + std::to_string(handle));
        return VRREC_STATUS_OK;
    }

    vrrec_status_t ConfigureOverlayPointerInput(
        std::uint64_t handle,
        std::uint32_t pixel_width,
        std::uint32_t pixel_height) noexcept override
    {
        state_->calls.emplace_back(
            "overlay-input:" + std::to_string(handle) + ':' +
            std::to_string(pixel_width) + 'x' +
            std::to_string(pixel_height));
        return overlay_pointer_input_status;
    }

    vrrec_status_t PollNextOverlayPointerEvent(
        std::uint64_t handle,
        OpenVrOverlayPointerEvent &event,
        bool &has_event) noexcept override
    {
        state_->calls.emplace_back(
            "overlay-poll:" + std::to_string(handle));
        event = state_->overlay_pointer_event;
        has_event = state_->has_overlay_pointer_event;
        state_->has_overlay_pointer_event = false;
        return VRREC_STATUS_OK;
    }

    vrrec_status_t SetOverlayPose(
        std::uint64_t handle,
        const OpenVrOverlayPose &pose) noexcept override
    {
        state_->calls.emplace_back(
            "overlay-pose-set:" + std::to_string(handle));
        state_->overlay_pose = pose;
        return VRREC_STATUS_OK;
    }

    vrrec_status_t GetOverlayPose(
        std::uint64_t handle,
        OpenVrOverlayPose &pose) noexcept override
    {
        state_->calls.emplace_back(
            "overlay-pose-get:" + std::to_string(handle));
        pose = state_->overlay_pose;
        return VRREC_STATUS_OK;
    }

    vrrec_status_t ConvertOverlayPose(
        std::uint64_t handle,
        OpenVrOverlayPlacementMode target_mode,
        OpenVrHand hand,
        OpenVrOverlayPose &pose) noexcept override
    {
        state_->calls.emplace_back(
            "overlay-pose-convert:" + std::to_string(handle));
        pose = state_->overlay_pose;
        pose.placement_mode = target_mode;
        pose.hand = target_mode == OpenVrOverlayPlacementMode::WristDock
            ? hand
            : OpenVrHand::None;
        pose.tracking_origin =
            target_mode == OpenVrOverlayPlacementMode::WristDock
                ? OpenVrTrackingOrigin::None
                : OpenVrTrackingOrigin::Standing;
        return VRREC_STATUS_OK;
    }

    vrrec_status_t GetDeviceProfile(
        OpenVrHand hand,
        OpenVrDeviceProfile &profile) noexcept override
    {
        state_->calls.emplace_back(
            "profile-get:" + std::to_string(static_cast<std::uint32_t>(hand)));
        profile = state_->device_profile;
        return VRREC_STATUS_OK;
    }

    vrrec_status_t SetActionManifestPath(
        std::string_view path) noexcept override
    {
        state_->calls.emplace_back("manifest:" + std::string(path));
        ++state_->manifest_calls;
        return manifest_status;
    }

    vrrec_status_t GetActionSetHandle(
        std::string_view path,
        std::uint64_t &handle) noexcept override
    {
        state_->calls.emplace_back("set:" + std::string(path));
        handle = action_set_handle;
        return action_set_status;
    }

    vrrec_status_t GetDigitalActionHandle(
        std::string_view path,
        std::uint64_t &handle) noexcept override
    {
        state_->calls.emplace_back("action:" + std::string(path));
        handle = zero_digital_action_handle
            ? 0
            : (path.ends_with("/mic") ? 33 : 22);
        return digital_action_status;
    }

    vrrec_status_t GetHapticActionHandle(
        std::string_view path,
        std::uint64_t &handle) noexcept override
    {
        state_->calls.emplace_back("haptic-action:" + std::string(path));
        handle = haptic_action_handle;
        return haptic_action_status;
    }

    vrrec_status_t GetInputSourceHandle(
        std::string_view path,
        std::uint64_t &handle) noexcept override
    {
        state_->calls.emplace_back("source:" + std::string(path));
        handle = input_source_handle;
        return input_source_status;
    }

    vrrec_status_t TriggerHapticVibrationAction(
        std::uint64_t action_handle,
        std::uint64_t source_handle,
        const OpenVrHapticPulse &pulse) noexcept override
    {
        state_->calls.emplace_back(
            "haptic-trigger:" + std::to_string(action_handle) + ':' +
            std::to_string(source_handle) + ':' +
            std::to_string(pulse.duration_seconds) + ':' +
            std::to_string(pulse.frequency_hertz) + ':' +
            std::to_string(pulse.amplitude));
        return VRREC_STATUS_OK;
    }

    vrrec_status_t UpdateActionState(std::uint64_t handle) noexcept override
    {
        state_->calls.emplace_back("update:" + std::to_string(handle));
        ++state_->update_calls;
        ++state_->generation;
        state_->polling_threads.push_back(std::this_thread::get_id());
        return state_->update_status;
    }

    vrrec_status_t GetDigitalActionData(
        std::uint64_t handle,
        OpenVrDigitalActionData &data) noexcept override
    {
        state_->calls.emplace_back("digital:" + std::to_string(handle));
        ++state_->digital_calls;
        state_->polling_threads.push_back(std::this_thread::get_id());
        data = {true, state_->generation % 2 == 1, true};
        return handle == state_->failing_digital_handle
            ? VRREC_STATUS_INTERNAL_ERROR
            : VRREC_STATUS_OK;
    }

    void Shutdown() noexcept override
    {
        state_->calls.emplace_back("shutdown");
        ++state_->shutdown_calls;
    }

    vrrec_status_t initialize_status = VRREC_STATUS_OK;
    vrrec_status_t application_manifest_status = VRREC_STATUS_OK;
    vrrec_status_t overlay_create_status = VRREC_STATUS_OK;
    vrrec_status_t overlay_width_status = VRREC_STATUS_OK;
    vrrec_status_t overlay_pointer_input_status = VRREC_STATUS_OK;
    vrrec_status_t manifest_status = VRREC_STATUS_OK;
    vrrec_status_t action_set_status = VRREC_STATUS_OK;
    std::uint64_t action_set_handle = 11;
    vrrec_status_t digital_action_status = VRREC_STATUS_OK;
    bool zero_digital_action_handle = false;
    vrrec_status_t haptic_action_status = VRREC_STATUS_OK;
    std::uint64_t haptic_action_handle = 44;
    vrrec_status_t input_source_status = VRREC_STATUS_OK;
    std::uint64_t input_source_handle = 55;

private:
    std::shared_ptr<RawState> state_;
};

class ScriptedThreadFactory final : public NativeThreadFactoryPort {
public:
    vrrec_status_t Start(
        std::thread &thread,
        NativeThreadEntry entry,
        void *context) noexcept override
    {
        if (status == VRREC_STATUS_OK && publish_thread) {
            thread = std::thread(entry, context);
        }
        return status;
    }

    vrrec_status_t status = VRREC_STATUS_OK;
    bool publish_thread = true;
};

std::shared_ptr<OpenVrProcessRuntime> Runtime(
    std::shared_ptr<RawState> state,
    FakeRawApi *&raw,
    NativeThreadFactoryPort *thread_factory = nullptr)
{
    auto api = std::make_unique<FakeRawApi>(state);
    raw = api.get();
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto runtime = CreateOpenVrProcessRuntime(
        std::move(api),
        status,
        thread_factory);
    CHECK(status == VRREC_STATUS_OK);
    CHECK(runtime != nullptr);
    return runtime;
}

std::unique_ptr<OpenVrInputPort> Client(
    const std::shared_ptr<OpenVrProcessRuntime> &runtime);

std::unique_ptr<OpenVrHapticPort> HapticClient(
    const std::shared_ptr<OpenVrProcessRuntime> &runtime)
{
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto client = CreateOpenVrProcessHapticPort(runtime, status);
    CHECK(status == VRREC_STATUS_OK);
    CHECK(client != nullptr);
    return client;
}

void RoutesHapticsThroughTheSharedRuntimeGeneration()
{
    auto state = std::make_shared<RawState>();
    FakeRawApi *raw = nullptr;
    auto runtime = Runtime(state, raw);
    auto input = Client(runtime);
    auto haptic = HapticClient(runtime);

    CHECK(input->Initialize() == VRREC_STATUS_OK);
    CHECK(input->AddApplicationManifest(
              "C:/app/OpenVr/steamvr.vrmanifest",
              true) == VRREC_STATUS_OK);
    CHECK(input->SetActionManifestPath("C:/app/OpenVr/actions.json") ==
          VRREC_STATUS_OK);
    CHECK(haptic->Initialize() == VRREC_STATUS_OK);
    CHECK(haptic->AddApplicationManifest(
              "C:/app/OpenVr/steamvr.vrmanifest",
              true) == VRREC_STATUS_OK);
    CHECK(haptic->SetActionManifestPath("C:/app/OpenVr/actions.json") ==
          VRREC_STATUS_OK);

    auto action = std::uint64_t {0};
    auto source = std::uint64_t {0};
    CHECK(haptic->GetHapticActionHandle(
              "/actions/vrrecorder/out/haptic",
              action) == VRREC_STATUS_OK);
    CHECK(haptic->GetInputSourceHandle(
              "/user/hand/right",
              source) == VRREC_STATUS_OK);
    CHECK(haptic->TriggerHapticVibrationAction(
              action,
              source,
              OpenVrHapticPulse {0.03F, 120.0F, 0.65F}) ==
          VRREC_STATUS_OK);

    CHECK(state->initialize_calls == 1);
    CHECK(state->application_manifest_calls == 1);
    CHECK(state->manifest_calls == 1);
    CHECK(state->calls[state->calls.size() - 3] ==
          "haptic-action:/actions/vrrecorder/out/haptic");
    CHECK(state->calls[state->calls.size() - 2] ==
          "source:/user/hand/right");
    CHECK(state->calls.back() ==
          "haptic-trigger:44:55:0.030000:120.000000:0.650000");

    input.reset();
    CHECK(state->shutdown_calls == 0);
    haptic.reset();
    CHECK(state->shutdown_calls == 1);
    (void)raw;
}

void HapticPortRejectsInvalidPulseBeforeCallingTheRawApi()
{
    auto state = std::make_shared<RawState>();
    FakeRawApi *raw = nullptr;
    auto runtime = Runtime(state, raw);
    auto haptic = HapticClient(runtime);
    const auto valid = OpenVrHapticPulse {0.03F, 120.0F, 0.65F};

    CHECK(haptic->AddApplicationManifest(
              "C:/app/OpenVr/steamvr.vrmanifest",
              true) == VRREC_STATUS_INVALID_STATE);
    CHECK(haptic->SetActionManifestPath(
              "C:/app/OpenVr/actions.json") ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(haptic->TriggerHapticVibrationAction(44, 55, valid) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(haptic->Initialize() == VRREC_STATUS_OK);
    const auto before = state->calls.size();
    CHECK(haptic->TriggerHapticVibrationAction(
              0,
              55,
              valid) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(haptic->TriggerHapticVibrationAction(
              44,
              0,
              valid) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(haptic->TriggerHapticVibrationAction(
              44,
              55,
              OpenVrHapticPulse {0.0F, 120.0F, 0.65F}) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(haptic->TriggerHapticVibrationAction(
              44,
              55,
              OpenVrHapticPulse {0.03F, -1.0F, 0.65F}) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(haptic->TriggerHapticVibrationAction(
              44,
              55,
              OpenVrHapticPulse {0.03F, 120.0F, 1.01F}) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(state->calls.size() == before);
    haptic.reset();
    CHECK(state->shutdown_calls == 1);
    (void)raw;
}

void FansOutOneBackgroundPollRevisionToEveryActionClient()
{
    const auto test_thread = std::this_thread::get_id();
    auto state = std::make_shared<RawState>();
    FakeRawApi *raw = nullptr;
    auto runtime = Runtime(state, raw);
    auto recording = Client(runtime);
    auto microphone = Client(runtime);

    CHECK(recording->Initialize() == VRREC_STATUS_OK);
    CHECK(recording->AddApplicationManifest(
              "C:/app/OpenVr/steamvr.vrmanifest",
              true) == VRREC_STATUS_OK);
    CHECK(recording->SetActionManifestPath(
              "C:/app/OpenVr/actions.json") == VRREC_STATUS_OK);
    CHECK(microphone->Initialize() == VRREC_STATUS_OK);
    CHECK(microphone->AddApplicationManifest(
              "C:/app/OpenVr/steamvr.vrmanifest",
              true) == VRREC_STATUS_OK);
    CHECK(microphone->SetActionManifestPath(
              "C:/app/OpenVr/actions.json") == VRREC_STATUS_OK);

    std::uint64_t set = 0;
    std::uint64_t recording_action = 0;
    std::uint64_t microphone_action = 0;
    CHECK(recording->GetActionSetHandle("/actions/main", set) ==
          VRREC_STATUS_OK);
    CHECK(recording->GetDigitalActionHandle(
              "/actions/main/in/recording",
              recording_action) == VRREC_STATUS_OK);
    CHECK(microphone->GetActionSetHandle("/actions/main", set) ==
          VRREC_STATUS_OK);
    CHECK(microphone->GetDigitalActionHandle(
              "/actions/main/in/mic",
              microphone_action) == VRREC_STATUS_OK);

    CHECK(recording->UpdateActionState(set) == VRREC_STATUS_OK);
    OpenVrDigitalActionData recording_data {};
    CHECK(recording->GetDigitalActionData(
              recording_action,
              recording_data) == VRREC_STATUS_OK);
    CHECK(microphone->UpdateActionState(set) == VRREC_STATUS_OK);
    OpenVrDigitalActionData microphone_data {};
    CHECK(microphone->GetDigitalActionData(
              microphone_action,
              microphone_data) == VRREC_STATUS_OK);

    CHECK(state->update_calls == 1);
    CHECK(state->digital_calls == 2);
    CHECK(recording_data.state == microphone_data.state);
    CHECK(recording_data.changed == microphone_data.changed);
    CHECK(!state->polling_threads.empty());
    for (const auto thread : state->polling_threads) {
        CHECK(thread == state->polling_threads.front());
        CHECK(thread != test_thread);
    }

    CHECK(recording->UpdateActionState(set) == VRREC_STATUS_OK);
    CHECK(recording->GetDigitalActionData(
              recording_action,
              recording_data) == VRREC_STATUS_OK);
    CHECK(microphone->UpdateActionState(set) == VRREC_STATUS_OK);
    CHECK(microphone->GetDigitalActionData(
              microphone_action,
              microphone_data) == VRREC_STATUS_OK);
    CHECK(state->update_calls == 2);
    CHECK(state->digital_calls == 4);
    CHECK(recording_data.state == microphone_data.state);

    microphone.reset();
    recording.reset();
    CHECK(state->shutdown_calls == 1);
    (void)raw;
}

void FailsClosedWhenThePollThreadCannotBePublished()
{
    for (const auto scenario : {std::size_t {0}, std::size_t {1}}) {
        auto state = std::make_shared<RawState>();
        ScriptedThreadFactory thread_factory;
        thread_factory.status = scenario == 0
            ? VRREC_STATUS_OUT_OF_MEMORY
            : VRREC_STATUS_OK;
        thread_factory.publish_thread = scenario == 0;
        FakeRawApi *raw = nullptr;
        auto runtime = Runtime(state, raw, &thread_factory);
        auto client = Client(runtime);
        CHECK(client->Initialize() == VRREC_STATUS_OK);
        CHECK(client->AddApplicationManifest(
                  "C:/app/OpenVr/steamvr.vrmanifest",
                  true) == VRREC_STATUS_OK);
        CHECK(client->SetActionManifestPath(
                  "C:/app/OpenVr/actions.json") == VRREC_STATUS_OK);
        std::uint64_t set = 0;
        std::uint64_t action = 0;
        CHECK(client->GetActionSetHandle("/actions/main", set) ==
              VRREC_STATUS_OK);
        CHECK(client->GetDigitalActionHandle(
                  "/actions/main/in/recording",
                  action) == VRREC_STATUS_OK);

        CHECK(client->UpdateActionState(set) == (scenario == 0
            ? VRREC_STATUS_OUT_OF_MEMORY
            : VRREC_STATUS_INTERNAL_ERROR));
        CHECK(state->update_calls == 0);
        CHECK(state->digital_calls == 0);
        client.reset();
        CHECK(state->shutdown_calls == 1);
        (void)raw;
    }
}

void SharesPollFailuresAndIsolatesPerActionReadFailures()
{
    auto state = std::make_shared<RawState>();
    FakeRawApi *raw = nullptr;
    auto runtime = Runtime(state, raw);
    auto recording = Client(runtime);
    auto microphone = Client(runtime);
    CHECK(recording->Initialize() == VRREC_STATUS_OK);
    CHECK(recording->AddApplicationManifest(
              "C:/app/OpenVr/steamvr.vrmanifest",
              true) == VRREC_STATUS_OK);
    CHECK(recording->SetActionManifestPath(
              "C:/app/OpenVr/actions.json") == VRREC_STATUS_OK);
    CHECK(microphone->Initialize() == VRREC_STATUS_OK);

    std::uint64_t set = 0;
    std::uint64_t recording_action = 0;
    std::uint64_t microphone_action = 0;
    CHECK(recording->GetActionSetHandle("/actions/main", set) ==
          VRREC_STATUS_OK);
    CHECK(recording->GetDigitalActionHandle(
              "/actions/main/in/recording",
              recording_action) == VRREC_STATUS_OK);
    CHECK(microphone->GetActionSetHandle("/actions/main", set) ==
          VRREC_STATUS_OK);
    CHECK(microphone->GetDigitalActionHandle(
              "/actions/main/in/mic",
              microphone_action) == VRREC_STATUS_OK);

    state->update_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    CHECK(recording->UpdateActionState(set) ==
          VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(microphone->UpdateActionState(set) ==
          VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(state->update_calls == 1);
    CHECK(state->digital_calls == 0);

    state->update_status = VRREC_STATUS_OK;
    state->failing_digital_handle = microphone_action;
    CHECK(recording->UpdateActionState(set) == VRREC_STATUS_OK);
    OpenVrDigitalActionData recording_data {};
    CHECK(recording->GetDigitalActionData(
              recording_action,
              recording_data) == VRREC_STATUS_OK);
    CHECK(microphone->UpdateActionState(set) == VRREC_STATUS_OK);
    auto microphone_data = OpenVrDigitalActionData {true, true, true};
    CHECK(microphone->GetDigitalActionData(
              microphone_action,
              microphone_data) == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(!microphone_data.is_active);
    CHECK(!microphone_data.state);
    CHECK(!microphone_data.changed);
    CHECK(recording_data.is_active);
    CHECK(state->update_calls == 2);
    CHECK(state->digital_calls == 2);

    microphone.reset();
    recording.reset();
    (void)raw;
}

void SlowConsumerSkipsToTheLatestRevisionWithoutBlockingTheOwner()
{
    auto state = std::make_shared<RawState>();
    FakeRawApi *raw = nullptr;
    auto runtime = Runtime(state, raw);
    auto recording = Client(runtime);
    auto slow = Client(runtime);
    CHECK(recording->Initialize() == VRREC_STATUS_OK);
    CHECK(recording->AddApplicationManifest(
              "C:/app/OpenVr/steamvr.vrmanifest",
              true) == VRREC_STATUS_OK);
    CHECK(recording->SetActionManifestPath(
              "C:/app/OpenVr/actions.json") == VRREC_STATUS_OK);
    CHECK(slow->Initialize() == VRREC_STATUS_OK);

    std::uint64_t set = 0;
    std::uint64_t recording_action = 0;
    std::uint64_t slow_action = 0;
    CHECK(recording->GetActionSetHandle("/actions/main", set) ==
          VRREC_STATUS_OK);
    CHECK(recording->GetDigitalActionHandle(
              "/actions/main/in/recording",
              recording_action) == VRREC_STATUS_OK);
    CHECK(slow->GetActionSetHandle("/actions/main", set) ==
          VRREC_STATUS_OK);
    CHECK(slow->GetDigitalActionHandle(
              "/actions/main/in/mic",
              slow_action) == VRREC_STATUS_OK);

    auto latest = OpenVrDigitalActionData {};
    for (auto revision = 0; revision != 3; ++revision) {
        CHECK(recording->UpdateActionState(set) == VRREC_STATUS_OK);
        CHECK(recording->GetDigitalActionData(
                  recording_action,
                  latest) == VRREC_STATUS_OK);
    }
    CHECK(state->update_calls == 3);

    CHECK(slow->UpdateActionState(set) == VRREC_STATUS_OK);
    OpenVrDigitalActionData slow_data {};
    CHECK(slow->GetDigitalActionData(slow_action, slow_data) ==
          VRREC_STATUS_OK);
    CHECK(state->update_calls == 3);
    CHECK(slow_data.state == latest.state);
    CHECK(slow_data.changed == latest.changed);

    slow.reset();
    recording.reset();
    (void)raw;
}

void SharesOneRuntimeGenerationWithOverlayLifecycleClients()
{
    auto state = std::make_shared<RawState>();
    FakeRawApi *raw = nullptr;
    auto runtime = Runtime(state, raw);
    auto input = Client(runtime);
    CHECK(input->Initialize() == VRREC_STATUS_OK);

    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto overlay = CreateOpenVrProcessOverlayLifecyclePort(runtime, status);
    CHECK(status == VRREC_STATUS_OK);
    CHECK(overlay != nullptr);
    CHECK(state->initialize_calls == 1);

    std::uint64_t handle = 0;
    CHECK(overlay->CreateOverlay(
              "com.vrrecorder.desktop.wrist",
              "VR Recorder Wrist",
              handle) == VRREC_STATUS_OK);
    CHECK(handle == 91);
    CHECK(overlay->SetOverlayWidthInMeters(handle, 0.22F) ==
          VRREC_STATUS_OK);
    CHECK(overlay->ShowOverlay(handle) == VRREC_STATUS_OK);
    CHECK(overlay->HideOverlay(handle) == VRREC_STATUS_OK);
    CHECK(overlay->DestroyOverlay(handle) == VRREC_STATUS_OK);
    CHECK(state->overlay_destroy_calls == 1);

    input.reset();
    CHECK(state->shutdown_calls == 0);
    overlay.reset();
    CHECK(state->shutdown_calls == 1);
    CHECK(state->calls[state->calls.size() - 2] == "overlay-destroy:91");
    CHECK(state->calls.back() == "shutdown");
    (void)raw;
}

void RegistersTheApplicationBeforeComposingAnOverlay()
{
    auto state = std::make_shared<RawState>();
    FakeRawApi *raw = nullptr;
    auto runtime = Runtime(state, raw);
    const auto config = OpenVrOverlayLifecycleConfig {
        "com.vrrecorder.desktop.wrist",
        "VR Recorder Wrist",
        0.22F,
    };
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto overlay = CreateOpenVrProcessOverlayLifecycle(
        runtime,
        "C:/app/OpenVr/steamvr.vrmanifest",
        config,
        status);

    CHECK(status == VRREC_STATUS_OK);
    CHECK(overlay != nullptr);
    CHECK((state->calls == std::vector<std::string> {
        "initialize",
        "application:C:/app/OpenVr/steamvr.vrmanifest:temporary",
        "overlay-create:com.vrrecorder.desktop.wrist:VR Recorder Wrist",
        "overlay-width:91:0.220000",
        "overlay-input:91:1024x512",
    }));
    CHECK(overlay->Show() == VRREC_STATUS_OK);
    overlay.reset();
    CHECK(state->calls[state->calls.size() - 3] == "overlay-hide:91");
    CHECK(state->calls[state->calls.size() - 2] == "overlay-destroy:91");
    CHECK(state->calls.back() == "shutdown");
    CHECK(state->shutdown_calls == 1);
    (void)raw;
}

void RoutesOverlayTextureThroughTheSharedRuntimeGeneration()
{
    auto state = std::make_shared<RawState>();
    FakeRawApi *raw = nullptr;
    auto runtime = Runtime(state, raw);
    const auto config = OpenVrOverlayLifecycleConfig {
        "com.vrrecorder.desktop.wrist",
        "VR Recorder Wrist",
        0.22F,
    };
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto overlay = CreateOpenVrProcessOverlayLifecycle(
        runtime,
        "C:/app/OpenVr/steamvr.vrmanifest",
        config,
        status);
    std::vector<std::uint8_t> pixels(1024U * 512U * 4U, 0x5a);
    const auto frame = OpenVrBgraTextureFrame {
        pixels.data(),
        pixels.size(),
        1024,
        512,
        4096,
    };

    CHECK(status == VRREC_STATUS_OK);
    CHECK(overlay->UpdateBgraTexture(frame) == VRREC_STATUS_OK);
    CHECK(overlay->Show() == VRREC_STATUS_OK);
    overlay.reset();

    CHECK((state->calls == std::vector<std::string> {
        "initialize",
        "application:C:/app/OpenVr/steamvr.vrmanifest:temporary",
        "overlay-create:com.vrrecorder.desktop.wrist:VR Recorder Wrist",
        "overlay-width:91:0.220000",
        "overlay-input:91:1024x512",
        "overlay-texture:91:1024x512:4096:2097152",
        "overlay-show:91",
        "overlay-clear:91",
        "overlay-hide:91",
        "overlay-destroy:91",
        "shutdown",
    }));
    CHECK(state->shutdown_calls == 1);
    (void)raw;
}

void RoutesOverlayPointerEventsThroughTheSharedRuntimeGeneration()
{
    auto state = std::make_shared<RawState>();
    FakeRawApi *raw = nullptr;
    auto runtime = Runtime(state, raw);
    const auto config = OpenVrOverlayLifecycleConfig {
        "com.vrrecorder.desktop.wrist",
        "VR Recorder Wrist",
        0.22F,
    };
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto overlay = CreateOpenVrProcessOverlayLifecycle(
        runtime,
        "C:/app/OpenVr/steamvr.vrmanifest",
        config,
        status);
    state->overlay_pointer_event = OpenVrOverlayPointerEvent {
        OpenVrOverlayPointerEventKind::ButtonDown,
        700,
        300,
        1,
        4,
    };
    state->has_overlay_pointer_event = true;
    auto event = OpenVrOverlayPointerEvent {};
    auto has_event = false;

    CHECK(status == VRREC_STATUS_OK);
    CHECK(overlay->PollPointerEvent(event, has_event) == VRREC_STATUS_OK);
    CHECK(has_event);
    CHECK(event == state->overlay_pointer_event);
    CHECK((state->calls == std::vector<std::string> {
        "initialize",
        "application:C:/app/OpenVr/steamvr.vrmanifest:temporary",
        "overlay-create:com.vrrecorder.desktop.wrist:VR Recorder Wrist",
        "overlay-width:91:0.220000",
        "overlay-input:91:1024x512",
        "overlay-poll:91",
    }));
    overlay.reset();
    CHECK(state->shutdown_calls == 1);
    (void)raw;
}

void RoutesOverlayPoseThroughTheSharedRuntimeGeneration()
{
    auto state = std::make_shared<RawState>();
    FakeRawApi *raw = nullptr;
    auto runtime = Runtime(state, raw);
    const auto config = OpenVrOverlayLifecycleConfig {
        "com.vrrecorder.desktop.wrist",
        "VR Recorder Wrist",
        0.22F,
    };
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto overlay = CreateOpenVrProcessOverlayLifecycle(
        runtime,
        "C:/app/OpenVr/steamvr.vrmanifest",
        config,
        status);
    const auto pose = OpenVrOverlayPose {
        OpenVrOverlayPlacementMode::WristDock,
        OpenVrHand::Right,
        OpenVrTrackingOrigin::None,
        OpenVrMatrix34 {{
            1, 0, 0, -0.03F,
            0, 1, 0, 0.05F,
            0, 0, 1, -0.08F,
        }},
    };

    CHECK(status == VRREC_STATUS_OK);
    CHECK(overlay->SetPose(pose) == VRREC_STATUS_OK);
    auto readback = OpenVrOverlayPose {};
    CHECK(overlay->GetPose(readback) == VRREC_STATUS_OK);
    CHECK(readback == pose);
    auto profile = OpenVrDeviceProfile {};
    CHECK(overlay->GetDeviceProfile(OpenVrHand::Right, profile) ==
          VRREC_STATUS_OK);
    CHECK(profile == state->device_profile);
    CHECK(state->calls[state->calls.size() - 3] == "overlay-pose-set:91");
    CHECK(state->calls[state->calls.size() - 2] == "overlay-pose-get:91");
    CHECK(state->calls.back() == "profile-get:2");
    overlay.reset();
    CHECK(state->shutdown_calls == 1);
    (void)raw;
}

void ApplicationRegistrationFailureDoesNotCreateAnOverlay()
{
    auto state = std::make_shared<RawState>();
    FakeRawApi *raw = nullptr;
    auto runtime = Runtime(state, raw);
    raw->application_manifest_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    const auto config = OpenVrOverlayLifecycleConfig {
        "com.vrrecorder.desktop.wrist",
        "VR Recorder Wrist",
        0.22F,
    };
    auto status = VRREC_STATUS_OK;
    auto overlay = CreateOpenVrProcessOverlayLifecycle(
        runtime,
        "C:/app/OpenVr/steamvr.vrmanifest",
        config,
        status);

    CHECK(overlay == nullptr);
    CHECK(status == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK((state->calls == std::vector<std::string> {
        "initialize",
        "application:C:/app/OpenVr/steamvr.vrmanifest:temporary",
        "shutdown",
    }));
    CHECK(state->overlay_destroy_calls == 0);
    (void)raw;
}

void FailsClosedAtEveryOverlayCompositionStage()
{
    const auto config = OpenVrOverlayLifecycleConfig {
        "com.vrrecorder.desktop.wrist",
        "VR Recorder Wrist",
        0.22F,
    };

    {
        auto state = std::make_shared<RawState>();
        FakeRawApi *raw = nullptr;
        auto runtime = Runtime(state, raw);
        raw->initialize_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
        auto status = VRREC_STATUS_OK;
        CHECK(CreateOpenVrProcessOverlayLifecyclePort(runtime, status) ==
              nullptr);
        CHECK(status == VRREC_STATUS_BACKEND_UNAVAILABLE);
        CHECK(state->shutdown_calls == 0);
    }

    for (const auto failed_stage : {0, 1, 2}) {
        auto state = std::make_shared<RawState>();
        FakeRawApi *raw = nullptr;
        auto runtime = Runtime(state, raw);
        if (failed_stage == 0) {
            raw->overlay_create_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
        } else if (failed_stage == 1) {
            raw->overlay_width_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
        } else {
            raw->overlay_pointer_input_status =
                VRREC_STATUS_BACKEND_UNAVAILABLE;
        }
        auto status = VRREC_STATUS_OK;
        auto overlay = CreateOpenVrProcessOverlayLifecycle(
            runtime,
            "C:/app/OpenVr/steamvr.vrmanifest",
            config,
            status);
        CHECK(overlay == nullptr);
        CHECK(status == VRREC_STATUS_BACKEND_UNAVAILABLE);
        CHECK(state->shutdown_calls == 1);
    }
}

std::unique_ptr<OpenVrInputPort> Client(
    const std::shared_ptr<OpenVrProcessRuntime> &runtime)
{
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto client = CreateOpenVrProcessInputPort(runtime, status);
    CHECK(status == VRREC_STATUS_OK);
    CHECK(client != nullptr);
    return client;
}

void SharesInitializationManifestAndFinalShutdown()
{
    auto state = std::make_shared<RawState>();
    FakeRawApi *raw = nullptr;
    auto runtime = Runtime(state, raw);
    auto first = Client(runtime);
    auto second = Client(runtime);

    CHECK(first->Initialize() == VRREC_STATUS_OK);
    CHECK(first->AddApplicationManifest(
              "C:/app/OpenVr/steamvr.vrmanifest",
              true) == VRREC_STATUS_OK);
    CHECK(first->SetActionManifestPath("C:/app/OpenVr/actions.json") ==
          VRREC_STATUS_OK);
    CHECK(second->Initialize() == VRREC_STATUS_OK);
    CHECK(second->AddApplicationManifest(
              "C:/app/OpenVr/steamvr.vrmanifest",
              true) == VRREC_STATUS_OK);
    CHECK(second->SetActionManifestPath("C:/app/OpenVr/actions.json") ==
          VRREC_STATUS_OK);
    CHECK(state->initialize_calls == 1);
    CHECK(state->application_manifest_calls == 1);
    CHECK(state->manifest_calls == 1);

    std::uint64_t set = 0;
    std::uint64_t action = 0;
    CHECK(second->GetActionSetHandle("/actions/main", set) ==
          VRREC_STATUS_OK);
    CHECK(second->GetDigitalActionHandle("/actions/main/in/toggle", action) ==
          VRREC_STATUS_OK);
    CHECK(second->UpdateActionState(set) == VRREC_STATUS_OK);
    OpenVrDigitalActionData data {};
    CHECK(second->GetDigitalActionData(action, data) == VRREC_STATUS_OK);
    CHECK(data.is_active);
    CHECK(data.state);
    CHECK(data.changed);

    first->Shutdown();
    first->Shutdown();
    CHECK(state->shutdown_calls == 0);
    second.reset();
    CHECK(state->shutdown_calls == 1);
    runtime.reset();
    CHECK(state->shutdown_calls == 1);
    (void)raw;
}

void RejectsManifestDriftWithoutCallingTheRawApiAgain()
{
    auto state = std::make_shared<RawState>();
    FakeRawApi *raw = nullptr;
    auto runtime = Runtime(state, raw);
    auto first = Client(runtime);
    auto second = Client(runtime);
    CHECK(first->Initialize() == VRREC_STATUS_OK);
    CHECK(first->AddApplicationManifest(
              "C:/one/steamvr.vrmanifest",
              true) == VRREC_STATUS_OK);
    CHECK(first->SetActionManifestPath("C:/one/actions.json") ==
          VRREC_STATUS_OK);
    CHECK(second->Initialize() == VRREC_STATUS_OK);
    CHECK(second->AddApplicationManifest(
              "C:/two/steamvr.vrmanifest",
              true) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(second->SetActionManifestPath("C:/two/actions.json") ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(state->application_manifest_calls == 1);
    CHECK(state->manifest_calls == 1);
    CHECK(state->shutdown_calls == 0);
    second.reset();
    first.reset();
    CHECK(state->shutdown_calls == 1);
    (void)raw;
}

void RegistersAgainOnlyAfterACompleteRuntimeGeneration()
{
    auto state = std::make_shared<RawState>();
    FakeRawApi *raw = nullptr;
    auto runtime = Runtime(state, raw);
    auto first = Client(runtime);
    CHECK(first->Initialize() == VRREC_STATUS_OK);
    CHECK(first->AddApplicationManifest(
              "C:/app/OpenVr/steamvr.vrmanifest",
              true) == VRREC_STATUS_OK);
    first.reset();

    auto second = Client(runtime);
    CHECK(second->Initialize() == VRREC_STATUS_OK);
    CHECK(second->AddApplicationManifest(
              "C:/app/OpenVr/steamvr.vrmanifest",
              true) == VRREC_STATUS_OK);
    second.reset();

    CHECK(state->initialize_calls == 2);
    CHECK(state->application_manifest_calls == 2);
    CHECK(state->shutdown_calls == 2);
    (void)raw;
}

void RejectsClientStateAndZeroRawHandles()
{
    auto state = std::make_shared<RawState>();
    FakeRawApi *raw = nullptr;
    auto runtime = Runtime(state, raw);
    auto input = Client(runtime);
    std::uint64_t set = 99;
    std::uint64_t action = 99;
    OpenVrDigitalActionData data {true, true, true};

    CHECK(input->AddApplicationManifest("C:/app/app.vrmanifest", true) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(input->GetActionSetHandle("/actions/main", set) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(input->GetDigitalActionHandle("/actions/main/in/rec", action) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(input->UpdateActionState(11) == VRREC_STATUS_INVALID_STATE);
    CHECK(input->GetDigitalActionData(22, data) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(!data.is_active && !data.state && !data.changed);

    CHECK(input->Initialize() == VRREC_STATUS_OK);
    CHECK(input->Initialize() == VRREC_STATUS_INVALID_STATE);
    raw->manifest_status = VRREC_STATUS_INTERNAL_ERROR;
    CHECK(input->SetActionManifestPath("C:/app/actions.json") ==
          VRREC_STATUS_INTERNAL_ERROR);
    raw->manifest_status = VRREC_STATUS_OK;
    CHECK(input->SetActionManifestPath("C:/app/actions.json") ==
          VRREC_STATUS_OK);
    CHECK(input->AddApplicationManifest("C:/app/app.vrmanifest", true) ==
          VRREC_STATUS_OK);
    CHECK(input->AddApplicationManifest("C:/app/app.vrmanifest", false) ==
          VRREC_STATUS_INVALID_ARGUMENT);

    raw->action_set_status = VRREC_STATUS_INTERNAL_ERROR;
    CHECK(input->GetActionSetHandle("/actions/main", set) ==
          VRREC_STATUS_INTERNAL_ERROR);
    raw->action_set_status = VRREC_STATUS_OK;
    raw->action_set_handle = 0;
    CHECK(input->GetActionSetHandle("/actions/main", set) ==
          VRREC_STATUS_INTERNAL_ERROR);
    CHECK(set == 0);
    raw->action_set_handle = 11;
    CHECK(input->GetActionSetHandle("/actions/main", set) ==
          VRREC_STATUS_OK);
    raw->action_set_handle = 12;
    CHECK(input->GetActionSetHandle("/actions/main", set) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(set == 0);
    raw->action_set_handle = 11;

    raw->digital_action_status = VRREC_STATUS_INTERNAL_ERROR;
    CHECK(input->GetDigitalActionHandle("/actions/main/in/rec", action) ==
          VRREC_STATUS_INTERNAL_ERROR);
    raw->digital_action_status = VRREC_STATUS_OK;
    raw->zero_digital_action_handle = true;
    CHECK(input->GetDigitalActionHandle("/actions/main/in/rec", action) ==
          VRREC_STATUS_INTERNAL_ERROR);
    CHECK(action == 0);
    raw->zero_digital_action_handle = false;
    CHECK(input->GetDigitalActionHandle("/actions/main/in/rec", action) ==
          VRREC_STATUS_OK);
    CHECK(input->UpdateActionState(0) == VRREC_STATUS_INVALID_STATE);
    CHECK(input->UpdateActionState(12) == VRREC_STATUS_INVALID_STATE);
    CHECK(input->UpdateActionState(11) == VRREC_STATUS_OK);
    CHECK(input->GetDigitalActionData(action + 1, data) ==
          VRREC_STATUS_INVALID_STATE);
    input->Shutdown();
    CHECK(input->SetActionManifestPath("C:/app/actions.json") ==
          VRREC_STATUS_INVALID_STATE);

    auto haptic = HapticClient(runtime);
    std::uint64_t handle = 99;
    CHECK(haptic->GetHapticActionHandle("/actions/main/out/haptic", handle) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(haptic->GetInputSourceHandle("/user/hand/right", handle) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(haptic->Initialize() == VRREC_STATUS_OK);
    CHECK(haptic->Initialize() == VRREC_STATUS_INVALID_STATE);
    raw->haptic_action_status = VRREC_STATUS_INTERNAL_ERROR;
    CHECK(haptic->GetHapticActionHandle(
              "/actions/main/out/haptic",
              handle) == VRREC_STATUS_INTERNAL_ERROR);
    raw->haptic_action_status = VRREC_STATUS_OK;
    raw->haptic_action_handle = 0;
    CHECK(haptic->GetHapticActionHandle(
              "/actions/main/out/haptic",
              handle) == VRREC_STATUS_INTERNAL_ERROR);
    raw->input_source_status = VRREC_STATUS_INTERNAL_ERROR;
    CHECK(haptic->GetInputSourceHandle("/user/hand/right", handle) ==
          VRREC_STATUS_INTERNAL_ERROR);
    raw->input_source_status = VRREC_STATUS_OK;
    raw->input_source_handle = 0;
    CHECK(haptic->GetInputSourceHandle("/user/hand/right", handle) ==
          VRREC_STATUS_INTERNAL_ERROR);
    haptic->Shutdown();
    haptic->Shutdown();
}

void RejectsInvalidOverlayFactoryArguments()
{
    auto status = VRREC_STATUS_OK;
    CHECK(CreateOpenVrProcessOverlayLifecyclePort(nullptr, status) == nullptr);
    CHECK(status == VRREC_STATUS_INVALID_ARGUMENT);
    const auto config = OpenVrOverlayLifecycleConfig {
        "com.vrrecorder.desktop.wrist",
        "VR Recorder Wrist",
        0.22F,
    };
    CHECK(CreateOpenVrProcessOverlayLifecycle(
              nullptr,
              "C:/app/OpenVr/steamvr.vrmanifest",
              config,
              status) == nullptr);
    CHECK(status == VRREC_STATUS_INVALID_ARGUMENT);

    auto state = std::make_shared<RawState>();
    FakeRawApi *raw = nullptr;
    auto runtime = Runtime(state, raw);
    CHECK(CreateOpenVrProcessOverlayLifecycle(
              runtime,
              "",
              config,
              status) == nullptr);
    CHECK(status == VRREC_STATUS_INVALID_ARGUMENT);
    (void)raw;
}

void FailsClosedBeforeAcquiringAReference()
{
    auto state = std::make_shared<RawState>();
    FakeRawApi *raw = nullptr;
    auto runtime = Runtime(state, raw);
    raw->initialize_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    auto client = Client(runtime);
    CHECK(client->Initialize() == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(client->SetActionManifestPath("C:/app/actions.json") ==
          VRREC_STATUS_INVALID_STATE);
    client.reset();
    CHECK(state->shutdown_calls == 0);

    auto status = VRREC_STATUS_OK;
    CHECK(CreateOpenVrProcessRuntime(nullptr, status) == nullptr);
    CHECK(status == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(CreateOpenVrProcessInputPort(nullptr, status) == nullptr);
    CHECK(status == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(CreateOpenVrProcessHapticPort(nullptr, status) == nullptr);
    CHECK(status == VRREC_STATUS_INVALID_ARGUMENT);
}

void ReportsOutOfMemoryFromProcessRuntimeFactories()
{
    auto state = std::make_shared<RawState>();
    auto api = std::make_unique<FakeRawApi>(state);
    auto status = VRREC_STATUS_OK;
    allocation_failure::fail_on_allocation = 1;
    auto failed_runtime = CreateOpenVrProcessRuntime(
        std::move(api),
        status);
    allocation_failure::fail_on_allocation = 0;
    CHECK(failed_runtime == nullptr);
    CHECK(status == VRREC_STATUS_OUT_OF_MEMORY);

    FakeRawApi *raw = nullptr;
    auto runtime = Runtime(state, raw);

    allocation_failure::fail_on_allocation = 1;
    auto failed_input = CreateOpenVrProcessInputPort(runtime, status);
    allocation_failure::fail_on_allocation = 0;
    CHECK(failed_input == nullptr);
    CHECK(status == VRREC_STATUS_OUT_OF_MEMORY);

    allocation_failure::fail_on_allocation = 1;
    auto failed_haptic = CreateOpenVrProcessHapticPort(runtime, status);
    allocation_failure::fail_on_allocation = 0;
    CHECK(failed_haptic == nullptr);
    CHECK(status == VRREC_STATUS_OUT_OF_MEMORY);

    allocation_failure::fail_on_allocation = 1;
    auto failed_overlay = CreateOpenVrProcessOverlayLifecyclePort(
        runtime,
        status);
    allocation_failure::fail_on_allocation = 0;
    CHECK(failed_overlay == nullptr);
    CHECK(status == VRREC_STATUS_OUT_OF_MEMORY);
    (void)raw;
}

}

int main()
{
    SharesInitializationManifestAndFinalShutdown();
    RejectsManifestDriftWithoutCallingTheRawApiAgain();
    RegistersAgainOnlyAfterACompleteRuntimeGeneration();
    FansOutOneBackgroundPollRevisionToEveryActionClient();
    FailsClosedWhenThePollThreadCannotBePublished();
    SharesPollFailuresAndIsolatesPerActionReadFailures();
    SlowConsumerSkipsToTheLatestRevisionWithoutBlockingTheOwner();
    SharesOneRuntimeGenerationWithOverlayLifecycleClients();
    RegistersTheApplicationBeforeComposingAnOverlay();
    RoutesOverlayTextureThroughTheSharedRuntimeGeneration();
    RoutesOverlayPointerEventsThroughTheSharedRuntimeGeneration();
    RoutesOverlayPoseThroughTheSharedRuntimeGeneration();
    RoutesHapticsThroughTheSharedRuntimeGeneration();
    HapticPortRejectsInvalidPulseBeforeCallingTheRawApi();
    ApplicationRegistrationFailureDoesNotCreateAnOverlay();
    FailsClosedAtEveryOverlayCompositionStage();
    RejectsClientStateAndZeroRawHandles();
    RejectsInvalidOverlayFactoryArguments();
    FailsClosedBeforeAcquiringAReference();
    ReportsOutOfMemoryFromProcessRuntimeFactories();
    return 0;
}
