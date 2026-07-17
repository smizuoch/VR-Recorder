#include "wasapi_audio_capture_source_core.hpp"
#include "allocation_failure_test_support.hpp"

#include <chrono>
#include <condition_variable>
#include <cstdlib>
#include <cstring>
#include <deque>
#include <future>
#include <iostream>
#include <limits>
#include <memory>
#include <mutex>
#include <thread>
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

struct ScriptStep final {
    WasapiCapturePortResult result = WasapiCapturePortResult::Ok;
    std::uint64_t device_position = 0;
    std::uint64_t qpc_100ns = 1'000'000;
    std::uint32_t frame_count = 0;
    std::vector<std::byte> bytes;
    bool silent = false;
    bool discontinuity = false;
    bool timestamp_error = false;
    WasapiCapturePortResult release_result =
        WasapiCapturePortResult::Ok;
};

struct PortState final {
    std::mutex mutex;
    std::condition_variable changed;
    CapturePcmFormat format {
        48'000,
        2,
        CaptureSampleEncoding::IeeeFloat,
        32,
        32,
        8,
        0x0000'0003,
    };
    WasapiCapturePortResult start_result = WasapiCapturePortResult::Ok;
    std::deque<ScriptStep> steps;
    std::vector<std::uint32_t> released_frames;
    std::thread::id start_thread;
    std::thread::id acquire_thread;
    std::thread::id release_thread;
    std::thread::id close_thread;
    std::thread::id destroy_thread;
    std::size_t start_calls = 0;
    std::size_t acquire_calls = 0;
    std::size_t abort_calls = 0;
    std::size_t close_calls = 0;
    std::size_t destroy_calls = 0;
    bool block_acquire = false;
    bool acquire_entered = false;
    bool aborted = false;
};

class ScriptedPort final : public WasapiCapturePort {
public:
    explicit ScriptedPort(std::shared_ptr<PortState> state)
        : state_(std::move(state))
    {
    }

    ~ScriptedPort() override
    {
        const std::lock_guard lock(state_->mutex);
        ++state_->destroy_calls;
        state_->destroy_thread = std::this_thread::get_id();
    }

    WasapiCapturePortResult Start(
        const AudioCaptureSourceConfig &,
        CapturePcmFormat &format) noexcept override
    {
        const std::lock_guard lock(state_->mutex);
        ++state_->start_calls;
        state_->start_thread = std::this_thread::get_id();
        format = state_->format;
        return state_->start_result;
    }

    WasapiCapturePortResult Acquire(
        WasapiCapturePacket &packet) noexcept override
    {
        std::unique_lock lock(state_->mutex);
        ++state_->acquire_calls;
        state_->acquire_thread = std::this_thread::get_id();
        state_->acquire_entered = true;
        state_->changed.notify_all();
        state_->changed.wait(lock, [&] {
            return !state_->block_acquire || state_->aborted;
        });
        if (state_->aborted) {
            return WasapiCapturePortResult::Aborted;
        }
        if (state_->steps.empty()) {
            return WasapiCapturePortResult::Failed;
        }

        acquired_ = std::move(state_->steps.front());
        state_->steps.pop_front();
        if (acquired_.result != WasapiCapturePortResult::Ok) {
            return acquired_.result;
        }

        packet = {
            acquired_.device_position,
            acquired_.qpc_100ns,
            acquired_.frame_count,
            std::span<const std::byte>(acquired_.bytes),
            acquired_.silent,
            acquired_.discontinuity,
            acquired_.timestamp_error,
        };
        return WasapiCapturePortResult::Ok;
    }

    WasapiCapturePortResult Release(
        std::uint32_t frame_count) noexcept override
    {
        const std::lock_guard lock(state_->mutex);
        state_->release_thread = std::this_thread::get_id();
        state_->released_frames.push_back(frame_count);
        const auto result = acquired_.release_result;
        acquired_ = {};
        return result;
    }

    void Abort() noexcept override
    {
        {
            const std::lock_guard lock(state_->mutex);
            ++state_->abort_calls;
            state_->aborted = true;
        }
        state_->changed.notify_all();
    }

    void Close() noexcept override
    {
        const std::lock_guard lock(state_->mutex);
        ++state_->close_calls;
        state_->close_thread = std::this_thread::get_id();
    }

private:
    std::shared_ptr<PortState> state_;
    ScriptStep acquired_;
};

std::vector<std::byte> FloatBytes(std::initializer_list<float> samples)
{
    std::vector<std::byte> bytes(samples.size() * sizeof(float));
    std::memcpy(bytes.data(), samples.begin(), bytes.size());
    return bytes;
}

AudioCaptureSourceConfig Config()
{
    return {
        AudioCaptureRole::DesktopLoopback,
        "default-render",
        1'000'000,
    };
}

std::unique_ptr<WasapiAudioCaptureSourceCore> Source(
    const std::shared_ptr<PortState> &state)
{
    return std::make_unique<WasapiAudioCaptureSourceCore>(
        std::make_unique<ScriptedPort>(state));
}

void ReleasesEverySuccessfullyAcquiredBuffer()
{
    {
        auto state = std::make_shared<PortState>();
        state->steps.push_back({
            WasapiCapturePortResult::Empty,
            0,
            1'000'000,
            0,
            {},
        });
        state->steps.push_back({
            WasapiCapturePortResult::Ok,
            0,
            1'000'000,
            2,
            FloatBytes({0.25F, -0.25F, 0.5F, -0.5F}),
        });
        auto source = Source(state);
        CHECK(source->Start(Config()) == VRREC_STATUS_OK);
        const auto read = source->Read();
        CHECK(read.result == AudioCaptureReadResult::Packet);
        CHECK(read.packet.frame_count_48k == 2);
        CHECK(state->acquire_calls == 2);
        CHECK(state->released_frames == std::vector<std::uint32_t> {2});
    }

    {
        auto state = std::make_shared<PortState>();
        state->steps.push_back({
            WasapiCapturePortResult::Ok,
            0,
            1'000'000,
            0,
            {},
        });
        auto source = Source(state);
        CHECK(source->Start(Config()) == VRREC_STATUS_OK);
        CHECK(source->Read().result == AudioCaptureReadResult::Failed);
        CHECK(state->released_frames == std::vector<std::uint32_t> {0});
    }

    {
        auto state = std::make_shared<PortState>();
        state->steps.push_back({
            WasapiCapturePortResult::Ok,
            0,
            1'000'000,
            2,
            {},
        });
        auto source = Source(state);
        CHECK(source->Start(Config()) == VRREC_STATUS_OK);
        CHECK(source->Read().result == AudioCaptureReadResult::Failed);
        CHECK(state->released_frames == std::vector<std::uint32_t> {2});
    }

    {
        auto state = std::make_shared<PortState>();
        state->steps.push_back({
            WasapiCapturePortResult::Ok,
            0,
            std::numeric_limits<std::uint64_t>::max(),
            2,
            FloatBytes({0.0F, 0.0F, 0.0F, 0.0F}),
        });
        auto source = Source(state);
        CHECK(source->Start(Config()) == VRREC_STATUS_OK);
        CHECK(source->Read().result == AudioCaptureReadResult::Failed);
        CHECK(state->released_frames == std::vector<std::uint32_t> {2});
    }
}

void PreservesSilentDiscontinuityAndTimestampSemantics()
{
    auto state = std::make_shared<PortState>();
    state->steps.push_back({
        WasapiCapturePortResult::Ok,
        0,
        1'000'000,
        2,
        FloatBytes({0.25F, -0.25F, 0.5F, -0.5F}),
    });
    state->steps.push_back({
        WasapiCapturePortResult::Ok,
        2,
        0,
        2,
        {},
        true,
        false,
        true,
    });
    state->steps.push_back({
        WasapiCapturePortResult::Ok,
        100,
        1'001'000,
        2,
        FloatBytes({0.75F, 0.75F, 0.75F, 0.75F}),
        false,
        true,
        false,
    });
    auto source = Source(state);
    CHECK(source->Start(Config()) == VRREC_STATUS_OK);

    CHECK(source->Read().result == AudioCaptureReadResult::Packet);
    const auto silent = source->Read();
    CHECK(silent.result == AudioCaptureReadResult::Packet);
    CHECK(silent.packet.silent);
    CHECK(silent.packet.interleaved_samples.empty());
    CHECK(silent.packet.qpc_100ns > 0);
    const auto discontinuity = source->Read();
    CHECK(discontinuity.result == AudioCaptureReadResult::Packet);
    CHECK(discontinuity.packet.discontinuity);
    CHECK(state->released_frames ==
          (std::vector<std::uint32_t> {2, 2, 2}));
}

void SkipsAReleasedDiscontinuityPacketThatPredatesTheSession()
{
    auto state = std::make_shared<PortState>();
    state->steps.push_back({
        WasapiCapturePortResult::Ok,
        100,
        999'000,
        2,
        FloatBytes({0.25F, -0.25F, 0.5F, -0.5F}),
        false,
        true,
        false,
    });
    state->steps.push_back({
        WasapiCapturePortResult::Ok,
        102,
        1'010'000,
        2,
        FloatBytes({0.75F, -0.75F, 1.0F, -1.0F}),
    });
    auto source = Source(state);
    CHECK(source->Start(Config()) == VRREC_STATUS_OK);

    const auto read = source->Read();

    CHECK(read.result == AudioCaptureReadResult::Packet);
    CHECK(read.packet.start_frame_48k == 48);
    CHECK(read.packet.device_position == 102);
    CHECK(read.packet.frame_count_48k == 2);
    CHECK(state->acquire_calls == 2);
    CHECK(state->released_frames ==
          (std::vector<std::uint32_t> {2, 2}));
}

void MapsAcquireAndReleaseFailures()
{
    {
        auto state = std::make_shared<PortState>();
        state->steps.push_back({
            WasapiCapturePortResult::DeviceLost,
            0,
            1'000'000,
            0,
            {},
        });
        auto source = Source(state);
        CHECK(source->Start(Config()) == VRREC_STATUS_OK);
        CHECK(source->Read().result == AudioCaptureReadResult::DeviceLost);
        CHECK(state->released_frames.empty());
    }

    {
        auto state = std::make_shared<PortState>();
        state->steps.push_back({
            WasapiCapturePortResult::Ok,
            0,
            1'000'000,
            2,
            FloatBytes({0.0F, 0.0F, 0.0F, 0.0F}),
            false,
            false,
            false,
            WasapiCapturePortResult::DeviceLost,
        });
        auto source = Source(state);
        CHECK(source->Start(Config()) == VRREC_STATUS_OK);
        CHECK(source->Read().result == AudioCaptureReadResult::DeviceLost);
        CHECK(state->released_frames == std::vector<std::uint32_t> {2});
    }
}

void RejectsCrossThreadReadAndCleansUpOnTheCaptureThread()
{
    auto state = std::make_shared<PortState>();
    const auto capture_thread = std::this_thread::get_id();
    {
        auto source = Source(state);
        CHECK(source->Start(Config()) == VRREC_STATUS_OK);
        auto cross_thread = std::async(std::launch::async, [&] {
            return source->Read().result;
        });
        CHECK(cross_thread.get() == AudioCaptureReadResult::Failed);
        CHECK(state->acquire_calls == 0);
    }

    CHECK(state->start_thread == capture_thread);
    CHECK(state->close_thread == capture_thread);
    CHECK(state->destroy_thread == capture_thread);
    CHECK(state->close_calls == 1);
    CHECK(state->destroy_calls == 1);
}

void AbortWakesAcquireAndCleanupRemainsOnTheCaptureThread()
{
    auto state = std::make_shared<PortState>();
    state->block_acquire = true;
    std::promise<AudioCaptureSource *> published;
    auto worker = std::async(std::launch::async, [&] {
        auto source = Source(state);
        CHECK(source->Start(Config()) == VRREC_STATUS_OK);
        published.set_value(source.get());
        const auto read = source->Read();
        CHECK(read.result == AudioCaptureReadResult::Aborted);
    });

    auto *source = published.get_future().get();
    {
        std::unique_lock lock(state->mutex);
        state->changed.wait(lock, [&] { return state->acquire_entered; });
    }
    source->Abort();
    source->Abort();
    CHECK(worker.wait_for(std::chrono::seconds(1)) ==
          std::future_status::ready);
    worker.get();

    CHECK(state->abort_calls == 1);
    CHECK(state->released_frames.empty());
    CHECK(state->start_thread == state->acquire_thread);
    CHECK(state->start_thread == state->close_thread);
    CHECK(state->start_thread == state->destroy_thread);
    CHECK(state->close_calls == 1);
    CHECK(state->destroy_calls == 1);
}

void StartFailureClosesExactlyOnce()
{
    auto state = std::make_shared<PortState>();
    state->start_result = WasapiCapturePortResult::OutOfMemory;
    {
        auto source = Source(state);
        CHECK(source->Start(Config()) == VRREC_STATUS_OUT_OF_MEMORY);
        CHECK(source->Read().result == AudioCaptureReadResult::Failed);
    }

    CHECK(state->start_calls == 1);
    CHECK(state->close_calls == 1);
    CHECK(state->destroy_calls == 1);
    CHECK(state->start_thread == state->close_thread);
}

void RejectsEveryInvalidStartStateAndAcceptsBothRoles()
{
    {
        auto source = std::make_unique<WasapiAudioCaptureSourceCore>(nullptr);
        CHECK(source->Start(Config()) == VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(source->Read().result == AudioCaptureReadResult::Failed);
        source->Abort();
    }
    {
        auto state = std::make_shared<PortState>();
        auto source = Source(state);
        CHECK(source->Start(Config()) == VRREC_STATUS_OK);
        CHECK(source->Start(Config()) == VRREC_STATUS_INVALID_ARGUMENT);
    }
    {
        auto state = std::make_shared<PortState>();
        auto source = Source(state);
        source->Abort();
        CHECK(source->Start(Config()) == VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(state->start_calls == 0);
    }
    {
        auto state = std::make_shared<PortState>();
        auto source = Source(state);
        auto config = Config();
        config.session_start_qpc_100ns = -1;
        CHECK(source->Start(config) == VRREC_STATUS_INVALID_ARGUMENT);
        config = Config();
        config.role = static_cast<AudioCaptureRole>(UINT32_MAX);
        CHECK(source->Start(config) == VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(state->start_calls == 0);
    }
    {
        auto state = std::make_shared<PortState>();
        auto source = Source(state);
        auto config = Config();
        config.role = AudioCaptureRole::Microphone;
        CHECK(source->Start(config) == VRREC_STATUS_OK);
    }
}

void MapsEveryPortStartFailureAndClosesOnce()
{
    using Case = std::pair<WasapiCapturePortResult, vrrec_status_t>;
    for (const auto &[port_result, expected] : {
             Case {WasapiCapturePortResult::OutOfMemory,
                   VRREC_STATUS_OUT_OF_MEMORY},
             Case {WasapiCapturePortResult::InvalidArgument,
                   VRREC_STATUS_INVALID_ARGUMENT},
             Case {WasapiCapturePortResult::DeviceLost,
                   VRREC_STATUS_BACKEND_UNAVAILABLE},
             Case {WasapiCapturePortResult::BackendUnavailable,
                   VRREC_STATUS_BACKEND_UNAVAILABLE},
             Case {WasapiCapturePortResult::Empty,
                   VRREC_STATUS_INTERNAL_ERROR},
             Case {WasapiCapturePortResult::Aborted,
                   VRREC_STATUS_INTERNAL_ERROR},
             Case {WasapiCapturePortResult::Failed,
                   VRREC_STATUS_INTERNAL_ERROR},
         }) {
        auto state = std::make_shared<PortState>();
        state->start_result = port_result;
        {
            auto source = Source(state);
            CHECK(source->Start(Config()) == expected);
            CHECK(source->Start(Config()) == VRREC_STATUS_INVALID_ARGUMENT);
        }
        CHECK(state->start_calls == 1);
        CHECK(state->close_calls == 1);
    }
}

void MapsRemainingAcquireAndReleaseFailures()
{
    {
        auto state = std::make_shared<PortState>();
        state->steps.push_back({
            WasapiCapturePortResult::Aborted,
            0,
            1'000'000,
            0,
            {},
        });
        auto source = Source(state);
        CHECK(source->Start(Config()) == VRREC_STATUS_OK);
        CHECK(source->Read().result == AudioCaptureReadResult::Aborted);
        CHECK(state->released_frames.empty());
    }
    {
        auto state = std::make_shared<PortState>();
        auto source = Source(state);
        CHECK(source->Start(Config()) == VRREC_STATUS_OK);
        CHECK(source->Read().result == AudioCaptureReadResult::Failed);
        CHECK(state->released_frames.empty());
    }
    {
        auto state = std::make_shared<PortState>();
        state->steps.push_back({
            WasapiCapturePortResult::Ok,
            0,
            1'000'000,
            2,
            FloatBytes({0.0F, 0.0F, 0.0F, 0.0F}),
            false,
            false,
            false,
            WasapiCapturePortResult::Failed,
        });
        auto source = Source(state);
        CHECK(source->Start(Config()) == VRREC_STATUS_OK);
        CHECK(source->Read().result == AudioCaptureReadResult::Failed);
        CHECK(state->released_frames == std::vector<std::uint32_t> {2});
    }
}

void ReportsNormalizerAllocationFailureAndClosesOnce()
{
    auto state = std::make_shared<PortState>();
    auto source = Source(state);

    allocation_failure::fail_on_allocation = 1;
    const auto status = source->Start(Config());
    allocation_failure::fail_on_allocation = 0;

    CHECK(status == VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(state->start_calls == 1);
    CHECK(state->close_calls == 1);
    CHECK(source->Read().result == AudioCaptureReadResult::Failed);
}

}

int main()
{
    ReleasesEverySuccessfullyAcquiredBuffer();
    PreservesSilentDiscontinuityAndTimestampSemantics();
    SkipsAReleasedDiscontinuityPacketThatPredatesTheSession();
    MapsAcquireAndReleaseFailures();
    RejectsCrossThreadReadAndCleansUpOnTheCaptureThread();
    AbortWakesAcquireAndCleanupRemainsOnTheCaptureThread();
    StartFailureClosesExactlyOnce();
    RejectsEveryInvalidStartStateAndAcceptsBothRoles();
    MapsEveryPortStartFailureAndClosesOnce();
    MapsRemainingAcquireAndReleaseFailures();
    ReportsNormalizerAllocationFailureAndClosesOnce();
    return 0;
}
