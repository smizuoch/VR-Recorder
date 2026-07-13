cmake_minimum_required(VERSION 3.24)

cmake_path(GET CMAKE_CURRENT_LIST_DIR PARENT_PATH tests_directory)
cmake_path(GET tests_directory PARENT_PATH repository_root)

function(require_file relative_path)
    set(absolute_path "${repository_root}/${relative_path}")
    if(NOT EXISTS "${absolute_path}")
        message(FATAL_ERROR "Required native CMake file is missing: ${relative_path}")
    endif()
endfunction()

function(require_text relative_path required_pattern)
    file(READ "${repository_root}/${relative_path}" contents)
    if(NOT contents MATCHES "${required_pattern}")
        message(
            FATAL_ERROR
            "${relative_path} does not satisfy native build contract: ${required_pattern}")
    endif()
endfunction()

require_file("CMakeLists.txt")
require_file("src/VRRecorder.Native/CMakeLists.txt")
require_file("src/VRRecorder.Native/Makefile")
require_file("src/VRRecorder.Native/vrrecorder_native.def")
require_file("tests/VRRecorder.Native.Tests/CMakeLists.txt")
require_file("tests/VRRecorder.Native.Tests/Makefile")
require_file("tests/VRRecorder.Native.Tests/verify_exports.cmake")
require_file(".github/workflows/native-windows.yml")

require_text("CMakeLists.txt" "add_subdirectory\\(src/VRRecorder.Native\\)")
require_text("CMakeLists.txt" "add_subdirectory\\(tests/VRRecorder.Native.Tests\\)")
require_text(
    "src/VRRecorder.Native/CMakeLists.txt"
    "add_library\\(vrrecorder_native SHARED")
require_text(
    "src/VRRecorder.Native/CMakeLists.txt"
    "VRRECORDER_NATIVE_EXPORTS")
require_text(
    "src/VRRecorder.Native/CMakeLists.txt"
    "vrrecorder_native.def")
require_text(
    "src/VRRecorder.Native/CMakeLists.txt"
    "src/ffmpeg_encoder_state_machine.cpp")
require_text(
    "src/VRRecorder.Native/CMakeLists.txt"
    "src/ffmpeg_fragmented_mp4_muxer.cpp")
require_text(
    "src/VRRecorder.Native/Makefile"
    "src/ffmpeg_encoder_state_machine.cpp")
require_text(
    "src/VRRecorder.Native/Makefile"
    "src/ffmpeg_fragmented_mp4_muxer.cpp")
require_text(
    "tests/VRRecorder.Native.Tests/CMakeLists.txt"
    "add_test\\(NAME vrrecorder_native_abi_contract")
require_text(
    "tests/VRRecorder.Native.Tests/CMakeLists.txt"
    "add_test\\(NAME vrrecorder_native_c_header_smoke")
require_text(
    "tests/VRRecorder.Native.Tests/CMakeLists.txt"
    "add_test\\(NAME vrrecorder_native_ffmpeg_encoder_state_machine")
require_text(
    "tests/VRRecorder.Native.Tests/CMakeLists.txt"
    "add_test\\(NAME vrrecorder_native_ffmpeg_fragmented_mp4_muxer")
require_text(
    "tests/VRRecorder.Native.Tests/Makefile"
    "ffmpeg_encoder_state_machine_tests")
require_text(
    "tests/VRRecorder.Native.Tests/Makefile"
    "ffmpeg_fragmented_mp4_muxer_tests")
require_text(
    "tests/VRRecorder.Native.Tests/CMakeLists.txt"
    "add_test\\(NAME vrrecorder_native_exports")
require_text(".github/workflows/native-windows.yml" "windows-latest")
require_text(".github/workflows/native-windows.yml" "-A x64")
require_text(".github/workflows/native-windows.yml" "ctest")

file(
    STRINGS
    "${repository_root}/tests/VRRecorder.Native.Tests/expected_exports.txt"
    expected_exports)
list(FILTER expected_exports EXCLUDE REGEX "^ *$")
list(SORT expected_exports)

file(
    STRINGS
    "${repository_root}/src/VRRecorder.Native/vrrecorder_native.def"
    definition_lines)
set(definition_exports "")
foreach(line IN LISTS definition_lines)
    string(STRIP "${line}" line)
    if(line STREQUAL "" OR line MATCHES "^(LIBRARY|EXPORTS)([ \\t]|$)")
        continue()
    endif()
    list(APPEND definition_exports "${line}")
endforeach()
list(SORT definition_exports)

if(NOT definition_exports STREQUAL expected_exports)
    message(
        FATAL_ERROR
        "Windows module-definition exports do not exactly match expected_exports.txt")
endif()

list(LENGTH definition_exports export_count)
if(NOT export_count EQUAL 17)
    message(FATAL_ERROR "Expected exactly 17 production exports, found ${export_count}")
endif()

message(STATUS "Native CMake/Windows build contract passed")
