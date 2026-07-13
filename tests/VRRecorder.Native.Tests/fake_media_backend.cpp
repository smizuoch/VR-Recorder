#include "fake_media_backend.hpp"

#include <condition_variable>
#include <deque>
#include <memory>
#include <mutex>
#include <string>
#include <utility>

#include "encoder_probe_backend.hpp"
#include "media_backend.hpp"
#include "spout_source_backend.hpp"
#include "steamvr_input_backend.hpp"

namespace vrrecorder::native {
namespace {

class FakeMediaBackend final : public MediaBackend {
public:
    FakeMediaBackend(
        const vrrec_session_config_v1 &config,
        MediaEventSink &events) noexcept
        : events_(events),
          encoder_kind_(config.encoder_kind),
          config_ {
              config.width,
              config.height,
              config.source_width,
              config.source_height,
              config.destination_x,
              config.destination_y,
              config.destination_width,
              config.destination_height,
              config.canvas_background,
              config.rotation,
              config.audio_routing,
              config.quality_preset,
              config.desktop_endpoint_id_utf8,
              config.microphone_endpoint_id_utf8,
              config.desktop_gain_db,
              config.microphone_gain_db,
              config.spout_sender_identity_utf8,
              config.spout_adapter_luid,
              config.encoder_adapter_luid,
              config.gpu_identity_utf8,
              config.source_pixel_format,
              config.estimated_source_fps,
          },
          video_layout_ {
              sizeof(vrrec_video_layout_v1),
              VRREC_ABI_V1,
              config.source_width,
              config.source_height,
              config.width,
              config.height,
              config.destination_x,
              config.destination_y,
              config.destination_width,
              config.destination_height,
              config.canvas_background,
              config.rotation,
          },
          audio_routing_(config.audio_routing),
          statistics_ {
              sizeof(vrrec_session_statistics_v1),
              VRREC_ABI_V1,
              0,
              0,
              0,
              0,
              0,
              0,
              0,
              0,
          }
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
        std::unique_lock lock(control_mutex_);
        if (block_next_start_) {
            start_entered_ = true;
            control_condition_.notify_all();
            control_condition_.wait(lock, [this] {
                return release_start_;
            });
            block_next_start_ = false;
            release_start_ = false;
        }
        return start_status_;
    }

    vrrec_status_t RequestStop() noexcept override
    {
        std::unique_lock lock(control_mutex_);
        ++request_stop_call_count_;
        if (block_next_stop_) {
            stop_entered_ = true;
            control_condition_.notify_all();
            control_condition_.wait(lock, [this] {
                return release_stop_;
            });
        }
        return stop_status_;
    }

    vrrec_status_t UpdateVideoLayout(
        const vrrec_video_layout_v1 &layout) noexcept override
    {
        auto fault = false;
        {
            std::unique_lock lock(control_mutex_);
            video_layout_ = layout;
            ++video_layout_update_count_;
            if (block_next_video_layout_update_) {
                video_layout_update_entered_ = true;
                control_condition_.notify_all();
                control_condition_.wait(lock, [this] {
                    return release_video_layout_update_;
                });
                block_next_video_layout_update_ = false;
                release_video_layout_update_ = false;
            }

            fault = fault_during_next_video_layout_update_;
            fault_during_next_video_layout_update_ = false;
        }

        if (fault) {
            events_.Faulted(
                VRREC_STATUS_INTERNAL_ERROR,
                "layout update failed");
            return VRREC_STATUS_INTERNAL_ERROR;
        }

        return VRREC_STATUS_OK;
    }

    vrrec_status_t GetStatistics(
        vrrec_session_statistics_v1 &statistics) noexcept override
    {
        const std::lock_guard lock(control_mutex_);
        ++statistics_call_count_;
        if (statistics_status_ != VRREC_STATUS_OK) {
            return statistics_status_;
        }

        statistics = statistics_;
        return VRREC_STATUS_OK;
    }

    vrrec_status_t UpdateAudioRouting(
        vrrec_audio_routing_t routing) noexcept override
    {
        auto fault = false;
        {
            std::unique_lock lock(control_mutex_);
            audio_routing_ = routing;
            ++audio_routing_update_count_;
            if (block_next_audio_routing_update_) {
                audio_routing_update_entered_ = true;
                control_condition_.notify_all();
                control_condition_.wait(lock, [this] {
                    return release_audio_routing_update_;
                });
                block_next_audio_routing_update_ = false;
                release_audio_routing_update_ = false;
            }

            fault = fault_during_next_audio_routing_update_;
            fault_during_next_audio_routing_update_ = false;
        }

        if (fault) {
            events_.Faulted(
                VRREC_STATUS_INTERNAL_ERROR,
                "audio routing update failed");
            return VRREC_STATUS_INTERNAL_ERROR;
        }

        return VRREC_STATUS_OK;
    }

    void RequestAbort() noexcept override
    {
        const std::lock_guard lock(control_mutex_);
        aborted_ = true;
        ++request_abort_call_count_;
        control_condition_.notify_all();
    }

    void JoinAfterAbort() noexcept override
    {
    }

    void CommitMuxedVideoPacket() noexcept
    {
        events_.FirstVideoPacketMuxed();
    }

    void BlockNextStart(vrrec_status_t status) noexcept
    {
        const std::lock_guard lock(control_mutex_);
        start_status_ = status;
        block_next_start_ = true;
        start_entered_ = false;
        release_start_ = false;
    }

    bool WaitUntilStartEntered(
        std::chrono::milliseconds timeout) noexcept
    {
        std::unique_lock lock(control_mutex_);
        return control_condition_.wait_for(lock, timeout, [this] {
            return start_entered_;
        });
    }

    void ReleaseStart() noexcept
    {
        const std::lock_guard lock(control_mutex_);
        release_start_ = true;
        control_condition_.notify_all();
    }

    void BlockNextStop(vrrec_status_t status) noexcept
    {
        const std::lock_guard lock(control_mutex_);
        stop_status_ = status;
        block_next_stop_ = true;
        stop_entered_ = false;
        release_stop_ = false;
    }

    bool WaitUntilStopEntered(
        std::chrono::milliseconds timeout) noexcept
    {
        std::unique_lock lock(control_mutex_);
        return control_condition_.wait_for(lock, timeout, [this] {
            return stop_entered_;
        });
    }

    void ReleaseStop() noexcept
    {
        const std::lock_guard lock(control_mutex_);
        block_next_stop_ = false;
        release_stop_ = true;
        control_condition_.notify_all();
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

    void SetAudioEndpointAvailable(
        AudioEndpointRole role,
        bool available,
        std::uint64_t frame_position) noexcept
    {
        events_.AudioEndpointAvailabilityChanged(
            role,
            available,
            frame_position);
    }

    void EmitAvDrift(
        std::uint64_t video_pts_microseconds,
        std::uint64_t audio_pts_microseconds) noexcept
    {
        const auto drift = video_pts_microseconds >= audio_pts_microseconds
            ? video_pts_microseconds - audio_pts_microseconds
            : audio_pts_microseconds - video_pts_microseconds;
        events_.AvSyncDriftExceeded(
            video_pts_microseconds,
            audio_pts_microseconds,
            drift);
    }

    void EmitAudioBufferHealth(
        AudioEndpointRole role,
        AudioBufferHealth health,
        std::uint64_t frame_position) noexcept
    {
        events_.AudioBufferHealthChanged(role, health, frame_position);
    }

    static FakeMediaBackend *Active() noexcept
    {
        return active_;
    }

    std::uint32_t EncoderKind() const noexcept
    {
        return encoder_kind_;
    }

    const testing::ObservedMediaSessionConfig &SessionConfig() const noexcept
    {
        return config_;
    }

    const vrrec_video_layout_v1 &VideoLayout() const noexcept
    {
        return video_layout_;
    }

    std::uint32_t VideoLayoutUpdateCount() const noexcept
    {
        return video_layout_update_count_;
    }

    std::uint32_t AudioRouting() const noexcept
    {
        const std::lock_guard lock(control_mutex_);
        return audio_routing_;
    }

    std::uint32_t AudioRoutingUpdateCount() const noexcept
    {
        const std::lock_guard lock(control_mutex_);
        return audio_routing_update_count_;
    }

    void FaultDuringNextAudioRoutingUpdate() noexcept
    {
        const std::lock_guard lock(control_mutex_);
        fault_during_next_audio_routing_update_ = true;
    }

    void BlockNextAudioRoutingUpdate() noexcept
    {
        const std::lock_guard lock(control_mutex_);
        block_next_audio_routing_update_ = true;
        audio_routing_update_entered_ = false;
        release_audio_routing_update_ = false;
    }

    bool WaitUntilAudioRoutingUpdateEntered(
        std::chrono::milliseconds timeout) noexcept
    {
        std::unique_lock lock(control_mutex_);
        return control_condition_.wait_for(lock, timeout, [this] {
            return audio_routing_update_entered_;
        });
    }

    void ReleaseAudioRoutingUpdate() noexcept
    {
        const std::lock_guard lock(control_mutex_);
        release_audio_routing_update_ = true;
        control_condition_.notify_all();
    }

    void SetStatistics(
        const vrrec_session_statistics_v1 &statistics) noexcept
    {
        const std::lock_guard lock(control_mutex_);
        statistics_ = statistics;
    }

    void SetStatisticsStatus(vrrec_status_t status) noexcept
    {
        const std::lock_guard lock(control_mutex_);
        statistics_status_ = status;
    }

    void FaultDuringNextVideoLayoutUpdate() noexcept
    {
        const std::lock_guard lock(control_mutex_);
        fault_during_next_video_layout_update_ = true;
    }

    void BlockNextVideoLayoutUpdate() noexcept
    {
        const std::lock_guard lock(control_mutex_);
        block_next_video_layout_update_ = true;
        video_layout_update_entered_ = false;
        release_video_layout_update_ = false;
    }

    bool WaitUntilVideoLayoutUpdateEntered(
        std::chrono::milliseconds timeout) noexcept
    {
        std::unique_lock lock(control_mutex_);
        return control_condition_.wait_for(lock, timeout, [this] {
            return video_layout_update_entered_;
        });
    }

    void ReleaseVideoLayoutUpdate() noexcept
    {
        const std::lock_guard lock(control_mutex_);
        release_video_layout_update_ = true;
        control_condition_.notify_all();
    }

    std::uint32_t RequestStopCallCount() noexcept
    {
        const std::lock_guard lock(control_mutex_);
        return request_stop_call_count_;
    }

    std::uint32_t StatisticsCallCount() noexcept
    {
        const std::lock_guard lock(control_mutex_);
        return statistics_call_count_;
    }

    bool WaitUntilAbortRequested(
        std::chrono::milliseconds timeout) noexcept
    {
        std::unique_lock lock(control_mutex_);
        return control_condition_.wait_for(lock, timeout, [this] {
            return request_abort_call_count_ != 0;
        });
    }

    std::uint32_t RequestAbortCallCount() noexcept
    {
        const std::lock_guard lock(control_mutex_);
        return request_abort_call_count_;
    }

private:
    MediaEventSink &events_;
    std::uint32_t encoder_kind_;
    testing::ObservedMediaSessionConfig config_;
    vrrec_video_layout_v1 video_layout_;
    std::uint32_t audio_routing_;
    vrrec_session_statistics_v1 statistics_;
    std::uint32_t video_layout_update_count_ = 0;
    mutable std::mutex control_mutex_;
    std::condition_variable control_condition_;
    bool fault_during_next_video_layout_update_ = false;
    bool block_next_video_layout_update_ = false;
    bool video_layout_update_entered_ = false;
    bool release_video_layout_update_ = false;
    bool fault_during_next_audio_routing_update_ = false;
    bool block_next_audio_routing_update_ = false;
    bool audio_routing_update_entered_ = false;
    bool release_audio_routing_update_ = false;
    std::uint32_t request_stop_call_count_ = 0;
    std::uint32_t statistics_call_count_ = 0;
    std::uint32_t request_abort_call_count_ = 0;
    vrrec_status_t stop_status_ = VRREC_STATUS_OK;
    bool block_next_stop_ = false;
    bool stop_entered_ = false;
    bool release_stop_ = false;
    vrrec_status_t start_status_ = VRREC_STATUS_OK;
    bool block_next_start_ = false;
    bool start_entered_ = false;
    bool release_start_ = false;
    std::uint32_t audio_routing_update_count_ = 0;
    vrrec_status_t statistics_status_ = VRREC_STATUS_OK;
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

struct FakeSpoutSourceState {
    std::mutex mutex;
    std::condition_variable condition;
    std::vector<testing::TestSpoutSenderSnapshot> snapshot;
    std::deque<testing::TestSpoutFrame> frames;
    bool block_next_poll = false;
    bool poll_entered = false;
    bool release_poll = false;
    std::uint32_t active_source_count = 0;
    std::uint32_t destroy_count = 0;
};

FakeSpoutSourceState fake_spout_source;

class FakeSpoutSourceBackend final : public SpoutSourceBackend {
public:
    FakeSpoutSourceBackend()
    {
        const std::lock_guard lock(fake_spout_source.mutex);
        ++fake_spout_source.active_source_count;
    }

    ~FakeSpoutSourceBackend() override
    {
        const std::lock_guard lock(fake_spout_source.mutex);
        --fake_spout_source.active_source_count;
        ++fake_spout_source.destroy_count;
        fake_spout_source.condition.notify_all();
    }

    vrrec_status_t Snapshot(
        std::vector<SpoutSenderSnapshot> &senders) override
    {
        const std::lock_guard lock(fake_spout_source.mutex);
        senders.clear();
        senders.reserve(fake_spout_source.snapshot.size());
        for (const auto &sender : fake_spout_source.snapshot) {
            senders.push_back(SpoutSenderSnapshot {
                sender.sender_id,
                sender.latest_frame_generation,
            });
        }

        return VRREC_STATUS_OK;
    }

    vrrec_status_t Poll(
        std::chrono::milliseconds timeout,
        SpoutFrame &frame) override
    {
        std::unique_lock lock(fake_spout_source.mutex);
        fake_spout_source.poll_entered = true;
        fake_spout_source.condition.notify_all();
        if (fake_spout_source.block_next_poll) {
            fake_spout_source.condition.wait(lock, [] {
                return fake_spout_source.release_poll;
            });
            fake_spout_source.block_next_poll = false;
            fake_spout_source.release_poll = false;
        }

        if (fake_spout_source.frames.empty() && timeout.count() > 0) {
            (void)fake_spout_source.condition.wait_for(lock, timeout, [] {
                return !fake_spout_source.frames.empty();
            });
        }

        if (fake_spout_source.frames.empty()) {
            return VRREC_STATUS_TIMEOUT;
        }

        auto observed = std::move(fake_spout_source.frames.front());
        fake_spout_source.frames.pop_front();
        frame = SpoutFrame {
            std::move(observed.sender_id),
            observed.adapter_luid,
            std::move(observed.gpu_identity),
            observed.gpu_vendor,
            observed.width,
            observed.height,
            observed.pixel_format,
            observed.estimated_source_fps,
            observed.frame_sequence,
            observed.monotonic_timestamp_microseconds,
        };
        return VRREC_STATUS_OK;
    }
};

struct FakeEncoderProbeState {
    std::mutex mutex;
    std::condition_variable condition;
    vrrec_status_t status = VRREC_STATUS_OK;
    bool packet_produced = false;
    bool block_next = false;
    bool entered = false;
    bool release = false;
    std::uint32_t call_count = 0;
    testing::ObservedEncoderProbeConfig observed {
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        "",
    };
};

FakeEncoderProbeState fake_encoder_probe;

class FakeEncoderProbeBackend final : public EncoderProbeBackend {
public:
    vrrec_status_t Probe(
        const vrrec_encoder_probe_config_v1 &config,
        bool &packet_produced) noexcept override
    {
        std::unique_lock lock(fake_encoder_probe.mutex);
        ++fake_encoder_probe.call_count;
        fake_encoder_probe.observed =
            testing::ObservedEncoderProbeConfig {
                config.encoder_kind,
                config.synthetic_frame_count,
                config.adapter_luid,
                config.width,
                config.height,
                config.fps_numerator,
                config.fps_denominator,
                config.gpu_identity_utf8,
            };
        fake_encoder_probe.entered = true;
        fake_encoder_probe.condition.notify_all();
        if (fake_encoder_probe.block_next) {
            fake_encoder_probe.condition.wait(lock, [] {
                return fake_encoder_probe.release;
            });
            fake_encoder_probe.block_next = false;
            fake_encoder_probe.release = false;
        }

        packet_produced = fake_encoder_probe.packet_produced;
        return fake_encoder_probe.status;
    }
};

}

std::unique_ptr<MediaBackend> CreateMediaBackend(
    const vrrec_session_config_v1 &config,
    MediaEventSink &events,
    vrrec_status_t &status)
{
    status = VRREC_STATUS_OK;
    return std::make_unique<FakeMediaBackend>(config, events);
}

std::unique_ptr<SteamVrInputBackend> CreateSteamVrInputBackend(
    const vrrec_steamvr_input_config_v1 &config,
    vrrec_status_t &status)
{
    status = VRREC_STATUS_OK;
    return std::make_unique<FakeSteamVrInputBackend>(config);
}

std::unique_ptr<SpoutSourceBackend> CreateSpoutSourceBackend(
    const vrrec_spout_source_config_v1 &config,
    vrrec_status_t &status)
{
    (void)config;
    status = VRREC_STATUS_OK;
    return std::make_unique<FakeSpoutSourceBackend>();
}

std::unique_ptr<EncoderProbeBackend> CreateEncoderProbeBackend(
    vrrec_status_t &status)
{
    status = VRREC_STATUS_OK;
    return std::make_unique<FakeEncoderProbeBackend>();
}

namespace testing {

void CommitMuxedVideoPacket()
{
    FakeMediaBackend::Active()->CommitMuxedVideoPacket();
}

void BlockNextMediaStart(std::int32_t status)
{
    FakeMediaBackend::Active()->BlockNextStart(status);
}

bool WaitUntilMediaStartEntered(std::chrono::milliseconds timeout)
{
    return FakeMediaBackend::Active()->WaitUntilStartEntered(timeout);
}

void ReleaseMediaStart()
{
    FakeMediaBackend::Active()->ReleaseStart();
}

void BlockNextMediaStop(std::int32_t status)
{
    FakeMediaBackend::Active()->BlockNextStop(status);
}

bool WaitUntilMediaStopEntered(std::chrono::milliseconds timeout)
{
    return FakeMediaBackend::Active()->WaitUntilStopEntered(timeout);
}

void ReleaseMediaStop()
{
    FakeMediaBackend::Active()->ReleaseStop();
}

bool WaitUntilMediaAbortRequested(std::chrono::milliseconds timeout)
{
    return FakeMediaBackend::Active()->WaitUntilAbortRequested(timeout);
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

void EmitAvDrift(
    std::uint64_t video_pts_microseconds,
    std::uint64_t audio_pts_microseconds)
{
    FakeMediaBackend::Active()->EmitAvDrift(
        video_pts_microseconds,
        audio_pts_microseconds);
}

void EmitAudioBufferHealth(
    AudioEndpointRole role,
    AudioBufferHealth health,
    std::uint64_t frame_position)
{
    FakeMediaBackend::Active()->EmitAudioBufferHealth(
        role,
        health,
        frame_position);
}

void SetDesktopAudioEndpointAvailable(
    bool available,
    std::uint64_t frame_position)
{
    FakeMediaBackend::Active()->SetAudioEndpointAvailable(
        AudioEndpointRole::Desktop,
        available,
        frame_position);
}

void SetMicrophoneAudioEndpointAvailable(
    bool available,
    std::uint64_t frame_position)
{
    FakeMediaBackend::Active()->SetAudioEndpointAvailable(
        AudioEndpointRole::Microphone,
        available,
        frame_position);
}

std::uint32_t EncoderKind()
{
    return FakeMediaBackend::Active()->EncoderKind();
}

const ObservedMediaSessionConfig &SessionConfig()
{
    return FakeMediaBackend::Active()->SessionConfig();
}

const vrrec_video_layout_v1 &VideoLayout()
{
    return FakeMediaBackend::Active()->VideoLayout();
}

std::uint32_t VideoLayoutUpdateCount()
{
    return FakeMediaBackend::Active()->VideoLayoutUpdateCount();
}

std::uint32_t AudioRouting()
{
    return FakeMediaBackend::Active()->AudioRouting();
}

std::uint32_t AudioRoutingUpdateCount()
{
    return FakeMediaBackend::Active()->AudioRoutingUpdateCount();
}

void FaultDuringNextAudioRoutingUpdate()
{
    FakeMediaBackend::Active()->FaultDuringNextAudioRoutingUpdate();
}

void BlockNextAudioRoutingUpdate()
{
    FakeMediaBackend::Active()->BlockNextAudioRoutingUpdate();
}

bool WaitUntilAudioRoutingUpdateEntered(std::chrono::milliseconds timeout)
{
    return FakeMediaBackend::Active()->WaitUntilAudioRoutingUpdateEntered(
        timeout);
}

void ReleaseAudioRoutingUpdate()
{
    FakeMediaBackend::Active()->ReleaseAudioRoutingUpdate();
}

void SetStatistics(const vrrec_session_statistics_v1 &statistics)
{
    FakeMediaBackend::Active()->SetStatistics(statistics);
}

void SetStatisticsStatus(std::int32_t status)
{
    FakeMediaBackend::Active()->SetStatisticsStatus(status);
}

void FaultDuringNextVideoLayoutUpdate()
{
    FakeMediaBackend::Active()->FaultDuringNextVideoLayoutUpdate();
}

void BlockNextVideoLayoutUpdate()
{
    FakeMediaBackend::Active()->BlockNextVideoLayoutUpdate();
}

bool WaitUntilVideoLayoutUpdateEntered(std::chrono::milliseconds timeout)
{
    return FakeMediaBackend::Active()->WaitUntilVideoLayoutUpdateEntered(
        timeout);
}

void ReleaseVideoLayoutUpdate()
{
    FakeMediaBackend::Active()->ReleaseVideoLayoutUpdate();
}

std::uint32_t RequestStopCallCount()
{
    return FakeMediaBackend::Active()->RequestStopCallCount();
}

std::uint32_t StatisticsCallCount()
{
    return FakeMediaBackend::Active()->StatisticsCallCount();
}

std::uint32_t RequestAbortCallCount()
{
    return FakeMediaBackend::Active()->RequestAbortCallCount();
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

bool HasActiveSteamVrInput()
{
    return FakeSteamVrInputBackend::Active() != nullptr;
}

void ResetSpoutSource()
{
    const std::lock_guard lock(fake_spout_source.mutex);
    fake_spout_source.snapshot.clear();
    fake_spout_source.frames.clear();
    fake_spout_source.block_next_poll = false;
    fake_spout_source.poll_entered = false;
    fake_spout_source.release_poll = false;
    fake_spout_source.destroy_count = 0;
}

void SetSpoutSnapshot(std::vector<TestSpoutSenderSnapshot> senders)
{
    const std::lock_guard lock(fake_spout_source.mutex);
    fake_spout_source.snapshot = std::move(senders);
}

void AddSpoutSnapshotSender(TestSpoutSenderSnapshot sender)
{
    const std::lock_guard lock(fake_spout_source.mutex);
    fake_spout_source.snapshot.push_back(std::move(sender));
}

void PushSpoutFrame(TestSpoutFrame frame)
{
    const std::lock_guard lock(fake_spout_source.mutex);
    fake_spout_source.frames.push_back(std::move(frame));
    fake_spout_source.condition.notify_all();
}

void BlockNextSpoutPoll()
{
    const std::lock_guard lock(fake_spout_source.mutex);
    fake_spout_source.block_next_poll = true;
    fake_spout_source.poll_entered = false;
    fake_spout_source.release_poll = false;
}

bool WaitUntilSpoutPollEntered(std::chrono::milliseconds timeout)
{
    std::unique_lock lock(fake_spout_source.mutex);
    return fake_spout_source.condition.wait_for(lock, timeout, [] {
        return fake_spout_source.poll_entered;
    });
}

void ReleaseSpoutPoll()
{
    const std::lock_guard lock(fake_spout_source.mutex);
    fake_spout_source.release_poll = true;
    fake_spout_source.condition.notify_all();
}

std::uint32_t ActiveSpoutSourceCount()
{
    const std::lock_guard lock(fake_spout_source.mutex);
    return fake_spout_source.active_source_count;
}

std::uint32_t SpoutSourceDestroyCount()
{
    const std::lock_guard lock(fake_spout_source.mutex);
    return fake_spout_source.destroy_count;
}

void ResetEncoderProbe()
{
    const std::lock_guard lock(fake_encoder_probe.mutex);
    fake_encoder_probe.status = VRREC_STATUS_OK;
    fake_encoder_probe.packet_produced = false;
    fake_encoder_probe.block_next = false;
    fake_encoder_probe.entered = false;
    fake_encoder_probe.release = false;
    fake_encoder_probe.call_count = 0;
    fake_encoder_probe.observed = ObservedEncoderProbeConfig {
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        "",
    };
}

void SetEncoderProbeResult(
    std::int32_t status,
    bool packet_produced)
{
    const std::lock_guard lock(fake_encoder_probe.mutex);
    fake_encoder_probe.status = status;
    fake_encoder_probe.packet_produced = packet_produced;
}

std::uint32_t EncoderProbeCallCount()
{
    const std::lock_guard lock(fake_encoder_probe.mutex);
    return fake_encoder_probe.call_count;
}

ObservedEncoderProbeConfig EncoderProbeConfig()
{
    const std::lock_guard lock(fake_encoder_probe.mutex);
    return fake_encoder_probe.observed;
}

void BlockNextEncoderProbe()
{
    const std::lock_guard lock(fake_encoder_probe.mutex);
    fake_encoder_probe.block_next = true;
    fake_encoder_probe.entered = false;
    fake_encoder_probe.release = false;
}

bool WaitUntilEncoderProbeEntered(std::chrono::milliseconds timeout)
{
    std::unique_lock lock(fake_encoder_probe.mutex);
    return fake_encoder_probe.condition.wait_for(lock, timeout, [] {
        return fake_encoder_probe.entered;
    });
}

void ReleaseEncoderProbe()
{
    const std::lock_guard lock(fake_encoder_probe.mutex);
    fake_encoder_probe.release = true;
    fake_encoder_probe.condition.notify_all();
}

}
}
