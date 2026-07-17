#include "openvr_overlay_texture_presenter.hpp"

#include "allocation_failure_test_support.hpp"

#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <memory>
#include <string>
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

struct FakeState final {
    std::vector<std::string> calls;
};

class FakeResource final : public OpenVrOverlayTextureGraphicsResource {
public:
    FakeResource(std::shared_ptr<FakeState> state, std::uint32_t id) noexcept
        : state_(std::move(state)), id_(id)
    {
    }

    ~FakeResource() override
    {
        state_->calls.emplace_back("release:" + std::to_string(id_));
    }

    std::uint32_t Id() const noexcept
    {
        return id_;
    }

private:
    std::shared_ptr<FakeState> state_;
    std::uint32_t id_;
};

class FakeGraphicsPort final : public OpenVrOverlayTextureGraphicsPort {
public:
    explicit FakeGraphicsPort(std::shared_ptr<FakeState> state) noexcept
        : state_(std::move(state))
    {
    }

    vrrec_status_t CreateBgraTexture(
        std::uint32_t width,
        std::uint32_t height,
        std::unique_ptr<OpenVrOverlayTextureGraphicsResource> &resource)
        noexcept override
    {
        resource.reset();
        state_->calls.emplace_back(
            "create:" + std::to_string(width) + 'x' +
            std::to_string(height));
        if (create_status != VRREC_STATUS_OK) {
            return create_status;
        }
        if (!publish_resource) {
            return VRREC_STATUS_OK;
        }
        resource = std::make_unique<FakeResource>(state_, next_id++);
        return VRREC_STATUS_OK;
    }

    vrrec_status_t UploadBgraTexture(
        OpenVrOverlayTextureGraphicsResource &resource,
        const OpenVrBgraTextureFrame &frame) noexcept override
    {
        const auto &fake = static_cast<const FakeResource &>(resource);
        state_->calls.emplace_back(
            "upload:" + std::to_string(fake.Id()) + ':' +
            std::to_string(frame.pixel_bytes_size));
        return upload_status;
    }

    vrrec_status_t SubmitOverlayTexture(
        std::uint64_t handle,
        OpenVrOverlayTextureGraphicsResource &resource) noexcept override
    {
        const auto &fake = static_cast<const FakeResource &>(resource);
        state_->calls.emplace_back(
            "submit:" + std::to_string(handle) + ':' +
            std::to_string(fake.Id()));
        return submit_status;
    }

    vrrec_status_t ClearOverlayTexture(
        std::uint64_t handle) noexcept override
    {
        state_->calls.emplace_back("clear:" + std::to_string(handle));
        return clear_status;
    }

    vrrec_status_t create_status = VRREC_STATUS_OK;
    vrrec_status_t upload_status = VRREC_STATUS_OK;
    vrrec_status_t submit_status = VRREC_STATUS_OK;
    vrrec_status_t clear_status = VRREC_STATUS_OK;
    bool publish_resource = true;

private:
    std::shared_ptr<FakeState> state_;
    std::uint32_t next_id = 1;
};

OpenVrBgraTextureFrame Frame(std::vector<std::uint8_t> &pixels)
{
    return OpenVrBgraTextureFrame {
        pixels.data(),
        pixels.size(),
        1024,
        512,
        4096,
    };
}

void CreatesOnceUploadsEveryFrameAndReleasesAfterClear()
{
    auto state = std::make_shared<FakeState>();
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto presenter = CreateOpenVrOverlayTexturePresenter(
        std::make_unique<FakeGraphicsPort>(state),
        status);
    std::vector<std::uint8_t> pixels(1024U * 512U * 4U);

    CHECK(status == VRREC_STATUS_OK);
    CHECK(presenter->SetOverlayBgraTexture(47, Frame(pixels)) ==
          VRREC_STATUS_OK);
    pixels.front() = 1;
    CHECK(presenter->SetOverlayBgraTexture(47, Frame(pixels)) ==
          VRREC_STATUS_OK);
    CHECK(presenter->ClearOverlayTexture(47) == VRREC_STATUS_OK);
    CHECK(presenter->ClearOverlayTexture(47) == VRREC_STATUS_OK);
    CHECK((state->calls == std::vector<std::string> {
        "create:1024x512",
        "upload:1:2097152",
        "submit:47:1",
        "upload:1:2097152",
        "submit:47:1",
        "clear:47",
        "release:1",
    }));
}

void DropsAnUnsubmittedResourceAfterUploadOrSubmitFailure()
{
    for (const auto fail_upload : {true, false}) {
        auto state = std::make_shared<FakeState>();
        auto port = std::make_unique<FakeGraphicsPort>(state);
        if (fail_upload) {
            port->upload_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
        } else {
            port->submit_status = VRREC_STATUS_INTERNAL_ERROR;
        }
        auto status = VRREC_STATUS_INTERNAL_ERROR;
        auto presenter = CreateOpenVrOverlayTexturePresenter(
            std::move(port),
            status);
        std::vector<std::uint8_t> pixels(1024U * 512U * 4U);

        CHECK(presenter->SetOverlayBgraTexture(47, Frame(pixels)) ==
              (fail_upload
                  ? VRREC_STATUS_BACKEND_UNAVAILABLE
                  : VRREC_STATUS_INTERNAL_ERROR));
        CHECK(state->calls.back() == "release:1");
        CHECK(presenter->ClearOverlayTexture(47) == VRREC_STATUS_OK);
    }
}

void RetainsASubmittedResourceUntilClearSucceeds()
{
    auto state = std::make_shared<FakeState>();
    auto port = std::make_unique<FakeGraphicsPort>(state);
    auto *raw = port.get();
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto presenter = CreateOpenVrOverlayTexturePresenter(
        std::move(port),
        status);
    std::vector<std::uint8_t> pixels(1024U * 512U * 4U);
    CHECK(presenter->SetOverlayBgraTexture(47, Frame(pixels)) ==
          VRREC_STATUS_OK);
    raw->clear_status = VRREC_STATUS_BACKEND_UNAVAILABLE;

    CHECK(presenter->ClearOverlayTexture(47) ==
          VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(state->calls.back() == "clear:47");
    raw->clear_status = VRREC_STATUS_OK;
    CHECK(presenter->ClearOverlayTexture(47) == VRREC_STATUS_OK);
    CHECK(state->calls.back() == "release:1");
}

void RetainsASubmittedResourceAfterANewerUploadFails()
{
    auto state = std::make_shared<FakeState>();
    auto port = std::make_unique<FakeGraphicsPort>(state);
    auto *raw = port.get();
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto presenter = CreateOpenVrOverlayTexturePresenter(
        std::move(port),
        status);
    std::vector<std::uint8_t> pixels(1024U * 512U * 4U);
    CHECK(presenter->SetOverlayBgraTexture(47, Frame(pixels)) ==
          VRREC_STATUS_OK);
    raw->upload_status = VRREC_STATUS_BACKEND_UNAVAILABLE;

    CHECK(presenter->SetOverlayBgraTexture(47, Frame(pixels)) ==
          VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(state->calls.back() == "upload:1:2097152");
    CHECK(presenter->ClearOverlayTexture(47) == VRREC_STATUS_OK);
    CHECK(state->calls[state->calls.size() - 2] == "clear:47");
    CHECK(state->calls.back() == "release:1");
}

void RejectsUnsafeFramesBeforeTouchingGraphics()
{
    auto state = std::make_shared<FakeState>();
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto presenter = CreateOpenVrOverlayTexturePresenter(
        std::make_unique<FakeGraphicsPort>(state),
        status);
    std::vector<std::uint8_t> pixels(1024U * 512U * 4U);
    auto frame = Frame(pixels);

    CHECK(presenter->SetOverlayBgraTexture(0, frame) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    for (const auto invalid_case : {0, 1, 2, 3, 4}) {
        frame = Frame(pixels);
        if (invalid_case == 0) {
            frame.pixel_bytes = nullptr;
        } else if (invalid_case == 1) {
            --frame.pixel_bytes_size;
        } else if (invalid_case == 2) {
            --frame.width;
        } else if (invalid_case == 3) {
            --frame.height;
        } else {
            --frame.stride_bytes;
        }
        CHECK(presenter->SetOverlayBgraTexture(47, frame) ==
              VRREC_STATUS_INVALID_ARGUMENT);
    }
    CHECK(state->calls.empty());
    CHECK(presenter->ClearOverlayTexture(0) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(CreateOpenVrOverlayTexturePresenter(nullptr, status) == nullptr);
    CHECK(status == VRREC_STATUS_INVALID_ARGUMENT);
}

void RejectsTextureCreationContractFailuresAndFactoryOom()
{
    std::vector<std::uint8_t> pixels(1024U * 512U * 4U);
    for (const auto missing_resource : {false, true}) {
        auto state = std::make_shared<FakeState>();
        auto graphics = std::make_unique<FakeGraphicsPort>(state);
        auto *raw = graphics.get();
        if (missing_resource) {
            raw->publish_resource = false;
        } else {
            raw->create_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
        }
        auto status = VRREC_STATUS_INTERNAL_ERROR;
        auto presenter = CreateOpenVrOverlayTexturePresenter(
            std::move(graphics), status);
        CHECK(presenter != nullptr);
        const auto expected = missing_resource
            ? VRREC_STATUS_INTERNAL_ERROR
            : VRREC_STATUS_BACKEND_UNAVAILABLE;
        CHECK(presenter->SetOverlayBgraTexture(47, Frame(pixels)) == expected);
        CHECK(presenter->ClearOverlayTexture(47) == VRREC_STATUS_OK);
    }

    auto state = std::make_shared<FakeState>();
    auto graphics = std::make_unique<FakeGraphicsPort>(state);
    auto status = VRREC_STATUS_OK;
    allocation_failure::fail_on_allocation = 1;
    auto presenter = CreateOpenVrOverlayTexturePresenter(
        std::move(graphics), status);
    allocation_failure::fail_on_allocation = 0;
    CHECK(presenter == nullptr);
    CHECK(status == VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(state->calls.empty());
}

void ReleasesResourcesWhenTheRuntimeShutsDown()
{
    auto state = std::make_shared<FakeState>();
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto presenter = CreateOpenVrOverlayTexturePresenter(
        std::make_unique<FakeGraphicsPort>(state),
        status);
    std::vector<std::uint8_t> pixels(1024U * 512U * 4U);
    CHECK(presenter->SetOverlayBgraTexture(47, Frame(pixels)) ==
          VRREC_STATUS_OK);

    presenter.reset();
    CHECK(state->calls.back() == "release:1");
}

}

int main()
{
    CreatesOnceUploadsEveryFrameAndReleasesAfterClear();
    DropsAnUnsubmittedResourceAfterUploadOrSubmitFailure();
    RetainsASubmittedResourceUntilClearSucceeds();
    RetainsASubmittedResourceAfterANewerUploadFails();
    RejectsUnsafeFramesBeforeTouchingGraphics();
    RejectsTextureCreationContractFailuresAndFactoryOom();
    ReleasesResourcesWhenTheRuntimeShutsDown();
    return 0;
}
