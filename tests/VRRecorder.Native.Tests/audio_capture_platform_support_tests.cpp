#include "audio_capture_platform_support.hpp"

#include <chrono>
#include <cstdlib>
#include <future>
#include <iostream>
#include <memory>

namespace {

#define CHECK(condition)                                                        \
    do {                                                                        \
        if (!(condition)) {                                                     \
            std::cerr << "check failed at " << __FILE__ << ':' << __LINE__      \
                      << ": " #condition << '\n';                              \
            std::abort();                                                       \
        }                                                                       \
    } while (false)

void ProviderUsesThePlatformWasapiFactory()
{
    vrrecorder::native::PlatformAudioCaptureSourceProvider provider;
    std::unique_ptr<vrrecorder::native::AudioCaptureSource> source;
    const auto status = provider.Create(source);

#if defined(_WIN32)
    CHECK(status == VRREC_STATUS_OK);
    CHECK(source != nullptr);
#else
    CHECK(status == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(source == nullptr);
#endif
}

void AbortReleasesTheProductionRecoveryWaiter()
{
    using namespace std::chrono_literals;

    vrrecorder::native::ConditionVariableAudioCaptureRecoveryWaiter waiter;
    auto waiting = std::async(std::launch::async, [&] {
        return waiter.Wait(5s);
    });
    CHECK(waiting.wait_for(20ms) == std::future_status::timeout);

    waiter.Abort();
    waiter.Abort();

    CHECK(waiting.wait_for(1s) == std::future_status::ready);
    CHECK(!waiting.get());
    CHECK(!waiter.Wait(1ms));
}

}

int main()
{
    ProviderUsesThePlatformWasapiFactory();
    AbortReleasesTheProductionRecoveryWaiter();
    return 0;
}
