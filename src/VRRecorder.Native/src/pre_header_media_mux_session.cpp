#include "pre_header_media_mux_session.hpp"

#include <utility>

namespace vrrecorder::native {

PreHeaderMediaMuxSession::PreHeaderMediaMuxSession(
    PreHeaderCoordinator &coordinator,
    std::int64_t capture_epoch,
    const void *video_encoder_identity,
    FragmentedMp4StreamConfiguration configuration,
    bool publish_initial_video_descriptor)
    : coordinator_(coordinator),
      capture_epoch_(capture_epoch),
      video_encoder_identity_(video_encoder_identity),
      configuration_(std::move(configuration)),
      publish_initial_video_descriptor_(publish_initial_video_descriptor)
{
}

vrrec_status_t PreHeaderMediaMuxSession::Start(
    const FragmentedMp4StreamConfiguration &configuration) noexcept
{
    if (start_attempted_.exchange(true)) {
        return VRREC_STATUS_INVALID_STATE;
    }
    if (configuration != configuration_) {
        coordinator_.Abort();
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    auto status = coordinator_.BeginPriming(capture_epoch_);
    if (status == VRREC_STATUS_OK && publish_initial_video_descriptor_) {
        status = coordinator_.PublishVideoDescriptor(
            video_encoder_identity_,
            configuration_.video);
    }
    if (status == VRREC_STATUS_OK) {
        status = coordinator_.ProducerStarted(MediaStreamKind::Video);
    }
    if (status == VRREC_STATUS_OK) {
        status = coordinator_.ProducerStarted(MediaStreamKind::Audio);
    }
    if (status != VRREC_STATUS_OK) {
        coordinator_.Abort();
    }
    return status;
}

void PreHeaderMediaMuxSession::RequestAbort() noexcept
{
    coordinator_.RequestAbort();
}

void PreHeaderMediaMuxSession::Abort() noexcept
{
    coordinator_.Abort();
}

std::int64_t PreHeaderMediaMuxSession::AudioVideoOffsetMicroseconds()
    const noexcept
{
    return coordinator_.AudioVideoOffsetMicroseconds();
}

}
