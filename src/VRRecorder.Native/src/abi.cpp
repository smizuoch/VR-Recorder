#include "vrrecorder_native.h"

#include <cstdint>
#include <memory>
#include <mutex>
#include <new>
#include <string>

#include "media_backend.hpp"

namespace {

enum class SessionState {
    Created,
    Started,
    Terminal,
    Aborted,
};

}

struct vrrec_session final : vrrecorder::native::MediaEventSink {
    vrrec_session(
        const vrrec_session_config_v1 &config,
        const vrrec_callbacks_v1 &callbacks)
        : temporary_output_path_(config.temporary_output_path_utf8),
          config_(config),
          callbacks_(callbacks)
    {
        config_.temporary_output_path_utf8 = temporary_output_path_.c_str();
    }

    vrrec_status_t InitializeBackend()
    {
        auto status = VRREC_STATUS_INTERNAL_ERROR;
        backend_ = vrrecorder::native::CreateMediaBackend(
            config_,
            *this,
            status);
        if (backend_ == nullptr) {
            return status == VRREC_STATUS_OK
                ? VRREC_STATUS_INTERNAL_ERROR
                : status;
        }

        return status;
    }

    vrrec_status_t Start() noexcept
    {
        {
            const std::lock_guard lock(state_mutex_);
            if (state_ != SessionState::Created) {
                return VRREC_STATUS_INVALID_STATE;
            }

            state_ = SessionState::Started;
        }

        const auto status = backend_->Start();
        if (status != VRREC_STATUS_OK) {
            const std::lock_guard lock(state_mutex_);
            if (state_ == SessionState::Started) {
                state_ = SessionState::Created;
            }
        }

        return status;
    }

    vrrec_status_t RequestStop() noexcept
    {
        {
            const std::lock_guard lock(state_mutex_);
            if (state_ == SessionState::Terminal) {
                return VRREC_STATUS_OK;
            }

            if (state_ != SessionState::Started) {
                return state_ == SessionState::Aborted
                    ? VRREC_STATUS_OK
                    : VRREC_STATUS_INVALID_STATE;
            }

            if (stop_requested_) {
                return VRREC_STATUS_OK;
            }

            stop_requested_ = true;
        }

        const auto status = backend_->RequestStop();
        if (status != VRREC_STATUS_OK) {
            const std::lock_guard lock(state_mutex_);
            if (state_ == SessionState::Started) {
                stop_requested_ = false;
            }
        }

        return status;
    }

    vrrec_status_t Abort() noexcept
    {
        {
            const std::lock_guard lock(state_mutex_);
            if (state_ == SessionState::Aborted) {
                return VRREC_STATUS_OK;
            }

            state_ = SessionState::Aborted;
        }

        backend_->Abort();
        const std::lock_guard callbacks_quiesced(callback_mutex_);
        return VRREC_STATUS_OK;
    }

    void FirstVideoPacketMuxed() noexcept override
    {
        const std::lock_guard callback_lock(callback_mutex_);
        std::uint64_t sequence;
        {
            const std::lock_guard state_lock(state_mutex_);
            if (state_ != SessionState::Started || first_packet_emitted_) {
                return;
            }

            first_packet_emitted_ = true;
            sequence = ++sequence_;
        }

        Emit(vrrec_event_v1 {
            sizeof(vrrec_event_v1),
            VRREC_ABI_V1,
            VRREC_EVENT_FIRST_VIDEO_PACKET_MUXED,
            VRREC_STATUS_OK,
            sequence,
            0,
            0,
            nullptr,
        });
    }

    void Stopped(
        std::uint64_t video_packet_count,
        std::uint64_t audio_packet_count) noexcept override
    {
        const std::lock_guard callback_lock(callback_mutex_);
        std::uint64_t sequence;
        {
            const std::lock_guard state_lock(state_mutex_);
            if (state_ != SessionState::Started || !stop_requested_) {
                return;
            }

            state_ = SessionState::Terminal;
            sequence = ++sequence_;
        }

        Emit(vrrec_event_v1 {
            sizeof(vrrec_event_v1),
            VRREC_ABI_V1,
            VRREC_EVENT_STOPPED,
            VRREC_STATUS_OK,
            sequence,
            video_packet_count,
            audio_packet_count,
            nullptr,
        });
    }

    void Faulted(
        vrrec_status_t status,
        const char *message_utf8) noexcept override
    {
        const std::lock_guard callback_lock(callback_mutex_);
        std::uint64_t sequence;
        {
            const std::lock_guard state_lock(state_mutex_);
            if (state_ == SessionState::Terminal ||
                state_ == SessionState::Aborted) {
                return;
            }

            state_ = SessionState::Terminal;
            sequence = ++sequence_;
        }

        Emit(vrrec_event_v1 {
            sizeof(vrrec_event_v1),
            VRREC_ABI_V1,
            VRREC_EVENT_FAULTED,
            status,
            sequence,
            0,
            0,
            message_utf8,
        });
    }

private:
    void Emit(const vrrec_event_v1 &event) noexcept
    {
        try {
            callbacks_.on_event(callbacks_.user_data, &event);
        } catch (...) {
            // No exception may cross the C ABI callback boundary.
        }
    }

    std::string temporary_output_path_;
    vrrec_session_config_v1 config_;
    vrrec_callbacks_v1 callbacks_;
    std::unique_ptr<vrrecorder::native::MediaBackend> backend_;
    std::mutex state_mutex_;
    std::mutex callback_mutex_;
    SessionState state_ = SessionState::Created;
    bool stop_requested_ = false;
    bool first_packet_emitted_ = false;
    std::uint64_t sequence_ = 0;
};

namespace {

vrrec_status_t ValidateCreateArguments(
    const vrrec_session_config_v1 *config,
    const vrrec_callbacks_v1 *callbacks,
    vrrec_session_t **out_session) noexcept
{
    if (out_session == nullptr) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    *out_session = nullptr;
    if (config == nullptr || callbacks == nullptr) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    if (config->struct_size < sizeof(vrrec_session_config_v1) ||
        callbacks->struct_size < sizeof(vrrec_callbacks_v1)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    if (config->abi_version != VRREC_ABI_V1 ||
        callbacks->abi_version != VRREC_ABI_V1) {
        return VRREC_STATUS_UNSUPPORTED_ABI;
    }

    if (config->temporary_output_path_utf8 == nullptr ||
        config->temporary_output_path_utf8[0] == '\0' ||
        config->width == 0 ||
        config->height == 0 ||
        config->fps_numerator == 0 ||
        config->fps_denominator == 0 ||
        callbacks->on_event == nullptr) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    return VRREC_STATUS_OK;
}

}

extern "C" VRREC_API std::uint32_t VRREC_CALL vrrec_abi_version(void)
{
    return VRREC_ABI_V1;
}

extern "C" VRREC_API vrrec_status_t VRREC_CALL vrrec_session_create_v1(
    const vrrec_session_config_v1 *config,
    const vrrec_callbacks_v1 *callbacks,
    vrrec_session_t **out_session)
{
    const auto validation = ValidateCreateArguments(
        config,
        callbacks,
        out_session);
    if (validation != VRREC_STATUS_OK) {
        return validation;
    }

    try {
        auto session = std::make_unique<vrrec_session>(*config, *callbacks);
        const auto status = session->InitializeBackend();
        if (status != VRREC_STATUS_OK) {
            return status;
        }

        *out_session = session.release();
        return VRREC_STATUS_OK;
    } catch (const std::bad_alloc &) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

extern "C" VRREC_API vrrec_status_t VRREC_CALL vrrec_session_start_v1(
    vrrec_session_t *session)
{
    if (session == nullptr) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    try {
        return session->Start();
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

extern "C" VRREC_API vrrec_status_t VRREC_CALL
vrrec_session_request_stop_v1(vrrec_session_t *session)
{
    if (session == nullptr) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    try {
        return session->RequestStop();
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

extern "C" VRREC_API vrrec_status_t VRREC_CALL vrrec_session_abort_v1(
    vrrec_session_t *session)
{
    if (session == nullptr) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    try {
        return session->Abort();
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

extern "C" VRREC_API void VRREC_CALL vrrec_session_destroy_v1(
    vrrec_session_t *session)
{
    if (session == nullptr) {
        return;
    }

    try {
        (void)session->Abort();
        delete session;
    } catch (...) {
        // Destruction is the final fail-safe and cannot report across C ABI.
    }
}
