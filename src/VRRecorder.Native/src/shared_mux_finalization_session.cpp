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
    return SubmitBatch(
        packet.stream,
        std::span<const EncodedMediaPacket>(&packet, 1));
}

Mp4MuxResult SharedMuxFinalizationSession::SubmitBatch(
    MediaStreamKind producer,
    std::span<const EncodedMediaPacket> packets) noexcept
{
    const std::lock_guard operation_lock(operation_mutex_);
    Mp4MuxResult validation_result = Mp4MuxResult::Written;
    {
        const std::lock_guard state_lock(state_mutex_);
        if (terminal_) {
            return Mp4MuxResult::InvalidState;
        }
        if (producer != MediaStreamKind::Video &&
            producer != MediaStreamKind::Audio) {
            terminal_ = true;
            abort_requested_ = true;
            validation_result = Mp4MuxResult::InvalidPacket;
        } else if (
            (producer == MediaStreamKind::Video && video_finished_) ||
            (producer == MediaStreamKind::Audio && audio_finished_)) {
            terminal_ = true;
            abort_requested_ = true;
            validation_result = Mp4MuxResult::InvalidState;
        } else {
            for (const auto &packet : packets) {
                if (packet.stream != producer) {
                    terminal_ = true;
                    abort_requested_ = true;
                    validation_result = Mp4MuxResult::InvalidPacket;
                    break;
                }
            }
        }
    }
    if (validation_result != Mp4MuxResult::Written) {
        mux_.Abort();
        return validation_result;
    }

    const auto result = mux_.SubmitBatch(packets);
    if (result != Mp4MuxResult::Written) {
        {
            const std::lock_guard state_lock(state_mutex_);
            terminal_ = true;
            abort_requested_ = true;
        }
        mux_.Abort();
        return result;
    }
    {
        const std::lock_guard state_lock(state_mutex_);
        if (terminal_) {
            return Mp4MuxResult::MuxFailed;
        }
    }
    return Mp4MuxResult::Written;
}

vrrec_status_t SharedMuxFinalizationSession::EncoderFinished(
    MediaStreamKind stream) noexcept
{
    const std::lock_guard operation_lock(operation_mutex_);
    bool abort = false;
    bool finalize = false;
    {
        const std::lock_guard state_lock(state_mutex_);
        if (terminal_) {
            return VRREC_STATUS_INVALID_STATE;
        }
        if (stream != MediaStreamKind::Video &&
            stream != MediaStreamKind::Audio) {
            terminal_ = true;
            abort_requested_ = true;
            abort = true;
        } else {
            auto &finished = stream == MediaStreamKind::Video
                ? video_finished_
                : audio_finished_;
            if (finished) {
                terminal_ = true;
                abort_requested_ = true;
                abort = true;
            } else {
                finished = true;
                finalize = video_finished_ && audio_finished_;
                if (finalize) {
                    terminal_ = true;
                }
            }
        }
    }
    if (abort) {
        mux_.Abort();
        return VRREC_STATUS_INVALID_STATE;
    }
    if (!finalize) {
        return VRREC_STATUS_OK;
    }
    auto status = mux_.Finish();
    bool abort_after_failure = false;
    {
        const std::lock_guard state_lock(state_mutex_);
        if (abort_requested_) {
            if (status == VRREC_STATUS_OK) {
                status = VRREC_STATUS_INVALID_STATE;
            }
        } else if (status == VRREC_STATUS_OK) {
            completed_ = true;
        } else {
            abort_requested_ = true;
            abort_after_failure = true;
        }
    }
    if (abort_after_failure) {
        mux_.Abort();
    }
    return status;
}

void SharedMuxFinalizationSession::EncoderFailed(
    MediaStreamKind) noexcept
{
    Abort();
}

void SharedMuxFinalizationSession::Abort() noexcept
{
    {
        const std::lock_guard state_lock(state_mutex_);
        if (completed_ || abort_requested_) {
            return;
        }
        abort_requested_ = true;
        terminal_ = true;
    }
    mux_.Abort();
}

}
