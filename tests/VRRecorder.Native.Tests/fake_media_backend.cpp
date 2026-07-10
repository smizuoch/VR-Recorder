#include "fake_media_backend.hpp"

#include <memory>
#include <string>

#include "media_backend.hpp"

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

}
}
