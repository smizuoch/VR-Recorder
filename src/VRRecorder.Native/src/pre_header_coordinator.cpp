#include "pre_header_coordinator.hpp"

#include <algorithm>
#include <limits>
#include <new>
#include <utility>

namespace vrrecorder::native {
namespace {

bool IsVideoDescriptorValid(
    const H264StreamDescriptor &descriptor) noexcept
{
    constexpr std::uint32_t maximum_dimension = 16'384;
    const auto profile_valid = descriptor.profile == H264Profile::Main ||
        descriptor.profile == H264Profile::High;
    return descriptor.packet_time_base == MicrosecondPacketTimeBase &&
        descriptor.width != 0 && descriptor.height != 0 &&
        descriptor.width <= maximum_dimension &&
        descriptor.height <= maximum_dimension &&
        descriptor.width % 2 == 0 && descriptor.height % 2 == 0 &&
        profile_valid &&
        descriptor.packet_format == H264PacketFormat::AvccLengthPrefixed &&
        !descriptor.codec_extradata.empty();
}

bool IsAudioDescriptorValid(
    const AacStreamDescriptor &descriptor) noexcept
{
    return descriptor.packet_time_base == MicrosecondPacketTimeBase &&
        descriptor.sample_rate == 48'000 && descriptor.channel_count == 2 &&
        descriptor.frame_size != 0 &&
        descriptor.frame_size <=
            static_cast<std::uint32_t>(
                std::numeric_limits<std::int32_t>::max()) &&
        descriptor.initial_padding_samples <=
            static_cast<std::uint32_t>(
                std::numeric_limits<std::int32_t>::max()) &&
        descriptor.profile == AacProfile::LowComplexity &&
        descriptor.channel_layout == AudioChannelLayout::Stereo &&
        descriptor.packet_format == AacPacketFormat::RawAccessUnit &&
        !descriptor.codec_extradata.empty() &&
        descriptor.bitrate_bits_per_second ==
            AacTargetBitrateBitsPerSecond;
}

struct BatchMeasurements final {
    std::size_t packet_count = 0;
    std::size_t byte_count = 0;
    std::int64_t minimum_dts = 0;
    std::int64_t maximum_dts = 0;
};

bool MeasureBatch(
    MediaStreamKind producer,
    std::span<const EncodedMediaPacket> packets,
    BatchMeasurements &measurements) noexcept
{
    if (packets.empty() ||
        (producer != MediaStreamKind::Video &&
         producer != MediaStreamKind::Audio)) {
        return false;
    }
    measurements.packet_count = packets.size();
    bool has_dts = false;
    for (const auto &packet : packets) {
        if (packet.stream != producer || packet.payload.empty() ||
            packet.pts_microseconds == UnknownMediaTimestamp ||
            packet.dts_microseconds == UnknownMediaTimestamp ||
            packet.pts_microseconds < packet.dts_microseconds ||
            packet.duration_microseconds <= 0) {
            return false;
        }
        auto packet_bytes = packet.payload.size();
        for (const auto &side_data : packet.side_data) {
            if (side_data.payload.size() >
                std::numeric_limits<std::size_t>::max() - packet_bytes) {
                return false;
            }
            packet_bytes += side_data.payload.size();
        }
        if (packet_bytes >
            std::numeric_limits<std::size_t>::max() -
                measurements.byte_count) {
            return false;
        }
        measurements.byte_count += packet_bytes;
        if (!has_dts) {
            measurements.minimum_dts = packet.dts_microseconds;
            measurements.maximum_dts = packet.dts_microseconds;
            has_dts = true;
        } else {
            measurements.minimum_dts = std::min(
                measurements.minimum_dts,
                packet.dts_microseconds);
            measurements.maximum_dts = std::max(
                measurements.maximum_dts,
                packet.dts_microseconds);
        }
    }
    return true;
}

bool FitsQueueLimits(
    const PreHeaderQueueLimits &limits,
    const BatchMeasurements &batch,
    const std::size_t current_packet_count,
    const std::size_t current_byte_count,
    const bool has_current_dts,
    const std::int64_t current_minimum_dts,
    const std::int64_t current_maximum_dts) noexcept
{
    if (batch.packet_count > limits.maximum_packets_per_stream ||
        current_packet_count >
            limits.maximum_packets_per_stream - batch.packet_count ||
        batch.byte_count > limits.maximum_bytes_per_stream ||
        current_byte_count >
            limits.maximum_bytes_per_stream - batch.byte_count) {
        return false;
    }
    const auto minimum_dts = has_current_dts
        ? std::min(current_minimum_dts, batch.minimum_dts)
        : batch.minimum_dts;
    const auto maximum_dts = has_current_dts
        ? std::max(current_maximum_dts, batch.maximum_dts)
        : batch.maximum_dts;
    const auto span = static_cast<std::uint64_t>(maximum_dts) -
        static_cast<std::uint64_t>(minimum_dts);
    return span <= static_cast<std::uint64_t>(
        limits.maximum_dts_span_microseconds_per_stream);
}

}

PreHeaderCoordinator::PreHeaderCoordinator(
    MediaMuxSessionPort &mux_session,
    EncodedMediaPacketSubmissionPort &submission,
    AacStreamDescriptor audio_descriptor,
    FragmentedMp4FragmentPolicy fragment_policy,
    const void *expected_video_encoder_identity,
    PreHeaderQueueLimits queue_limits)
    : mux_session_(mux_session),
      submission_(submission),
      audio_descriptor_(std::move(audio_descriptor)),
      fragment_policy_(fragment_policy),
      expected_video_encoder_identity_(expected_video_encoder_identity),
      queue_limits_(queue_limits)
{
}

PreHeaderCoordinator::~PreHeaderCoordinator()
{
    Abort();
}

vrrec_status_t PreHeaderCoordinator::BeginPriming(
    std::int64_t capture_epoch) noexcept
{
    std::lock_guard lock(mutex_);
    if (state_ != PreHeaderState::Created) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (capture_epoch < 0 || expected_video_encoder_identity_ == nullptr ||
        !IsAudioDescriptorValid(audio_descriptor_) ||
        fragment_policy_ != DefaultFragmentedMp4FragmentPolicy ||
        queue_limits_.maximum_packets_per_stream == 0 ||
        queue_limits_.maximum_bytes_per_stream == 0 ||
        queue_limits_.maximum_dts_span_microseconds_per_stream < 0) {
        return FailLocked(VRREC_STATUS_INVALID_ARGUMENT);
    }
    if (abort_requested_.load()) {
        state_ = PreHeaderState::Aborted;
        FailQueuedSubmissionsLocked(Mp4MuxResult::MuxFailed);
        AbortDownstreamLocked();
        return VRREC_STATUS_INVALID_STATE;
    }

    capture_epoch_ = capture_epoch;
    state_ = PreHeaderState::Priming;
    return VRREC_STATUS_OK;
}

vrrec_status_t PreHeaderCoordinator::ProducerStarted(
    MediaStreamKind producer) noexcept
{
    std::unique_lock lock(mutex_);
    if (state_ != PreHeaderState::Priming) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (abort_requested_.load()) {
        state_ = PreHeaderState::Aborted;
        FailQueuedSubmissionsLocked(Mp4MuxResult::MuxFailed);
        AbortDownstreamLocked();
        return VRREC_STATUS_INVALID_STATE;
    }

    bool *started = nullptr;
    if (producer == MediaStreamKind::Video) {
        started = &video_started_;
    } else if (producer == MediaStreamKind::Audio) {
        started = &audio_started_;
    } else {
        return FailLocked(VRREC_STATUS_INVALID_ARGUMENT);
    }
    if (*started) {
        return FailLocked(VRREC_STATUS_INVALID_STATE);
    }
    *started = true;
    return TryStartHeaderLocked(lock);
}

vrrec_status_t PreHeaderCoordinator::PublishVideoDescriptor(
    const void *encoder_identity,
    const H264StreamDescriptor &descriptor) noexcept
{
    std::unique_lock lock(mutex_);
    if (state_ != PreHeaderState::Priming) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (abort_requested_.load()) {
        state_ = PreHeaderState::Aborted;
        FailQueuedSubmissionsLocked(Mp4MuxResult::MuxFailed);
        AbortDownstreamLocked();
        return VRREC_STATUS_INVALID_STATE;
    }
    if (encoder_identity == nullptr ||
        encoder_identity != expected_video_encoder_identity_) {
        return FailLocked(VRREC_STATUS_INVALID_ARGUMENT);
    }
    if (!IsVideoDescriptorValid(descriptor)) {
        return FailLocked(VRREC_STATUS_INVALID_ARGUMENT);
    }
    if (video_descriptor_.has_value()) {
        return FailLocked(VRREC_STATUS_INVALID_STATE);
    }

    try {
        video_descriptor_ = descriptor;
    } catch (const std::bad_alloc &) {
        return FailLocked(VRREC_STATUS_OUT_OF_MEMORY);
    } catch (...) {
        return FailLocked(VRREC_STATUS_INTERNAL_ERROR);
    }
    return TryStartHeaderLocked(lock);
}

Mp4MuxResult PreHeaderCoordinator::SubmitBatch(
    MediaStreamKind producer,
    std::span<const EncodedMediaPacket> packets) noexcept
{
    BatchMeasurements measurements;
    const auto batch_valid = MeasureBatch(producer, packets, measurements);

    std::unique_lock lock(mutex_);
    if (abort_requested_.load() || state_ == PreHeaderState::Aborted) {
        return Mp4MuxResult::MuxFailed;
    }
    if (!batch_valid) {
        if (state_ == PreHeaderState::Priming ||
            state_ == PreHeaderState::HeaderStarting ||
            state_ == PreHeaderState::DrainingPreHeader ||
            state_ == PreHeaderState::Running) {
            FailLocked(VRREC_STATUS_INVALID_ARGUMENT);
        }
        return Mp4MuxResult::InvalidPacket;
    }
    if (state_ == PreHeaderState::Running) {
        lock.unlock();
        const auto result = submission_.SubmitBatch(producer, packets);
        if (abort_requested_.load()) {
            return Mp4MuxResult::MuxFailed;
        }
        if (result != Mp4MuxResult::Written) {
            lock.lock();
            if (state_ == PreHeaderState::Running) {
                FailLocked(VRREC_STATUS_INTERNAL_ERROR);
            }
        }
        return result;
    }
    if (state_ != PreHeaderState::Priming &&
        state_ != PreHeaderState::HeaderStarting &&
        state_ != PreHeaderState::DrainingPreHeader) {
        return Mp4MuxResult::InvalidState;
    }

    auto &usage = producer == MediaStreamKind::Video
        ? video_queue_usage_
        : audio_queue_usage_;
    if (!FitsQueueLimits(
            queue_limits_,
            measurements,
            usage.packet_count,
            usage.byte_count,
            usage.has_dts,
            usage.minimum_dts,
            usage.maximum_dts)) {
        FailLocked(VRREC_STATUS_BUFFER_TOO_SMALL);
        return Mp4MuxResult::MuxFailed;
    }

    std::shared_ptr<SubmissionTicket> ticket;
    QueuedBatch batch;
    try {
        ticket = std::make_shared<SubmissionTicket>();
        ticket->producer = producer;
        ticket->packet_count = measurements.packet_count;
        ticket->byte_count = measurements.byte_count;
        ticket->minimum_dts = measurements.minimum_dts;
        ticket->maximum_dts = measurements.maximum_dts;
        batch = {
            producer,
            std::vector<EncodedMediaPacket>(packets.begin(), packets.end()),
            ticket,
            next_submission_sequence_,
        };
        queued_batches_.push_back(std::move(batch));
        outstanding_tickets_.push_back(ticket);
    } catch (...) {
        FailLocked(VRREC_STATUS_OUT_OF_MEMORY);
        return Mp4MuxResult::MuxFailed;
    }
    ++next_submission_sequence_;
    queued_packet_count_ += measurements.packet_count;
    usage.packet_count += measurements.packet_count;
    usage.byte_count += measurements.byte_count;
    usage.minimum_dts = usage.has_dts
        ? std::min(usage.minimum_dts, measurements.minimum_dts)
        : measurements.minimum_dts;
    usage.maximum_dts = usage.has_dts
        ? std::max(usage.maximum_dts, measurements.maximum_dts)
        : measurements.maximum_dts;
    usage.has_dts = true;

    ticket->changed.wait(lock, [&] {
        return ticket->completed;
    });
    return ticket->result;
}

vrrec_status_t PreHeaderCoordinator::EncoderFinished(
    MediaStreamKind stream) noexcept
{
    std::unique_lock lock(mutex_);
    if (stream != MediaStreamKind::Video &&
        stream != MediaStreamKind::Audio) {
        return FailLocked(VRREC_STATUS_INVALID_ARGUMENT);
    }
    if (state_ != PreHeaderState::Running) {
        if (state_ == PreHeaderState::Failed ||
            state_ == PreHeaderState::Aborted) {
            return VRREC_STATUS_INVALID_STATE;
        }
        return FailLocked(VRREC_STATUS_INVALID_STATE);
    }
    lock.unlock();
    const auto status = submission_.EncoderFinished(stream);
    if (status != VRREC_STATUS_OK) {
        lock.lock();
        FailLocked(status);
    }
    return status;
}

void PreHeaderCoordinator::EncoderFailed(MediaStreamKind stream) noexcept
{
    std::lock_guard lock(mutex_);
    if (state_ == PreHeaderState::Failed ||
        state_ == PreHeaderState::Aborted) {
        FailQueuedSubmissionsLocked(Mp4MuxResult::MuxFailed);
        return;
    }
    state_ = PreHeaderState::Failed;
    FailQueuedSubmissionsLocked(Mp4MuxResult::MuxFailed);
    submission_.EncoderFailed(stream);
    AbortDownstreamLocked();
}

void PreHeaderCoordinator::Abort() noexcept
{
    abort_requested_.store(true);
    std::lock_guard lock(mutex_);
    if (state_ == PreHeaderState::Failed ||
        state_ == PreHeaderState::Aborted) {
        FailQueuedSubmissionsLocked(Mp4MuxResult::MuxFailed);
        return;
    }
    state_ = PreHeaderState::Aborted;
    FailQueuedSubmissionsLocked(Mp4MuxResult::MuxFailed);
    AbortDownstreamLocked();
}

PreHeaderState PreHeaderCoordinator::State() const noexcept
{
    std::lock_guard lock(mutex_);
    return state_;
}

#if defined(VRRECORDER_NATIVE_TESTING)
std::size_t PreHeaderCoordinator::QueuedPacketCountForTesting()
    const noexcept
{
    std::lock_guard lock(mutex_);
    return queued_packet_count_;
}
#endif

vrrec_status_t PreHeaderCoordinator::TryStartHeaderLocked(
    std::unique_lock<std::mutex> &lock) noexcept
{
    if (!video_descriptor_.has_value() || !video_started_ ||
        !audio_started_) {
        return VRREC_STATUS_OK;
    }

    FragmentedMp4StreamConfiguration configuration;
    try {
        configuration = {
            *video_descriptor_,
            audio_descriptor_,
            fragment_policy_,
        };
    } catch (const std::bad_alloc &) {
        return FailLocked(VRREC_STATUS_OUT_OF_MEMORY);
    } catch (...) {
        return FailLocked(VRREC_STATUS_INTERNAL_ERROR);
    }

    state_ = PreHeaderState::HeaderStarting;
    lock.unlock();
    const auto status = mux_session_.Start(configuration);
    lock.lock();

    if (abort_requested_.load()) {
        state_ = PreHeaderState::Aborted;
        FailQueuedSubmissionsLocked(Mp4MuxResult::MuxFailed);
        AbortDownstreamLocked();
        return VRREC_STATUS_INVALID_STATE;
    }
    if (status != VRREC_STATUS_OK) {
        return FailLocked(status);
    }

    state_ = PreHeaderState::DrainingPreHeader;
    lock.unlock();
    return DrainQueuedPackets();
}

vrrec_status_t PreHeaderCoordinator::DrainQueuedPackets() noexcept
{
    bool pre_header_batch = true;
    for (;;) {
        std::vector<QueuedBatch> batches;
        {
            std::lock_guard lock(mutex_);
            if (abort_requested_.load() ||
                state_ == PreHeaderState::Aborted) {
                return VRREC_STATUS_INVALID_STATE;
            }
            if (state_ != PreHeaderState::DrainingPreHeader) {
                return VRREC_STATUS_INVALID_STATE;
            }
            if (queued_batches_.empty()) {
                state_ = PreHeaderState::Running;
                return VRREC_STATUS_OK;
            }
            batches.swap(queued_batches_);
        }

        if (pre_header_batch) {
            struct PacketReference final {
                std::size_t batch_index;
                std::size_t packet_index;
            };
            std::vector<PacketReference> order;
            try {
                for (std::size_t batch_index = 0;
                     batch_index < batches.size(); ++batch_index) {
                    for (std::size_t packet_index = 0;
                         packet_index < batches[batch_index].packets.size();
                         ++packet_index) {
                        order.push_back({batch_index, packet_index});
                    }
                }
            } catch (...) {
                std::lock_guard lock(mutex_);
                for (const auto &batch : batches) {
                    CompleteTicketLocked(
                        batch.ticket,
                        Mp4MuxResult::MuxFailed);
                }
                return FailLocked(VRREC_STATUS_OUT_OF_MEMORY);
            }
            std::sort(order.begin(), order.end(), [&](const auto &left,
                                                      const auto &right) {
                const auto &left_packet =
                    batches[left.batch_index].packets[left.packet_index];
                const auto &right_packet =
                    batches[right.batch_index].packets[right.packet_index];
                if (left_packet.dts_microseconds !=
                    right_packet.dts_microseconds) {
                    return left_packet.dts_microseconds <
                        right_packet.dts_microseconds;
                }
                if (left_packet.stream != right_packet.stream) {
                    return left_packet.stream == MediaStreamKind::Video;
                }
                const auto left_sequence =
                    batches[left.batch_index].sequence;
                const auto right_sequence =
                    batches[right.batch_index].sequence;
                return left_sequence < right_sequence ||
                    (left_sequence == right_sequence &&
                     left.packet_index < right.packet_index);
            });

            for (const auto reference : order) {
                if (abort_requested_.load()) {
                    std::lock_guard lock(mutex_);
                    for (const auto &batch : batches) {
                        CompleteTicketLocked(
                            batch.ticket,
                            Mp4MuxResult::MuxFailed);
                    }
                    return VRREC_STATUS_INVALID_STATE;
                }
                const auto &batch = batches[reference.batch_index];
                const auto &packet = batch.packets[reference.packet_index];
                const auto result = submission_.SubmitBatch(
                    batch.producer,
                    std::span<const EncodedMediaPacket>(&packet, 1));
                if (result != Mp4MuxResult::Written) {
                    std::lock_guard lock(mutex_);
                    for (const auto &pending : batches) {
                        CompleteTicketLocked(pending.ticket, result);
                    }
                    return FailLocked(VRREC_STATUS_INTERNAL_ERROR);
                }
            }
        } else {
            for (const auto &batch : batches) {
                if (abort_requested_.load()) {
                    std::lock_guard lock(mutex_);
                    CompleteTicketLocked(
                        batch.ticket,
                        Mp4MuxResult::MuxFailed);
                    return VRREC_STATUS_INVALID_STATE;
                }
                const auto result = submission_.SubmitBatch(
                    batch.producer,
                    batch.packets);
                if (result != Mp4MuxResult::Written) {
                    std::lock_guard lock(mutex_);
                    CompleteTicketLocked(batch.ticket, result);
                    return FailLocked(VRREC_STATUS_INTERNAL_ERROR);
                }
                std::lock_guard lock(mutex_);
                CompleteTicketLocked(batch.ticket, Mp4MuxResult::Written);
            }
        }

        if (pre_header_batch) {
            std::lock_guard lock(mutex_);
            for (const auto &batch : batches) {
                CompleteTicketLocked(batch.ticket, Mp4MuxResult::Written);
            }
            pre_header_batch = false;
        }
    }
}

vrrec_status_t PreHeaderCoordinator::FailLocked(
    vrrec_status_t status) noexcept
{
    if (abort_requested_.load()) {
        state_ = PreHeaderState::Aborted;
        FailQueuedSubmissionsLocked(Mp4MuxResult::MuxFailed);
        AbortDownstreamLocked();
        return VRREC_STATUS_INVALID_STATE;
    }
    state_ = PreHeaderState::Failed;
    FailQueuedSubmissionsLocked(Mp4MuxResult::MuxFailed);
    AbortDownstreamLocked();
    return status;
}

void PreHeaderCoordinator::FailQueuedSubmissionsLocked(
    Mp4MuxResult result) noexcept
{
    for (const auto &ticket : outstanding_tickets_) {
        ticket->result = result;
        ticket->completed = true;
        ticket->changed.notify_all();
    }
    outstanding_tickets_.clear();
    queued_batches_.clear();
    queued_packet_count_ = 0;
    video_queue_usage_ = {};
    audio_queue_usage_ = {};
}

void PreHeaderCoordinator::CompleteTicketLocked(
    const std::shared_ptr<SubmissionTicket> &ticket,
    Mp4MuxResult result) noexcept
{
    if (ticket->completed) {
        return;
    }
    ticket->result = result;
    ticket->completed = true;
    ticket->changed.notify_all();
    if (queued_packet_count_ >= ticket->packet_count) {
        queued_packet_count_ -= ticket->packet_count;
    } else {
        queued_packet_count_ = 0;
    }
    const auto found = std::find(
        outstanding_tickets_.begin(),
        outstanding_tickets_.end(),
        ticket);
    if (found != outstanding_tickets_.end()) {
        outstanding_tickets_.erase(found);
    }
    RecomputeQueueUsageLocked();
}

void PreHeaderCoordinator::RecomputeQueueUsageLocked() noexcept
{
    video_queue_usage_ = {};
    audio_queue_usage_ = {};
    for (const auto &pending : outstanding_tickets_) {
        if (pending->completed) {
            continue;
        }
        auto &usage = pending->producer == MediaStreamKind::Video
            ? video_queue_usage_
            : audio_queue_usage_;
        usage.packet_count += pending->packet_count;
        usage.byte_count += pending->byte_count;
        usage.minimum_dts = usage.has_dts
            ? std::min(usage.minimum_dts, pending->minimum_dts)
            : pending->minimum_dts;
        usage.maximum_dts = usage.has_dts
            ? std::max(usage.maximum_dts, pending->maximum_dts)
            : pending->maximum_dts;
        usage.has_dts = true;
    }
}

void PreHeaderCoordinator::AbortDownstreamLocked() noexcept
{
    if (downstream_aborted_) {
        return;
    }
    downstream_aborted_ = true;
    mux_session_.RequestAbort();
    mux_session_.Abort();
}

}
