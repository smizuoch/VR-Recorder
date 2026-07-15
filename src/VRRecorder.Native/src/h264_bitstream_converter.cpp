#include "h264_bitstream_converter.hpp"
#include "h264_sps_parser.hpp"

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <new>
#include <span>
#include <vector>

namespace vrrecorder::native {

namespace {

constexpr std::uint8_t HighProfileIdc = 100;
constexpr std::uint8_t MainProfileIdc = 77;

struct NalView final {
    std::span<const std::byte> bytes;
    unsigned int type = 0;
};

struct SequenceParameterSetEntry final {
    const NalView *nal = nullptr;
    H264SpsInfo info {};
};

struct PictureParameterSetEntry final {
    const NalView *nal = nullptr;
    H264PpsInfo info {};
};

using SequenceParameterSets = std::array<SequenceParameterSetEntry, 32>;
using PictureParameterSets = std::array<PictureParameterSetEntry, 256>;

bool IsStartCodeAt(std::span<const std::byte> bytes, std::size_t offset, std::size_t &length) noexcept
{
    if (offset + 3U <= bytes.size() &&
        bytes[offset] == std::byte {0} &&
        bytes[offset + 1U] == std::byte {0} &&
        bytes[offset + 2U] == std::byte {1}) {
        length = 3U;
        return true;
    }
    if (offset + 4U <= bytes.size() &&
        bytes[offset] == std::byte {0} &&
        bytes[offset + 1U] == std::byte {0} &&
        bytes[offset + 2U] == std::byte {0} &&
        bytes[offset + 3U] == std::byte {1}) {
        length = 4U;
        return true;
    }
    return false;
}

bool ParseAnnexB(std::span<const std::byte> bytes, std::vector<NalView> &nals)
{
    nals.clear();
    std::size_t cursor = 0;
    while (cursor < bytes.size()) {
        std::size_t start_code_length = 0;
        if (!IsStartCodeAt(bytes, cursor, start_code_length)) {
            return false;
        }
        const auto nal_start = cursor + start_code_length;
        if (nal_start >= bytes.size()) {
            return false;
        }
        std::size_t nal_end = nal_start;
        while (nal_end < bytes.size()) {
            std::size_t ignored = 0;
            if (IsStartCodeAt(bytes, nal_end, ignored)) {
                break;
            }
            ++nal_end;
        }
        if (nal_end == nal_start) {
            return false;
        }
        const auto header = std::to_integer<unsigned int>(bytes[nal_start]);
        if ((header & 0x80U) != 0U) {
            return false;
        }
        nals.push_back({bytes.subspan(nal_start, nal_end - nal_start), header & 0x1fU});
        cursor = nal_end;
    }
    return !nals.empty();
}

bool IsSupportedVcl(unsigned int type) noexcept
{
    return type >= 1U && type <= 5U;
}

bool MatchesExpectedSps(
    const H264SpsInfo &sps,
    std::uint32_t expected_width,
    std::uint32_t expected_height,
    H264Profile expected_profile) noexcept
{
    const auto expected_profile_idc = expected_profile == H264Profile::High
        ? HighProfileIdc
        : MainProfileIdc;
    return sps.profile_idc == expected_profile_idc &&
           sps.width == expected_width && sps.height == expected_height &&
           sps.chroma_format_idc == 1U && sps.bit_depth_luma == 8U &&
           sps.bit_depth_chroma == 8U;
}

void AppendBigEndian16(std::vector<std::byte> &bytes, std::size_t value)
{
    bytes.push_back(static_cast<std::byte>((value >> 8U) & 0xffU));
    bytes.push_back(static_cast<std::byte>(value & 0xffU));
}

void AppendBigEndian32(std::vector<std::byte> &bytes, std::size_t value)
{
    bytes.push_back(static_cast<std::byte>((value >> 24U) & 0xffU));
    bytes.push_back(static_cast<std::byte>((value >> 16U) & 0xffU));
    bytes.push_back(static_cast<std::byte>((value >> 8U) & 0xffU));
    bytes.push_back(static_cast<std::byte>(value & 0xffU));
}

vrrec_status_t CollectParameterSets(
    std::span<const NalView> nals,
    std::uint32_t expected_width,
    std::uint32_t expected_height,
    H264Profile expected_profile,
    SequenceParameterSets &sequence_parameter_sets,
    std::size_t &sequence_parameter_set_count,
    PictureParameterSets &picture_parameter_sets,
    std::size_t &picture_parameter_set_count) noexcept
{
    sequence_parameter_sets = {};
    picture_parameter_sets = {};
    sequence_parameter_set_count = 0;
    picture_parameter_set_count = 0;
    for (const auto &nal : nals) {
        if (nal.type == 7U) {
            H264SpsInfo info {};
            const auto parse_status = ParseH264Sps(nal.bytes, info);
            if (parse_status != VRREC_STATUS_OK) {
                return parse_status;
            }
            if (!MatchesExpectedSps(
                    info,
                    expected_width,
                    expected_height,
                    expected_profile)) {
                return VRREC_STATUS_INVALID_ARGUMENT;
            }
            auto &entry =
                sequence_parameter_sets[info.sequence_parameter_set_id];
            if (entry.nal != nullptr) {
                if (!std::ranges::equal(nal.bytes, entry.nal->bytes)) {
                    return VRREC_STATUS_INVALID_ARGUMENT;
                }
                continue;
            }
            entry = {&nal, info};
            ++sequence_parameter_set_count;
        } else if (nal.type == 8U) {
            H264PpsInfo info {};
            const auto parse_status = ParseH264Pps(nal.bytes, info);
            if (parse_status != VRREC_STATUS_OK) {
                return parse_status;
            }
            auto &entry =
                picture_parameter_sets[info.picture_parameter_set_id];
            if (entry.nal != nullptr) {
                if (!std::ranges::equal(nal.bytes, entry.nal->bytes)) {
                    return VRREC_STATUS_INVALID_ARGUMENT;
                }
                continue;
            }
            entry = {&nal, info};
            ++picture_parameter_set_count;
        }
    }

    if ((sequence_parameter_set_count == 0U) !=
        (picture_parameter_set_count == 0U)) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }
    for (const auto &entry : picture_parameter_sets) {
        if (entry.nal != nullptr &&
            sequence_parameter_sets[entry.info.sequence_parameter_set_id].nal ==
                nullptr) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }
    }
    if (sequence_parameter_set_count > 31U ||
        picture_parameter_set_count > 255U) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }
    return VRREC_STATUS_OK;
}

vrrec_status_t BuildAvcc(
    const SequenceParameterSets &sequence_parameter_sets,
    std::size_t sequence_parameter_set_count,
    const PictureParameterSets &picture_parameter_sets,
    std::size_t picture_parameter_set_count,
    std::vector<std::byte> &avcc)
{
    if (sequence_parameter_set_count == 0U ||
        sequence_parameter_set_count > 31U ||
        picture_parameter_set_count == 0U ||
        picture_parameter_set_count > 255U) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }
    const SequenceParameterSetEntry *primary_sps = nullptr;
    std::size_t parameter_set_bytes = 0;
    for (const auto &entry : sequence_parameter_sets) {
        if (entry.nal == nullptr) {
            continue;
        }
        if (entry.nal->bytes.size() >
            std::numeric_limits<std::uint16_t>::max()) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }
        if (primary_sps == nullptr) {
            primary_sps = &entry;
        }
        parameter_set_bytes += 2U + entry.nal->bytes.size();
    }
    for (const auto &entry : picture_parameter_sets) {
        if (entry.nal == nullptr) {
            continue;
        }
        if (entry.nal->bytes.size() >
            std::numeric_limits<std::uint16_t>::max()) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }
        parameter_set_bytes += 2U + entry.nal->bytes.size();
    }
    if (primary_sps == nullptr) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    const auto &sps_info = primary_sps->info;
    try {
        avcc.clear();
        avcc.reserve(7U + parameter_set_bytes + 4U);
        avcc.push_back(std::byte {0x01});
        avcc.push_back(static_cast<std::byte>(sps_info.profile_idc));
        avcc.push_back(
            static_cast<std::byte>(sps_info.profile_compatibility));
        avcc.push_back(static_cast<std::byte>(sps_info.level_idc));
        avcc.push_back(std::byte {0xff});
        avcc.push_back(static_cast<std::byte>(
            0xe0U | sequence_parameter_set_count));
        for (const auto &entry : sequence_parameter_sets) {
            if (entry.nal == nullptr) {
                continue;
            }
            AppendBigEndian16(avcc, entry.nal->bytes.size());
            avcc.insert(
                avcc.end(), entry.nal->bytes.begin(), entry.nal->bytes.end());
        }
        avcc.push_back(
            static_cast<std::byte>(picture_parameter_set_count));
        for (const auto &entry : picture_parameter_sets) {
            if (entry.nal == nullptr) {
                continue;
            }
            AppendBigEndian16(avcc, entry.nal->bytes.size());
            avcc.insert(
                avcc.end(), entry.nal->bytes.begin(), entry.nal->bytes.end());
        }
        if (sps_info.profile_idc == HighProfileIdc) {
            avcc.push_back(static_cast<std::byte>(
                0xfcU | sps_info.chroma_format_idc));
            avcc.push_back(static_cast<std::byte>(
                0xf8U | (sps_info.bit_depth_luma - 8U)));
            avcc.push_back(static_cast<std::byte>(
                0xf8U | (sps_info.bit_depth_chroma - 8U)));
            avcc.push_back(std::byte {0x00});
        }
        return VRREC_STATUS_OK;
    } catch (const std::bad_alloc &) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

vrrec_status_t BuildAccessUnit(
    std::span<const NalView> nals,
    std::vector<std::byte> &access_unit,
    bool &key_frame)
{
    try {
        access_unit.clear();
        key_frame = false;
        for (const auto &nal : nals) {
            if (!IsSupportedVcl(nal.type)) {
                continue;
            }
            if (nal.bytes.empty() || nal.bytes.size() > 0xffffffffULL) {
                return VRREC_STATUS_INVALID_ARGUMENT;
            }
            key_frame = key_frame || nal.type == 5U;
            AppendBigEndian32(access_unit, nal.bytes.size());
            access_unit.insert(access_unit.end(), nal.bytes.begin(), nal.bytes.end());
        }
        return access_unit.empty()
            ? VRREC_STATUS_INVALID_ARGUMENT
            : VRREC_STATUS_OK;
    } catch (const std::bad_alloc &) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
}

} // namespace

vrrec_status_t ConvertH264AnnexBParameterSetsToAvcc(
    std::span<const std::byte> annex_b_parameter_sets,
    std::uint32_t expected_width,
    std::uint32_t expected_height,
    H264Profile expected_profile,
    std::vector<std::byte> &avcc) noexcept
{
    std::vector<NalView> nals;
    try {
        if (!ParseAnnexB(annex_b_parameter_sets, nals)) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }
    } catch (const std::bad_alloc &) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }
    if (std::ranges::any_of(nals, [](const NalView &nal) {
            return nal.type != 7U && nal.type != 8U;
        })) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    SequenceParameterSets sequence_parameter_sets {};
    PictureParameterSets picture_parameter_sets {};
    std::size_t sequence_parameter_set_count = 0;
    std::size_t picture_parameter_set_count = 0;
    const auto collect_status = CollectParameterSets(
        nals,
        expected_width,
        expected_height,
        expected_profile,
        sequence_parameter_sets,
        sequence_parameter_set_count,
        picture_parameter_sets,
        picture_parameter_set_count);
    if (collect_status != VRREC_STATUS_OK) {
        return collect_status;
    }
    std::vector<std::byte> built_avcc;
    const auto build_status = BuildAvcc(
        sequence_parameter_sets,
        sequence_parameter_set_count,
        picture_parameter_sets,
        picture_parameter_set_count,
        built_avcc);
    if (build_status != VRREC_STATUS_OK) {
        return build_status;
    }
    avcc.swap(built_avcc);
    return VRREC_STATUS_OK;
}

vrrec_status_t ConvertH264AnnexBToAvcc(
    std::span<const std::byte> annex_b_access_unit,
    std::uint32_t expected_width,
    std::uint32_t expected_height,
    H264Profile expected_profile,
    H264AnnexBConversionResult &result) noexcept
{
    return ConvertH264AnnexBToAvcc(
        annex_b_access_unit,
        expected_width,
        expected_height,
        expected_profile,
        false,
        result);
}

vrrec_status_t ConvertH264AnnexBToAvcc(
    std::span<const std::byte> annex_b_access_unit,
    std::uint32_t expected_width,
    std::uint32_t expected_height,
    H264Profile expected_profile,
    bool parameter_sets_already_known,
    H264AnnexBConversionResult &result) noexcept
{
    result = H264AnnexBConversionResult {};
    std::vector<NalView> nals;
    try {
        if (!ParseAnnexB(annex_b_access_unit, nals)) {
            return VRREC_STATUS_INVALID_ARGUMENT;
        }
    } catch (const std::bad_alloc &) {
        return VRREC_STATUS_OUT_OF_MEMORY;
    } catch (...) {
        return VRREC_STATUS_INTERNAL_ERROR;
    }

    SequenceParameterSets sequence_parameter_sets {};
    PictureParameterSets picture_parameter_sets {};
    std::size_t sequence_parameter_set_count = 0;
    std::size_t picture_parameter_set_count = 0;
    const auto collect_status = CollectParameterSets(
        nals,
        expected_width,
        expected_height,
        expected_profile,
        sequence_parameter_sets,
        sequence_parameter_set_count,
        picture_parameter_sets,
        picture_parameter_set_count);
    if (collect_status != VRREC_STATUS_OK) {
        return collect_status;
    }

    const bool contains_idr = [&] {
        for (const auto &nal : nals) {
            if (nal.type == 5U) {
                return true;
            }
        }
        return false;
    }();
    if (contains_idr && sequence_parameter_set_count == 0U &&
        !parameter_sets_already_known) {
        return VRREC_STATUS_INVALID_ARGUMENT;
    }

    if (sequence_parameter_set_count != 0U) {
        const auto avcc_status = BuildAvcc(
            sequence_parameter_sets,
            sequence_parameter_set_count,
            picture_parameter_sets,
            picture_parameter_set_count,
            result.avcc);
        if (avcc_status != VRREC_STATUS_OK) {
            return avcc_status;
        }
    }

    bool key_frame = false;
    const auto access_status = BuildAccessUnit(nals, result.access_unit, key_frame);
    if (access_status != VRREC_STATUS_OK) {
        return access_status;
    }
    result.profile = expected_profile;
    result.width = expected_width;
    result.height = expected_height;
    result.key_frame = key_frame;
    return VRREC_STATUS_OK;
}

}
