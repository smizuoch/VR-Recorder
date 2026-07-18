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
    WRITE "${sdk_root}/include/libswresample/version_major.h"
    "#define LIBSWRESAMPLE_VERSION_MAJOR 6\n")
file(
    WRITE "${sdk_root}/include/libswresample/version.h"
    "#define LIBSWRESAMPLE_VERSION_MINOR 3\n"
    "#define LIBSWRESAMPLE_VERSION_MICRO 102\n")

foreach(component IN ITEMS avcodec avformat avutil swresample)
    file(WRITE "${sdk_root}/lib/${component}.lib" "fake import library\n")
endforeach()
file(
    WRITE
    "${sdk_root}/lib/libavformat.so.62.12.102"
    "fake contract-test shared library\n")
file(
    WRITE
    "${sdk_root}/lib/libavcodec.so.62.28.102"
    "fake contract-test shared library\n")
file(
    WRITE
    "${sdk_root}/lib/libavutil.so.60.26.102"
    "fake contract-test shared library\n")
file(
    WRITE
    "${sdk_root}/lib/libswresample.so.6.3.102"
    "fake contract-test shared library\n")
foreach(runtime IN ITEMS
        "avcodec-62.dll"
        "avformat-62.dll"
        "avutil-60.dll"
        "swresample-6.dll"
        "libvpl.dll")
    file(WRITE "${sdk_root}/bin/${runtime}" "fake runtime DLL\n")
endforeach()

set(source_archive_relative_path
    "share/vrrecorder/sources/ffmpeg-8.1.2.tar.xz")
set(source_patch_relative_path
    "share/vrrecorder/patches/ffmpeg-8.1.2/0001-configure-redo-enabling-cbs-in-lavf.patch")
set(build_recipe_relative_path
    "share/vrrecorder/build-recipes/ffmpeg-windows-x64.md")
get_filename_component(module_directory "${PINNED_FFMPEG_MODULE}" DIRECTORY)
get_filename_component(repository_root "${module_directory}" DIRECTORY)
set(canonical_patch_path
    "${repository_root}/eng/patches/ffmpeg-8.1.2/0001-configure-redo-enabling-cbs-in-lavf.patch")
set(canonical_recipe_path
    "${repository_root}/eng/ffmpeg-windows-production-build-recipe.md")
if(NOT EXISTS "${canonical_patch_path}" OR IS_DIRECTORY "${canonical_patch_path}")
    message(FATAL_ERROR "Canonical FFmpeg source patch is missing")
endif()
if(NOT EXISTS "${canonical_recipe_path}" OR IS_DIRECTORY "${canonical_recipe_path}")
    message(FATAL_ERROR "Canonical FFmpeg build recipe is missing")
endif()
get_filename_component(
    source_archive_parent
    "${sdk_root}/${source_archive_relative_path}"
    DIRECTORY)
get_filename_component(
    source_patch_parent
    "${sdk_root}/${source_patch_relative_path}"
    DIRECTORY)
get_filename_component(
    build_recipe_parent
    "${sdk_root}/${build_recipe_relative_path}"
    DIRECTORY)
file(MAKE_DIRECTORY
    "${source_archive_parent}"
    "${source_patch_parent}"
    "${build_recipe_parent}")
file(
    WRITE "${sdk_root}/${source_archive_relative_path}"
    "fake pinned source archive\n")
configure_file(
    "${canonical_recipe_path}"
    "${sdk_root}/${build_recipe_relative_path}"
    COPYONLY)
configure_file(
    "${canonical_patch_path}"
    "${sdk_root}/${source_patch_relative_path}"
    COPYONLY)

file(SIZE "${sdk_root}/${source_archive_relative_path}" source_archive_length)
file(
    SHA256
    "${sdk_root}/${source_archive_relative_path}"
    source_archive_sha256)
file(SIZE "${sdk_root}/${source_patch_relative_path}" source_patch_length)
file(
    SHA256
    "${sdk_root}/${source_patch_relative_path}"
    source_patch_sha256)
file(SIZE "${sdk_root}/${build_recipe_relative_path}" build_recipe_length)
file(
    SHA256
    "${sdk_root}/${build_recipe_relative_path}"
    build_recipe_sha256)

set(artifact_paths
    "bin/avcodec-62.dll"
    "bin/avformat-62.dll"
    "bin/avutil-60.dll"
    "bin/swresample-6.dll"
    "bin/libvpl.dll"
    "lib/avcodec.lib"
    "lib/avformat.lib"
    "lib/avutil.lib"
    "lib/swresample.lib")
set(artifact_evidence "")
foreach(path IN LISTS artifact_paths)
    file(SIZE "${sdk_root}/${path}" artifact_length)
    file(SHA256 "${sdk_root}/${path}" artifact_sha256)
    string(
        APPEND artifact_evidence
        "    {\"path\": \"${path}\", \"length\": ${artifact_length}, \"sha256\": \"${artifact_sha256}\"},\n")
endforeach()
string(REGEX REPLACE ",\n$" "\n" artifact_evidence "${artifact_evidence}")

set(evidence_path
    "${sdk_root}/share/vrrecorder/ffmpeg-build-evidence.json")
file(
    WRITE "${evidence_path}"
    "{\n"
    "  \"schemaVersion\": 3,\n"
    "  \"version\": \"8.1.2\",\n"
    "  \"tag\": \"n8.1.2\",\n"
    "  \"sourceCommit\": \"38b88335f99e76ed89ff3c93f877fdefce736c13\",\n"
    "  \"sourceArchivePath\": \"${source_archive_relative_path}\",\n"
    "  \"sourceArchiveLength\": ${source_archive_length},\n"
    "  \"sourceArchiveSha256\": \"${source_archive_sha256}\",\n"
    "  \"sourcePatchPath\": \"${source_patch_relative_path}\",\n"
    "  \"sourcePatchLength\": ${source_patch_length},\n"
    "  \"sourcePatchSha256\": \"${source_patch_sha256}\",\n"
    "  \"sourcePatchUpstreamCommit\": \"cec19d7ddf725896dfbf79a4c308550d83eab5ec\",\n"
    "  \"sourcePatchUpstreamUrl\": \"https://code.ffmpeg.org/FFmpeg/FFmpeg/pulls/23039\",\n"
    "  \"platform\": \"windows-x64\",\n"
    "  \"toolchain\": \"msvc\",\n"
    "  \"msvcCompilerVersion\": \"19.44.35228\",\n"
    "  \"windowsSdkVersion\": \"10.0.26100.0\",\n"
    "  \"linkage\": \"shared\",\n"
    "  \"license\": \"LGPL version 2.1 or later\",\n"
    "  \"gpl\": false,\n"
    "  \"nonfree\": false,\n"
    "  \"vendorDependencies\": [\n"
    "    {\"name\": \"nv-codec-headers\", \"version\": \"n13.0.19.0\", \"commit\": \"e844e5b26f46bb77479f063029595293aa8f812d\"},\n"
    "    {\"name\": \"AMF\", \"version\": \"v1.5.2\", \"commit\": \"eadd00804d5f7e5cd8c85d540073198312870776\"},\n"
    "    {\"name\": \"libvpl\", \"version\": \"v2.17.0\", \"commit\": \"d77f9195cf495b937631607333288fd917ae8939\"}\n"
    "  ],\n"
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
    "    \"--enable-swresample\",\n"
    "    \"--enable-d3d11va\",\n"
    "    \"--enable-mediafoundation\",\n"
    "    \"--enable-ffnvcodec\",\n"
    "    \"--enable-nvenc\",\n"
    "    \"--enable-amf\",\n"
    "    \"--enable-libvpl\",\n"
    "    \"--enable-encoder=aac\",\n"
    "    \"--enable-encoder=h264_mf\",\n"
    "    \"--enable-encoder=h264_nvenc\",\n"
    "    \"--enable-encoder=h264_amf\",\n"
    "    \"--enable-encoder=h264_qsv\",\n"
    "    \"--enable-muxer=mp4\",\n"
    "    \"--enable-protocol=file\"\n"
    "  ],\n"
    "  \"enabledLibraries\": [\"avcodec\", \"avformat\", \"avutil\", \"swresample\"],\n"
    "  \"enabledEncoders\": [\"aac\", \"h264_amf\", \"h264_mf\", \"h264_nvenc\", \"h264_qsv\"],\n"
    "  \"enabledMuxers\": [\"mov\", \"mp4\"],\n"
    "  \"enabledParsers\": [\"ac3\"],\n"
    "  \"enabledBitstreamFilters\": [\"aac_adtstoasc\", \"vp9_superframe\"],\n"
    "  \"enabledProtocols\": [\"file\"],\n"
    "  \"enabledExternalLibraries\": [\"amf\", \"ffnvcodec\", \"libvpl\", \"mediafoundation\"],\n"
    "  \"enabledHardwareAccelerationLibraries\": [\"amf\", \"d3d11va\", \"nvenc\", \"qsv\"],\n"
    "  \"buildRecipePath\": \"${build_recipe_relative_path}\",\n"
    "  \"buildRecipeLength\": ${build_recipe_length},\n"
    "  \"buildRecipeSha256\": \"${build_recipe_sha256}\",\n"
    "  \"artifacts\": [\n"
    "${artifact_evidence}"
    "  ]\n"
    "}\n")
file(READ "${evidence_path}" valid_evidence)

file(
    WRITE "${runner_path}"
    "cmake_minimum_required(VERSION 3.24)\n"
    "include(\"${PINNED_FFMPEG_MODULE}\")\n"
    "if(NOT VRRECORDER_FFMPEG_SOURCE_ARCHIVE_SHA256 STREQUAL \"464beb5e7bf0c311e68b45ae2f04e9cc2af88851abb4082231742a74d97b524c\")\n"
    "  message(FATAL_ERROR \"Pinned source archive SHA-256 drifted\")\n"
    "endif()\n"
    "if(NOT VRRECORDER_FFMPEG_BUILD_RECIPE_SHA256 STREQUAL \"80cbf4fefde70a4b9fb89bc2a692370f0814efb50329a9de11ccd9304b54534e\")\n"
    "  message(FATAL_ERROR \"Pinned build recipe SHA-256 drifted\")\n"
    "endif()\n"
    "set(VRRECORDER_FFMPEG_SOURCE_ARCHIVE_SHA256 \"${source_archive_sha256}\")\n"
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

string(
    REPLACE
        "\"msvcCompilerVersion\": \"19.44.35228\""
        "\"msvcCompilerVersion\": \"19.43.34810\""
    wrong_msvc_evidence "${valid_evidence}")
file(WRITE "${evidence_path}" "${wrong_msvc_evidence}")
run_validation(FALSE "wrong MSVC compiler version")
file(WRITE "${evidence_path}" "${valid_evidence}")

string(
    REPLACE
        "\"windowsSdkVersion\": \"10.0.26100.0\""
        "\"windowsSdkVersion\": \"10.0.22621.0\""
    wrong_sdk_evidence "${valid_evidence}")
file(WRITE "${evidence_path}" "${wrong_sdk_evidence}")
run_validation(FALSE "wrong Windows SDK version")
file(WRITE "${evidence_path}" "${valid_evidence}")

string(
    REPLACE
        "${source_archive_sha256}"
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
    wrong_source_evidence "${valid_evidence}")
file(WRITE "${evidence_path}" "${wrong_source_evidence}")
run_validation(FALSE "wrong source archive SHA-256")
file(WRITE "${evidence_path}" "${valid_evidence}")

string(
    REPLACE
        "cec19d7ddf725896dfbf79a4c308550d83eab5ec"
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
    wrong_patch_commit_evidence "${valid_evidence}")
file(WRITE "${evidence_path}" "${wrong_patch_commit_evidence}")
run_validation(FALSE "wrong source patch upstream commit")
file(WRITE "${evidence_path}" "${valid_evidence}")

file(APPEND "${sdk_root}/bin/avcodec-62.dll" "tampered\n")
run_validation(FALSE "runtime DLL hash mismatch")
file(WRITE "${sdk_root}/bin/avcodec-62.dll" "fake runtime DLL\n")

file(REMOVE "${sdk_root}/${source_archive_relative_path}")
run_validation(FALSE "missing source archive")
file(
    WRITE "${sdk_root}/${source_archive_relative_path}"
    "fake pinned source archive\n")

file(
    WRITE "${sdk_root}/${source_archive_relative_path}"
    "Fake pinned source archive\n")
run_validation(FALSE "same-length source archive tamper")
file(
    WRITE "${sdk_root}/${source_archive_relative_path}"
    "fake pinned source archive\n")

file(APPEND "${sdk_root}/${source_patch_relative_path}" "tampered\n")
run_validation(FALSE "source patch hash mismatch")
configure_file(
    "${canonical_patch_path}"
    "${sdk_root}/${source_patch_relative_path}"
    COPYONLY)

file(REMOVE "${sdk_root}/${source_patch_relative_path}")
run_validation(FALSE "missing source patch")
configure_file(
    "${canonical_patch_path}"
    "${sdk_root}/${source_patch_relative_path}"
    COPYONLY)

file(APPEND "${sdk_root}/${build_recipe_relative_path}" "tampered\n")
run_validation(FALSE "build recipe hash mismatch")
configure_file(
    "${canonical_recipe_path}"
    "${sdk_root}/${build_recipe_relative_path}"
    COPYONLY)

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
        "[\"amf\", \"ffnvcodec\", \"libvpl\", \"mediafoundation\"]"
        "[\"amf\", \"ffnvcodec\", \"libvpl\", \"libx264\", \"mediafoundation\"]"
    external_library_evidence "${valid_evidence}")
file(WRITE "${evidence_path}" "${external_library_evidence}")
run_validation(FALSE "unknown external library")
file(WRITE "${evidence_path}" "${valid_evidence}")

string(
    REPLACE
        "e844e5b26f46bb77479f063029595293aa8f812d"
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
    wrong_vendor_commit_evidence "${valid_evidence}")
file(WRITE "${evidence_path}" "${wrong_vendor_commit_evidence}")
run_validation(FALSE "wrong vendor source commit")
file(WRITE "${evidence_path}" "${valid_evidence}")

string(
    REPLACE
        "[\"aac_adtstoasc\", \"vp9_superframe\"]"
        "[\"aac_adtstoasc\", \"noise\", \"vp9_superframe\"]"
    bitstream_filter_evidence "${valid_evidence}")
file(WRITE "${evidence_path}" "${bitstream_filter_evidence}")
run_validation(FALSE "unknown bitstream filter")
file(WRITE "${evidence_path}" "${valid_evidence}")

file(
    WRITE "${project_root}/CMakeLists.txt"
    "cmake_minimum_required(VERSION 3.24)\n"
    "project(PinnedFFmpegImport LANGUAGES NONE)\n"
    "include(\"${PINNED_FFMPEG_MODULE}\")\n"
    "set(VRRECORDER_FFMPEG_SOURCE_ARCHIVE_SHA256 \"${source_archive_sha256}\")\n"
    "vrrecorder_import_pinned_ffmpeg_sdk(\"\${SDK_ROOT}\")\n"
    "foreach(component IN ITEMS avcodec avformat avutil swresample)\n"
    "  if(NOT TARGET \"FFmpeg::\${component}\")\n"
    "    message(FATAL_ERROR \"Missing imported target FFmpeg::\${component}\")\n"
    "  endif()\n"
    "endforeach()\n"
    "get_target_property(location FFmpeg::avcodec IMPORTED_LOCATION)\n"
    "if(NOT location STREQUAL \"\${SDK_ROOT}/bin/avcodec-62.dll\")\n"
    "  message(FATAL_ERROR \"Unexpected imported runtime: \${location}\")\n"
    "endif()\n"
    "if(CMAKE_SYSTEM_NAME STREQUAL \"Linux\")\n"
    "  vrrecorder_import_ffmpeg_contract_test_sdk(\"\${SDK_ROOT}\")\n"
    "  foreach(component IN ITEMS avformat avcodec avutil swresample)\n"
    "    if(NOT TARGET \"FFmpegContractTest::\${component}\")\n"
    "      message(FATAL_ERROR \"Missing contract-test target: \${component}\")\n"
    "    endif()\n"
    "  endforeach()\n"
    "  get_target_property(contract_location FFmpegContractTest::avformat IMPORTED_LOCATION)\n"
    "  if(NOT contract_location STREQUAL \"\${SDK_ROOT}/lib/libavformat.so.62.12.102\")\n"
    "    message(FATAL_ERROR \"Unexpected contract-test runtime: \${contract_location}\")\n"
    "  endif()\n"
    "  get_target_property(contract_links FFmpegContractTest::avformat INTERFACE_LINK_LIBRARIES)\n"
    "  if(NOT contract_links STREQUAL \"FFmpegContractTest::avcodec;FFmpegContractTest::avutil\")\n"
    "    message(FATAL_ERROR \"Unexpected contract-test links: \${contract_links}\")\n"
    "  endif()\n"
    "  get_target_property(swr_location FFmpegContractTest::swresample IMPORTED_LOCATION)\n"
    "  if(NOT swr_location STREQUAL \"\${SDK_ROOT}/lib/libswresample.so.6.3.102\")\n"
    "    message(FATAL_ERROR \"Unexpected swresample runtime: \${swr_location}\")\n"
    "  endif()\n"
    "  get_target_property(swr_links FFmpegContractTest::swresample INTERFACE_LINK_LIBRARIES)\n"
    "  if(NOT swr_links STREQUAL \"FFmpegContractTest::avutil\")\n"
    "    message(FATAL_ERROR \"Unexpected swresample links: \${swr_links}\")\n"
    "  endif()\n"
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

if(CMAKE_HOST_SYSTEM_NAME STREQUAL "Linux")
    set(missing_contract_build_root
        "${work_root}/project-build-missing-contract-avformat")
    file(REMOVE "${sdk_root}/lib/libavformat.so.62.12.102")
    execute_process(
        COMMAND
            "${CMAKE_COMMAND}"
            -S "${project_root}"
            -B "${missing_contract_build_root}"
            "-DSDK_ROOT=${sdk_root}"
        RESULT_VARIABLE missing_contract_result
        OUTPUT_QUIET
        ERROR_QUIET)
    if(missing_contract_result EQUAL 0)
        message(
            FATAL_ERROR
            "Contract-test SDK import must reject a missing exact libavformat")
    endif()

    file(
        WRITE
        "${sdk_root}/lib/libavformat.so.62.12.102"
        "fake contract-test shared library\n")
    set(missing_swr_build_root
        "${work_root}/project-build-missing-contract-swresample")
    file(REMOVE "${sdk_root}/lib/libswresample.so.6.3.102")
    execute_process(
        COMMAND
            "${CMAKE_COMMAND}"
            -S "${project_root}"
            -B "${missing_swr_build_root}"
            "-DSDK_ROOT=${sdk_root}"
        RESULT_VARIABLE missing_swr_result
        OUTPUT_QUIET
        ERROR_QUIET)
    if(missing_swr_result EQUAL 0)
        message(
            FATAL_ERROR
            "Contract-test SDK import must reject a missing exact libswresample")
    endif()
endif()

message(STATUS "Pinned FFmpeg SDK contract passed")
