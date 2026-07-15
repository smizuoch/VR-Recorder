#include "software_encoder_probe_platform_identity.hpp"

#include <cstdint>
#include <cstdlib>
#include <functional>
#include <iostream>
#include <string>
#include <vector>

namespace {

#define CHECK(condition)                                                        \
    do {                                                                        \
        if (!(condition)) {                                                     \
            std::cerr << "check failed at " << __FILE__ << ':' << __LINE__      \
                      << ": " #condition << '\n';                              \
            std::abort();                                                       \
        }                                                                       \
    } while (false)

using namespace vrrecorder::native;

constexpr std::uint64_t AdapterLuid = UINT64_C(0x00000001abcdef01);
constexpr char GpuIdentity[] =
    "NVIDIA GeForce RTX 4090 [vendor:10DE device:2684]";

vrrec_encoder_probe_config_v1 Config()
{
    return {
        sizeof(vrrec_encoder_probe_config_v1),
        VRREC_ABI_V1,
        VRREC_ENCODER_MEDIA_FOUNDATION_SOFTWARE,
        EncoderProbeSyntheticFrameCount,
        AdapterLuid,
        32,
        16,
        30,
        1,
        GpuIdentity,
        0,
    };
}

class FakeLookup final : public SoftwareEncoderProbeAdapterIdentityLookupPort {
public:
    SoftwareEncoderProbeAdapterIdentityResult Lookup(
        std::uint64_t adapter_luid) noexcept override
    {
        ++call_count;
        observed_luid = adapter_luid;
        return result;
    }

    SoftwareEncoderProbeAdapterIdentityResult result {
        VRREC_STATUS_OK,
        {
            AdapterLuid,
            GpuIdentity,
            "dxgi-driver:32.0.16.1062",
            "pci\\ven_10de&dev_2684&subsys_00000000&rev_a1",
        },
    };
    std::uint32_t call_count = 0;
    std::uint64_t observed_luid = 0;
};

void ResolvesTheExactRequestedAdapterForBothPipelineStages()
{
    FakeLookup lookup;
    SoftwareEncoderProbePlatformIdentityResolver resolver(lookup);

    const auto result = resolver.Resolve(Config());
    CHECK(result.status == VRREC_STATUS_OK);
    CHECK(lookup.call_count == 1);
    CHECK(lookup.observed_luid == AdapterLuid);
    CHECK(result.identity.source_adapter_luid == AdapterLuid);
    CHECK(result.identity.processor_adapter_luid == AdapterLuid);
    CHECK(result.identity.gpu_identity == GpuIdentity);
    CHECK(result.identity.driver_identity == "dxgi-driver:32.0.16.1062");
    CHECK(result.identity.device_identity ==
          "pci\\ven_10de&dev_2684&subsys_00000000&rev_a1");
}

void RejectsInvalidRequestsBeforeEnumeratingDxgi()
{
    using Mutation = std::function<void(vrrec_encoder_probe_config_v1 &)>;
    const std::vector<Mutation> mutations {
        [](auto &value) { value.struct_size = sizeof(value) - 1; },
        [](auto &value) { ++value.abi_version; },
        [](auto &value) { value.encoder_kind = VRREC_ENCODER_NVENC; },
        [](auto &value) { --value.synthetic_frame_count; },
        [](auto &value) { value.adapter_luid = 0; },
        [](auto &value) { value.gpu_identity_utf8 = nullptr; },
        [](auto &value) { value.gpu_identity_utf8 = ""; },
        [](auto &value) { value.reserved = 1; },
    };
    for (const auto &mutate : mutations) {
        FakeLookup lookup;
        SoftwareEncoderProbePlatformIdentityResolver resolver(lookup);
        auto config = Config();
        mutate(config);
        CHECK(resolver.Resolve(config).status != VRREC_STATUS_OK);
        CHECK(lookup.call_count == 0);
    }
}

void FailsClosedOnLookupFailureOrIdentityDrift()
{
    {
        FakeLookup lookup;
        lookup.result.status = VRREC_STATUS_BACKEND_UNAVAILABLE;
        SoftwareEncoderProbePlatformIdentityResolver resolver(lookup);
        CHECK(resolver.Resolve(Config()).status ==
              VRREC_STATUS_BACKEND_UNAVAILABLE);
    }

    using Mutation = std::function<void(
        SoftwareEncoderProbeAdapterIdentity &)>;
    const std::vector<Mutation> mutations {
        [](auto &value) { ++value.adapter_luid; },
        [](auto &value) { value.gpu_identity = "different"; },
        [](auto &value) { value.driver_identity.clear(); },
        [](auto &value) { value.device_identity.clear(); },
    };
    for (const auto &mutate : mutations) {
        FakeLookup lookup;
        mutate(lookup.result.identity);
        SoftwareEncoderProbePlatformIdentityResolver resolver(lookup);
        const auto result = resolver.Resolve(Config());
        CHECK(result.status == VRREC_STATUS_BACKEND_UNAVAILABLE);
        CHECK(result.identity.source_adapter_luid == 0);
        CHECK(lookup.call_count == 1);
    }
}

}

int main()
{
    ResolvesTheExactRequestedAdapterForBothPipelineStages();
    RejectsInvalidRequestsBeforeEnumeratingDxgi();
    FailsClosedOnLookupFailureOrIdentityDrift();
    return 0;
}
