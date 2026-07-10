#include "fake_media_backend.hpp"

#include <memory>
#include <string>

#include "media_backend.hpp"
#include "steamvr_input_backend.hpp"

namespace vrrecorder::native {
namespace {

class FakeMediaBackend final : public MediaBackend {
public:
    explicit FakeMediaBackend(MediaEventSink &events) noexcept
        : events_(events)
    {
        active_ = this;
    }

    ~FakeMediaBackend() override
    {
        if (active_ == this) {
            active_ = nullptr;
        }
    }

    vrrec_status_t Start() noexcept override
    {
        return VRREC_STATUS_OK;
    }

    vrrec_status_t RequestStop() noexcept override
    {
        return VRREC_STATUS_OK;
    }

    void Abort() noexcept override
    {
        aborted_ = true;
    }

    void CommitMuxedVideoPacket() noexcept
    {
        events_.FirstVideoPacketMuxed();
    }

    void CompleteTrailerFlushClose(
        std::uint64_t video_packet_count,
        std::uint64_t audio_packet_count) noexcept
    {
        events_.Stopped(video_packet_count, audio_packet_count);
    }

    void Fail(vrrec_status_t status, const std::string &message) noexcept
    {
        events_.Faulted(status, message.c_str());
    }

    static FakeMediaBackend *Active() noexcept
    {
        return active_;
    }

private:
    MediaEventSink &events_;
    bool aborted_ = false;
    static FakeMediaBackend *active_;
};

FakeMediaBackend *FakeMediaBackend::active_ = nullptr;

class FakeSteamVrInputBackend final : public SteamVrInputBackend {
public:
    explicit FakeSteamVrInputBackend(
        const vrrec_steamvr_input_config_v1 &config)
        : manifest_path_(config.action_manifest_path_utf8),
          action_set_path_(config.action_set_path_utf8),
          digital_action_path_(config.digital_action_path_utf8)
    {
        active_ = this;
    }

    ~FakeSteamVrInputBackend() override
    {
        if (active_ == this) {
            active_ = nullptr;
        }
    }

    vrrec_status_t Poll(
        vrrec_steamvr_digital_state_v1 &state) noexcept override
    {
        ++poll_count_;
        state.is_active = is_active_ ? 1 : 0;
        state.state = state_ ? 1 : 0;
        state.changed = changed_ ? 1 : 0;
        state.reserved = 0;
        return VRREC_STATUS_OK;
    }

    void SetState(bool is_active, bool state, bool changed) noexcept
    {
        is_active_ = is_active;
        state_ = state;
        changed_ = changed;
    }

    static FakeSteamVrInputBackend *Active() noexcept
    {
        return active_;
    }

    const std::string &ManifestPath() const noexcept
    {
        return manifest_path_;
    }

    const std::string &ActionSetPath() const noexcept
    {
        return action_set_path_;
    }

    const std::string &DigitalActionPath() const noexcept
    {
        return digital_action_path_;
    }

    std::uint32_t PollCount() const noexcept
    {
        return poll_count_;
    }

private:
    std::string manifest_path_;
    std::string action_set_path_;
    std::string digital_action_path_;
    bool is_active_ = false;
    bool state_ = false;
    bool changed_ = false;
    std::uint32_t poll_count_ = 0;
    static FakeSteamVrInputBackend *active_;
};

FakeSteamVrInputBackend *FakeSteamVrInputBackend::active_ = nullptr;

}

std::unique_ptr<MediaBackend> CreateMediaBackend(
    const vrrec_session_config_v1 &config,
    MediaEventSink &events,
    vrrec_status_t &status)
{
    (void)config;
    status = VRREC_STATUS_OK;
    return std::make_unique<FakeMediaBackend>(events);
}

std::unique_ptr<SteamVrInputBackend> CreateSteamVrInputBackend(
    const vrrec_steamvr_input_config_v1 &config,
    vrrec_status_t &status)
{
    status = VRREC_STATUS_OK;
    return std::make_unique<FakeSteamVrInputBackend>(config);
}

namespace testing {

void CommitMuxedVideoPacket()
{
    FakeMediaBackend::Active()->CommitMuxedVideoPacket();
}

void CompleteTrailerFlushClose(
    std::uint64_t video_packet_count,
    std::uint64_t audio_packet_count)
{
    FakeMediaBackend::Active()->CompleteTrailerFlushClose(
        video_packet_count,
        audio_packet_count);
}

void Fail(std::int32_t status, std::string_view message)
{
    FakeMediaBackend::Active()->Fail(status, std::string(message));
}

void SetSteamVrDigitalState(bool is_active, bool state, bool changed)
{
    FakeSteamVrInputBackend::Active()->SetState(
        is_active,
        state,
        changed);
}

std::string_view SteamVrManifestPath()
{
    return FakeSteamVrInputBackend::Active()->ManifestPath();
}

std::string_view SteamVrActionSetPath()
{
    return FakeSteamVrInputBackend::Active()->ActionSetPath();
}

std::string_view SteamVrDigitalActionPath()
{
    return FakeSteamVrInputBackend::Active()->DigitalActionPath();
}

std::uint32_t SteamVrPollCount()
{
    return FakeSteamVrInputBackend::Active()->PollCount();
}

}
}
