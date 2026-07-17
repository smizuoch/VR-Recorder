#include "h264_avcc_to_annex_b.hpp"
#include "allocation_failure_test_support.hpp"

#include <cstddef>
#include <cstdlib>
#include <iostream>
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

std::vector<std::byte> AvccDescriptor()
{
    return {
        std::byte {1},
        std::byte {100},
        std::byte {0},
        std::byte {40},
        std::byte {0xff},
        std::byte {0xe1},
        std::byte {0},
        std::byte {4},
        std::byte {0x67},
        std::byte {0x64},
        std::byte {0},
        std::byte {0x28},
        std::byte {1},
        std::byte {0},
        std::byte {3},
        std::byte {0x68},
        std::byte {0xee},
        std::byte {0x3c},
    };
}

void ConvertsAllParameterSetsInOrder()
{
    std::vector<std::byte> output;
    CHECK(ConvertH264AvccDescriptorToAnnexB(
              AvccDescriptor(),
              output) == VRREC_STATUS_OK);
    const std::vector<std::byte> expected {
        std::byte {0}, std::byte {0}, std::byte {0}, std::byte {1},
        std::byte {0x67}, std::byte {0x64}, std::byte {0}, std::byte {0x28},
        std::byte {0}, std::byte {0}, std::byte {0}, std::byte {1},
        std::byte {0x68}, std::byte {0xee}, std::byte {0x3c},
    };
    CHECK(output == expected);
}

void ConvertsEveryLengthPrefixedNalInAnAccessUnit()
{
    const std::vector<std::byte> packet {
        std::byte {0}, std::byte {0}, std::byte {0}, std::byte {2},
        std::byte {0x09}, std::byte {0xf0},
        std::byte {0}, std::byte {0}, std::byte {0}, std::byte {3},
        std::byte {0x65}, std::byte {1}, std::byte {2},
    };
    std::vector<std::byte> output;
    CHECK(ConvertH264AvccAccessUnitToAnnexB(packet, output) ==
          VRREC_STATUS_OK);
    const std::vector<std::byte> expected {
        std::byte {0}, std::byte {0}, std::byte {0}, std::byte {1},
        std::byte {0x09}, std::byte {0xf0},
        std::byte {0}, std::byte {0}, std::byte {0}, std::byte {1},
        std::byte {0x65}, std::byte {1}, std::byte {2},
    };
    CHECK(output == expected);
}

void RejectsMalformedDescriptorWithoutChangingOutput()
{
    const auto valid = AvccDescriptor();
    using Mutation = void (*)(std::vector<std::byte> &);
    const Mutation mutations[] {
        [](auto &bytes) { bytes.clear(); },
        [](auto &bytes) { bytes[0] = std::byte {2}; },
        [](auto &bytes) { bytes[4] = std::byte {0x03}; },
        [](auto &bytes) { bytes[4] = std::byte {0xfe}; },
        [](auto &bytes) { bytes[5] = std::byte {0x01}; },
        [](auto &bytes) { bytes[5] = std::byte {0xe0}; },
        [](auto &bytes) {
            bytes[6] = std::byte {0};
            bytes[7] = std::byte {0};
        },
        [](auto &bytes) { bytes[8] = std::byte {0x68}; },
        [](auto &bytes) { bytes[8] = std::byte {0xe7}; },
        [](auto &bytes) { bytes[15] = std::byte {0x67}; },
        [](auto &bytes) { bytes[7] = std::byte {0xff}; },
        [](auto &bytes) { bytes[12] = std::byte {0}; },
        [](auto &bytes) { bytes.resize(12); },
        [](auto &bytes) { bytes.push_back(std::byte {0}); },
        [](auto &bytes) { bytes.pop_back(); },
    };
    for (const auto mutate : mutations) {
        auto input = valid;
        mutate(input);
        std::vector<std::byte> output {std::byte {0xaa}};
        CHECK(ConvertH264AvccDescriptorToAnnexB(input, output) ==
              VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(output == std::vector<std::byte> {std::byte {0xaa}});
    }
}

void RejectsMalformedAccessUnitWithoutChangingOutput()
{
    const std::vector<std::vector<std::byte>> invalid {
        {},
        {std::byte {0}, std::byte {0}, std::byte {0}},
        {std::byte {0}, std::byte {0}, std::byte {0}, std::byte {0}},
        {
            std::byte {0}, std::byte {0}, std::byte {0}, std::byte {2},
            std::byte {0x65},
        },
        {
            std::byte {0}, std::byte {0}, std::byte {0}, std::byte {1},
            std::byte {0x80},
        },
    };
    for (const auto &input : invalid) {
        std::vector<std::byte> output {std::byte {0xbb}};
        CHECK(ConvertH264AvccAccessUnitToAnnexB(input, output) ==
              VRREC_STATUS_INVALID_ARGUMENT);
        CHECK(output == std::vector<std::byte> {std::byte {0xbb}});
    }
}

void ReportsAllocationFailureWithoutChangingOutput()
{
    std::vector<std::byte> descriptor_output {std::byte {0xaa}};
    const auto descriptor = AvccDescriptor();
    allocation_failure::fail_on_allocation = 1;
    const auto descriptor_status = ConvertH264AvccDescriptorToAnnexB(
        descriptor,
        descriptor_output);
    allocation_failure::fail_on_allocation = 0;
    CHECK(descriptor_status == VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(descriptor_output ==
          std::vector<std::byte> {std::byte {0xaa}});

    const std::vector<std::byte> access_unit {
        std::byte {0}, std::byte {0}, std::byte {0}, std::byte {1},
        std::byte {0x65},
    };
    std::vector<std::byte> access_output {std::byte {0xbb}};
    allocation_failure::fail_on_allocation = 1;
    const auto access_status = ConvertH264AvccAccessUnitToAnnexB(
        access_unit,
        access_output);
    allocation_failure::fail_on_allocation = 0;
    CHECK(access_status == VRREC_STATUS_OUT_OF_MEMORY);
    CHECK(access_output == std::vector<std::byte> {std::byte {0xbb}});
}

}

int main()
{
    ConvertsAllParameterSetsInOrder();
    ConvertsEveryLengthPrefixedNalInAnAccessUnit();
    RejectsMalformedDescriptorWithoutChangingOutput();
    RejectsMalformedAccessUnitWithoutChangingOutput();
    ReportsAllocationFailureWithoutChangingOutput();
    return 0;
}
