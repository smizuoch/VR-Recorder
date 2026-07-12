cmake_minimum_required(VERSION 3.24)

foreach(required IN ITEMS GCOV_TOOL GCDA_ROOT OUTPUT_DIRECTORY)
    if(NOT DEFINED ${required} OR "${${required}}" STREQUAL "")
        message(FATAL_ERROR "${required} is required")
    endif()
endforeach()

file(REMOVE_RECURSE "${OUTPUT_DIRECTORY}")
file(MAKE_DIRECTORY "${OUTPUT_DIRECTORY}")
file(GLOB_RECURSE gcda_files LIST_DIRECTORIES FALSE "${GCDA_ROOT}/*.gcda")
if(NOT gcda_files)
    message(FATAL_ERROR "No gcda files were found below ${GCDA_ROOT}")
endif()

foreach(gcda_file IN LISTS gcda_files)
    execute_process(
        COMMAND "${GCOV_TOOL}" --json-format --branch-probabilities "${gcda_file}"
        WORKING_DIRECTORY "${OUTPUT_DIRECTORY}"
        RESULT_VARIABLE gcov_result
        OUTPUT_QUIET
        ERROR_VARIABLE gcov_error)
    if(NOT gcov_result EQUAL 0)
        message(FATAL_ERROR
            "gcov failed for ${gcda_file}: ${gcov_error}")
    endif()
endforeach()

file(GLOB artifacts LIST_DIRECTORIES FALSE
    "${OUTPUT_DIRECTORY}/*.gcov.json.gz")
if(NOT artifacts)
    message(FATAL_ERROR "gcov produced no JSON artifacts")
endif()
list(LENGTH artifacts artifact_count)
message(STATUS "Collected ${artifact_count} gcov JSON artifacts")
