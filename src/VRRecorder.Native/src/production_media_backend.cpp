#include "media_backend.hpp"

#if !defined(_WIN32)
#error "The production media backend requires Windows"
#endif

#include <Windows.h>

#include <chrono>
#include <cstdint>
#include <limits>
#include <memory>
#include <new>
#include <utility>

#include "audio_capture_platform_support.hpp"
#include "audio_capture_session.hpp"
#include "audio_encoder_config.hpp"
#include "audio_media_event_adapter.hpp"
#include "d3d11_nv12_frame_mapper.hpp"
#include "d3d11_video_frame_processor.hpp"
#include "ffmpeg_aac_packet_encoder.hpp"
#include "ffmpeg_fragmented_mp4_muxer.hpp"
#include "ffmpeg_h264_packet_encoder.hpp"
#include "ffmpeg_h264_system_memory_packet_encoder_adapter.hpp"
#include "ffmpeg_libavformat_fragmented_mp4_muxer_port.hpp"
#include "media_mux_pipeline.hpp"
#include "media_recording_pipeline.hpp"
#include "muxing_audio_encoder_sink.hpp"
#include "muxing_video_encoder_sink.hpp"
#include "pipeline_media_backend.hpp"
#include "pre_header_coordinator.hpp"
#include "pre_header_media_mux_session.hpp"
#include "production_media_configuration.hpp"
#include "spout_capture_pump.hpp"
#include "spout_capture_worker.hpp"
#include "spout_source_backend.hpp"
#include "steady_video_cfr_clock.hpp"
#include "timestamping_audio_pipeline_session.hpp"
#include "video_encoder_config.hpp"
#include "video_encoding_worker.hpp"
#include "video_pipeline_session.hpp"
#include "video_processing_encoder_sink.hpp"
#include "video_processing_layout_controller.hpp"
#include "windows_d3d11_nv12_readback_port.hpp"
#include "windows_d3d11_video_processor_port.hpp"

namespace vrrecorder::native {
namespace {

constexpr std::size_t AudioTimelineCapacityFrames = 48'000U * 2U;
constexpr auto SpoutPollTimeout = std::chrono::milliseconds {10};
constexpr std::uint64_t QpcUnitsPerSecond100ns = 10'000'000;

class WindowsAudioSessionStartClock final : public AudioSessionStartClock {
public:
    vrrec_status_t NowQpc100ns(
        std::int64_t &value) noexcept override
    {
        value = 0;
        LARGE_INTEGER counter {};
        LARGE_INTEGER frequency {};
        if (!QueryPerformanceCounter(&counter) ||
            !QueryPerformanceFrequency(&frequency) ||
            counter.QuadPart < 0 || frequency.QuadPart <= 0) {
            return VRREC_STATUS_BACKEND_UNAVAILABLE;
        }

        const auto ticks = static_cast<std::uint64_t>(counter.QuadPart);
        const auto ticks_per_second =
            static_cast<std::uint64_t>(frequency.QuadPart);
        const auto whole_seconds = ticks / ticks_per_second;
        const auto remainder = ticks % ticks_per_second;
        constexpr auto maximum = static_cast<std::uint64_t>(
            std::numeric_limits<std::int64_t>::max());
        if (whole_seconds > maximum / QpcUnitsPerSecond100ns ||
            remainder >
                std::numeric_limits<std::uint64_t>::max() /
                    QpcUnitsPerSecond100ns) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        const auto whole_100ns = whole_seconds * QpcUnitsPerSecond100ns;
        const auto remainder_100ns =
            remainder * QpcUnitsPerSecond100ns / ticks_per_second;
        if (remainder_100ns > maximum - whole_100ns) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }
        value = static_cast<std::int64_t>(whole_100ns + remainder_100ns);
        return VRREC_STATUS_OK;
    }
};

class ProductionMediaBackend final : public MediaBackend {
public:
    ProductionMediaBackend() = default;
    ~ProductionMediaBackend() override = default;

    ProductionMediaBackend(const ProductionMediaBackend &) = delete;
    ProductionMediaBackend &operator=(
        const ProductionMediaBackend &) = delete;

    vrrec_status_t Start() noexcept override
    {
        return backend_->Start();
    }

    vrrec_status_t UpdateVideoLayout(
        const vrrec_video_layout_v1 &layout) noexcept override
    {
        return backend_->UpdateVideoLayout(layout);
    }

    vrrec_status_t UpdateAudioRouting(
        vrrec_audio_routing_t routing) noexcept override
    {
        return backend_->UpdateAudioRouting(routing);
    }

    vrrec_status_t GetStatistics(
        vrrec_session_statistics_v1 &statistics) noexcept override
    {
        return backend_->GetStatistics(statistics);
    }

    vrrec_status_t RequestStop() noexcept override
    {
        return backend_->RequestStop();
    }

    void RequestAbort() noexcept override
    {
        backend_->RequestAbort();
    }

    void JoinAfterAbort() noexcept override
    {
        backend_->JoinAfterAbort();
    }

    std::unique_ptr<SpoutSourceBackend> spout_backend_;
    std::unique_ptr<VideoCfrScheduler> scheduler_;
    std::unique_ptr<SpoutCapturePump> spout_pump_;
    std::unique_ptr<SpoutCaptureWorker> spout_worker_;
    std::unique_ptr<SteadyVideoCfrClock> video_clock_;
    std::unique_ptr<D3d11VideoProcessorPort> d3d11_processor_port_;
    std::unique_ptr<D3d11VideoFrameProcessor> frame_processor_;
    std::unique_ptr<D3d11Nv12ReadbackPort> readback_port_;
    std::unique_ptr<D3d11SystemMemoryNv12FrameMapper> frame_mapper_;
    std::unique_ptr<FfmpegH264PacketEncoder> h264_encoder_;
    std::unique_ptr<FfmpegH264SystemMemoryPacketEncoderAdapter>
        h264_adapter_;
    std::unique_ptr<MediaAudioCaptureAvailabilitySink>
        audio_availability_;
    std::unique_ptr<PlatformAudioCaptureSourceProvider>
        desktop_provider_;
    std::unique_ptr<ConditionVariableAudioCaptureRecoveryWaiter>
        desktop_waiter_;
    std::unique_ptr<PlatformAudioCaptureSourceProvider>
        microphone_provider_;
    std::unique_ptr<ConditionVariableAudioCaptureRecoveryWaiter>
        microphone_waiter_;
    std::unique_ptr<StereoAudioCaptureSession> audio_capture_;
    std::unique_ptr<FfmpegAacPacketEncoder> aac_encoder_;
    std::unique_ptr<LibavformatFragmentedMp4MuxerPort> mux_port_;
    std::unique_ptr<FfmpegFragmentedMp4Muxer> muxer_;
    std::unique_ptr<MediaMuxPipeline> mux_pipeline_;
    std::unique_ptr<PreHeaderCoordinator> pre_header_coordinator_;
    std::unique_ptr<MuxingVideoEncoderSink> muxing_video_sink_;
    std::unique_ptr<ProcessingVideoEncoderSink> processing_video_sink_;
    std::unique_ptr<ProcessingVideoLayoutController> layout_controller_;
    std::unique_ptr<VideoEncodingWorker> video_encoding_worker_;
    std::unique_ptr<VideoPipelineSession> video_pipeline_;
    std::unique_ptr<MuxingAudioEncoderSink> muxing_audio_sink_;
    std::unique_ptr<StereoAudioPipelineSession> audio_pipeline_;
    std::unique_ptr<WindowsAudioSessionStartClock> audio_start_clock_;
    std::unique_ptr<TimestampingStereoAudioPipelineSession>
        timestamping_audio_pipeline_;
    std::unique_ptr<PreHeaderMediaMuxSession> pre_header_mux_session_;
    std::unique_ptr<MediaRecordingPipeline> recording_pipeline_;
    std::unique_ptr<PipelineMediaBackend> backend_;
};

vrrec_status_t CreateH264Encoder(
    std::uint32_t width,
    std::uint32_t height,
    std::uint32_t frames_per_second,
    std::unique_ptr<FfmpegH264PacketEncoder> &encoder) noexcept
{
    encoder.reset();
    H264VideoEncoderConfig config {};
    auto status = CreateH264VideoEncoderConfig(
        width,
        height,
        frames_per_second,
        true,
        config);
    if (status != VRREC_STATUS_OK) {
        return status;
    }

    auto created = FfmpegH264PacketEncoder::Create(config);
    if (created.status == VRREC_STATUS_BACKEND_UNAVAILABLE) {
        status = CreateH264VideoEncoderConfig(
            width,
            height,
            frames_per_second,
            false,
            config);
        if (status != VRREC_STATUS_OK) {
            return status;
        }
        created = FfmpegH264PacketEncoder::Create(config);
    }
    if (created.status != VRREC_STATUS_OK) {
        return created.status;
    }
    if (created.encoder == nullptr || created.encoder->Descriptor() == nullptr) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
    encoder = std::move(created.encoder);
    return VRREC_STATUS_OK;
}

}

std::unique_ptr<MediaBackend> CreateMediaBackend(
    const vrrec_session_config_v1 &config,
    MediaEventSink &events,
    vrrec_status_t &status)
{
    status = VRREC_STATUS_INTERNAL_ERROR;
    ProductionMediaConfiguration production_config {};
    status = ValidateProductionMediaConfiguration(
        config,
        production_config);
    if (status != VRREC_STATUS_OK) {
        return {};
    }

    try {
        auto graph = std::make_unique<ProductionMediaBackend>();

        const vrrec_spout_source_config_v1 spout_config {
            sizeof(vrrec_spout_source_config_v1),
            VRREC_ABI_V1,
            0,
            0,
        };
        graph->spout_backend_ = CreateSpoutSourceBackend(
            spout_config,
            status);
        if (status != VRREC_STATUS_OK || graph->spout_backend_ == nullptr) {
            status = status == VRREC_STATUS_OK
                ? VRREC_STATUS_INTERNAL_ERROR
                : status;
            return {};
        }
        graph->scheduler_ = std::make_unique<VideoCfrScheduler>();
        graph->spout_pump_ = std::make_unique<SpoutCapturePump>(
            *graph->spout_backend_,
            *graph->scheduler_,
            config.spout_sender_identity_utf8);
        graph->spout_worker_ = std::make_unique<SpoutCaptureWorker>(
            *graph->spout_pump_);
        graph->video_clock_ = std::make_unique<SteadyVideoCfrClock>(
            production_config.frames_per_second);

        graph->d3d11_processor_port_ =
            CreateWindowsAdaptiveD3d11VideoProcessorPort(
                config.spout_adapter_luid,
                status);
        if (status != VRREC_STATUS_OK ||
            graph->d3d11_processor_port_ == nullptr) {
            status = status == VRREC_STATUS_OK
                ? VRREC_STATUS_INTERNAL_ERROR
                : status;
            return {};
        }
        graph->frame_processor_ =
            std::make_unique<D3d11VideoFrameProcessor>(
                *graph->d3d11_processor_port_);
        graph->readback_port_ = CreateWindowsD3d11Nv12ReadbackPort(
            config.spout_adapter_luid,
            status);
        if (status != VRREC_STATUS_OK || graph->readback_port_ == nullptr) {
            status = status == VRREC_STATUS_OK
                ? VRREC_STATUS_INTERNAL_ERROR
                : status;
            return {};
        }
        graph->frame_mapper_ =
            std::make_unique<D3d11SystemMemoryNv12FrameMapper>(
                *graph->readback_port_);

        status = CreateH264Encoder(
            config.width,
            config.height,
            production_config.frames_per_second,
            graph->h264_encoder_);
        if (status != VRREC_STATUS_OK) {
            return {};
        }
        const auto *video_descriptor_pointer =
            graph->h264_encoder_->Descriptor();
        if (video_descriptor_pointer == nullptr) {
            status = VRREC_STATUS_INTERNAL_ERROR;
            return {};
        }
        const auto video_descriptor = *video_descriptor_pointer;
        graph->h264_adapter_ = std::make_unique<
            FfmpegH264SystemMemoryPacketEncoderAdapter>(
                *graph->h264_encoder_,
                *graph->frame_mapper_,
                production_config.frames_per_second);

        AacAudioEncoderConfig aac_config {};
        status = CreateAacAudioEncoderConfig(aac_config);
        if (status != VRREC_STATUS_OK) {
            return {};
        }
        auto aac_created = FfmpegAacPacketEncoder::Create(aac_config);
        if (aac_created.status != VRREC_STATUS_OK ||
            aac_created.encoder == nullptr ||
            !aac_created.descriptor.has_value()) {
            status = aac_created.status == VRREC_STATUS_OK
                ? VRREC_STATUS_INTERNAL_ERROR
                : aac_created.status;
            return {};
        }
        const auto audio_descriptor = *aac_created.descriptor;
        graph->aac_encoder_ = std::move(aac_created.encoder);

        graph->audio_availability_ =
            std::make_unique<MediaAudioCaptureAvailabilitySink>(events);
        graph->desktop_provider_ =
            std::make_unique<PlatformAudioCaptureSourceProvider>();
        graph->desktop_waiter_ = std::make_unique<
            ConditionVariableAudioCaptureRecoveryWaiter>();
        graph->microphone_provider_ =
            std::make_unique<PlatformAudioCaptureSourceProvider>();
        graph->microphone_waiter_ = std::make_unique<
            ConditionVariableAudioCaptureRecoveryWaiter>();
        graph->audio_capture_ = std::make_unique<StereoAudioCaptureSession>(
            *graph->desktop_provider_,
            *graph->desktop_waiter_,
            *graph->microphone_provider_,
            *graph->microphone_waiter_,
            AudioTimelineCapacityFrames,
            config.audio_routing,
            config.desktop_gain_db,
            config.microphone_gain_db,
            graph->audio_availability_.get());

        auto mux_created = LibavformatFragmentedMp4MuxerPort::Create(
            config.temporary_output_path_utf8);
        if (mux_created.status != VRREC_STATUS_OK ||
            mux_created.port == nullptr) {
            status = mux_created.status == VRREC_STATUS_OK
                ? VRREC_STATUS_INTERNAL_ERROR
                : mux_created.status;
            return {};
        }
        graph->mux_port_ = std::move(mux_created.port);
        graph->muxer_ = std::make_unique<FfmpegFragmentedMp4Muxer>(
            *graph->mux_port_);
        graph->mux_pipeline_ = std::make_unique<MediaMuxPipeline>(
            *graph->muxer_,
            events);

        const FragmentedMp4StreamConfiguration mux_configuration {
            video_descriptor,
            audio_descriptor,
            DefaultFragmentedMp4FragmentPolicy,
        };
        graph->pre_header_coordinator_ =
            std::make_unique<PreHeaderCoordinator>(
                *graph->mux_pipeline_,
                *graph->mux_pipeline_,
                audio_descriptor,
                DefaultFragmentedMp4FragmentPolicy,
                graph->h264_encoder_.get());
        graph->muxing_video_sink_ =
            std::make_unique<MuxingVideoEncoderSink>(
                *graph->h264_adapter_,
                *graph->pre_header_coordinator_,
                *graph->pre_header_coordinator_);
        graph->processing_video_sink_ =
            std::make_unique<ProcessingVideoEncoderSink>(
                *graph->frame_processor_,
                *graph->muxing_video_sink_,
                config.width,
                config.height);
        status = graph->processing_video_sink_->UpdateVideoLayout(
            production_config.layout);
        if (status != VRREC_STATUS_OK) {
            return {};
        }
        graph->layout_controller_ =
            std::make_unique<ProcessingVideoLayoutController>(
                *graph->processing_video_sink_);
        graph->video_encoding_worker_ =
            std::make_unique<VideoEncodingWorker>(
                *graph->scheduler_,
                *graph->video_clock_,
                *graph->processing_video_sink_,
                events);
        graph->video_pipeline_ = std::make_unique<VideoPipelineSession>(
            *graph->spout_worker_,
            *graph->video_encoding_worker_,
            events);

        graph->muxing_audio_sink_ =
            std::make_unique<MuxingAudioEncoderSink>(
                *graph->aac_encoder_,
                *graph->pre_header_coordinator_);
        graph->audio_pipeline_ =
            std::make_unique<StereoAudioPipelineSession>(
                *graph->audio_capture_,
                *graph->muxing_audio_sink_);
        graph->audio_start_clock_ =
            std::make_unique<WindowsAudioSessionStartClock>();
        graph->timestamping_audio_pipeline_ = std::make_unique<
            TimestampingStereoAudioPipelineSession>(
                *graph->audio_pipeline_,
                *graph->audio_start_clock_);

        graph->pre_header_mux_session_ =
            std::make_unique<PreHeaderMediaMuxSession>(
                *graph->pre_header_coordinator_,
                0,
                graph->h264_encoder_.get(),
                mux_configuration);
        const StereoAudioCaptureSessionConfig audio_capture_config {
            config.desktop_endpoint_id_utf8,
            config.microphone_endpoint_id_utf8,
            0,
        };
        graph->recording_pipeline_ =
            std::make_unique<MediaRecordingPipeline>(
                *graph->video_pipeline_,
                SpoutPollTimeout,
                *graph->timestamping_audio_pipeline_,
                audio_capture_config,
                *graph->pre_header_mux_session_,
                mux_configuration,
                events);
        graph->backend_ = std::make_unique<PipelineMediaBackend>(
            *graph->recording_pipeline_,
            *graph->layout_controller_);

        status = VRREC_STATUS_OK;
        return graph;
    } catch (const std::bad_alloc &) {
        status = VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        status = VRREC_STATUS_INTERNAL_ERROR;
    }
    return {};
}

}
