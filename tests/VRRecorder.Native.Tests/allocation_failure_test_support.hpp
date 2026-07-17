#ifndef VRRECORDER_NATIVE_ALLOCATION_FAILURE_TEST_SUPPORT_HPP
#define VRRECORDER_NATIVE_ALLOCATION_FAILURE_TEST_SUPPORT_HPP

#include <atomic>
#include <cstddef>
#include <cstdlib>
#include <new>

namespace allocation_failure {

std::atomic<std::size_t> fail_on_allocation {0};

bool ShouldFail() noexcept
{
    auto remaining = fail_on_allocation.load();
    while (remaining != 0) {
        if (fail_on_allocation.compare_exchange_weak(
                remaining,
                remaining - 1)) {
            return remaining == 1;
        }
    }
    return false;
}

}

void *operator new(std::size_t size)
{
    if (allocation_failure::ShouldFail()) {
        throw std::bad_alloc {};
    }
    if (auto *allocation = std::malloc(size); allocation != nullptr) {
        return allocation;
    }
    throw std::bad_alloc {};
}

void *operator new(std::size_t size, const std::nothrow_t &) noexcept
{
    if (allocation_failure::ShouldFail()) {
        return nullptr;
    }
    return std::malloc(size);
}

void *operator new[](std::size_t size)
{
    return ::operator new(size);
}

void operator delete(void *allocation) noexcept
{
    std::free(allocation);
}

void operator delete[](void *allocation) noexcept
{
    ::operator delete(allocation);
}

void operator delete(void *allocation, std::size_t) noexcept
{
    ::operator delete(allocation);
}

void operator delete[](void *allocation, std::size_t) noexcept
{
    ::operator delete[](allocation);
}

void operator delete(
    void *allocation,
    const std::nothrow_t &) noexcept
{
    ::operator delete(allocation);
}

#endif
