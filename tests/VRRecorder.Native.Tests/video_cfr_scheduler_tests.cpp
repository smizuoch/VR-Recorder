#include "video_cfr_scheduler.hpp"

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

void StartsOnlyAfterTheFirstSourceFrame()
{
    VideoCfrScheduler scheduler;
    ScheduledVideoFrame output {};

    CHECK(scheduler.Schedule(0, output) == VideoScheduleResult::NoFrame);
    CHECK(scheduler.Push({10, 1'000'000}) == VRREC_STATUS_OK);
    CHECK(scheduler.Schedule(1, output) == VideoScheduleResult::Ready);
    CHECK(output.output_tick == 1);
    CHECK(output.source_sequence == 10);
    CHECK(output.source_timestamp_microseconds == 1'000'000);
    CHECK(!output.duplicated);
    CHECK(output.dropped_before_output == 0);
}

void DuplicatesThePreviousFrameWhenNoNewFrameArrives()
{
    VideoCfrScheduler scheduler;
    ScheduledVideoFrame output {};
    CHECK(scheduler.Push({20, 2'000'000}) == VRREC_STATUS_OK);
    CHECK(scheduler.Schedule(0, output) == VideoScheduleResult::Ready);
    CHECK(scheduler.Schedule(1, output) == VideoScheduleResult::Ready);

    CHECK(output.output_tick == 1);
    CHECK(output.source_sequence == 20);
    CHECK(output.duplicated);
    CHECK(output.dropped_before_output == 0);
    const auto statistics = scheduler.Statistics();
    CHECK(statistics.source_frame_count == 1);
    CHECK(statistics.output_frame_count == 2);
    CHECK(statistics.dropped_source_frame_count == 0);
    CHECK(statistics.duplicated_output_frame_count == 1);
}

void KeepsOnlyTheLatestFrameAndCountsDiscardedSources()
{
    VideoCfrScheduler scheduler;
    ScheduledVideoFrame output {};
    CHECK(scheduler.Push({30, 3'000'000}) == VRREC_STATUS_OK);
    CHECK(scheduler.Schedule(0, output) == VideoScheduleResult::Ready);
    CHECK(scheduler.Push({31, 3'010'000}) == VRREC_STATUS_OK);
    CHECK(scheduler.Push({32, 3'020'000}) == VRREC_STATUS_OK);
    CHECK(scheduler.Push({33, 3'030'000}) == VRREC_STATUS_OK);
    CHECK(scheduler.Schedule(1, output) == VideoScheduleResult::Ready);

    CHECK(output.source_sequence == 33);
    CHECK(!output.duplicated);
    CHECK(output.dropped_before_output == 2);
    const auto statistics = scheduler.Statistics();
    CHECK(statistics.source_frame_count == 4);
    CHECK(statistics.output_frame_count == 2);
    CHECK(statistics.dropped_source_frame_count == 2);
    CHECK(statistics.duplicated_output_frame_count == 0);
}

void RejectsNonMonotonicInputAndOutputWithoutChangingStatistics()
{
    VideoCfrScheduler scheduler;
    ScheduledVideoFrame output {};
    CHECK(scheduler.Push({40, 4'000'000}) == VRREC_STATUS_OK);
    CHECK(scheduler.Push({40, 4'010'000}) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(scheduler.Push({41, 3'999'999}) ==
          VRREC_STATUS_INVALID_ARGUMENT);
    CHECK(scheduler.Schedule(4, output) == VideoScheduleResult::Ready);
    CHECK(scheduler.Schedule(4, output) ==
          VideoScheduleResult::InvalidTick);
    CHECK(scheduler.Schedule(3, output) ==
          VideoScheduleResult::InvalidTick);

    const auto statistics = scheduler.Statistics();
    CHECK(statistics.source_frame_count == 1);
    CHECK(statistics.output_frame_count == 1);
    CHECK(statistics.dropped_source_frame_count == 0);
    CHECK(statistics.duplicated_output_frame_count == 0);
}

}

int main()
{
    StartsOnlyAfterTheFirstSourceFrame();
    DuplicatesThePreviousFrameWhenNoNewFrameArrives();
    KeepsOnlyTheLatestFrameAndCountsDiscardedSources();
    RejectsNonMonotonicInputAndOutputWithoutChangingStatistics();
    return 0;
}
