cmake_minimum_required(VERSION 3.24)

foreach(required IN ITEMS COVERAGE_COLLECTOR CONTRACT_WORK_ROOT)
    if(NOT DEFINED ${required} OR "${${required}}" STREQUAL "")
        message(FATAL_ERROR "${required} is required")
    endif()
endforeach()

file(REMOVE_RECURSE "${CONTRACT_WORK_ROOT}")
set(gcda_root "${CONTRACT_WORK_ROOT}/gcda")
set(output_directory "${CONTRACT_WORK_ROOT}/output")
file(MAKE_DIRECTORY "${gcda_root}/first" "${gcda_root}/second")
file(WRITE "${gcda_root}/first/shared.cpp.gcda" "first")
file(WRITE "${gcda_root}/second/shared.cpp.gcda" "second")

set(fake_gcov "${CONTRACT_WORK_ROOT}/fake-gcov")
file(WRITE "${fake_gcov}" [=[#!/bin/sh
printf 'fixture' > shared.cpp.gcov.json.gz
]=])
file(
    CHMOD "${fake_gcov}"
    PERMISSIONS
        OWNER_READ OWNER_WRITE OWNER_EXECUTE
        GROUP_READ GROUP_EXECUTE
        WORLD_READ WORLD_EXECUTE)

execute_process(
    COMMAND
        "${CMAKE_COMMAND}"
        "-DGCOV_TOOL=${fake_gcov}"
        "-DGCDA_ROOT=${gcda_root}"
        "-DOUTPUT_DIRECTORY=${output_directory}"
        -P "${COVERAGE_COLLECTOR}"
    RESULT_VARIABLE collector_result
    ERROR_VARIABLE collector_error)
if(NOT collector_result EQUAL 0)
    message(FATAL_ERROR "The coverage collector failed: ${collector_error}")
endif()

file(GLOB artifacts LIST_DIRECTORIES FALSE
    "${output_directory}/*.gcov.json.gz")
list(LENGTH artifacts artifact_count)
if(NOT artifact_count EQUAL 2)
    message(FATAL_ERROR
        "Expected both same-named coverage artifacts, found ${artifact_count}")
endif()
