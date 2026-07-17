#include "spout2_source_backend_core.hpp"

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <cstdlib>
#include <deque>
#include <future>
#include <iostream>
#include <memory>
#include <mutex>
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

class FakeVideoSurface final : public VideoSurface {
public:
    FakeVideoSurface(
        VideoSurfaceDescriptor descriptor,
        std::shared_ptr<std::atomic_size_t> destructions) noexcept
        : descriptor_(descriptor), destructions_(std::move(destructions))
    {
    }

    ~FakeVideoSurface() override
    {
        ++*destructions_;
    }

    VideoSurfaceDescriptor Descriptor() const noexcept override
    {
        return descriptor_;
    }

    void *NativeHandle() const noexcept override
    {
        return reinterpret_cast<void *>(1);
    }

    VideoSurfaceAcquireResult AcquireForRead(
        std::chrono::milliseconds) noexcept override
    {
        return VideoSurfaceAcquireResult::Acquired;
    }

    vrrec_status_t ReleaseFromRead() noexcept override
    {
        return VRREC_STATUS_OK;
    }

private:
    VideoSurfaceDescriptor descriptor_;
    std::shared_ptr<std::atomic_size_t> destructions_;
};

struct ReceiveStep final {
    Spout2ReceiverResult result;
    Spout2TextureMetadata metadata;
};

class ScriptedReceiverPort final : public Spout2ReceiverPort {
public:
    vrrec_status_t Snapshot(
        std::vector<SpoutSenderSnapshot> &senders) override
    {
        const std::lock_guard lock(mutex_);
        senders = snapshot;
        return snapshot_status;
    }

    Spout2ReceiverResult Receive(
        std::chrono::milliseconds timeout,
        Spout2TextureMetadata &metadata) noexcept override
    {
        std::unique_lock lock(mutex_);
        last_timeout = timeout;
        ++receive_calls;
        receive_entered_ = true;
        changed_.notify_all();
        changed_.wait(lock, [this] { return !block_receive_; });
        if (receive_steps.empty()) {
            return Spout2ReceiverResult::Timeout;
        }
        auto step = std::move(receive_steps.front());
        receive_steps.pop_front();
        metadata = std::move(step.metadata);
        return step.result;
    }

    vrrec_status_t CopySurface(
        const Spout2TextureMetadata &metadata,
        std::uint64_t generation_id,
        std::shared_ptr<VideoSurface> &surface) noexcept override
    {
        std::unique_lock lock(mutex_);
        ++copy_calls;
        copied_generations.push_back(generation_id);
        copy_entered_ = true;
        changed_.notify_all();
        changed_.wait(lock, [this] { return !block_copy_; });
        if (copy_status != VRREC_STATUS_OK) {
            surface.reset();
            return copy_status;
        }
        auto descriptor = VideoSurfaceDescriptor {
            metadata.adapter_luid,
            metadata.width,
            metadata.height,
            metadata.pixel_format,
            generation_id,
        };
        if (mismatch_surface) {
            ++descriptor.width;
        }
        try {
            surface = std::make_shared<FakeVideoSurface>(
                descriptor,
                destructions);
            return VRREC_STATUS_OK;
        } catch (...) {
            surface.reset();
            return VRREC_STATUS_OUT_OF_MEMORY;
        }
    }

    void Abort() noexcept override
    {
        const std::lock_guard lock(mutex_);
        ++abort_calls;
    }

    void BlockReceive()
    {
        const std::lock_guard lock(mutex_);
        block_receive_ = true;
    }

    void WaitForReceive()
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [this] { return receive_entered_; });
    }

    void ReleaseReceive()
    {
        {
            const std::lock_guard lock(mutex_);
            block_receive_ = false;
        }
        changed_.notify_all();
    }

    void BlockCopy()
    {
        const std::lock_guard lock(mutex_);
        block_copy_ = true;
    }

    void WaitForCopy()
    {
        std::unique_lock lock(mutex_);
        changed_.wait(lock, [this] { return copy_entered_; });
    }

    void ReleaseCopy()
    {
        {
            const std::lock_guard lock(mutex_);
            block_copy_ = false;
        }
        changed_.notify_all();
    }

    vrrec_status_t snapshot_status = VRREC_STATUS_OK;
    std::vector<SpoutSenderSnapshot> snapshot;
    std::deque<ReceiveStep> receive_steps;
    vrrec_status_t copy_status = VRREC_STATUS_OK;
    bool mismatch_surface = false;
    std::shared_ptr<std::atomic_size_t> destructions =
        std::make_shared<std::atomic_size_t>(0);
    std::chrono::milliseconds last_timeout {0};
    std::size_t receive_calls = 0;
    std::size_t copy_calls = 0;
    std::size_t abort_calls = 0;
    std::vector<std::uint64_t> copied_generations;

private:
    std::mutex mutex_;
    std::condition_variable changed_;
    bool block_receive_ = false;
    bool block_copy_ = false;
    bool receive_entered_ = false;
    bool copy_entered_ = false;
};

Spout2TextureMetadata Metadata(
    std::uint64_t sequence,
    std::uint64_t resource_identity = 10,
    std::uint64_t receiver_epoch = 1,
    std::uint64_t adapter_luid = 42)
{
    return Spout2TextureMetadata {
        "sender",
        resource_identity,
        receiver_epoch,
        adapter_luid,
        "gpu",
        VRREC_GPU_VENDOR_NVIDIA,
        1'920,
        1'080,
        VRREC_SOURCE_PIXEL_FORMAT_BGRA8,
        60.0,
        sequence,
        static_cast<std::int64_t>(sequence * 10'000),
    };
}

std::unique_ptr<SpoutSourceBackend> CreateBackend(
    std::unique_ptr<ScriptedReceiverPort> port,
    ScriptedReceiverPort *&borrowed)
{
    borrowed = port.get();
    auto status = VRREC_STATUS_INTERNAL_ERROR;
    auto backend = CreateSpout2SourceBackend(std::move(port), status);
    CHECK(status == VRREC_STATUS_OK);
    CHECK(backend != nullptr);
    return backend;
}

void CanonicalizesZeroOneAndMultipleSenderSnapshots()
{
    auto port = std::make_unique<ScriptedReceiverPort>();
    ScriptedReceiverPort *borrowed = nullptr;
    auto backend = CreateBackend(std::move(port), borrowed);
    std::vector<SpoutSenderSnapshot> output;

    CHECK(backend->Snapshot(output) == VRREC_STATUS_OK);
    CHECK(output.empty());

    borrowed->snapshot = {{"only", 3}};
    CHECK(backend->Snapshot(output) == VRREC_STATUS_OK);
    CHECK(output.size() == 1);
    CHECK(output[0].sender_id == "only");
    CHECK(output[0].latest_frame_generation == 3);

    borrowed->snapshot = {{"zeta", 7}, {"alpha", 5}};
    CHECK(backend->Snapshot(output) == VRREC_STATUS_OK);
    CHECK(output.size() == 2);
    CHECK(output[0].sender_id == "alpha");
    CHECK(output[1].sender_id == "zeta");

    borrowed->snapshot = {{"duplicate", 1}, {"duplicate", 2}};
    CHECK(backend->Snapshot(output) == VRREC_STATUS_INTERNAL_ERROR);
    CHECK(output.empty());
}

void CopiesEveryFrameAndAdvancesOnlyResourceGenerations()
{
    auto port = std::make_unique<ScriptedReceiverPort>();
    port->receive_steps.push_back({
        Spout2ReceiverResult::FrameReady,
        Metadata(1),
    });
    port->receive_steps.push_back({
        Spout2ReceiverResult::FrameReady,
        Metadata(2),
    });
    port->receive_steps.push_back({
        Spout2ReceiverResult::FrameReady,
        Metadata(3, 20),
    });
    ScriptedReceiverPort *borrowed = nullptr;
    auto backend = CreateBackend(std::move(port), borrowed);

    SpoutFrame first;
    SpoutFrame second;
    SpoutFrame replacement;
    CHECK(backend->Poll(std::chrono::milliseconds(25), first) ==
          VRREC_STATUS_OK);
    CHECK(backend->Poll(std::chrono::milliseconds(25), second) ==
          VRREC_STATUS_OK);
    CHECK(backend->Poll(std::chrono::milliseconds(25), replacement) ==
          VRREC_STATUS_OK);

    CHECK(borrowed->last_timeout == std::chrono::milliseconds(25));
    CHECK(borrowed->copy_calls == 3);
    CHECK((borrowed->copied_generations ==
           std::vector<std::uint64_t> {1, 1, 2}));
    CHECK(first.surface != second.surface);
    CHECK(first.surface->Descriptor().generation_id == 1);
    CHECK(second.surface->Descriptor().generation_id == 1);
    CHECK(replacement.surface->Descriptor().generation_id == 2);
    CHECK(replacement.frame_sequence == 3);
}

void RejectsMetadataMutationWithoutAResourceChange()
{
    auto port = std::make_unique<ScriptedReceiverPort>();
    port->receive_steps.push_back({
        Spout2ReceiverResult::FrameReady,
        Metadata(1),
    });
    auto changed = Metadata(2);
    changed.width = 1'280;
    port->receive_steps.push_back({
        Spout2ReceiverResult::FrameReady,
        std::move(changed),
    });
    ScriptedReceiverPort *borrowed = nullptr;
    auto backend = CreateBackend(std::move(port), borrowed);
    SpoutFrame frame;

    CHECK(backend->Poll(std::chrono::milliseconds(10), frame) ==
          VRREC_STATUS_OK);
    frame.surface.reset();
    CHECK(backend->Poll(std::chrono::milliseconds(10), frame) ==
          VRREC_STATUS_INTERNAL_ERROR);
    CHECK(frame.surface == nullptr);
    CHECK(borrowed->copy_calls == 1);
}

void AllowsLossAndReappearanceWithANewReceiverEpoch()
{
    auto port = std::make_unique<ScriptedReceiverPort>();
    port->receive_steps.push_back({
        Spout2ReceiverResult::FrameReady,
        Metadata(1),
    });
    port->receive_steps.push_back({Spout2ReceiverResult::SenderLost, {}});
    port->receive_steps.push_back({
        Spout2ReceiverResult::FrameReady,
        Metadata(2, 10, 2),
    });
    ScriptedReceiverPort *borrowed = nullptr;
    auto backend = CreateBackend(std::move(port), borrowed);
    SpoutFrame frame;

    CHECK(backend->Poll(std::chrono::milliseconds(10), frame) ==
          VRREC_STATUS_OK);
    CHECK(backend->Poll(std::chrono::milliseconds(10), frame) ==
          VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(backend->Poll(std::chrono::milliseconds(10), frame) ==
          VRREC_STATUS_OK);
    CHECK(frame.surface->Descriptor().generation_id == 2);
    CHECK(borrowed->copy_calls == 2);
}

void AllowsFrameSequenceToRestartInANewReceiverEpoch()
{
    auto port = std::make_unique<ScriptedReceiverPort>();
    port->receive_steps.push_back({
        Spout2ReceiverResult::FrameReady,
        Metadata(10),
    });
    port->receive_steps.push_back({Spout2ReceiverResult::SenderLost, {}});
    port->receive_steps.push_back({
        Spout2ReceiverResult::FrameReady,
        Metadata(1, 10, 2),
    });
    ScriptedReceiverPort *borrowed = nullptr;
    auto backend = CreateBackend(std::move(port), borrowed);
    SpoutFrame frame;

    CHECK(backend->Poll(std::chrono::milliseconds(10), frame) ==
          VRREC_STATUS_OK);
    CHECK(backend->Poll(std::chrono::milliseconds(10), frame) ==
          VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(backend->Poll(std::chrono::milliseconds(10), frame) ==
          VRREC_STATUS_OK);
    CHECK(frame.frame_sequence == 1);
    CHECK(frame.surface->Descriptor().generation_id == 2);
    CHECK(borrowed->copy_calls == 2);
}

void MapsTimeoutAndPortFailuresWithoutPublishingAFrame()
{
    auto port = std::make_unique<ScriptedReceiverPort>();
    port->receive_steps.push_back({Spout2ReceiverResult::Timeout, {}});
    port->receive_steps.push_back({Spout2ReceiverResult::OutOfMemory, {}});
    port->receive_steps.push_back({Spout2ReceiverResult::Failed, {}});
    ScriptedReceiverPort *borrowed = nullptr;
    auto backend = CreateBackend(std::move(port), borrowed);
    SpoutFrame frame;

    CHECK(backend->Poll(std::chrono::milliseconds(10), frame) ==
          VRREC_STATUS_TIMEOUT);
    CHECK(frame.surface == nullptr);
    CHECK(backend->Poll(std::chrono::milliseconds(10), frame) ==
          VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(backend->Poll(std::chrono::milliseconds(10), frame) ==
          VRREC_STATUS_INTERNAL_ERROR);
    CHECK(borrowed->copy_calls == 0);
}

void RejectsASurfaceThatDoesNotMatchTheReceivedTexture()
{
    auto port = std::make_unique<ScriptedReceiverPort>();
    port->mismatch_surface = true;
    port->receive_steps.push_back({
        Spout2ReceiverResult::FrameReady,
        Metadata(1),
    });
    const auto destructions = port->destructions;
    ScriptedReceiverPort *borrowed = nullptr;
    auto backend = CreateBackend(std::move(port), borrowed);
    SpoutFrame frame;

    CHECK(backend->Poll(std::chrono::milliseconds(10), frame) ==
          VRREC_STATUS_INTERNAL_ERROR);
    CHECK(frame.surface == nullptr);
    CHECK(destructions->load() == 1);
}

void AbortWinsOverAFrameReturnedByAnInFlightReceive()
{
    auto port = std::make_unique<ScriptedReceiverPort>();
    port->BlockReceive();
    port->receive_steps.push_back({
        Spout2ReceiverResult::FrameReady,
        Metadata(1),
    });
    ScriptedReceiverPort *borrowed = nullptr;
    auto backend = CreateBackend(std::move(port), borrowed);
    SpoutFrame frame;
    auto polling = std::async(std::launch::async, [&] {
        return backend->Poll(std::chrono::milliseconds(100), frame);
    });
    borrowed->WaitForReceive();
    backend->Abort();
    backend->Abort();
    borrowed->ReleaseReceive();

    CHECK(polling.get() == VRREC_STATUS_INVALID_STATE);
    CHECK(frame.surface == nullptr);
    CHECK(borrowed->abort_calls == 1);
    CHECK(borrowed->copy_calls == 0);
}

void AbortDropsAndReleasesALateCopiedSurfaceExactlyOnce()
{
    auto port = std::make_unique<ScriptedReceiverPort>();
    port->BlockCopy();
    port->receive_steps.push_back({
        Spout2ReceiverResult::FrameReady,
        Metadata(1),
    });
    const auto destructions = port->destructions;
    ScriptedReceiverPort *borrowed = nullptr;
    auto backend = CreateBackend(std::move(port), borrowed);
    SpoutFrame frame;
    auto polling = std::async(std::launch::async, [&] {
        return backend->Poll(std::chrono::milliseconds(100), frame);
    });
    borrowed->WaitForCopy();
    backend->Abort();
    borrowed->ReleaseCopy();

    CHECK(polling.get() == VRREC_STATUS_INVALID_STATE);
    CHECK(frame.surface == nullptr);
    CHECK(destructions->load() == 1);
    CHECK(borrowed->abort_calls == 1);
}

void ReleasesAnAcceptedSurfaceWhenTheConsumerDropsIt()
{
    auto port = std::make_unique<ScriptedReceiverPort>();
    port->receive_steps.push_back({
        Spout2ReceiverResult::FrameReady,
        Metadata(1),
    });
    const auto destructions = port->destructions;
    ScriptedReceiverPort *borrowed = nullptr;
    auto backend = CreateBackend(std::move(port), borrowed);
    SpoutFrame frame;

    CHECK(backend->Poll(std::chrono::milliseconds(10), frame) ==
          VRREC_STATUS_OK);
    CHECK(destructions->load() == 0);
    frame.surface.reset();
    CHECK(destructions->load() == 1);
}

}

int main()
{
    CanonicalizesZeroOneAndMultipleSenderSnapshots();
    CopiesEveryFrameAndAdvancesOnlyResourceGenerations();
    RejectsMetadataMutationWithoutAResourceChange();
    AllowsLossAndReappearanceWithANewReceiverEpoch();
    AllowsFrameSequenceToRestartInANewReceiverEpoch();
    MapsTimeoutAndPortFailuresWithoutPublishingAFrame();
    RejectsASurfaceThatDoesNotMatchTheReceivedTexture();
    AbortWinsOverAFrameReturnedByAnInFlightReceive();
    AbortDropsAndReleasesALateCopiedSurfaceExactlyOnce();
    ReleasesAnAcceptedSurfaceWhenTheConsumerDropsIt();
    return 0;
}
