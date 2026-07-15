cmake_minimum_required(VERSION 3.24)

foreach(required IN ITEMS
        PRODUCTION_MEDIA_H264_ATTACHMENT_MODULE
        PRODUCTION_MEDIA_H264_ATTACHMENT_WORK_ROOT)
    if(NOT DEFINED ${required} OR NOT IS_ABSOLUTE "${${required}}")
        message(FATAL_ERROR "${required} must be absolute")
    endif()
endforeach()

set(work_root "${PRODUCTION_MEDIA_H264_ATTACHMENT_WORK_ROOT}")
set(source_root "${work_root}/native-source")
set(project_root "${work_root}/project")
set(runner "${work_root}/run-project.cmake")
file(REMOVE_RECURSE "${work_root}")
file(MAKE_DIRECTORY "${source_root}/src" "${project_root}")

set(source_names
    ffmpeg_h264_packet_encoder.cpp
    ffmpeg_h264_media_foundation_configuration.cpp
    ffmpeg_h264_nv12_frame.cpp
    ffmpeg_libavcodec_encoder_port.cpp)
foreach(source IN LISTS source_names)
    file(WRITE "${source_root}/src/${source}" "// attach fixture\n")
endforeach()

file(WRITE "${project_root}/CMakeLists.txt" [=[
cmake_minimum_required(VERSION 3.24)
project(ProductionMediaH264AttachmentContract LANGUAGES NONE)
include("${ATTACHMENT_MODULE}")
set(VRRECORDER_MEDIA_FACTORY_VARIANT "${MEDIA_VARIANT}")
set(VRRECORDER_ENABLE_FFMPEG_ADAPTERS "${ENABLE_ADAPTERS}")

if(TARGET_MODE STREQUAL "EXACT")
    foreach(component IN ITEMS avcodec avutil)
        add_library("FFmpeg::${component}" INTERFACE IMPORTED GLOBAL)
    endforeach()
elseif(TARGET_MODE STREQUAL "MISSING_AVCODEC")
    add_library(FFmpeg::avutil INTERFACE IMPORTED GLOBAL)
elseif(TARGET_MODE STREQUAL "MISSING_AVUTIL")
    add_library(FFmpeg::avcodec INTERFACE IMPORTED GLOBAL)
elseif(TARGET_MODE STREQUAL "CONTRACT_ONLY")
    foreach(component IN ITEMS avcodec avutil)
        add_library("FFmpegContractTest::${component}" INTERFACE IMPORTED GLOBAL)
    endforeach()
elseif(TARGET_MODE STREQUAL "ALIASED")
    foreach(component IN ITEMS avcodec avutil)
        add_library("fixture_${component}" INTERFACE)
        add_library("FFmpeg::${component}" ALIAS "fixture_${component}")
    endforeach()
elseif(NOT TARGET_MODE STREQUAL "NONE")
    message(FATAL_ERROR "Unknown target mode: ${TARGET_MODE}")
endif()

set(actual_sources "stale")
vrrecorder_resolve_production_media_h264_attachment(
    actual_sources "${NATIVE_SOURCE_ROOT}")
if(EXPECT_ATTACHED)
    set(expected_sources
        "${NATIVE_SOURCE_ROOT}/src/ffmpeg_h264_packet_encoder.cpp"
        "${NATIVE_SOURCE_ROOT}/src/ffmpeg_h264_media_foundation_configuration.cpp"
        "${NATIVE_SOURCE_ROOT}/src/ffmpeg_h264_nv12_frame.cpp"
        "${NATIVE_SOURCE_ROOT}/src/ffmpeg_libavcodec_encoder_port.cpp")
    if(NOT "${actual_sources}" STREQUAL "${expected_sources}")
        message(FATAL_ERROR "production H264 source plan is not exact: '${actual_sources}'")
    endif()
elseif(NOT actual_sources STREQUAL "")
    message(FATAL_ERROR "portable variant acquired H264 sources: '${actual_sources}'")
endif()
]=])

file(WRITE "${runner}" [=[
cmake_minimum_required(VERSION 3.24)
execute_process(
    COMMAND "${CMAKE_COMMAND}"
        -S "${PROJECT_ROOT}" -B "${BUILD_ROOT}"
        "-DATTACHMENT_MODULE=${ATTACHMENT_MODULE}"
        "-DNATIVE_SOURCE_ROOT=${NATIVE_SOURCE_ROOT}"
        "-DMEDIA_VARIANT=${MEDIA_VARIANT}"
        "-DENABLE_ADAPTERS=${ENABLE_ADAPTERS}"
        "-DTARGET_MODE=${TARGET_MODE}"
        "-DEXPECT_ATTACHED=${EXPECT_ATTACHED}"
    RESULT_VARIABLE result OUTPUT_VARIABLE output ERROR_VARIABLE error)
if(EXPECT_SUCCESS AND NOT result EQUAL 0)
    message(FATAL_ERROR "${LABEL} should configure (${result}):\n${output}\n${error}")
endif()
if(NOT EXPECT_SUCCESS AND result EQUAL 0)
    message(FATAL_ERROR "${LABEL} should fail closed, but configured")
endif()
]=])

function(run_case label success variant adapters targets attached fixture_root)
    string(MAKE_C_IDENTIFIER "${label}" case_id)
    execute_process(
        COMMAND "${CMAKE_COMMAND}"
            "-DPROJECT_ROOT=${project_root}"
            "-DBUILD_ROOT=${work_root}/build-${case_id}"
            "-DATTACHMENT_MODULE=${PRODUCTION_MEDIA_H264_ATTACHMENT_MODULE}"
            "-DNATIVE_SOURCE_ROOT=${fixture_root}"
            "-DMEDIA_VARIANT=${variant}"
            "-DENABLE_ADAPTERS=${adapters}"
            "-DTARGET_MODE=${targets}"
            "-DEXPECT_ATTACHED=${attached}"
            "-DEXPECT_SUCCESS=${success}"
            "-DLABEL=${label}"
            -P "${runner}"
        RESULT_VARIABLE result OUTPUT_VARIABLE output ERROR_VARIABLE error)
    if(NOT result EQUAL 0)
        message(FATAL_ERROR "Attachment harness failed (${result}):\n${output}\n${error}")
    endif()
endfunction()

run_case("portable variant" TRUE UNAVAILABLE OFF NONE FALSE "${source_root}")
run_case("production without adapters" FALSE PRODUCTION OFF EXACT TRUE "${source_root}")
run_case("production without targets" FALSE PRODUCTION ON NONE TRUE "${source_root}")
run_case("production missing avcodec" FALSE PRODUCTION ON MISSING_AVCODEC TRUE "${source_root}")
run_case("production missing avutil" FALSE PRODUCTION ON MISSING_AVUTIL TRUE "${source_root}")
run_case("production with test targets" FALSE PRODUCTION ON CONTRACT_ONLY TRUE "${source_root}")
run_case("production with aliases" FALSE PRODUCTION ON ALIASED TRUE "${source_root}")
run_case("production exact plan" TRUE PRODUCTION ON EXACT TRUE "${source_root}")
run_case("empty source root" FALSE PRODUCTION ON EXACT TRUE "")
run_case("relative source root" FALSE PRODUCTION ON EXACT TRUE "relative")
run_case("missing source root" FALSE PRODUCTION ON EXACT TRUE "${work_root}/missing")

foreach(missing_source IN LISTS source_names)
    file(REMOVE "${source_root}/src/${missing_source}")
    run_case(
        "missing ${missing_source}" FALSE PRODUCTION ON EXACT TRUE "${source_root}")
    file(WRITE "${source_root}/src/${missing_source}" "// attach fixture\n")
endforeach()

message(STATUS "Production media H264 attachment contract passed")
