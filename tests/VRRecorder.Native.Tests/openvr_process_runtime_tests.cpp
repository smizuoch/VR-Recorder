#include "openvr_process_runtime.hpp"

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

struct RawState final {
    std::vector<std::string> calls;
    std::size_t initialize_calls = 0;
    std::size_t manifest_calls = 0;
    std::size_t shutdown_calls = 0;
};

class FakeRawApi final : public OpenVrInputPort {
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
        handle = 11;
        return VRREC_STATUS_OK;
    }

    vrrec_status_t GetDigitalActionHandle(
        std::string_view path,
        std::uint64_t &handle) noexcept override
    {
        state_->calls.emplace_back("action:" + std::string(path));
        handle = 22;
        return VRREC_STATUS_OK;
    }

    vrrec_status_t UpdateActionState(std::uint64_t handle) noexcept override
    {
        state_->calls.emplace_back("update:" + std::to_string(handle));
        return VRREC_STATUS_OK;
    }

    vrrec_status_t GetDigitalActionData(
        std::uint64_t handle,
        OpenVrDigitalActionData &data) noexcept override
    {
        state_->calls.emplace_back("digital:" + std::to_string(handle));
        data = {true, false, true};
        return VRREC_STATUS_OK;
    }

    void Shutdown() noexcept override
    {
        state_->calls.emplace_back("shutdown");
        ++state_->shutdown_calls;
    }

    vrrec_status_t initialize_status = VRREC_STATUS_OK;
    vrrec_status_t manifest_status = VRREC_STATUS_OK;

private:
    std::shared_ptr<RawState> state_;
};

std::shared_ptr<OpenVrProcessRuntime> Runtime(
    std::shared_ptr<RawState> state,
    FakeRawApi *&raw)
{
    auto api = std::make_unique<FakeRawApi>(state);
    raw = api.get();
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto runtime = CreateOpenVrProcessRuntime(std::move(api), status);
    CHECK(status == VRREC_STATUS_OK);
    CHECK(runtime != nullptr);
    return runtime;
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
    CHECK(first->SetActionManifestPath("C:/app/OpenVr/actions.json") ==
          VRREC_STATUS_OK);
    CHECK(second->Initialize() == VRREC_STATUS_OK);
    CHECK(second->SetActionManifestPath("C:/app/OpenVr/actions.json") ==
          VRREC_STATUS_OK);
    CHECK(state->initialize_calls == 1);
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
    CHECK(!data.state);
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
    CHECK(first->SetActionManifestPath("C:/one/actions.json") ==
          VRREC_STATUS_OK);
    CHECK(second->Initialize() == VRREC_STATUS_OK);
    CHECK(second->SetActionManifestPath("C:/two/actions.json") ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(state->manifest_calls == 1);
    CHECK(state->shutdown_calls == 0);
    second.reset();
    first.reset();
    CHECK(state->shutdown_calls == 1);
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
}

}

int main()
{
    SharesInitializationManifestAndFinalShutdown();
    RejectsManifestDriftWithoutCallingTheRawApiAgain();
    FailsClosedBeforeAcquiringAReference();
    return 0;
}
