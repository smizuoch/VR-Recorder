include_guard(GLOBAL)

set(VRRECORDER_FFMPEG_VERSION "8.1.2")
set(VRRECORDER_FFMPEG_TAG "n8.1.2")
set(
    VRRECORDER_FFMPEG_SOURCE_COMMIT
    "38b88335f99e76ed89ff3c93f877fdefce736c13")
set(
    VRRECORDER_FFMPEG_SOURCE_ARCHIVE_SHA256
    "464beb5e7bf0c311e68b45ae2f04e9cc2af88851abb4082231742a74d97b524c")
set(VRRECORDER_FFMPEG_MSVC_COMPILER_VERSION "19.44.35228")
set(VRRECORDER_FFMPEG_WINDOWS_SDK_VERSION "10.0.26100.0")
set(
    VRRECORDER_FFMPEG_SOURCE_ARCHIVE_RELATIVE_PATH
    "share/vrrecorder/sources/ffmpeg-8.1.2.tar.xz")
set(
    VRRECORDER_FFMPEG_SOURCE_PATCH_RELATIVE_PATH
    "share/vrrecorder/patches/ffmpeg-8.1.2/0001-configure-redo-enabling-cbs-in-lavf.patch")
set(
    VRRECORDER_FFMPEG_SOURCE_PATCH_SHA256
    "c8aca5fee1f02dbd1a1623de0333013e0c41fb691adf0ede3d4479ee32ac41c0")
set(
    VRRECORDER_FFMPEG_SOURCE_PATCH_UPSTREAM_COMMIT
    "cec19d7ddf725896dfbf79a4c308550d83eab5ec")
set(
    VRRECORDER_FFMPEG_SOURCE_PATCH_UPSTREAM_URL
    "https://code.ffmpeg.org/FFmpeg/FFmpeg/pulls/23039")
set(
    VRRECORDER_FFMPEG_BUILD_RECIPE_RELATIVE_PATH
    "share/vrrecorder/build-recipes/ffmpeg-windows-x64.md")
set(
    VRRECORDER_FFMPEG_BUILD_RECIPE_SHA256
    "3579cddeb30c04a3a17bf3956ebbbfe87dccdd12081c0432fb4626e049beff01")
set(
    _vrrecorder_ffmpeg_artifact_paths
    "bin/avcodec-62.dll"
    "bin/avformat-62.dll"
    "bin/avutil-60.dll"
    "bin/swresample-6.dll"
    "lib/avcodec.lib"
    "lib/avformat.lib"
    "lib/avutil.lib"
    "lib/swresample.lib")

set(
    _vrrecorder_ffmpeg_configure_arguments
    "--prefix=<SDK_ROOT>"
    "--toolchain=msvc"
    "--enable-cross-compile"
    "--host-cc=cl.exe"
    "--arch=x86_64"
    "--target-os=win32"
    "--enable-shared"
    "--disable-static"
    "--disable-programs"
    "--disable-doc"
    "--disable-network"
    "--disable-autodetect"
    "--disable-everything"
    "--disable-avdevice"
    "--disable-avfilter"
    "--disable-swscale"
    "--disable-iconv"
    "--disable-zlib"
    "--disable-bzlib"
    "--disable-lzma"
    "--disable-debug"
    "--disable-iamf"
    "--disable-x86asm"
    "--enable-avcodec"
    "--enable-avformat"
    "--enable-avutil"
    "--enable-swresample"
    "--enable-d3d11va"
    "--enable-mediafoundation"
    "--enable-encoder=aac"
    "--enable-encoder=h264_mf"
    "--enable-muxer=mp4"
    "--enable-protocol=file")

function(_vrrecorder_ffmpeg_require_file path description)
    if(NOT EXISTS "${path}" OR IS_DIRECTORY "${path}")
        message(FATAL_ERROR "Pinned FFmpeg ${description} is missing: ${path}")
    endif()
endfunction()

function(_vrrecorder_ffmpeg_require_json_value json property expected)
    string(JSON actual ERROR_VARIABLE json_error GET "${json}" "${property}")
    if(NOT json_error STREQUAL "NOTFOUND" OR NOT actual STREQUAL expected)
        message(
            FATAL_ERROR
            "Pinned FFmpeg evidence property ${property} must be '${expected}'")
    endif()
endfunction()

function(_vrrecorder_ffmpeg_read_json_array output json property)
    string(JSON length ERROR_VARIABLE json_error LENGTH "${json}" "${property}")
    if(NOT json_error STREQUAL "NOTFOUND")
        message(
            FATAL_ERROR
            "Pinned FFmpeg evidence array ${property} is missing or invalid")
    endif()

    set(values "")
    if(length GREATER 0)
        math(EXPR last_index "${length} - 1")
        foreach(index RANGE 0 ${last_index})
            string(
                JSON value
                ERROR_VARIABLE json_error
                GET "${json}" "${property}" ${index})
            if(NOT json_error STREQUAL "NOTFOUND")
                message(
                    FATAL_ERROR
                    "Pinned FFmpeg evidence array ${property} is invalid")
            endif()
            list(APPEND values "${value}")
        endforeach()
    endif()
    set(${output} "${values}" PARENT_SCOPE)
endfunction()

function(_vrrecorder_ffmpeg_require_exact_array json property)
    set(expected ${ARGN})
    _vrrecorder_ffmpeg_read_json_array(actual "${json}" "${property}")
    if(NOT "${actual}" STREQUAL "${expected}")
        message(
            FATAL_ERROR
            "Pinned FFmpeg evidence array ${property} does not match the exact contract")
    endif()
endfunction()

function(
        _vrrecorder_ffmpeg_require_file_length
        root json path_property length_property expected_path description)
    _vrrecorder_ffmpeg_require_json_value(
        "${json}" "${path_property}" "${expected_path}")
    set(path "${root}/${expected_path}")
    _vrrecorder_ffmpeg_require_file("${path}" "${description}")
    file(SIZE "${path}" actual_length)
    _vrrecorder_ffmpeg_require_json_value(
        "${json}" "${length_property}" "${actual_length}")
endfunction()

function(
        _vrrecorder_ffmpeg_require_file_identity
        root json path_property length_property sha_property
        expected_path description)
    _vrrecorder_ffmpeg_require_file_length(
        "${root}"
        "${json}"
        "${path_property}"
        "${length_property}"
        "${expected_path}"
        "${description}")
    set(path "${root}/${expected_path}")
    file(SHA256 "${path}" actual_sha256)
    _vrrecorder_ffmpeg_require_json_value(
        "${json}" "${sha_property}" "${actual_sha256}")
endfunction()

function(_vrrecorder_ffmpeg_validate_artifact_identities root evidence)
    string(
        JSON artifact_count
        ERROR_VARIABLE json_error
        LENGTH "${evidence}" artifacts)
    if(NOT json_error STREQUAL "NOTFOUND")
        message(FATAL_ERROR "Pinned FFmpeg artifact evidence is missing")
    endif()
    list(LENGTH _vrrecorder_ffmpeg_artifact_paths expected_count)
    if(NOT artifact_count EQUAL expected_count)
        message(
            FATAL_ERROR
            "Pinned FFmpeg artifact evidence must contain the exact artifact set")
    endif()

    math(EXPR last_index "${expected_count} - 1")
    foreach(index RANGE 0 ${last_index})
        list(GET _vrrecorder_ffmpeg_artifact_paths ${index} expected_path)
        string(
            JSON artifact
            ERROR_VARIABLE json_error
            GET "${evidence}" artifacts ${index})
        if(NOT json_error STREQUAL "NOTFOUND")
            message(FATAL_ERROR "Pinned FFmpeg artifact evidence is invalid")
        endif()
        _vrrecorder_ffmpeg_require_file_identity(
            "${root}"
            "${artifact}"
            path
            length
            sha256
            "${expected_path}"
            "artifact ${expected_path}")
    endforeach()
endfunction()

function(_vrrecorder_ffmpeg_require_version header macro expected)
    _vrrecorder_ffmpeg_require_file("${header}" "version header")
    file(
        STRINGS "${header}" matches
        REGEX "^#define[ \t]+${macro}[ \t]+${expected}([ \t]|$)")
    list(LENGTH matches match_count)
    if(NOT match_count EQUAL 1)
        message(
            FATAL_ERROR
            "Pinned FFmpeg header ${header} must define ${macro} as ${expected}")
    endif()
endfunction()

function(_vrrecorder_ffmpeg_validate_exact_headers include_root)
    foreach(header IN ITEMS
            "libavcodec/avcodec.h"
            "libavformat/avformat.h"
            "libavutil/avutil.h"
            "libswresample/swresample.h")
        _vrrecorder_ffmpeg_require_file(
            "${include_root}/${header}"
            "public header")
    endforeach()

    _vrrecorder_ffmpeg_require_version(
        "${include_root}/libavcodec/version_major.h"
        LIBAVCODEC_VERSION_MAJOR 62)
    _vrrecorder_ffmpeg_require_version(
        "${include_root}/libavcodec/version.h"
        LIBAVCODEC_VERSION_MINOR 28)
    _vrrecorder_ffmpeg_require_version(
        "${include_root}/libavcodec/version.h"
        LIBAVCODEC_VERSION_MICRO 102)
    _vrrecorder_ffmpeg_require_version(
        "${include_root}/libavformat/version_major.h"
        LIBAVFORMAT_VERSION_MAJOR 62)
    _vrrecorder_ffmpeg_require_version(
        "${include_root}/libavformat/version.h"
        LIBAVFORMAT_VERSION_MINOR 12)
    _vrrecorder_ffmpeg_require_version(
        "${include_root}/libavformat/version.h"
        LIBAVFORMAT_VERSION_MICRO 102)
    _vrrecorder_ffmpeg_require_version(
        "${include_root}/libavutil/version.h"
        LIBAVUTIL_VERSION_MAJOR 60)
    _vrrecorder_ffmpeg_require_version(
        "${include_root}/libavutil/version.h"
        LIBAVUTIL_VERSION_MINOR 26)
    _vrrecorder_ffmpeg_require_version(
        "${include_root}/libavutil/version.h"
        LIBAVUTIL_VERSION_MICRO 102)
    _vrrecorder_ffmpeg_require_version(
        "${include_root}/libswresample/version_major.h"
        LIBSWRESAMPLE_VERSION_MAJOR 6)
    _vrrecorder_ffmpeg_require_version(
        "${include_root}/libswresample/version.h"
        LIBSWRESAMPLE_VERSION_MINOR 3)
    _vrrecorder_ffmpeg_require_version(
        "${include_root}/libswresample/version.h"
        LIBSWRESAMPLE_VERSION_MICRO 102)
endfunction()

function(vrrecorder_validate_pinned_ffmpeg_sdk root)
    if(root STREQUAL "" OR NOT IS_ABSOLUTE "${root}")
        message(FATAL_ERROR "VRRECORDER_FFMPEG_ROOT must be an absolute path")
    endif()

    cmake_path(NORMAL_PATH root OUTPUT_VARIABLE normalized_root)
    set(include_root "${normalized_root}/include")
    set(library_root "${normalized_root}/lib")
    set(runtime_root "${normalized_root}/bin")
    set(evidence_path
        "${normalized_root}/share/vrrecorder/ffmpeg-build-evidence.json")

    _vrrecorder_ffmpeg_validate_exact_headers("${include_root}")

    foreach(component IN ITEMS avcodec avformat avutil swresample)
        _vrrecorder_ffmpeg_require_file(
            "${library_root}/${component}.lib"
            "MSVC import library")
    endforeach()
    foreach(runtime IN ITEMS
            "avcodec-62.dll"
            "avformat-62.dll"
            "avutil-60.dll"
            "swresample-6.dll")
        _vrrecorder_ffmpeg_require_file(
            "${runtime_root}/${runtime}"
            "runtime DLL")
    endforeach()

    _vrrecorder_ffmpeg_require_file("${evidence_path}" "build evidence")
    file(READ "${evidence_path}" evidence)
    _vrrecorder_ffmpeg_require_json_value("${evidence}" schemaVersion "3")
    _vrrecorder_ffmpeg_require_json_value(
        "${evidence}" version "${VRRECORDER_FFMPEG_VERSION}")
    _vrrecorder_ffmpeg_require_json_value(
        "${evidence}" tag "${VRRECORDER_FFMPEG_TAG}")
    _vrrecorder_ffmpeg_require_json_value(
        "${evidence}" sourceCommit "${VRRECORDER_FFMPEG_SOURCE_COMMIT}")
    _vrrecorder_ffmpeg_require_file_identity(
        "${normalized_root}"
        "${evidence}"
        sourceArchivePath
        sourceArchiveLength
        sourceArchiveSha256
        "${VRRECORDER_FFMPEG_SOURCE_ARCHIVE_RELATIVE_PATH}"
        "source archive")
    _vrrecorder_ffmpeg_require_json_value(
        "${evidence}"
        sourceArchiveSha256
        "${VRRECORDER_FFMPEG_SOURCE_ARCHIVE_SHA256}")
    _vrrecorder_ffmpeg_require_file_identity(
        "${normalized_root}"
        "${evidence}"
        sourcePatchPath
        sourcePatchLength
        sourcePatchSha256
        "${VRRECORDER_FFMPEG_SOURCE_PATCH_RELATIVE_PATH}"
        "source patch")
    _vrrecorder_ffmpeg_require_json_value(
        "${evidence}"
        sourcePatchSha256
        "${VRRECORDER_FFMPEG_SOURCE_PATCH_SHA256}")
    _vrrecorder_ffmpeg_require_json_value(
        "${evidence}"
        sourcePatchUpstreamCommit
        "${VRRECORDER_FFMPEG_SOURCE_PATCH_UPSTREAM_COMMIT}")
    _vrrecorder_ffmpeg_require_json_value(
        "${evidence}"
        sourcePatchUpstreamUrl
        "${VRRECORDER_FFMPEG_SOURCE_PATCH_UPSTREAM_URL}")
    _vrrecorder_ffmpeg_require_json_value(
        "${evidence}" platform "windows-x64")
    _vrrecorder_ffmpeg_require_json_value("${evidence}" toolchain "msvc")
    _vrrecorder_ffmpeg_require_json_value(
        "${evidence}"
        msvcCompilerVersion
        "${VRRECORDER_FFMPEG_MSVC_COMPILER_VERSION}")
    _vrrecorder_ffmpeg_require_json_value(
        "${evidence}"
        windowsSdkVersion
        "${VRRECORDER_FFMPEG_WINDOWS_SDK_VERSION}")
    _vrrecorder_ffmpeg_require_json_value("${evidence}" linkage "shared")
    _vrrecorder_ffmpeg_require_json_value(
        "${evidence}" license "LGPL version 2.1 or later")
    _vrrecorder_ffmpeg_require_json_value("${evidence}" gpl "OFF")
    _vrrecorder_ffmpeg_require_json_value("${evidence}" nonfree "OFF")

    _vrrecorder_ffmpeg_read_json_array(
        configure_arguments "${evidence}" configureArguments)
    set(normalized_arguments "")
    set(prefix_count 0)
    foreach(argument IN LISTS configure_arguments)
        if(argument MATCHES "^--prefix=.+")
            math(EXPR prefix_count "${prefix_count} + 1")
            list(APPEND normalized_arguments "--prefix=<SDK_ROOT>")
        else()
            list(APPEND normalized_arguments "${argument}")
        endif()
        if(argument MATCHES "^--enable-(gpl|nonfree|version3|lib.+)$")
            message(
                FATAL_ERROR
                "Pinned FFmpeg evidence enables a forbidden license/external feature: ${argument}")
        endif()
    endforeach()
    if(NOT prefix_count EQUAL 1 OR
       NOT "${normalized_arguments}" STREQUAL
           "${_vrrecorder_ffmpeg_configure_arguments}")
        message(
            FATAL_ERROR
            "Pinned FFmpeg configure arguments do not match the exact LGPL-only contract")
    endif()

    _vrrecorder_ffmpeg_require_exact_array(
        "${evidence}" enabledLibraries
        avcodec avformat avutil swresample)
    _vrrecorder_ffmpeg_require_exact_array(
        "${evidence}" enabledEncoders
        aac h264_mf)
    _vrrecorder_ffmpeg_require_exact_array(
        "${evidence}" enabledMuxers
        mov mp4)
    _vrrecorder_ffmpeg_require_exact_array(
        "${evidence}" enabledParsers
        ac3)
    _vrrecorder_ffmpeg_require_exact_array(
        "${evidence}" enabledBitstreamFilters
        aac_adtstoasc vp9_superframe)
    _vrrecorder_ffmpeg_require_exact_array(
        "${evidence}" enabledProtocols
        file)
    _vrrecorder_ffmpeg_require_exact_array(
        "${evidence}" enabledExternalLibraries
        mediafoundation)
    _vrrecorder_ffmpeg_require_exact_array(
        "${evidence}" enabledHardwareAccelerationLibraries
        d3d11va)
    _vrrecorder_ffmpeg_require_file_identity(
        "${normalized_root}"
        "${evidence}"
        buildRecipePath
        buildRecipeLength
        buildRecipeSha256
        "${VRRECORDER_FFMPEG_BUILD_RECIPE_RELATIVE_PATH}"
        "build recipe")
    _vrrecorder_ffmpeg_require_json_value(
        "${evidence}"
        buildRecipeSha256
        "${VRRECORDER_FFMPEG_BUILD_RECIPE_SHA256}")
    _vrrecorder_ffmpeg_validate_artifact_identities(
        "${normalized_root}" "${evidence}")
endfunction()

function(vrrecorder_import_pinned_ffmpeg_sdk root)
    vrrecorder_validate_pinned_ffmpeg_sdk("${root}")
    cmake_path(NORMAL_PATH root OUTPUT_VARIABLE normalized_root)

    foreach(component IN ITEMS avcodec avformat avutil swresample)
        if(TARGET "FFmpeg::${component}")
            message(
                FATAL_ERROR
                "Pinned FFmpeg target already exists: FFmpeg::${component}")
        endif()
    endforeach()

    set(avcodec_runtime "avcodec-62.dll")
    set(avformat_runtime "avformat-62.dll")
    set(avutil_runtime "avutil-60.dll")
    set(swresample_runtime "swresample-6.dll")
    foreach(component IN ITEMS avcodec avformat avutil swresample)
        add_library("FFmpeg::${component}" SHARED IMPORTED GLOBAL)
        set_target_properties(
            "FFmpeg::${component}"
            PROPERTIES
                IMPORTED_IMPLIB "${normalized_root}/lib/${component}.lib"
                IMPORTED_LOCATION
                    "${normalized_root}/bin/${${component}_runtime}"
                INTERFACE_INCLUDE_DIRECTORIES
                    "${normalized_root}/include")
    endforeach()

    set_property(
        TARGET FFmpeg::avcodec
        PROPERTY INTERFACE_LINK_LIBRARIES FFmpeg::avutil)
    set_property(
        TARGET FFmpeg::avformat
        PROPERTY INTERFACE_LINK_LIBRARIES
            "FFmpeg::avcodec;FFmpeg::avutil")
    set_property(
        TARGET FFmpeg::swresample
        PROPERTY INTERFACE_LINK_LIBRARIES FFmpeg::avutil)
endfunction()

function(vrrecorder_import_ffmpeg_contract_test_sdk root)
    if(NOT CMAKE_SYSTEM_NAME STREQUAL "Linux")
        message(
            FATAL_ERROR
            "The unpackaged FFmpeg contract-test SDK is Linux-only")
    endif()
    if(root STREQUAL "" OR NOT IS_ABSOLUTE "${root}")
        message(
            FATAL_ERROR
            "VRRECORDER_FFMPEG_CONTRACT_TEST_ROOT must be an absolute path")
    endif()

    cmake_path(NORMAL_PATH root OUTPUT_VARIABLE normalized_root)
    _vrrecorder_ffmpeg_validate_exact_headers("${normalized_root}/include")
    set(
        avformat_library
        "${normalized_root}/lib/libavformat.so.62.12.102")
    set(
        avcodec_library
        "${normalized_root}/lib/libavcodec.so.62.28.102")
    set(
        avutil_library
        "${normalized_root}/lib/libavutil.so.60.26.102")
    set(
        swresample_library
        "${normalized_root}/lib/libswresample.so.6.3.102")
    _vrrecorder_ffmpeg_require_file(
        "${avformat_library}" "contract-test libavformat")
    _vrrecorder_ffmpeg_require_file(
        "${avcodec_library}" "contract-test libavcodec")
    _vrrecorder_ffmpeg_require_file(
        "${avutil_library}" "contract-test libavutil")
    _vrrecorder_ffmpeg_require_file(
        "${swresample_library}" "contract-test libswresample")

    foreach(component IN ITEMS avformat avcodec avutil swresample)
        if(TARGET "FFmpegContractTest::${component}")
            message(
                FATAL_ERROR
                "FFmpeg contract-test target already exists: ${component}")
        endif()
    endforeach()

    add_library(FFmpegContractTest::avutil SHARED IMPORTED GLOBAL)
    set_target_properties(
        FFmpegContractTest::avutil
        PROPERTIES
            IMPORTED_LOCATION "${avutil_library}"
            INTERFACE_INCLUDE_DIRECTORIES "${normalized_root}/include")
    add_library(FFmpegContractTest::avcodec SHARED IMPORTED GLOBAL)
    set_target_properties(
        FFmpegContractTest::avcodec
        PROPERTIES
            IMPORTED_LOCATION "${avcodec_library}"
            INTERFACE_INCLUDE_DIRECTORIES "${normalized_root}/include"
            INTERFACE_LINK_LIBRARIES FFmpegContractTest::avutil)
    add_library(FFmpegContractTest::avformat SHARED IMPORTED GLOBAL)
    set_target_properties(
        FFmpegContractTest::avformat
        PROPERTIES
            IMPORTED_LOCATION "${avformat_library}"
            INTERFACE_INCLUDE_DIRECTORIES "${normalized_root}/include"
            INTERFACE_LINK_LIBRARIES
                "FFmpegContractTest::avcodec;FFmpegContractTest::avutil")
    add_library(FFmpegContractTest::swresample SHARED IMPORTED GLOBAL)
    set_target_properties(
        FFmpegContractTest::swresample
        PROPERTIES
            IMPORTED_LOCATION "${swresample_library}"
            INTERFACE_INCLUDE_DIRECTORIES "${normalized_root}/include"
            INTERFACE_LINK_LIBRARIES FFmpegContractTest::avutil)
endfunction()

function(vrrecorder_import_ffmpeg_contract_oracle_sdk root)
    if(NOT CMAKE_SYSTEM_NAME STREQUAL "Linux")
        message(
            FATAL_ERROR
            "The unpackaged FFmpeg contract oracle SDK is Linux-only")
    endif()
    if(root STREQUAL "" OR NOT IS_ABSOLUTE "${root}")
        message(
            FATAL_ERROR
            "VRRECORDER_FFMPEG_CONTRACT_ORACLE_ROOT must be an absolute path")
    endif()

    cmake_path(NORMAL_PATH root OUTPUT_VARIABLE normalized_root)
    _vrrecorder_ffmpeg_validate_exact_headers("${normalized_root}/include")
    set(
        avformat_library
        "${normalized_root}/lib/libavformat.so.62.12.102")
    set(
        avcodec_library
        "${normalized_root}/lib/libavcodec.so.62.28.102")
    set(
        avutil_library
        "${normalized_root}/lib/libavutil.so.60.26.102")
    _vrrecorder_ffmpeg_require_file(
        "${avformat_library}" "contract-oracle libavformat")
    _vrrecorder_ffmpeg_require_file(
        "${avcodec_library}" "contract-oracle libavcodec")
    _vrrecorder_ffmpeg_require_file(
        "${avutil_library}" "contract-oracle libavutil")
    _vrrecorder_ffmpeg_require_file(
        "${normalized_root}/share/vrrecorder/contract-oracle-build.txt"
        "contract-oracle build marker")

    foreach(component IN ITEMS avformat avcodec avutil)
        if(TARGET "FFmpegContractOracle::${component}")
            message(
                FATAL_ERROR
                "FFmpeg contract oracle target already exists: ${component}")
        endif()
    endforeach()

    add_library(FFmpegContractOracle::avutil SHARED IMPORTED GLOBAL)
    set_target_properties(
        FFmpegContractOracle::avutil
        PROPERTIES
            IMPORTED_LOCATION "${avutil_library}"
            INTERFACE_INCLUDE_DIRECTORIES "${normalized_root}/include")
    add_library(FFmpegContractOracle::avcodec SHARED IMPORTED GLOBAL)
    set_target_properties(
        FFmpegContractOracle::avcodec
        PROPERTIES
            IMPORTED_LOCATION "${avcodec_library}"
            INTERFACE_INCLUDE_DIRECTORIES "${normalized_root}/include"
            INTERFACE_LINK_LIBRARIES FFmpegContractOracle::avutil)
    add_library(FFmpegContractOracle::avformat SHARED IMPORTED GLOBAL)
    set_target_properties(
        FFmpegContractOracle::avformat
        PROPERTIES
            IMPORTED_LOCATION "${avformat_library}"
            INTERFACE_INCLUDE_DIRECTORIES "${normalized_root}/include"
            INTERFACE_LINK_LIBRARIES
                "FFmpegContractOracle::avcodec;FFmpegContractOracle::avutil")
endfunction()
