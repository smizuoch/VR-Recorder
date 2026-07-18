#include "h264_avcc_to_annex_b.hpp"

#include <cstddef>
#include <cstdint>
#include <limits>
#include <new>
#include <span>
#include <vector>

namespace vrrecorder::native {
namespace {

constexpr std::byte StartCode[] {
    std::byte {0},
    std::byte {0},
    std::byte {0},
    std::byte {1},
};

std::uint16_t ReadBigEndian16(
    std::span<const std::byte> bytes,
    std::size_t offset) noexcept
{
    return static_cast<std::uint16_t>(
        (std::to_integer<std::uint16_t>(bytes[offset]) << 8U) |
        std::to_integer<std::uint16_t>(bytes[offset + 1U]));
}

std::uint32_t ReadBigEndian32(
    std::span<const std::byte> bytes,
    std::size_t offset) noexcept
{
    return
        (std::to_integer<std::uint32_t>(bytes[offset]) << 24U) |
        (std::to_integer<std::uint32_t>(bytes[offset + 1U]) << 16U) |
        (std::to_integer<std::uint32_t>(bytes[offset + 2U]) << 8U) |
        std::to_integer<std::uint32_t>(bytes[offset + 3U]);
}

bool AppendNal(
    std::span<const std::byte> nal,
    unsigned int required_type,
    std::vector<std::byte> &output)
{
    if (nal.empty()) {
        return false;
    }
    const auto header = std::to_integer<unsigned int>(nal.front());
    const auto maximum = std::numeric_limits<std::size_t>::max();
    if ((header & 0x80U) != 0U ||
        (required_type != 0U && (header & 0x1fU) != required_type) ||
        output.size() > maximum - std::size(StartCode) ||
        nal.size() > maximum - output.size() - std::size(StartCode)) {
        return false;
    }
    output.insert(output.end(), std::begin(StartCode), std::end(StartCode));
    output.insert(output.end(), nal.begin(), nal.end());
    return true;
}

bool ReadDescriptorNal(
    std::span<const std::byte> descriptor,
    std::size_t &offset,
    unsigned int required_type,
    std::vector<std::byte> &output)
{
    if (offset > descriptor.size() ||
        descriptor.size() - offset < 2U) {
        return false;
    }
    const auto length = static_cast<std::size_t>(
        ReadBigEndian16(descriptor, offset));
    offset += 2U;
    if (length == 0 || offset > descriptor.size() ||
        length > descriptor.size() - offset ||
        !AppendNal(
            descriptor.subspan(offset, length),
            required_type,
            output)) {
        return false;
    }
    offset += length;
    return true;
}

}

vrrec_status_t ConvertH264AvccDescriptorToAnnexB(
    std::span<const std::byte> descriptor,
    std::vector<std::byte> &annex_b) noexcept
{
    if (descriptor.size() < 7U || descriptor[0] != std::byte {1} ||
        (std::to_integer<unsigned int>(descriptor[4]) & 0xfcU) != 0xfcU ||
        (std::to_integer<unsigned int>(descriptor[4]) & 0x03U) != 0x03U ||
        (std::to_integer<unsigned int>(descriptor[5]) & 0xe0U) != 0xe0U) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    try {
        std::vector<std::byte> converted;
        converted.reserve(descriptor.size() + 8U);
        std::size_t offset = 6U;
        const auto sps_count =
            std::to_integer<unsigned int>(descriptor[5]) & 0x1fU;
        if (sps_count == 0) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }
        for (unsigned int index = 0; index < sps_count; ++index) {
            if (!ReadDescriptorNal(
                    descriptor,
                    offset,
                    7U,
                    converted)) {
                return VRREC_STATUS_INVALID_ARGUMENT;
            }
        }
        if (offset >= descriptor.size()) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }
        const auto pps_count =
            std::to_integer<unsigned int>(descriptor[offset++]);
        if (pps_count == 0) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }
        for (unsigned int index = 0; index < pps_count; ++index) {
            if (!ReadDescriptorNal(
                    descriptor,
                    offset,
                    8U,
                    converted)) {
                return VRREC_STATUS_INVALID_ARGUMENT;
            }
        }
        if (offset != descriptor.size()) {
            if (descriptor[1] != std::byte {100} ||
                descriptor.size() - offset < 4U ||
                (std::to_integer<unsigned int>(descriptor[offset]) &
                    0xfcU) != 0xfcU ||
                (std::to_integer<unsigned int>(descriptor[offset + 1U]) &
                    0xf8U) != 0xf8U ||
                (std::to_integer<unsigned int>(descriptor[offset + 2U]) &
                    0xf8U) != 0xf8U) {
                return VRREC_STATUS_INVALID_ARGUMENT;
            }
            const auto sequence_parameter_set_extension_count =
                std::to_integer<unsigned int>(descriptor[offset + 3U]);
            offset += 4U;
            for (unsigned int index = 0;
                 index < sequence_parameter_set_extension_count;
                 ++index) {
                if (!ReadDescriptorNal(
                        descriptor,
                        offset,
                        13U,
                        converted)) {
                    return VRREC_STATUS_INVALID_ARGUMENT;
                }
            }
            if (offset != descriptor.size()) {
                return VRREC_STATUS_INVALID_ARGUMENT;
            }
        }
        annex_b.swap(converted);
        return VRREC_STATUS_OK;
    } catch (const std::bad_alloc &) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

vrrec_status_t ConvertH264AvccAccessUnitToAnnexB(
    std::span<const std::byte> access_unit,
    std::vector<std::byte> &annex_b) noexcept
{
    if (access_unit.empty()) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }
    try {
        std::vector<std::byte> converted;
        converted.reserve(access_unit.size());
        std::size_t offset = 0;
        while (offset < access_unit.size()) {
            if (access_unit.size() - offset < 4U) {
                return VRREC_STATUS_INVALID_ARGUMENT;
            }
            const auto length = static_cast<std::size_t>(
                ReadBigEndian32(access_unit, offset));
            offset += 4U;
            if (length == 0 || length > access_unit.size() - offset ||
                !AppendNal(
                    access_unit.subspan(offset, length),
                    0U,
                    converted)) {
                return VRREC_STATUS_INVALID_ARGUMENT;
            }
            offset += length;
        }
        annex_b.swap(converted);
        return VRREC_STATUS_OK;
    } catch (const std::bad_alloc &) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

}
