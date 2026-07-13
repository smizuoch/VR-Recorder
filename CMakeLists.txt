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
    "Build the Windows production adapters against the pinned FFmpeg SDK"
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

if(BUILD_TESTING AND NOT VRRECORDER_FFMPEG_CONTRACT_TEST_ROOT STREQUAL "")
    include("${CMAKE_CURRENT_SOURCE_DIR}/cmake/PinnedFFmpeg.cmake")
    vrrecorder_import_ffmpeg_contract_test_sdk(
        "${VRRECORDER_FFMPEG_CONTRACT_TEST_ROOT}")
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
    set_tests_properties(
        vrrecorder_native_pinned_ffmpeg_contract
        PROPERTIES WORKING_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}")
endif()
