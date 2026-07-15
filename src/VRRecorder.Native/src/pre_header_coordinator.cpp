#include "pre_header_coordinator.hpp"

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

}

PreHeaderCoordinator::PreHeaderCoordinator(
    MediaMuxSessionPort &mux_session,
    EncodedMediaPacketSubmissionPort &submission,
    AacStreamDescriptor audio_descriptor,
    FragmentedMp4FragmentPolicy fragment_policy,
    const void *expected_video_encoder_identity)
    : mux_session_(mux_session),
      submission_(submission),
      audio_descriptor_(std::move(audio_descriptor)),
      fragment_policy_(fragment_policy),
      expected_video_encoder_identity_(expected_video_encoder_identity)
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
        fragment_policy_ != DefaultFragmentedMp4FragmentPolicy) {
        return FailLocked(VRREC_STATUS_INVALID_ARGUMENT);
    }
    if (abort_requested_.load()) {
        state_ = PreHeaderState::Aborted;
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

void PreHeaderCoordinator::Abort() noexcept
{
    abort_requested_.store(true);
    std::lock_guard lock(mutex_);
    if (state_ == PreHeaderState::Failed ||
        state_ == PreHeaderState::Aborted) {
        return;
    }
    state_ = PreHeaderState::Aborted;
    AbortDownstreamLocked();
}

PreHeaderState PreHeaderCoordinator::State() const noexcept
{
    std::lock_guard lock(mutex_);
    return state_;
}

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
        AbortDownstreamLocked();
        return VRREC_STATUS_INVALID_STATE;
    }
    if (status != VRREC_STATUS_OK) {
        return FailLocked(status);
    }

    state_ = PreHeaderState::DrainingPreHeader;
    return VRREC_STATUS_OK;
}

vrrec_status_t PreHeaderCoordinator::FailLocked(
    vrrec_status_t status) noexcept
{
    if (abort_requested_.load()) {
        state_ = PreHeaderState::Aborted;
        AbortDownstreamLocked();
        return VRREC_STATUS_INVALID_STATE;
    }
    state_ = PreHeaderState::Failed;
    AbortDownstreamLocked();
    return status;
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
