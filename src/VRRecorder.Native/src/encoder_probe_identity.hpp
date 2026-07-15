#ifndef VRRECORDER_NATIVE_ENCODER_PROBE_IDENTITY_HPP
#define VRRECORDER_NATIVE_ENCODER_PROBE_IDENTITY_HPP

#include <optional>
#include <string_view>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

struct ExpectedEncoderProbeIdentity final {
    std::string_view codec_name;
    bool hardware_accelerated;
    vrrec_encoder_input_format_t input_format;
};

inline std::optional<ExpectedEncoderProbeIdentity>
FindExpectedEncoderProbeIdentity(vrrec_encoder_kind_t kind) noexcept
{
    switch (kind) {
    case VRREC_ENCODER_NVENC:
        return ExpectedEncoderProbeIdentity {
            "h264_nvenc",
            true,
            VRREC_ENCODER_INPUT_D3D11_NV12,
        };
    case VRREC_ENCODER_AMF:
        return ExpectedEncoderProbeIdentity {
            "h264_amf",
            true,
            VRREC_ENCODER_INPUT_D3D11_NV12,
        };
    case VRREC_ENCODER_QSV:
        return ExpectedEncoderProbeIdentity {
            "h264_qsv",
            true,
            VRREC_ENCODER_INPUT_QSV_NV12,
        };
    case VRREC_ENCODER_MEDIA_FOUNDATION_SOFTWARE:
        return ExpectedEncoderProbeIdentity {
            "h264_mf",
            false,
            VRREC_ENCODER_INPUT_SYSTEM_MEMORY_NV12,
        };
    default:
        return std::nullopt;
    }
}

}

#endif
