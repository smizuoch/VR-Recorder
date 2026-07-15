cmake_minimum_required(VERSION 3.24)

if(NOT DEFINED PINNED_FFMPEG_MODULE OR
   NOT IS_ABSOLUTE "${PINNED_FFMPEG_MODULE}")
    message(FATAL_ERROR "PINNED_FFMPEG_MODULE must be absolute")
endif()

set(work_root "${CMAKE_CURRENT_BINARY_DIR}/windows-ffmpeg-oracle-contract")
set(sdk_root "${work_root}/sdk")
set(runner "${work_root}/validate.cmake")
set(project_root "${work_root}/project")
set(project_build_root "${work_root}/project-build")
file(REMOVE_RECURSE "${work_root}")
file(MAKE_DIRECTORY
    "${sdk_root}/include/libavcodec"
    "${sdk_root}/include/libavformat"
    "${sdk_root}/include/libavutil"
    "${sdk_root}/include/libswresample"
    "${sdk_root}/bin"
    "${sdk_root}/lib"
    "${sdk_root}/share/vrrecorder/sources"
    "${sdk_root}/share/vrrecorder/build-recipes"
    "${project_root}")

foreach(header IN ITEMS
        "libavcodec/avcodec.h"
        "libavformat/avformat.h"
        "libavutil/avutil.h"
        "libswresample/swresample.h")
    file(WRITE "${sdk_root}/include/${header}" "/* fake oracle SDK */\n")
endforeach()
file(WRITE "${sdk_root}/include/libavcodec/version_major.h"
    "#define LIBAVCODEC_VERSION_MAJOR 62\n")
file(WRITE "${sdk_root}/include/libavcodec/version.h"
    "#define LIBAVCODEC_VERSION_MINOR 28\n"
    "#define LIBAVCODEC_VERSION_MICRO 102\n")
file(WRITE "${sdk_root}/include/libavformat/version_major.h"
    "#define LIBAVFORMAT_VERSION_MAJOR 62\n")
file(WRITE "${sdk_root}/include/libavformat/version.h"
    "#define LIBAVFORMAT_VERSION_MINOR 12\n"
    "#define LIBAVFORMAT_VERSION_MICRO 102\n")
file(WRITE "${sdk_root}/include/libavutil/version.h"
    "#define LIBAVUTIL_VERSION_MAJOR 60\n"
    "#define LIBAVUTIL_VERSION_MINOR 26\n"
    "#define LIBAVUTIL_VERSION_MICRO 102\n")
file(WRITE "${sdk_root}/include/libswresample/version_major.h"
    "#define LIBSWRESAMPLE_VERSION_MAJOR 6\n")
file(WRITE "${sdk_root}/include/libswresample/version.h"
    "#define LIBSWRESAMPLE_VERSION_MINOR 3\n"
    "#define LIBSWRESAMPLE_VERSION_MICRO 102\n")

set(artifact_paths
    "bin/avcodec-62.dll"
    "bin/avformat-62.dll"
    "bin/avutil-60.dll"
    "bin/ffprobe.exe"
    "lib/avcodec.lib"
    "lib/avformat.lib"
    "lib/avutil.lib")
foreach(path IN LISTS artifact_paths)
    file(WRITE "${sdk_root}/${path}" "fake oracle artifact ${path}\n")
endforeach()

set(source_relative_path
    "share/vrrecorder/sources/ffmpeg-8.1.2.tar.xz")
set(recipe_relative_path
    "share/vrrecorder/build-recipes/ffmpeg-windows-oracle-x64.md")
file(WRITE "${sdk_root}/${source_relative_path}" "fake oracle source\n")
file(SIZE "${sdk_root}/${source_relative_path}" source_length)
file(SHA256 "${sdk_root}/${source_relative_path}" source_sha256)
get_filename_component(module_directory "${PINNED_FFMPEG_MODULE}" DIRECTORY)
get_filename_component(repository_root "${module_directory}" DIRECTORY)
set(canonical_recipe
    "${repository_root}/eng/ffmpeg-windows-oracle-build-recipe.md")
configure_file(
    "${canonical_recipe}"
    "${sdk_root}/${recipe_relative_path}"
    COPYONLY)
file(SIZE "${sdk_root}/${recipe_relative_path}" recipe_length)
file(SHA256 "${sdk_root}/${recipe_relative_path}" recipe_sha256)

set(artifact_evidence "")
foreach(path IN LISTS artifact_paths)
    file(SIZE "${sdk_root}/${path}" artifact_length)
    file(SHA256 "${sdk_root}/${path}" artifact_sha256)
    string(APPEND artifact_evidence
        "    {\"path\": \"${path}\", \"length\": ${artifact_length}, \"sha256\": \"${artifact_sha256}\"},\n")
endforeach()
string(REGEX REPLACE ",\n$" "\n" artifact_evidence "${artifact_evidence}")

set(evidence_path
    "${sdk_root}/share/vrrecorder/ffmpeg-oracle-build-evidence.json")
file(WRITE "${evidence_path}"
    "{\n"
    "  \"schemaVersion\": 1,\n"
    "  \"evidenceKind\": \"ffmpeg-windows-contract-oracle-sdk\",\n"
    "  \"version\": \"8.1.2\",\n"
    "  \"tag\": \"n8.1.2\",\n"
    "  \"sourceCommit\": \"38b88335f99e76ed89ff3c93f877fdefce736c13\",\n"
    "  \"sourceArchivePath\": \"${source_relative_path}\",\n"
    "  \"sourceArchiveLength\": ${source_length},\n"
    "  \"sourceArchiveSha256\": \"${source_sha256}\",\n"
    "  \"platform\": \"windows-x64\",\n"
    "  \"toolchain\": \"msvc\",\n"
    "  \"msvcCompilerVersion\": \"19.44.35228\",\n"
    "  \"windowsSdkVersion\": \"10.0.26100.0\",\n"
    "  \"linkage\": \"shared\",\n"
    "  \"license\": \"LGPL version 2.1 or later\",\n"
    "  \"gpl\": false,\n"
    "  \"nonfree\": false,\n"
    "  \"configureArguments\": [\n"
    "    \"--prefix=${sdk_root}\",\n"
    "    \"--toolchain=msvc\",\n"
    "    \"--enable-cross-compile\",\n"
    "    \"--host-cc=cl.exe\",\n"
    "    \"--arch=x86_64\",\n"
    "    \"--target-os=win32\",\n"
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
    "    \"--disable-swresample\",\n"
    "    \"--disable-iconv\",\n"
    "    \"--disable-zlib\",\n"
    "    \"--disable-bzlib\",\n"
    "    \"--disable-lzma\",\n"
    "    \"--disable-debug\",\n"
    "    \"--disable-iamf\",\n"
    "    \"--disable-x86asm\",\n"
    "    \"--enable-avcodec\",\n"
    "    \"--enable-avformat\",\n"
    "    \"--enable-avutil\",\n"
    "    \"--enable-decoder=aac\",\n"
    "    \"--enable-decoder=h264\",\n"
    "    \"--enable-demuxer=mov\",\n"
    "    \"--enable-protocol=file\",\n"
    "    \"--enable-ffprobe\"\n"
    "  ],\n"
    "  \"enabledLibraries\": [\"avcodec\", \"avformat\", \"avutil\"],\n"
    "  \"enabledDecoders\": [\"aac\", \"h264\"],\n"
    "  \"enabledEncoders\": [],\n"
    "  \"enabledDemuxers\": [\"mov\"],\n"
    "  \"enabledMuxers\": [],\n"
    "  \"enabledProtocols\": [\"file\"],\n"
    "  \"enabledPrograms\": [\"ffprobe\"],\n"
    "  \"buildRecipePath\": \"${recipe_relative_path}\",\n"
    "  \"buildRecipeLength\": ${recipe_length},\n"
    "  \"buildRecipeSha256\": \"${recipe_sha256}\",\n"
    "  \"artifacts\": [\n${artifact_evidence}  ]\n"
    "}\n")
file(READ "${evidence_path}" valid_evidence)

file(WRITE "${runner}"
    "cmake_minimum_required(VERSION 3.24)\n"
    "include(\"${PINNED_FFMPEG_MODULE}\")\n"
    "if(NOT VRRECORDER_FFMPEG_WINDOWS_ORACLE_BUILD_RECIPE_SHA256 STREQUAL \"bb2f193738396b8d16d1d9dc0543b36584bd7915a5d9d994d82d91f789093981\")\n"
    "  message(FATAL_ERROR \"Oracle recipe pin drifted\")\n"
    "endif()\n"
    "set(VRRECORDER_FFMPEG_SOURCE_ARCHIVE_SHA256 \"${source_sha256}\")\n"
    "vrrecorder_validate_windows_ffmpeg_contract_oracle_sdk(\"\${SDK_ROOT}\")\n")

function(run_validation expected_success label)
    execute_process(
        COMMAND "${CMAKE_COMMAND}" "-DSDK_ROOT=${sdk_root}" -P "${runner}"
        RESULT_VARIABLE result
        OUTPUT_VARIABLE output
        ERROR_VARIABLE error)
    if(expected_success AND NOT result EQUAL 0)
        message(FATAL_ERROR
            "${label} should pass (${result}):\n${output}\n${error}")
    endif()
    if(NOT expected_success AND result EQUAL 0)
        message(FATAL_ERROR "${label} should fail closed, but passed")
    endif()
endfunction()

run_validation(TRUE "exact Windows oracle SDK")

file(APPEND "${sdk_root}/bin/ffprobe.exe" "tampered\n")
run_validation(FALSE "tampered ffprobe")
file(WRITE "${sdk_root}/bin/ffprobe.exe"
    "fake oracle artifact bin/ffprobe.exe\n")

string(REPLACE
    "[\"aac\", \"h264\"]"
    "[\"aac\"]"
    wrong_decoders "${valid_evidence}")
file(WRITE "${evidence_path}" "${wrong_decoders}")
run_validation(FALSE "missing H264 decoder evidence")
file(WRITE "${evidence_path}" "${valid_evidence}")

file(REMOVE "${sdk_root}/${recipe_relative_path}")
run_validation(FALSE "missing oracle build recipe")
configure_file(
    "${canonical_recipe}"
    "${sdk_root}/${recipe_relative_path}"
    COPYONLY)

file(WRITE "${project_root}/CMakeLists.txt"
    "cmake_minimum_required(VERSION 3.24)\n"
    "project(WindowsOracleImport LANGUAGES NONE)\n"
    "include(\"${PINNED_FFMPEG_MODULE}\")\n"
    "set(VRRECORDER_FFMPEG_SOURCE_ARCHIVE_SHA256 \"${source_sha256}\")\n"
    "vrrecorder_import_ffmpeg_contract_oracle_sdk(\"\${SDK_ROOT}\")\n"
    "foreach(component IN ITEMS avformat avcodec avutil ffprobe)\n"
    "  if(NOT TARGET \"FFmpegContractOracle::\${component}\")\n"
    "    message(FATAL_ERROR \"Missing oracle target \${component}\")\n"
    "  endif()\n"
    "endforeach()\n"
    "get_target_property(location FFmpegContractOracle::avformat IMPORTED_LOCATION)\n"
    "if(NOT location STREQUAL \"\${SDK_ROOT}/bin/avformat-62.dll\")\n"
    "  message(FATAL_ERROR \"Wrong oracle runtime: \${location}\")\n"
    "endif()\n"
    "get_target_property(ffprobe FFmpegContractOracle::ffprobe IMPORTED_LOCATION)\n"
    "if(NOT ffprobe STREQUAL \"\${SDK_ROOT}/bin/ffprobe.exe\")\n"
    "  message(FATAL_ERROR \"Wrong ffprobe: \${ffprobe}\")\n"
    "endif()\n")
execute_process(
    COMMAND "${CMAKE_COMMAND}"
        -S "${project_root}"
        -B "${project_build_root}"
        -DCMAKE_SYSTEM_NAME=Windows
        "-DSDK_ROOT=${sdk_root}"
    RESULT_VARIABLE import_result
    OUTPUT_VARIABLE import_output
    ERROR_VARIABLE import_error)
if(NOT import_result EQUAL 0)
    message(FATAL_ERROR
        "Windows oracle targets should import (${import_result}):\n"
        "${import_output}\n${import_error}")
endif()

message(STATUS "Windows FFmpeg oracle SDK contract passed")
