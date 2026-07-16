#include "openvr_overlay_texture_presenter.hpp"

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
    frame.stride_bytes -= 1;
    CHECK(presenter->SetOverlayBgraTexture(47, frame) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(state->calls.empty());
    CHECK(CreateOpenVrOverlayTexturePresenter(nullptr, status) == nullptr);
    CHECK(status == VRREC_STATUS_INVALID_ARGUMENT);
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
    ReleasesResourcesWhenTheRuntimeShutsDown();
    return 0;
}
