#include "audio_capture_source_factory.hpp"

#include <cstdlib>
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

void FactoryIsExplicitOnEveryPlatform()
{
    std::unique_ptr<vrrecorder::native::AudioCaptureSource> source;
    const auto status =
        vrrecorder::native::CreateWasapiAudioCaptureSource(source);

#if defined(_WIN32)
    CHECK(status == VRREC_STATUS_OK);
    CHECK(source != nullptr);
#else
    CHECK(status == VRREC_STATUS_BACKEND_UNAVAILABLE);
    CHECK(source == nullptr);
#endif
}

}

int main()
{
    FactoryIsExplicitOnEveryPlatform();
    return 0;
}
