#include "encoder_probe_pipeline.hpp"

#include <cstdint>
#include <iterator>
#include <new>
#include <utility>
#include <vector>

#include "encoder_probe_identity.hpp"

namespace vrrecorder::native {
namespace {

constexpr std::int64_t MicrosecondsPerSecond = INT64_C(1000000);

bool IsRunnableConfig(const vrrec_encoder_probe_config_v1 &config) noexcept
{
    return config.struct_size >= sizeof(vrrec_encoder_probe_config_v1) &&
        config.abi_version == VRREC_ABI_V1 &&
        FindExpectedEncoderProbeIdentity(config.encoder_kind).has_value() &&
        config.synthetic_frame_count ==
            EncoderProbeSyntheticFrameCount &&
        config.adapter_luid != 0 && config.width != 0 &&
        config.height != 0 && config.fps_numerator != 0 &&
        config.fps_denominator == 1 &&
        config.gpu_identity_utf8 != nullptr &&
        config.gpu_identity_utf8[0] != '\0' && config.reserved == 0;
}

void AppendPackets(
    std::vector<EncodedMediaPacket> &destination,
    std::vector<EncodedMediaPacket> &source)
{
    destination.insert(
        destination.end(),
        std::make_move_iterator(source.begin()),
        std::make_move_iterator(source.end()));
}

}

VerifiedEncoderProbeBackend::VerifiedEncoderProbeBackend(
    EncoderProbeEncodeSessionFactoryPort &factory,
    EncoderProbeDecodePort &decoder) noexcept
    : factory_(factory), decoder_(decoder)
{
}

vrrec_status_t VerifiedEncoderProbeBackend::Probe(
    const vrrec_encoder_probe_config_v1 &config,
    bool &packet_produced) noexcept
{
    packet_produced = false;
    EncoderProbeEvidence evidence;
    const auto status = RunVerifiedEncoderProbe(
        config,
        factory_,
        decoder_,
        evidence);
    if (status == VRREC_STATUS_OK) {
        packet_produced = true;
    }
    return status;
}

vrrec_status_t VerifiedEncoderProbeBackend::ProbeV2(
    const vrrec_encoder_probe_config_v1 &config,
    EncoderProbeEvidence &evidence)
{
    return RunVerifiedEncoderProbe(
        config,
        factory_,
        decoder_,
        evidence);
}

vrrec_status_t RunVerifiedEncoderProbe(
    const vrrec_encoder_probe_config_v1 &config,
    EncoderProbeEncodeSessionFactoryPort &factory,
    EncoderProbeDecodePort &decoder,
    EncoderProbeEvidence &evidence) noexcept
{
    if (!IsRunnableConfig(config)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    std::unique_ptr<EncoderProbeEncodeSession> session;
    bool aborted = false;
    const auto abort = [&session, &aborted]() noexcept {
        if (session != nullptr && !aborted) {
            aborted = true;
            session->Abort();
        }
    };
    try {
        auto creation = factory.Create(config);
        session = std::move(creation.session);
        if (creation.status != VRREC_STATUS_OK) {
            abort();
            return creation.status;
        }
        if (session == nullptr) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }

        std::vector<EncodedMediaPacket> packets;
        packets.reserve(config.synthetic_frame_count);
        for (std::uint32_t index = 0;
             index < config.synthetic_frame_count;
             ++index) {
            const auto start = static_cast<std::int64_t>(index) *
                MicrosecondsPerSecond / config.fps_numerator;
            const auto end = static_cast<std::int64_t>(index + 1U) *
                MicrosecondsPerSecond / config.fps_numerator;
            const EncoderProbeSyntheticFrame frame {
                index,
                config.width,
                config.height,
                start,
                end - start,
            };
            auto batch = session->EncodeSyntheticFrame(frame);
            if (batch.status != VRREC_STATUS_OK) {
                abort();
                return batch.status;
            }
            AppendPackets(packets, batch.packets);
        }

        auto final_batch = session->Finish();
        if (final_batch.status != VRREC_STATUS_OK) {
            abort();
            return final_batch.status;
        }
        AppendPackets(packets, final_batch.packets);
        return BuildVerifiedEncoderProbeEvidence(
            config,
            session->OpenedIdentity(),
            packets,
            decoder,
            evidence);
    } catch (const std::bad_alloc &) {
        abort();
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        abort();
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

}
