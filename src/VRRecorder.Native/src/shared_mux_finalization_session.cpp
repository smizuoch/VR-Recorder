#include "shared_mux_finalization_session.hpp"

namespace vrrecorder::native {

SharedMuxFinalizationSession::SharedMuxFinalizationSession(
    FragmentedMp4MuxCoordinator &mux) noexcept
    : mux_(mux)
{
}

SharedMuxFinalizationSession::~SharedMuxFinalizationSession()
{
    Abort();
}

Mp4MuxResult SharedMuxFinalizationSession::Submit(
    const EncodedMediaPacket &packet) noexcept
{
    const std::lock_guard lock(mutex_);
    if (terminal_ ||
        (packet.stream == MediaStreamKind::Video && video_finished_) ||
        (packet.stream == MediaStreamKind::Audio && audio_finished_)) {
        return Mp4MuxResult::InvalidState;
    }
    const auto result = mux_.Submit(packet);
    if (result == Mp4MuxResult::MuxFailed ||
        result == Mp4MuxResult::InvalidState) {
        terminal_ = true;
    }
    return result;
}

vrrec_status_t SharedMuxFinalizationSession::EncoderFinished(
    MediaStreamKind stream) noexcept
{
    const std::lock_guard lock(mutex_);
    if (terminal_ ||
        (stream != MediaStreamKind::Video &&
         stream != MediaStreamKind::Audio)) {
        return VRREC_STATUS_INVALID_STATE;
    }

    auto &finished = stream == MediaStreamKind::Video
        ? video_finished_
        : audio_finished_;
    if (finished) {
        return VRREC_STATUS_INVALID_STATE;
    }
    finished = true;
    if (!video_finished_ || !audio_finished_) {
        return VRREC_STATUS_OK;
    }

    terminal_ = true;
    return mux_.Finish();
}

void SharedMuxFinalizationSession::EncoderFailed(
    MediaStreamKind) noexcept
{
    Abort();
}

void SharedMuxFinalizationSession::Abort() noexcept
{
    const std::lock_guard lock(mutex_);
    if (terminal_) {
        return;
    }
    terminal_ = true;
    mux_.Abort();
}

}
