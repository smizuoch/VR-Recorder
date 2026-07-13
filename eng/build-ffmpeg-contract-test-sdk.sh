#!/usr/bin/env bash
set -euo pipefail

readonly ffmpeg_version="8.1.2"
readonly source_sha256="464beb5e7bf0c311e68b45ae2f04e9cc2af88851abb4082231742a74d97b524c"
readonly source_url="https://ffmpeg.org/releases/ffmpeg-${ffmpeg_version}.tar.xz"

default_cache_root="${XDG_CACHE_HOME:-${HOME}/.cache}/vr-recorder"
sdk_root="${1:-${default_cache_root}/ffmpeg-${ffmpeg_version}-contract-test}"
if (( $# > 1 )); then
    printf 'Usage: %s [absolute-sdk-output-path]\n' "$0" >&2
    exit 2
fi
if [[ "${sdk_root}" != /* ]]; then
    printf 'SDK output path must be absolute: %s\n' "${sdk_root}" >&2
    exit 2
fi
if [[ "${sdk_root}" == / || "${sdk_root}" == "${HOME}" ]]; then
    printf 'Refusing unsafe SDK output path: %s\n' "${sdk_root}" >&2
    exit 2
fi

work_root="${sdk_root}.work"
archive_path="${work_root}/ffmpeg-${ffmpeg_version}.tar.xz"
source_root="${work_root}/source"
marker_path="${sdk_root}/share/vrrecorder/contract-test-build.txt"
ownership_path="${sdk_root}/share/vrrecorder/contract-test-sdk-owned.txt"
work_ownership_path="${work_root}/contract-test-work-owned.txt"
readonly ownership_token="vr-recorder FFmpeg contract-test workspace v1"
configure_arguments=(
    "--prefix=${sdk_root}"
    --enable-shared
    --disable-static
    --disable-programs
    --disable-doc
    --disable-network
    --disable-autodetect
    --disable-everything
    --disable-avdevice
    --disable-avfilter
    --disable-swscale
    --disable-iconv
    --disable-zlib
    --disable-bzlib
    --disable-lzma
    --disable-debug
    --disable-iamf
    --disable-x86asm
    --enable-avcodec
    --enable-avformat
    --enable-avutil
    --enable-swresample
    --enable-encoder=aac
    --enable-muxer=mp4
    --enable-protocol=file
)
configure_sha256="$({
    printf '%s\n' "${configure_arguments[@]}"
} | sha256sum | cut -d ' ' -f 1)"
expected_marker="source-sha256=${source_sha256}
configure-sha256=${configure_sha256}"

sdk_owned=false
if [[ -f "${ownership_path}" ]] &&
   [[ "$(<"${ownership_path}")" == "${ownership_token}" ]]; then
    sdk_owned=true
fi

if [[ "${sdk_owned}" == true ]] &&
   [[ -f "${marker_path}" ]] &&
   [[ "$(<"${marker_path}")" == "${expected_marker}" ]] &&
   [[ -f "${sdk_root}/lib/libavformat.so.62.12.102" ]] &&
   [[ -f "${sdk_root}/lib/libavcodec.so.62.28.102" ]] &&
   [[ -f "${sdk_root}/lib/libavutil.so.60.26.102" ]] &&
   [[ -f "${sdk_root}/lib/libswresample.so.6.3.102" ]]; then
    printf '%s\n' "${sdk_root}"
    exit 0
fi

if [[ -e "${sdk_root}" && "${sdk_owned}" != true ]]; then
    printf 'Refusing to replace an SDK directory not owned by this script: %s\n' \
        "${sdk_root}" >&2
    exit 2
fi

if [[ -e "${work_root}" ]]; then
    if [[ ! -f "${work_ownership_path}" ]] ||
       [[ "$(<"${work_ownership_path}")" != "${ownership_token}" ]]; then
        printf 'Refusing to use a work directory not owned by this script: %s\n' \
            "${work_root}" >&2
        exit 2
    fi
else
    mkdir -p "${work_root}"
    printf '%s\n' "${ownership_token}" >"${work_ownership_path}"
fi
if [[ ! -f "${archive_path}" ]]; then
    temporary_archive="${archive_path}.download"
    rm -f "${temporary_archive}"
    curl \
        --fail \
        --location \
        --show-error \
        --output "${temporary_archive}" \
        "${source_url}"
    mv "${temporary_archive}" "${archive_path}"
fi
printf '%s  %s\n' "${source_sha256}" "${archive_path}" |
    sha256sum --check --status

rm -rf "${source_root}" "${sdk_root}"
mkdir -p "${source_root}" "${sdk_root}"
mkdir -p "$(dirname "${ownership_path}")"
printf '%s\n' "${ownership_token}" >"${ownership_path}"
tar \
    --extract \
    --xz \
    --file "${archive_path}" \
    --directory "${source_root}" \
    --strip-components=1

(
    cd "${source_root}"
    ./configure "${configure_arguments[@]}"
    build_jobs="${VRRECORDER_BUILD_JOBS:-$(getconf _NPROCESSORS_ONLN 2>/dev/null || printf '2')}"
    make -j"${build_jobs}"
    make install
)

mkdir -p "$(dirname "${marker_path}")"
printf '%s\n' "${expected_marker}" >"${marker_path}"
printf '%s\n' "${sdk_root}"
