cmake_minimum_required(VERSION 3.24)

foreach(required IN ITEMS GCOV_TOOL GCDA_ROOT OUTPUT_DIRECTORY)
    if(NOT DEFINED ${required} OR "${${required}}" STREQUAL "")
        message(FATAL_ERROR "${required} is required")
    endif()
endforeach()

file(REMOVE_RECURSE "${OUTPUT_DIRECTORY}")
file(MAKE_DIRECTORY "${OUTPUT_DIRECTORY}")
set(work_root "${OUTPUT_DIRECTORY}/.work")
file(MAKE_DIRECTORY "${work_root}")
file(GLOB_RECURSE gcda_files LIST_DIRECTORIES FALSE "${GCDA_ROOT}/*.gcda")
if(NOT gcda_files)
    message(FATAL_ERROR "No gcda files were found below ${GCDA_ROOT}")
endif()

foreach(gcda_file IN LISTS gcda_files)
    string(SHA256 artifact_prefix "${gcda_file}")
    set(gcda_work_directory "${work_root}/${artifact_prefix}")
    file(MAKE_DIRECTORY "${gcda_work_directory}")
    execute_process(
        COMMAND "${GCOV_TOOL}" --json-format --branch-probabilities "${gcda_file}"
        WORKING_DIRECTORY "${gcda_work_directory}"
        RESULT_VARIABLE gcov_result
        OUTPUT_QUIET
        ERROR_VARIABLE gcov_error)
    if(NOT gcov_result EQUAL 0)
        message(FATAL_ERROR
            "gcov failed for ${gcda_file}: ${gcov_error}")
    endif()

    file(GLOB gcda_artifacts LIST_DIRECTORIES FALSE
        "${gcda_work_directory}/*.gcov.json.gz")
    if(NOT gcda_artifacts)
        message(FATAL_ERROR
            "gcov produced no JSON artifact for ${gcda_file}")
    endif()
    foreach(gcda_artifact IN LISTS gcda_artifacts)
        get_filename_component(artifact_name "${gcda_artifact}" NAME)
        file(COPY_FILE
            "${gcda_artifact}"
            "${OUTPUT_DIRECTORY}/${artifact_prefix}-${artifact_name}")
    endforeach()
endforeach()

file(REMOVE_RECURSE "${work_root}")

file(GLOB artifacts LIST_DIRECTORIES FALSE
    "${OUTPUT_DIRECTORY}/*.gcov.json.gz")
if(NOT artifacts)
    message(FATAL_ERROR "gcov produced no JSON artifacts")
endif()
list(LENGTH artifacts artifact_count)
message(STATUS "Collected ${artifact_count} gcov JSON artifacts")
