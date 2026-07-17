cmake_minimum_required(VERSION 3.24)

project(VRRecorderNative VERSION 0.1.0 LANGUAGES C CXX)

if(NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "VR Recorder native ABI supports x64 builds only")
endif()

function(vrrecorder_enable_strict_warnings target)
    if(MSVC)
        target_compile_options(
            ${target}
            PRIVATE
                "$<$<COMPILE_LANGUAGE:C>:/W4;/WX>"
                "$<$<COMPILE_LANGUAGE:CXX>:/W4;/WX;/permissive-;/EHsc>")
    else()
        target_compile_options(
            ${target}
            PRIVATE
                -Wall
                -Wextra
                -Wpedantic
                -Werror)
    endif()
endfunction()

include(CTest)
find_package(Threads REQUIRED)

option(
    VRRECORDER_ENABLE_FFMPEG_ADAPTERS
    "Import the pinned Windows FFmpeg SDK for adapter tests or production factories"
    OFF)
option(
    VRRECORDER_ENABLE_SPOUT2_ADAPTER
    "Import the pinned Spout2 SDK for the production receiver"
    OFF)
option(
    VRRECORDER_ENABLE_OPENVR_ADAPTER
    "Import the pinned OpenVR SDK for the production SteamVR backend"
    OFF)
set(
    VRRECORDER_FFMPEG_ROOT
    ""
    CACHE PATH
    "Absolute path to the exact FFmpeg 8.1.2 Windows x64 shared SDK")
set(
    VRRECORDER_FFMPEG_CONTRACT_TEST_ROOT
    ""
    CACHE PATH
    "Optional exact FFmpeg 8.1.2 Linux SDK for real adapter tests")
set(
    VRRECORDER_FFMPEG_CONTRACT_ORACLE_ROOT
    ""
    CACHE PATH
    "Optional exact FFmpeg 8.1.2 Linux or Windows demux/decode SDK for out-of-process test oracles")
set(
    VRRECORDER_SPOUT2_ROOT
    ""
    CACHE PATH
    "Absolute path to the exact Spout2 2.007.017 Windows x64 static SDK")
set(
    VRRECORDER_OPENVR_ROOT
    ""
    CACHE PATH
    "Absolute path to the exact OpenVR 2.15.6 Windows x64 SDK")

include("${CMAKE_CURRENT_SOURCE_DIR}/cmake/NativeFactoryVariants.cmake")
vrrecorder_declare_native_factory_options()

if(VRRECORDER_ENABLE_FFMPEG_ADAPTERS)
    if(NOT WIN32)
        message(
            FATAL_ERROR
            "The production FFmpeg adapters are supported only on Windows x64")
    endif()
    if(NOT MSVC)
        message(
            FATAL_ERROR
            "The production FFmpeg SDK contract requires the MSVC toolchain")
    endif()
    include("${CMAKE_CURRENT_SOURCE_DIR}/cmake/PinnedFFmpeg.cmake")
    vrrecorder_import_pinned_ffmpeg_sdk("${VRRECORDER_FFMPEG_ROOT}")
endif()

if(VRRECORDER_ENABLE_SPOUT2_ADAPTER)
    if(NOT WIN32)
        message(
            FATAL_ERROR
            "The production Spout2 adapter is supported only on Windows x64")
    endif()
    if(NOT MSVC)
        message(
            FATAL_ERROR
            "The production Spout2 SDK contract requires the MSVC toolchain")
    endif()
    include("${CMAKE_CURRENT_SOURCE_DIR}/cmake/PinnedSpout2.cmake")
    vrrecorder_import_pinned_spout2_sdk("${VRRECORDER_SPOUT2_ROOT}")
endif()

if(VRRECORDER_ENABLE_OPENVR_ADAPTER)
    if(NOT WIN32 OR NOT MSVC)
        message(
            FATAL_ERROR
            "The production OpenVR SDK contract requires Windows MSVC")
    endif()
    include("${CMAKE_CURRENT_SOURCE_DIR}/cmake/PinnedOpenVR.cmake")
    vrrecorder_import_pinned_openvr_sdk("${VRRECORDER_OPENVR_ROOT}")
endif()

if(BUILD_TESTING AND NOT VRRECORDER_FFMPEG_CONTRACT_TEST_ROOT STREQUAL "")
    include("${CMAKE_CURRENT_SOURCE_DIR}/cmake/PinnedFFmpeg.cmake")
    vrrecorder_import_ffmpeg_contract_test_sdk(
        "${VRRECORDER_FFMPEG_CONTRACT_TEST_ROOT}")
endif()

if(BUILD_TESTING AND NOT VRRECORDER_FFMPEG_CONTRACT_ORACLE_ROOT STREQUAL "")
    include("${CMAKE_CURRENT_SOURCE_DIR}/cmake/PinnedFFmpeg.cmake")
    vrrecorder_import_ffmpeg_contract_oracle_sdk(
        "${VRRECORDER_FFMPEG_CONTRACT_ORACLE_ROOT}")
endif()

add_subdirectory(src/VRRecorder.Native)

if(BUILD_TESTING)
    add_subdirectory(tests/VRRecorder.Native.Tests)
    add_test(NAME vrrecorder_native_pinned_ffmpeg_contract
        COMMAND
            "${CMAKE_COMMAND}"
            "-DPINNED_FFMPEG_MODULE=${CMAKE_CURRENT_SOURCE_DIR}/cmake/PinnedFFmpeg.cmake"
            -P
            "${CMAKE_CURRENT_SOURCE_DIR}/tests/cmake/pinned_ffmpeg_contract.cmake")
    add_test(NAME vrrecorder_native_windows_ffmpeg_oracle_contract
        COMMAND
            "${CMAKE_COMMAND}"
            "-DPINNED_FFMPEG_MODULE=${CMAKE_CURRENT_SOURCE_DIR}/cmake/PinnedFFmpeg.cmake"
            -P
            "${CMAKE_CURRENT_SOURCE_DIR}/tests/cmake/windows_ffmpeg_oracle_contract.cmake")
    add_test(NAME vrrecorder_native_pinned_spout2_contract
        COMMAND
            "${CMAKE_COMMAND}"
            "-DPINNED_SPOUT2_MODULE=${CMAKE_CURRENT_SOURCE_DIR}/cmake/PinnedSpout2.cmake"
            -P
            "${CMAKE_CURRENT_SOURCE_DIR}/tests/cmake/pinned_spout2_contract.cmake")
    add_test(NAME vrrecorder_native_pinned_openvr_contract
        COMMAND
            "${CMAKE_COMMAND}"
            "-DPINNED_OPENVR_MODULE=${CMAKE_CURRENT_SOURCE_DIR}/cmake/PinnedOpenVR.cmake"
            -P
            "${CMAKE_CURRENT_SOURCE_DIR}/tests/cmake/pinned_openvr_contract.cmake")
    add_test(NAME vrrecorder_native_factory_variant_contract
        COMMAND
            "${CMAKE_COMMAND}"
            "-DNATIVE_FACTORY_VARIANTS_MODULE=${CMAKE_CURRENT_SOURCE_DIR}/cmake/NativeFactoryVariants.cmake"
            "-DNATIVE_FACTORY_EVIDENCE_WRITER=${CMAKE_CURRENT_SOURCE_DIR}/cmake/WriteNativeFactoryEvidence.cmake"
            "-DNATIVE_SOURCE_ROOT=${CMAKE_CURRENT_SOURCE_DIR}/src/VRRecorder.Native"
            "-DNATIVE_FACTORY_CONTRACT_WORK_ROOT=${CMAKE_CURRENT_BINARY_DIR}/native-factory-variant-contract"
            "-DNATIVE_BINARY=$<TARGET_FILE:vrrecorder_native>"
            "-DNATIVE_FACTORY_SELECTION_INTENT=${CMAKE_CURRENT_BINARY_DIR}/src/VRRecorder.Native/native-factory-selection.intent.json"
            "-DNATIVE_FACTORY_BINARY_EVIDENCE=${CMAKE_CURRENT_BINARY_DIR}/native-factory-selection-$<CONFIG>.json"
            -P
            "${CMAKE_CURRENT_SOURCE_DIR}/tests/cmake/native_factory_variant_contract.cmake")
    add_test(NAME vrrecorder_native_production_media_aac_attachment_contract
        COMMAND
            "${CMAKE_COMMAND}"
            "-DPRODUCTION_MEDIA_AAC_ATTACHMENT_MODULE=${CMAKE_CURRENT_SOURCE_DIR}/cmake/ProductionMediaAacAttachment.cmake"
            "-DPRODUCTION_MEDIA_AAC_ATTACHMENT_WORK_ROOT=${CMAKE_CURRENT_BINARY_DIR}/production-media-aac-attachment-contract"
            -P
            "${CMAKE_CURRENT_SOURCE_DIR}/tests/cmake/production_media_aac_attachment_contract.cmake")
    add_test(NAME vrrecorder_native_production_media_h264_attachment_contract
        COMMAND
            "${CMAKE_COMMAND}"
            "-DPRODUCTION_MEDIA_H264_ATTACHMENT_MODULE=${CMAKE_CURRENT_SOURCE_DIR}/cmake/ProductionMediaH264Attachment.cmake"
            "-DPRODUCTION_MEDIA_H264_ATTACHMENT_WORK_ROOT=${CMAKE_CURRENT_BINARY_DIR}/production-media-h264-attachment-contract"
            -P
            "${CMAKE_CURRENT_SOURCE_DIR}/tests/cmake/production_media_h264_attachment_contract.cmake")
    if(NOT WIN32)
        add_test(NAME vrrecorder_native_gcov_collector_contract
            COMMAND
                "${CMAKE_COMMAND}"
                "-DCOVERAGE_COLLECTOR=${CMAKE_CURRENT_SOURCE_DIR}/tests/VRRecorder.Native.Tests/collect_gcov_json.cmake"
                "-DCONTRACT_WORK_ROOT=${CMAKE_CURRENT_BINARY_DIR}/gcov-collector-contract"
                -P
                "${CMAKE_CURRENT_SOURCE_DIR}/tests/VRRecorder.Native.Tests/collect_gcov_json_contract.cmake")
    endif()
    set_tests_properties(
        vrrecorder_native_pinned_ffmpeg_contract
        vrrecorder_native_windows_ffmpeg_oracle_contract
        vrrecorder_native_pinned_spout2_contract
        vrrecorder_native_pinned_openvr_contract
        PROPERTIES WORKING_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}")
    set_tests_properties(
        vrrecorder_native_factory_variant_contract
        PROPERTIES WORKING_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}")
    set_tests_properties(
        vrrecorder_native_production_media_aac_attachment_contract
        vrrecorder_native_production_media_h264_attachment_contract
        PROPERTIES WORKING_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}")
endif()
