#include "ffmpeg_h264_nv12_frame.hpp"

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <span>
#include <vector>

extern "C" {
#include <libavutil/frame.h>
#include <libavutil/pixfmt.h>
}

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

class OwnedNv12Frame final {
public:
    OwnedNv12Frame(std::uint32_t width, std::uint32_t height)
        : frame_(av_frame_alloc())
    {
        CHECK(frame_ != nullptr);
        frame_->format = AV_PIX_FMT_NV12;
        frame_->width = static_cast<int>(width);
        frame_->height = static_cast<int>(height);
        CHECK(av_frame_get_buffer(frame_, 32) == 0);
    }

    ~OwnedNv12Frame()
    {
        av_frame_free(&frame_);
    }

    AVFrame &Get() noexcept
    {
        return *frame_;
    }

private:
    AVFrame *frame_;
};

std::uint8_t ByteAt(const AVFrame &frame, int plane, int row, int column)
{
    return frame.data[plane][row * frame.linesize[plane] + column];
}

void CopiesPaddedTwoPlaneInputIntoFfmpegOwnedStorage()
{
    constexpr std::uint32_t width = 4;
    constexpr std::uint32_t height = 4;
    constexpr std::uint32_t stride = 6;
    std::vector<std::byte> y(22, std::byte {0x7f});
    std::vector<std::byte> uv(10, std::byte {0x6f});
    for (std::uint32_t row = 0; row < height; ++row) {
        for (std::uint32_t column = 0; column < width; ++column) {
            y[row * stride + column] = static_cast<std::byte>(
                row * 16U + column);
        }
    }
    for (std::uint32_t row = 0; row < height / 2U; ++row) {
        for (std::uint32_t column = 0; column < width; ++column) {
            uv[row * stride + column] = static_cast<std::byte>(
                0x80U + row * 16U + column);
        }
    }
    OwnedNv12Frame destination(width, height);

    CHECK(CopySystemMemoryNv12FrameToFfmpeg(
              {
                  width,
                  height,
                  stride,
                  stride,
                  y,
                  uv,
                  7,
              },
              destination.Get()) == VRREC_STATUS_OK);
    CHECK(destination.Get().pts == 7);
    for (int row = 0; row < static_cast<int>(height); ++row) {
        for (int column = 0; column < static_cast<int>(width); ++column) {
            CHECK(ByteAt(destination.Get(), 0, row, column) ==
                  static_cast<std::uint8_t>(row * 16 + column));
        }
    }
    for (int row = 0; row < static_cast<int>(height / 2U); ++row) {
        for (int column = 0; column < static_cast<int>(width); ++column) {
            CHECK(ByteAt(destination.Get(), 1, row, column) ==
                  static_cast<std::uint8_t>(0x80 + row * 16 + column));
        }
    }

    std::ranges::fill(y, std::byte {0});
    std::ranges::fill(uv, std::byte {0});
    CHECK(ByteAt(destination.Get(), 0, 3, 3) == 51);
    CHECK(ByteAt(destination.Get(), 1, 1, 3) == 147);
}

void AcceptsTheSmallestEvenFrame()
{
    std::vector<std::byte> y {
        std::byte {1},
        std::byte {2},
        std::byte {3},
        std::byte {4},
    };
    std::vector<std::byte> uv {std::byte {5}, std::byte {6}};
    OwnedNv12Frame destination(2, 2);

    CHECK(CopySystemMemoryNv12FrameToFfmpeg(
              {2, 2, 2, 2, y, uv, 0},
              destination.Get()) == VRREC_STATUS_OK);
    CHECK(ByteAt(destination.Get(), 0, 1, 1) == 4);
    CHECK(ByteAt(destination.Get(), 1, 0, 1) == 6);
}

void PreservesOutstandingFfmpegReferencesWhenReusingTheFrame()
{
    std::vector<std::byte> first_y(16, std::byte {0x11});
    std::vector<std::byte> first_uv(8, std::byte {0x22});
    std::vector<std::byte> second_y(16, std::byte {0x33});
    std::vector<std::byte> second_uv(8, std::byte {0x44});
    OwnedNv12Frame destination(4, 4);

    CHECK(CopySystemMemoryNv12FrameToFfmpeg(
              {4, 4, 4, 4, first_y, first_uv, 1},
              destination.Get()) == VRREC_STATUS_OK);
    AVFrame *retained = av_frame_alloc();
    CHECK(retained != nullptr);
    CHECK(av_frame_ref(retained, &destination.Get()) == 0);

    CHECK(CopySystemMemoryNv12FrameToFfmpeg(
              {4, 4, 4, 4, second_y, second_uv, 2},
              destination.Get()) == VRREC_STATUS_OK);
    CHECK(retained->pts == 1);
    CHECK(ByteAt(*retained, 0, 0, 0) == 0x11);
    CHECK(ByteAt(*retained, 1, 0, 0) == 0x22);
    CHECK(destination.Get().pts == 2);
    CHECK(ByteAt(destination.Get(), 0, 0, 0) == 0x33);
    CHECK(ByteAt(destination.Get(), 1, 0, 0) == 0x44);

    av_frame_free(&retained);
}

void CheckRejectedWithoutDestinationMutation(
    const SystemMemoryNv12FrameView &source)
{
    OwnedNv12Frame destination(4, 4);
    destination.Get().pts = 99;
    destination.Get().data[0][0] = 0x55;

    CHECK(CopySystemMemoryNv12FrameToFfmpeg(
              source,
              destination.Get()) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(destination.Get().pts == 99);
    CHECK(destination.Get().data[0][0] == 0x55);
}

void RejectsInvalidPlaneLayoutsBeforeWritingTheDestination()
{
    std::vector<std::byte> y(16);
    std::vector<std::byte> uv(8);
    auto source = SystemMemoryNv12FrameView {4, 4, 4, 4, y, uv, 1};

    source.width = 3;
    CheckRejectedWithoutDestinationMutation(source);
    source = {4, 4, 3, 4, y, uv, 1};
    CheckRejectedWithoutDestinationMutation(source);
    source = {4, 4, 4, 3, y, uv, 1};
    CheckRejectedWithoutDestinationMutation(source);
    source = {4, 4, 4, 4, std::span(y).first(15), uv, 1};
    CheckRejectedWithoutDestinationMutation(source);
    source = {4, 4, 4, 4, y, std::span(uv).first(7), 1};
    CheckRejectedWithoutDestinationMutation(source);
    source = {4, 4, 4, 4, y, uv, -1};
    CheckRejectedWithoutDestinationMutation(source);
}

void RejectsAnIncompatibleDestinationBeforeWritingIt()
{
    std::vector<std::byte> y(16);
    std::vector<std::byte> uv(8);
    const auto source = SystemMemoryNv12FrameView {4, 4, 4, 4, y, uv, 1};
    OwnedNv12Frame destination(4, 4);
    destination.Get().pts = 99;
    destination.Get().format = AV_PIX_FMT_YUV420P;

    CHECK(CopySystemMemoryNv12FrameToFfmpeg(
              source,
              destination.Get()) == VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(destination.Get().pts == 99);
}

}

int main()
{
    CopiesPaddedTwoPlaneInputIntoFfmpegOwnedStorage();
    AcceptsTheSmallestEvenFrame();
    PreservesOutstandingFfmpegReferencesWhenReusingTheFrame();
    RejectsInvalidPlaneLayoutsBeforeWritingTheDestination();
    RejectsAnIncompatibleDestinationBeforeWritingIt();
    return 0;
}
