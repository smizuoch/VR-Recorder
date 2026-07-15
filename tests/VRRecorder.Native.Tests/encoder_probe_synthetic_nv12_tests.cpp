#include "encoder_probe_synthetic_nv12.hpp"

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <iostream>

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

EncoderProbeSyntheticFrame Frame(
    std::uint32_t index = 3,
    std::uint32_t width = 8,
    std::uint32_t height = 4)
{
    return {
        index,
        width,
        height,
        100'000,
        33'333,
    };
}

std::byte Luma(
    std::uint32_t index,
    std::uint32_t x,
    std::uint32_t y)
{
    return static_cast<std::byte>(
        16U + (x * 7U + y * 13U + index * 17U) % 220U);
}

std::byte ChromaU(
    std::uint32_t index,
    std::uint32_t pair,
    std::uint32_t y)
{
    return static_cast<std::byte>(
        16U + (pair * 11U + y * 5U + index * 19U) % 225U);
}

std::byte ChromaV(
    std::uint32_t index,
    std::uint32_t pair,
    std::uint32_t y)
{
    return static_cast<std::byte>(
        16U + (pair * 3U + y * 17U + index * 23U) % 225U);
}

void CreatesDeterministicLimitedRangeNv12WithCodecTickPts()
{
    OwnedEncoderProbeNv12Frame owned;
    CHECK(CreateEncoderProbeSyntheticNv12Frame(Frame(), owned) ==
          VRREC_STATUS_OK);
    const auto view = owned.View();
    CHECK(view.width == 8);
    CHECK(view.height == 4);
    CHECK(view.y_stride_bytes == 8);
    CHECK(view.uv_stride_bytes == 8);
    CHECK(view.y_plane.size() == 32);
    CHECK(view.uv_plane.size() == 16);
    CHECK(view.pts == 3);

    for (std::uint32_t y = 0; y < view.height; ++y) {
        for (std::uint32_t x = 0; x < view.width; ++x) {
            CHECK(view.y_plane[y * view.y_stride_bytes + x] ==
                  Luma(3, x, y));
        }
    }
    for (std::uint32_t y = 0; y < view.height / 2U; ++y) {
        for (std::uint32_t pair = 0; pair < view.width / 2U; ++pair) {
            const auto offset = y * view.uv_stride_bytes + pair * 2U;
            CHECK(view.uv_plane[offset] == ChromaU(3, pair, y));
            CHECK(view.uv_plane[offset + 1U] == ChromaV(3, pair, y));
        }
    }
}

void EverySyntheticFrameHasAStableDistinctPattern()
{
    OwnedEncoderProbeNv12Frame first;
    OwnedEncoderProbeNv12Frame repeated;
    OwnedEncoderProbeNv12Frame next;
    CHECK(CreateEncoderProbeSyntheticNv12Frame(Frame(0), first) ==
          VRREC_STATUS_OK);
    CHECK(CreateEncoderProbeSyntheticNv12Frame(Frame(0), repeated) ==
          VRREC_STATUS_OK);
    CHECK(CreateEncoderProbeSyntheticNv12Frame(Frame(1), next) ==
          VRREC_STATUS_OK);
    CHECK(first.View().y_plane.data() != repeated.View().y_plane.data());
    CHECK(first.View().y_plane.size() == repeated.View().y_plane.size());
    CHECK(std::equal(
        first.View().y_plane.begin(),
        first.View().y_plane.end(),
        repeated.View().y_plane.begin()));
    CHECK(!std::equal(
        first.View().y_plane.begin(),
        first.View().y_plane.end(),
        next.View().y_plane.begin()));
    CHECK(first.View().pts == 0);
    CHECK(next.View().pts == 1);
}

void InvalidFrameDoesNotReplaceCallerOwnedPlanes()
{
    OwnedEncoderProbeNv12Frame owned;
    CHECK(CreateEncoderProbeSyntheticNv12Frame(Frame(), owned) ==
          VRREC_STATUS_OK);
    const auto original_y = owned.View().y_plane.data();
    const auto original_uv = owned.View().uv_plane.data();
    const auto original_pts = owned.View().pts;

    auto invalid = Frame();
    invalid.frame_index = EncoderProbeSyntheticFrameCount;
    CHECK(CreateEncoderProbeSyntheticNv12Frame(invalid, owned) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    invalid = Frame();
    invalid.width = 7;
    CHECK(CreateEncoderProbeSyntheticNv12Frame(invalid, owned) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    invalid = Frame();
    invalid.height = 3;
    CHECK(CreateEncoderProbeSyntheticNv12Frame(invalid, owned) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    invalid = Frame();
    invalid.pts_microseconds = -1;
    CHECK(CreateEncoderProbeSyntheticNv12Frame(invalid, owned) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    invalid = Frame();
    invalid.duration_microseconds = 0;
    CHECK(CreateEncoderProbeSyntheticNv12Frame(invalid, owned) ==
          VRREC_STATUS_INVALID_ARGUMENT);

    CHECK(owned.View().y_plane.data() == original_y);
    CHECK(owned.View().uv_plane.data() == original_uv);
    CHECK(owned.View().pts == original_pts);
}

}

int main()
{
    CreatesDeterministicLimitedRangeNv12WithCodecTickPts();
    EverySyntheticFrameHasAStableDistinctPattern();
    InvalidFrameDoesNotReplaceCallerOwnedPlanes();
}
