cmake_minimum_required(VERSION 3.24)

if(NOT DEFINED PRODUCTION_MEDIA_AAC_ATTACHMENT_MODULE OR
   NOT IS_ABSOLUTE "${PRODUCTION_MEDIA_AAC_ATTACHMENT_MODULE}")
    message(
        FATAL_ERROR
        "PRODUCTION_MEDIA_AAC_ATTACHMENT_MODULE must be absolute")
endif()
if(NOT DEFINED PRODUCTION_MEDIA_AAC_ATTACHMENT_WORK_ROOT OR
   NOT IS_ABSOLUTE "${PRODUCTION_MEDIA_AAC_ATTACHMENT_WORK_ROOT}")
    message(
        FATAL_ERROR
        "PRODUCTION_MEDIA_AAC_ATTACHMENT_WORK_ROOT must be absolute")
endif()

set(work_root "${PRODUCTION_MEDIA_AAC_ATTACHMENT_WORK_ROOT}")
set(source_root "${work_root}/native-source")
set(project_root "${work_root}/project")
set(runner "${work_root}/run-project.cmake")
file(REMOVE_RECURSE "${work_root}")
file(MAKE_DIRECTORY "${source_root}/src" "${project_root}")

set(source_names
    ffmpeg_aac_audio_pipeline.cpp
    ffmpeg_aac_packet_encoder.cpp
    ffmpeg_libavcodec_encoder_port.cpp)
foreach(source IN LISTS source_names)
    file(WRITE "${source_root}/src/${source}" "// attach fixture\n")
endforeach()

file(
    WRITE "${project_root}/CMakeLists.txt"
    [=[
cmake_minimum_required(VERSION 3.24)
project(ProductionMediaAacAttachmentContract LANGUAGES NONE)

include("${ATTACHMENT_MODULE}")
set(VRRECORDER_MEDIA_FACTORY_VARIANT "${MEDIA_VARIANT}")
set(VRRECORDER_ENABLE_FFMPEG_ADAPTERS "${ENABLE_ADAPTERS}")

if(TARGET_MODE STREQUAL "EXACT")
    foreach(component IN ITEMS avcodec avutil swresample)
        add_library("FFmpeg::${component}" INTERFACE IMPORTED GLOBAL)
    endforeach()
elseif(TARGET_MODE STREQUAL "MISSING_AVCODEC")
    foreach(component IN ITEMS avutil swresample)
        add_library("FFmpeg::${component}" INTERFACE IMPORTED GLOBAL)
    endforeach()
elseif(TARGET_MODE STREQUAL "MISSING_AVUTIL")
    foreach(component IN ITEMS avcodec swresample)
        add_library("FFmpeg::${component}" INTERFACE IMPORTED GLOBAL)
    endforeach()
elseif(TARGET_MODE STREQUAL "MISSING_SWRESAMPLE")
    foreach(component IN ITEMS avcodec avutil)
        add_library("FFmpeg::${component}" INTERFACE IMPORTED GLOBAL)
    endforeach()
elseif(TARGET_MODE STREQUAL "CONTRACT_ONLY")
    foreach(component IN ITEMS avcodec avutil swresample)
        add_library("FFmpegContractTest::${component}" INTERFACE IMPORTED GLOBAL)
    endforeach()
elseif(TARGET_MODE STREQUAL "ALIASED")
    foreach(component IN ITEMS avcodec avutil swresample)
        add_library("fixture_${component}" INTERFACE)
        add_library("FFmpeg::${component}" ALIAS "fixture_${component}")
    endforeach()
elseif(NOT TARGET_MODE STREQUAL "NONE")
    message(FATAL_ERROR "Unknown fixture TARGET_MODE: ${TARGET_MODE}")
endif()

set(actual_sources "stale-value")
vrrecorder_resolve_production_media_aac_attachment(
    actual_sources
    "${NATIVE_SOURCE_ROOT}")

if(EXPECT_ATTACHED)
    set(expected_sources
        "${NATIVE_SOURCE_ROOT}/src/ffmpeg_aac_audio_pipeline.cpp"
        "${NATIVE_SOURCE_ROOT}/src/ffmpeg_aac_packet_encoder.cpp"
        "${NATIVE_SOURCE_ROOT}/src/ffmpeg_libavcodec_encoder_port.cpp")
    if(NOT "${actual_sources}" STREQUAL "${expected_sources}")
        message(
            FATAL_ERROR
            "production AAC source plan is not exact: '${actual_sources}'")
    endif()
else()
    if(NOT actual_sources STREQUAL "")
        message(
            FATAL_ERROR
            "portable variant acquired a production AAC source plan: '${actual_sources}'")
    endif()
endif()
]=])

file(
    WRITE "${runner}"
    [=[
cmake_minimum_required(VERSION 3.24)
execute_process(
    COMMAND
        "${CMAKE_COMMAND}"
        -S "${PROJECT_ROOT}"
        -B "${BUILD_ROOT}"
        "-DATTACHMENT_MODULE=${ATTACHMENT_MODULE}"
        "-DNATIVE_SOURCE_ROOT=${NATIVE_SOURCE_ROOT}"
        "-DMEDIA_VARIANT=${MEDIA_VARIANT}"
        "-DENABLE_ADAPTERS=${ENABLE_ADAPTERS}"
        "-DTARGET_MODE=${TARGET_MODE}"
        "-DEXPECT_ATTACHED=${EXPECT_ATTACHED}"
    RESULT_VARIABLE result
    OUTPUT_VARIABLE output
    ERROR_VARIABLE error)
if(EXPECT_SUCCESS AND NOT result EQUAL 0)
    message(
        FATAL_ERROR
        "${LABEL} should configure (${result}):\n${output}\n${error}")
endif()
if(NOT EXPECT_SUCCESS AND result EQUAL 0)
    message(FATAL_ERROR "${LABEL} should fail closed, but configured")
endif()
]=])

function(run_case
        label
        expected_success
        variant
        enable_adapters
        target_mode
        expect_attached
        fixture_source_root)
    string(MAKE_C_IDENTIFIER "${label}" case_id)
    execute_process(
        COMMAND
            "${CMAKE_COMMAND}"
            "-DPROJECT_ROOT=${project_root}"
            "-DBUILD_ROOT=${work_root}/build-${case_id}"
            "-DATTACHMENT_MODULE=${PRODUCTION_MEDIA_AAC_ATTACHMENT_MODULE}"
            "-DNATIVE_SOURCE_ROOT=${fixture_source_root}"
            "-DMEDIA_VARIANT=${variant}"
            "-DENABLE_ADAPTERS=${enable_adapters}"
            "-DTARGET_MODE=${target_mode}"
            "-DEXPECT_ATTACHED=${expect_attached}"
            "-DEXPECT_SUCCESS=${expected_success}"
            "-DLABEL=${label}"
            -P "${runner}"
        RESULT_VARIABLE result
        OUTPUT_VARIABLE output
        ERROR_VARIABLE error)
    if(NOT result EQUAL 0)
        message(
            FATAL_ERROR
            "Attachment contract harness failed (${result}):\n${output}\n${error}")
    endif()
endfunction()

run_case(
    "portable variant"
    TRUE UNAVAILABLE OFF NONE FALSE "${source_root}")
run_case(
    "production without adapter admission"
    FALSE PRODUCTION OFF EXACT TRUE "${source_root}")
run_case(
    "production without canonical targets"
    FALSE PRODUCTION ON NONE TRUE "${source_root}")
run_case(
    "production missing avcodec target"
    FALSE PRODUCTION ON MISSING_AVCODEC TRUE "${source_root}")
run_case(
    "production missing avutil target"
    FALSE PRODUCTION ON MISSING_AVUTIL TRUE "${source_root}")
run_case(
    "production missing swresample target"
    FALSE PRODUCTION ON MISSING_SWRESAMPLE TRUE "${source_root}")
run_case(
    "production with test-only targets"
    FALSE PRODUCTION ON CONTRACT_ONLY TRUE "${source_root}")
run_case(
    "production with non-imported aliases"
    FALSE PRODUCTION ON ALIASED TRUE "${source_root}")
run_case(
    "production exact attachment plan"
    TRUE PRODUCTION ON EXACT TRUE "${source_root}")
run_case(
    "production empty source root"
    FALSE PRODUCTION ON EXACT TRUE "")
run_case(
    "production relative source root"
    FALSE PRODUCTION ON EXACT TRUE "relative-native-source")
run_case(
    "production nonexistent source root"
    FALSE PRODUCTION ON EXACT TRUE "${work_root}/missing-native-source")

foreach(missing_source IN LISTS source_names)
    file(REMOVE "${source_root}/src/${missing_source}")
    run_case(
        "production missing ${missing_source}"
        FALSE PRODUCTION ON EXACT TRUE "${source_root}")
    file(WRITE "${source_root}/src/${missing_source}" "// attach fixture\n")
endforeach()

message(STATUS "Production media AAC attachment contract passed")
