#include "spout2_source_backend_core.hpp"

#include <atomic>
#include <array>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstdint>
#include <cstdlib>
#include <deque>
#include <future>
#include <iostream>
#include <limits>
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
        std::shared_ptr<std::atomic_size_t> destructions,
        bool has_native_handle = true) noexcept
        : descriptor_(descriptor), destructions_(std::move(destructions)),
          has_native_handle_(has_native_handle)
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
        return has_native_handle_ ? reinterpret_cast<void *>(1) : nullptr;
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
    bool has_native_handle_;
};

enum class SurfaceFault {
    None,
    Missing,
    NullHandle,
    Adapter,
    Width,
    Height,
    PixelFormat,
    Generation,
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
        if (surface_fault == SurfaceFault::Missing) {
            surface.reset();
            return VRREC_STATUS_OK;
        }
        auto descriptor = VideoSurfaceDescriptor {
            metadata.adapter_luid,
            metadata.width,
            metadata.height,
            metadata.pixel_format,
            generation_id,
        };
        if (surface_fault == SurfaceFault::Adapter) {
            ++descriptor.adapter_luid;
        } else if (surface_fault == SurfaceFault::Width) {
            ++descriptor.width;
        } else if (surface_fault == SurfaceFault::Height) {
            ++descriptor.height;
        } else if (surface_fault == SurfaceFault::PixelFormat) {
            descriptor.pixel_format = VRREC_SOURCE_PIXEL_FORMAT_RGBA8;
        } else if (surface_fault == SurfaceFault::Generation) {
            ++descriptor.generation_id;
        }
        try {
            surface = std::make_shared<FakeVideoSurface>(
                descriptor,
                destructions,
                surface_fault != SurfaceFault::NullHandle);
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
    SurfaceFault surface_fault = SurfaceFault::None;
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

void RejectsInvalidSenderSnapshotsAndPortFailures()
{
    auto port = std::make_unique<ScriptedReceiverPort>();
    ScriptedReceiverPort *borrowed = nullptr;
    auto backend = CreateBackend(std::move(port), borrowed);
    std::vector<SpoutSenderSnapshot> output { {"stale", 99} };

    borrowed->snapshot_status = VRREC_STATUS_BACKEND_UNAVAILABLE;
    CHECK(backend->Snapshot(output) == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(output.empty());
    borrowed->snapshot_status = VRREC_STATUS_OK;

    const auto rejects = [&](std::string sender_id) {
        borrowed->snapshot = {{std::move(sender_id), 1}};
        output = {{"stale", 99}};
        CHECK(backend->Snapshot(output) == VRREC_STATUS_INTERNAL_ERROR);
        CHECK(output.empty());
    };
    rejects("");
    rejects(std::string(VRREC_SPOUT_MAX_IDENTITY_UTF8_SIZE + 1, 's'));
    rejects(std::string("sender\0suffix", 13));

    borrowed->snapshot.clear();
    borrowed->snapshot.reserve(VRREC_SPOUT_MAX_SNAPSHOT_ENTRIES + 1);
    for (std::uint32_t index = 0;
         index <= VRREC_SPOUT_MAX_SNAPSHOT_ENTRIES;
         ++index) {
        borrowed->snapshot.push_back({"sender-" + std::to_string(index), 1});
    }
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

void RejectsEveryMalformedTextureMetadataBoundary()
{
    const auto rejects = [](Spout2TextureMetadata metadata) {
        auto port = std::make_unique<ScriptedReceiverPort>();
        port->receive_steps.push_back({
            Spout2ReceiverResult::FrameReady,
            std::move(metadata),
        });
        ScriptedReceiverPort *borrowed = nullptr;
        auto backend = CreateBackend(std::move(port), borrowed);
        SpoutFrame frame;
        CHECK(backend->Poll(std::chrono::milliseconds(10), frame) ==
              VRREC_STATUS_INTERNAL_ERROR);
        CHECK(frame.surface == nullptr);
        CHECK(borrowed->copy_calls == 0);
    };

    auto invalid = Metadata(1);
    invalid.sender_id.clear();
    rejects(std::move(invalid));
    invalid = Metadata(1);
    invalid.sender_id.assign(VRREC_SPOUT_MAX_IDENTITY_UTF8_SIZE + 1, 's');
    rejects(std::move(invalid));
    invalid = Metadata(1);
    invalid.sender_id = std::string("sender\0suffix", 13);
    rejects(std::move(invalid));
    invalid = Metadata(1);
    invalid.resource_identity = 0;
    rejects(std::move(invalid));
    invalid = Metadata(1);
    invalid.receiver_epoch = 0;
    rejects(std::move(invalid));
    invalid = Metadata(1);
    invalid.adapter_luid = 0;
    rejects(std::move(invalid));
    invalid = Metadata(1);
    invalid.gpu_identity.clear();
    rejects(std::move(invalid));
    invalid = Metadata(1);
    invalid.gpu_identity.assign(VRREC_SPOUT_MAX_IDENTITY_UTF8_SIZE + 1, 'g');
    rejects(std::move(invalid));
    invalid = Metadata(1);
    invalid.gpu_identity = std::string("gpu\0identity", 12);
    rejects(std::move(invalid));
    invalid = Metadata(1);
    invalid.gpu_vendor = static_cast<vrrec_gpu_vendor_t>(99);
    rejects(std::move(invalid));
    invalid = Metadata(1);
    invalid.width = 0;
    rejects(std::move(invalid));
    invalid = Metadata(1);
    invalid.height = 0;
    rejects(std::move(invalid));
    invalid = Metadata(1);
    invalid.pixel_format = static_cast<vrrec_source_pixel_format_t>(99);
    rejects(std::move(invalid));
    invalid = Metadata(1);
    invalid.estimated_source_fps =
        std::numeric_limits<double>::quiet_NaN();
    rejects(std::move(invalid));
    invalid = Metadata(1);
    invalid.estimated_source_fps =
        std::numeric_limits<double>::infinity();
    rejects(std::move(invalid));
    invalid = Metadata(1);
    invalid.estimated_source_fps = 0.0;
    rejects(std::move(invalid));
    invalid = Metadata(1);
    invalid.estimated_source_fps = -1.0;
    rejects(std::move(invalid));
    invalid = Metadata(1);
    invalid.estimated_source_fps = 1'000.01;
    rejects(std::move(invalid));
    invalid = Metadata(1);
    invalid.frame_sequence = 0;
    rejects(std::move(invalid));
    invalid = Metadata(1);
    invalid.monotonic_timestamp_microseconds = -1;
    rejects(std::move(invalid));
}

void AcceptsEverySupportedVendorAndPixelFormat()
{
    constexpr std::array vendors {
        VRREC_GPU_VENDOR_UNKNOWN,
        VRREC_GPU_VENDOR_NVIDIA,
        VRREC_GPU_VENDOR_AMD,
        VRREC_GPU_VENDOR_INTEL,
    };
    constexpr std::array pixel_formats {
        VRREC_SOURCE_PIXEL_FORMAT_BGRA8,
        VRREC_SOURCE_PIXEL_FORMAT_RGBA8,
        VRREC_SOURCE_PIXEL_FORMAT_NV12,
    };
    for (const auto vendor : vendors) {
        for (const auto pixel_format : pixel_formats) {
            auto metadata = Metadata(1);
            metadata.gpu_vendor = vendor;
            metadata.pixel_format = pixel_format;
            auto port = std::make_unique<ScriptedReceiverPort>();
            port->receive_steps.push_back({
                Spout2ReceiverResult::FrameReady,
                std::move(metadata),
            });
            ScriptedReceiverPort *borrowed = nullptr;
            auto backend = CreateBackend(std::move(port), borrowed);
            SpoutFrame frame;
            CHECK(backend->Poll(std::chrono::milliseconds(10), frame) ==
                  VRREC_STATUS_OK);
            CHECK(frame.gpu_vendor == vendor);
            CHECK(frame.pixel_format == pixel_format);
        }
    }
}

void RejectsRegressedAndDuplicateFramesWithinAnEpoch()
{
    auto port = std::make_unique<ScriptedReceiverPort>();
    port->receive_steps.push_back({Spout2ReceiverResult::FrameReady, Metadata(2)});
    port->receive_steps.push_back({Spout2ReceiverResult::FrameReady, Metadata(2)});
    port->receive_steps.push_back({Spout2ReceiverResult::FrameReady, Metadata(1)});
    port->receive_steps.push_back({
        Spout2ReceiverResult::FrameReady,
        Metadata(3, 10, 0),
    });
    ScriptedReceiverPort *borrowed = nullptr;
    auto backend = CreateBackend(std::move(port), borrowed);
    SpoutFrame frame;

    CHECK(backend->Poll(std::chrono::milliseconds(10), frame) ==
          VRREC_STATUS_OK);
    CHECK(backend->Poll(std::chrono::milliseconds(10), frame) ==
          VRREC_STATUS_TIMEOUT);
    CHECK(backend->Poll(std::chrono::milliseconds(10), frame) ==
          VRREC_STATUS_INTERNAL_ERROR);
    CHECK(backend->Poll(std::chrono::milliseconds(10), frame) ==
          VRREC_STATUS_INTERNAL_ERROR);
    CHECK(borrowed->copy_calls == 1);
}

void RejectsEverySurfaceDescriptorMismatch()
{
    constexpr std::array faults {
        SurfaceFault::Missing,
        SurfaceFault::NullHandle,
        SurfaceFault::Adapter,
        SurfaceFault::Width,
        SurfaceFault::Height,
        SurfaceFault::PixelFormat,
        SurfaceFault::Generation,
    };
    for (const auto fault : faults) {
        auto port = std::make_unique<ScriptedReceiverPort>();
        port->surface_fault = fault;
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
        CHECK(destructions->load() ==
              (fault == SurfaceFault::Missing ? 0 : 1));
    }
}

void ValidatesPollTimeoutsAndReceiverResults()
{
    auto port = std::make_unique<ScriptedReceiverPort>();
    port->receive_steps.push_back({Spout2ReceiverResult::Aborted, {}});
    port->receive_steps.push_back({Spout2ReceiverResult::Timeout, {}});
    ScriptedReceiverPort *borrowed = nullptr;
    auto backend = CreateBackend(std::move(port), borrowed);
    SpoutFrame frame;

    CHECK(backend->Poll(std::chrono::milliseconds(-1), frame) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(backend->Poll(std::chrono::milliseconds(
              VRREC_SPOUT_MAX_POLL_TIMEOUT_MILLISECONDS + 1), frame) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(borrowed->receive_calls == 0);
    CHECK(backend->Poll(std::chrono::milliseconds(0), frame) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(backend->Poll(std::chrono::milliseconds(
              VRREC_SPOUT_MAX_POLL_TIMEOUT_MILLISECONDS), frame) ==
          VRREC_STATUS_TIMEOUT);
    CHECK(borrowed->receive_calls == 2);
}

void ForwardsCopyFailuresWithoutPublishingASurface()
{
    auto port = std::make_unique<ScriptedReceiverPort>();
    port->copy_status = VRREC_STATUS_OUT_OF_MEMORY;
    port->receive_steps.push_back({
        Spout2ReceiverResult::FrameReady,
        Metadata(1),
    });
    ScriptedReceiverPort *borrowed = nullptr;
    auto backend = CreateBackend(std::move(port), borrowed);
    SpoutFrame frame;

    CHECK(backend->Poll(std::chrono::milliseconds(10), frame) ==
          VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(frame.surface == nullptr);
    CHECK(borrowed->copy_calls == 1);
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
    port->surface_fault = SurfaceFault::Width;
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

void RejectsCreationWithoutAReceiverAndOperationsAfterAbort()
{
    auto status = VRREC_STATUS_OK;
    CHECK(CreateSpout2SourceBackend(nullptr, status) == nullptr);
    CHECK(status == VRREC_STATUS_INVALID_ARGUMENT);

    auto port = std::make_unique<ScriptedReceiverPort>();
    ScriptedReceiverPort *borrowed = nullptr;
    auto backend = CreateBackend(std::move(port), borrowed);
    backend->Abort();
    std::vector<SpoutSenderSnapshot> senders {{"stale", 99}};
    SpoutFrame frame;
    CHECK(backend->Snapshot(senders) == VRREC_STATUS_INVALID_STATE);
    CHECK(senders.empty());
    CHECK(backend->Poll(std::chrono::milliseconds(10), frame) ==
          VRREC_STATUS_INVALID_STATE);
    CHECK(frame.surface == nullptr);
    CHECK(borrowed->abort_calls == 1);
    CHECK(borrowed->receive_calls == 0);
}

}

int main()
{
    CanonicalizesZeroOneAndMultipleSenderSnapshots();
    RejectsInvalidSenderSnapshotsAndPortFailures();
    CopiesEveryFrameAndAdvancesOnlyResourceGenerations();
    RejectsMetadataMutationWithoutAResourceChange();
    AllowsLossAndReappearanceWithANewReceiverEpoch();
    AllowsFrameSequenceToRestartInANewReceiverEpoch();
    RejectsEveryMalformedTextureMetadataBoundary();
    AcceptsEverySupportedVendorAndPixelFormat();
    RejectsRegressedAndDuplicateFramesWithinAnEpoch();
    RejectsEverySurfaceDescriptorMismatch();
    ValidatesPollTimeoutsAndReceiverResults();
    ForwardsCopyFailuresWithoutPublishingASurface();
    MapsTimeoutAndPortFailuresWithoutPublishingAFrame();
    RejectsASurfaceThatDoesNotMatchTheReceivedTexture();
    AbortWinsOverAFrameReturnedByAnInFlightReceive();
    AbortDropsAndReleasesALateCopiedSurfaceExactlyOnce();
    ReleasesAnAcceptedSurfaceWhenTheConsumerDropsIt();
    RejectsCreationWithoutAReceiverAndOperationsAfterAbort();
    return 0;
}
