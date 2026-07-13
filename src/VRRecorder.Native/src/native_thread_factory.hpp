#ifndef VRRECORDER_NATIVE_NATIVE_THREAD_FACTORY_HPP
#define VRRECORDER_NATIVE_NATIVE_THREAD_FACTORY_HPP

#include <new>
#include <system_error>
#include <thread>

#include "vrrecorder_native.h"

namespace vrrecorder::native {

using NativeThreadEntry = void (*)(void *) noexcept;

class NativeThreadFactoryPort {
public:
    virtual ~NativeThreadFactoryPort() = default;

    // A failed launch must leave thread non-joinable. Callers also reject a
    // successful status unless a joinable thread was published.
    virtual vrrec_status_t Start(
        std::thread &thread,
        NativeThreadEntry entry,
        void *context) noexcept = 0;
};

class StandardNativeThreadFactory final : public NativeThreadFactoryPort {
public:
    vrrec_status_t Start(
        std::thread &thread,
        NativeThreadEntry entry,
        void *context) noexcept override
    {
        try {
            thread = std::thread(entry, context);
        } catch (const std::bad_alloc &) {
            return VRREC_STATUS_OUT_OF_MEMORY;
        } catch (const std::system_error &) {
            return VRREC_STATUS_INTERNAL_ERROR;
        } catch (...) {
            return VRREC_STATUS_INTERNAL_ERROR;
        }

        return VRREC_STATUS_OK;
    }
};

inline NativeThreadFactoryPort &DefaultNativeThreadFactory() noexcept
{
    static StandardNativeThreadFactory factory;
    return factory;
}

}

#endif
