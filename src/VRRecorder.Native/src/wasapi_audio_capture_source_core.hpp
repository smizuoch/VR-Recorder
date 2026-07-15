#ifndef VRRECORDER_NATIVE_WASAPI_AUDIO_CAPTURE_SOURCE_CORE_HPP
#define VRRECORDER_NATIVE_WASAPI_AUDIO_CAPTURE_SOURCE_CORE_HPP

#include <atomic>
#include <cstdint>
#include <memory>
#include <thread>

#include "audio_capture_source.hpp"
#include "wasapi_audio_capture_port.hpp"

namespace vrrecorder::native {

class WasapiAudioCaptureSourceCore final : public AudioCaptureSource {
public:
    explicit WasapiAudioCaptureSourceCore(
        std::unique_ptr<WasapiCapturePort> port) noexcept;
    ~WasapiAudioCaptureSourceCore() override;

    vrrec_status_t Start(
        const AudioCaptureSourceConfig &config) noexcept override;
    AudioCaptureRead Read() noexcept override;
    void Abort() noexcept override;

private:
    static vrrec_status_t MapStartResult(
        WasapiCapturePortResult result) noexcept;
    static AudioCaptureRead FailedRead() noexcept;
    static AudioCaptureRead AbortedRead() noexcept;
    AudioCaptureRead DeviceLost() const noexcept;
    AudioCaptureRead MapReleaseResult(
        WasapiCapturePortResult result) const noexcept;
    void CloseOnce() noexcept;

    std::unique_ptr<WasapiCapturePort> port_;
    std::unique_ptr<StereoCaptureNormalizer48k> normalizer_;
    CapturePcmFormat format_ {};
    std::thread::id capture_thread_;
    std::atomic_bool abort_requested_ = false;
    std::uint64_t next_frame_48k_ = 0;
    bool started_ = false;
    bool closed_ = false;
};

}

#endif
