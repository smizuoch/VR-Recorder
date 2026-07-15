#ifndef VRRECORDER_NATIVE_PRE_HEADER_COORDINATOR_HPP
#define VRRECORDER_NATIVE_PRE_HEADER_COORDINATOR_HPP

#include <atomic>
#include <condition_variable>
#include <cstdint>
#include <memory>
#include <mutex>
#include <optional>
#include <span>
#include <vector>

#include "encoded_media_packet_submission_port.hpp"
#include "media_recording_session.hpp"

namespace vrrecorder::native {

enum class PreHeaderState : std::uint8_t {
    Created,
    Priming,
    HeaderStarting,
    DrainingPreHeader,
    Running,
    Finishing,
    Failed,
    Aborted,
};

class PreHeaderCoordinator final : public EncodedMediaPacketSubmissionPort {
public:
    PreHeaderCoordinator(
        MediaMuxSessionPort &mux_session,
        EncodedMediaPacketSubmissionPort &submission,
        AacStreamDescriptor audio_descriptor,
        FragmentedMp4FragmentPolicy fragment_policy,
        const void *expected_video_encoder_identity);
    ~PreHeaderCoordinator();

    PreHeaderCoordinator(const PreHeaderCoordinator &) = delete;
    PreHeaderCoordinator &operator=(const PreHeaderCoordinator &) = delete;

    vrrec_status_t BeginPriming(std::int64_t capture_epoch) noexcept;
    vrrec_status_t ProducerStarted(MediaStreamKind producer) noexcept;
    vrrec_status_t PublishVideoDescriptor(
        const void *encoder_identity,
        const H264StreamDescriptor &descriptor) noexcept;
    Mp4MuxResult SubmitBatch(
        MediaStreamKind producer,
        std::span<const EncodedMediaPacket> packets) noexcept override;
    vrrec_status_t EncoderFinished(
        MediaStreamKind stream) noexcept override;
    void EncoderFailed(MediaStreamKind stream) noexcept override;
    void Abort() noexcept;
    PreHeaderState State() const noexcept;
#if defined(VRRECORDER_NATIVE_TESTING)
    std::size_t QueuedPacketCountForTesting() const noexcept;
#endif

private:
    struct SubmissionTicket final {
        std::condition_variable changed;
        Mp4MuxResult result = Mp4MuxResult::MuxFailed;
        std::size_t packet_count = 0;
        bool completed = false;
    };

    struct QueuedBatch final {
        MediaStreamKind producer;
        std::vector<EncodedMediaPacket> packets;
        std::shared_ptr<SubmissionTicket> ticket;
        std::uint64_t sequence;
    };

    vrrec_status_t TryStartHeaderLocked(
        std::unique_lock<std::mutex> &lock) noexcept;
    vrrec_status_t DrainQueuedPackets() noexcept;
    vrrec_status_t FailLocked(vrrec_status_t status) noexcept;
    void FailQueuedSubmissionsLocked(Mp4MuxResult result) noexcept;
    void CompleteTicketLocked(
        const std::shared_ptr<SubmissionTicket> &ticket,
        Mp4MuxResult result) noexcept;
    void AbortDownstreamLocked() noexcept;

    MediaMuxSessionPort &mux_session_;
    EncodedMediaPacketSubmissionPort &submission_;
    AacStreamDescriptor audio_descriptor_;
    FragmentedMp4FragmentPolicy fragment_policy_;
    const void *expected_video_encoder_identity_;
    mutable std::mutex mutex_;
    std::optional<H264StreamDescriptor> video_descriptor_;
    std::vector<QueuedBatch> queued_batches_;
    std::vector<std::shared_ptr<SubmissionTicket>> outstanding_tickets_;
    std::int64_t capture_epoch_ = 0;
    std::size_t queued_packet_count_ = 0;
    std::uint64_t next_submission_sequence_ = 0;
    bool video_started_ = false;
    bool audio_started_ = false;
    bool downstream_aborted_ = false;
    PreHeaderState state_ = PreHeaderState::Created;
    std::atomic_bool abort_requested_ = false;
};

}

#endif
