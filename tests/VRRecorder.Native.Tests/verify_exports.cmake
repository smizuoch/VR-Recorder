cmake_minimum_required(VERSION 3.24)

foreach(required_variable LIBRARY_FILE EXPECTED_EXPORTS_FILE PLATFORM_NAME)
    if(NOT DEFINED ${required_variable} OR "${${required_variable}}" STREQUAL "")
        message(FATAL_ERROR "Missing required variable: ${required_variable}")
    endif()
endforeach()

if(NOT EXISTS "${LIBRARY_FILE}")
    message(FATAL_ERROR "Native library does not exist: ${LIBRARY_FILE}")
endif()
if(NOT EXISTS "${EXPECTED_EXPORTS_FILE}")
    message(FATAL_ERROR "Expected export list does not exist: ${EXPECTED_EXPORTS_FILE}")
endif()

if(PLATFORM_NAME STREQUAL "Windows")
    if(NOT DEFINED PE_EXPORT_TOOL OR PE_EXPORT_TOOL STREQUAL "")
        message(FATAL_ERROR "The MSVC linker is required to inspect PE exports")
    endif()
    execute_process(
        COMMAND
            "${PE_EXPORT_TOOL}"
            /dump
            /nologo
            /exports
            "${LIBRARY_FILE}"
        RESULT_VARIABLE inspection_result
        OUTPUT_VARIABLE inspection_output
        ERROR_VARIABLE inspection_error)
else()
    if(NOT DEFINED NM_TOOL OR NM_TOOL STREQUAL "")
        message(FATAL_ERROR "nm is required to inspect shared-library exports")
    endif()
    execute_process(
        COMMAND "${NM_TOOL}" -D --defined-only "${LIBRARY_FILE}"
        RESULT_VARIABLE inspection_result
        OUTPUT_VARIABLE inspection_output
        ERROR_VARIABLE inspection_error)
endif()

if(NOT inspection_result EQUAL 0)
    message(
        FATAL_ERROR
        "Could not inspect native exports (${inspection_result}): ${inspection_error}")
endif()

string(REPLACE "\r\n" "\n" inspection_output "${inspection_output}")
string(ASCII 9 horizontal_tab)
string(REPLACE "${horizontal_tab}" " " inspection_output "${inspection_output}")
string(REPLACE "\n" ";" inspection_lines "${inspection_output}")
set(actual_exports "")
foreach(line IN LISTS inspection_lines)
    if(PLATFORM_NAME STREQUAL "Windows")
        if(line MATCHES
           "^ *[0-9]+ +[0-9A-Fa-f]+ +[0-9A-Fa-f]+ +([^ =]+)")
            list(APPEND actual_exports "${CMAKE_MATCH_1}")
        endif()
    elseif(line MATCHES
           "^[0-9A-Fa-f]+ +[A-Za-z] +([^ ]+)")
        list(APPEND actual_exports "${CMAKE_MATCH_1}")
    endif()
endforeach()
list(SORT actual_exports)

file(STRINGS "${EXPECTED_EXPORTS_FILE}" expected_exports)
list(FILTER expected_exports EXCLUDE REGEX "^ *$")
list(SORT expected_exports)

if(NOT actual_exports STREQUAL expected_exports)
    string(JOIN ", " actual_display ${actual_exports})
    string(JOIN ", " expected_display ${expected_exports})
    message(
        FATAL_ERROR
        "Native export mismatch. Expected [${expected_display}], actual [${actual_display}]")
endif()

list(LENGTH actual_exports export_count)
list(LENGTH expected_exports expected_export_count)
if(NOT export_count EQUAL expected_export_count)
    message(
        FATAL_ERROR
        "Expected exactly ${expected_export_count} production exports, found ${export_count}")
endif()

message(
    STATUS
    "Verified exactly ${expected_export_count} native exports in ${LIBRARY_FILE}")
