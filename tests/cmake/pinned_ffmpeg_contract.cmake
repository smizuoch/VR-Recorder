cmake_minimum_required(VERSION 3.24)

if(NOT DEFINED PINNED_FFMPEG_MODULE)
    message(FATAL_ERROR "PINNED_FFMPEG_MODULE is required")
endif()

set(work_root "${CMAKE_CURRENT_BINARY_DIR}/pinned-ffmpeg-contract")
set(sdk_root "${work_root}/sdk")
set(runner_path "${work_root}/validate.cmake")
set(project_root "${work_root}/project")
set(project_build_root "${work_root}/project-build")
file(REMOVE_RECURSE "${work_root}")
file(MAKE_DIRECTORY
    "${sdk_root}/include/libavcodec"
    "${sdk_root}/include/libavformat"
    "${sdk_root}/include/libavutil"
    "${sdk_root}/include/libswresample"
    "${sdk_root}/lib"
    "${sdk_root}/bin"
    "${sdk_root}/share/vrrecorder"
    "${project_root}")

foreach(header IN ITEMS
        "libavcodec/avcodec.h"
        "libavformat/avformat.h"
        "libavutil/avutil.h"
        "libswresample/swresample.h")
    file(WRITE "${sdk_root}/include/${header}" "/* exact fake SDK */\n")
endforeach()

file(
    WRITE "${sdk_root}/include/libavcodec/version_major.h"
    "#define LIBAVCODEC_VERSION_MAJOR 62\n")
file(
    WRITE "${sdk_root}/include/libavcodec/version.h"
    "#define LIBAVCODEC_VERSION_MINOR 28\n"
    "#define LIBAVCODEC_VERSION_MICRO 102\n")
file(
    WRITE "${sdk_root}/include/libavformat/version_major.h"
    "#define LIBAVFORMAT_VERSION_MAJOR 62\n")
file(
    WRITE "${sdk_root}/include/libavformat/version.h"
    "#define LIBAVFORMAT_VERSION_MINOR 12\n"
    "#define LIBAVFORMAT_VERSION_MICRO 102\n")
file(
    WRITE "${sdk_root}/include/libavutil/version.h"
    "#define LIBAVUTIL_VERSION_MAJOR 60\n"
    "#define LIBAVUTIL_VERSION_MINOR 26\n"
    "#define LIBAVUTIL_VERSION_MICRO 102\n")
file(
    WRITE "${sdk_root}/include/libswresample/version.h"
    "#define LIBSWRESAMPLE_VERSION_MAJOR 6\n"
    "#define LIBSWRESAMPLE_VERSION_MINOR 3\n"
    "#define LIBSWRESAMPLE_VERSION_MICRO 102\n")

foreach(component IN ITEMS avcodec avformat avutil swresample)
    file(WRITE "${sdk_root}/lib/${component}.lib" "fake import library\n")
endforeach()
foreach(runtime IN ITEMS
        "avcodec-62.dll"
        "avformat-62.dll"
        "avutil-60.dll"
        "swresample-6.dll")
    file(WRITE "${sdk_root}/bin/${runtime}" "fake runtime DLL\n")
endforeach()

set(evidence_path
    "${sdk_root}/share/vrrecorder/ffmpeg-build-evidence.json")
file(
    WRITE "${evidence_path}"
    "{\n"
    "  \"schemaVersion\": 1,\n"
    "  \"version\": \"8.1.2\",\n"
    "  \"tag\": \"n8.1.2\",\n"
    "  \"sourceCommit\": \"38b88335f99e76ed89ff3c93f877fdefce736c13\",\n"
    "  \"sourceArchiveSha256\": \"464beb5e7bf0c311e68b45ae2f04e9cc2af88851abb4082231742a74d97b524c\",\n"
    "  \"platform\": \"windows-x64\",\n"
    "  \"toolchain\": \"msvc\",\n"
    "  \"linkage\": \"shared\",\n"
    "  \"license\": \"LGPL version 2.1 or later\",\n"
    "  \"gpl\": false,\n"
    "  \"nonfree\": false,\n"
    "  \"configureArguments\": [\n"
    "    \"--prefix=${sdk_root}\",\n"
    "    \"--toolchain=msvc\",\n"
    "    \"--arch=x86_64\",\n"
    "    \"--target-os=win64\",\n"
    "    \"--enable-shared\",\n"
    "    \"--disable-static\",\n"
    "    \"--disable-programs\",\n"
    "    \"--disable-doc\",\n"
    "    \"--disable-network\",\n"
    "    \"--disable-autodetect\",\n"
    "    \"--disable-everything\",\n"
    "    \"--disable-avdevice\",\n"
    "    \"--disable-avfilter\",\n"
    "    \"--disable-swscale\",\n"
    "    \"--disable-iconv\",\n"
    "    \"--disable-zlib\",\n"
    "    \"--disable-bzlib\",\n"
    "    \"--disable-lzma\",\n"
    "    \"--disable-debug\",\n"
    "    \"--enable-avcodec\",\n"
    "    \"--enable-avformat\",\n"
    "    \"--enable-avutil\",\n"
    "    \"--enable-swresample\",\n"
    "    \"--enable-mediafoundation\",\n"
    "    \"--enable-encoder=aac\",\n"
    "    \"--enable-encoder=h264_mf\",\n"
    "    \"--enable-muxer=mp4\",\n"
    "    \"--enable-protocol=file\"\n"
    "  ],\n"
    "  \"enabledLibraries\": [\"avcodec\", \"avformat\", \"avutil\", \"swresample\"],\n"
    "  \"enabledEncoders\": [\"aac\", \"h264_mf\"],\n"
    "  \"enabledMuxers\": [\"mov\", \"mp4\"],\n"
    "  \"enabledProtocols\": [\"file\"],\n"
    "  \"enabledExternalLibraries\": [\"mediafoundation\"]\n"
    "}\n")
file(READ "${evidence_path}" valid_evidence)

file(
    WRITE "${runner_path}"
    "cmake_minimum_required(VERSION 3.24)\n"
    "include(\"${PINNED_FFMPEG_MODULE}\")\n"
    "vrrecorder_validate_pinned_ffmpeg_sdk(\"\${SDK_ROOT}\")\n")

function(run_validation expected_success label)
    execute_process(
        COMMAND
            "${CMAKE_COMMAND}"
            "-DSDK_ROOT=${sdk_root}"
            -P "${runner_path}"
        RESULT_VARIABLE result
        OUTPUT_VARIABLE output
        ERROR_VARIABLE error)
    if(expected_success AND NOT result EQUAL 0)
        message(
            FATAL_ERROR
            "${label} should pass, but failed (${result}):\n${output}\n${error}")
    endif()
    if(NOT expected_success AND result EQUAL 0)
        message(FATAL_ERROR "${label} should fail closed, but passed")
    endif()
endfunction()

run_validation(TRUE "exact pinned SDK")

file(REMOVE "${sdk_root}/bin/avcodec-62.dll")
run_validation(FALSE "missing runtime DLL")
file(WRITE "${sdk_root}/bin/avcodec-62.dll" "fake runtime DLL\n")

file(
    WRITE "${sdk_root}/include/libavcodec/version_major.h"
    "#define LIBAVCODEC_VERSION_MAJOR 61\n")
run_validation(FALSE "wrong libavcodec major")
file(
    WRITE "${sdk_root}/include/libavcodec/version_major.h"
    "#define LIBAVCODEC_VERSION_MAJOR 62\n")

string(
    REPLACE "\"--disable-debug\"" "\"--enable-gpl\""
    forbidden_evidence "${valid_evidence}")
file(WRITE "${evidence_path}" "${forbidden_evidence}")
run_validation(FALSE "GPL configure flag")
file(WRITE "${evidence_path}" "${valid_evidence}")

string(
    REPLACE
        "[\"mediafoundation\"]"
        "[\"libx264\", \"mediafoundation\"]"
    external_library_evidence "${valid_evidence}")
file(WRITE "${evidence_path}" "${external_library_evidence}")
run_validation(FALSE "unknown external library")
file(WRITE "${evidence_path}" "${valid_evidence}")

file(
    WRITE "${project_root}/CMakeLists.txt"
    "cmake_minimum_required(VERSION 3.24)\n"
    "project(PinnedFFmpegImport LANGUAGES NONE)\n"
    "include(\"${PINNED_FFMPEG_MODULE}\")\n"
    "vrrecorder_import_pinned_ffmpeg_sdk(\"\${SDK_ROOT}\")\n"
    "foreach(component IN ITEMS avcodec avformat avutil swresample)\n"
    "  if(NOT TARGET \"FFmpeg::\${component}\")\n"
    "    message(FATAL_ERROR \"Missing imported target FFmpeg::\${component}\")\n"
    "  endif()\n"
    "endforeach()\n"
    "get_target_property(location FFmpeg::avcodec IMPORTED_LOCATION)\n"
    "if(NOT location STREQUAL \"\${SDK_ROOT}/bin/avcodec-62.dll\")\n"
    "  message(FATAL_ERROR \"Unexpected imported runtime: \${location}\")\n"
    "endif()\n")
execute_process(
    COMMAND
        "${CMAKE_COMMAND}"
        -S "${project_root}"
        -B "${project_build_root}"
        "-DSDK_ROOT=${sdk_root}"
    RESULT_VARIABLE import_result
    OUTPUT_VARIABLE import_output
    ERROR_VARIABLE import_error)
if(NOT import_result EQUAL 0)
    message(
        FATAL_ERROR
        "Pinned imported targets should configure (${import_result}):\n"
        "${import_output}\n${import_error}")
endif()

message(STATUS "Pinned FFmpeg SDK contract passed")
