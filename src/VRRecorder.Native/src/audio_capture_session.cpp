#include "audio_capture_session.hpp"

#include <new>

namespace vrrecorder::native {

StereoAudioCaptureSession::StereoAudioCaptureSession(
    AudioCaptureSourceProvider &desktop_provider,
    AudioCaptureRecoveryWaiter &desktop_waiter,
    AudioCaptureSourceProvider &microphone_provider,
    AudioCaptureRecoveryWaiter &microphone_waiter,
    std::size_t timeline_capacity_frames,
    vrrec_audio_routing_t initial_routing,
    double desktop_gain_db,
    double microphone_gain_db,
    AudioCaptureAvailabilitySink *availability_sink)
    : desktop_timeline_(timeline_capacity_frames),
      microphone_timeline_(timeline_capacity_frames),
      mixer_(initial_routing, desktop_gain_db, microphone_gain_db),
      desktop_worker_(
          desktop_provider,
          desktop_waiter,
          desktop_timeline_,
          availability_sink),
      microphone_worker_(
          microphone_provider,
          microphone_waiter,
          microphone_timeline_,
          availability_sink),
      mix_coordinator_(
          desktop_timeline_,
          microphone_timeline_,
          mixer_,
          availability_sink),
      initial_routing_(initial_routing)
{
}

StereoAudioCaptureSession::~StereoAudioCaptureSession()
{
    Abort();
}

vrrec_status_t StereoAudioCaptureSession::Start(
    const StereoAudioCaptureSessionConfig &config) noexcept
{
    if (start_attempted_.exchange(true) || aborted_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }

    if (config.session_start_qpc_100ns < 0 ||
        mixer_.SetRouting(initial_routing_) != VRREC_STATUS_OK) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    AudioCaptureSourceConfig desktop_config {
        AudioCaptureRole::DesktopLoopback,
        {},
        config.session_start_qpc_100ns,
    };
    AudioCaptureSourceConfig microphone_config {
        AudioCaptureRole::Microphone,
        {},
        config.session_start_qpc_100ns,
    };
    try {
        desktop_config.endpoint_id_utf8 = config.desktop_endpoint_id_utf8;
        microphone_config.endpoint_id_utf8 =
            config.microphone_endpoint_id_utf8;
    } catch (const std::bad_alloc &) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }

    const auto desktop_status = desktop_worker_.Start(desktop_config);
    if (desktop_status != VRREC_STATUS_OK) {
        return desktop_status;
    }

    const auto microphone_status = microphone_worker_.Start(
        microphone_config);
    if (microphone_status != VRREC_STATUS_OK) {
        desktop_worker_.Abort();
        return microphone_status;
    }

    active_.store(true);
    return VRREC_STATUS_OK;
}

StereoAudioMixResult StereoAudioCaptureSession::MixNext(
    std::size_t frame_count_48k,
    std::span<float> output_interleaved,
    StereoAudioMixRead &read) noexcept
{
    read = {};

    if (!active_.load() || aborted_.load()) {
        return StereoAudioMixResult::InvalidState;
    }

    return mix_coordinator_.MixNext(
        frame_count_48k,
        output_interleaved,
        read);
}

vrrec_status_t StereoAudioCaptureSession::SetRouting(
    vrrec_audio_routing_t routing) noexcept
{
    if (!active_.load() || aborted_.load()) {
        return VRREC_STATUS_INVALID_STATE;
    }

    return mixer_.SetRouting(routing);
}

void StereoAudioCaptureSession::Abort() noexcept
{
    if (aborted_.exchange(true)) {
        return;
    }

    active_.store(false);
    mix_coordinator_.Abort();
    desktop_worker_.Abort();
    microphone_worker_.Abort();
}

}
