cmake_minimum_required(VERSION 3.24)

foreach(required IN ITEMS
        NATIVE_FACTORY_VARIANTS_MODULE
        SELECTION_INTENT
        NATIVE_BINARY
        OUTPUT_PATH)
    if(NOT DEFINED ${required} OR "${${required}}" STREQUAL "")
        message(FATAL_ERROR "${required} is required")
    endif()
endforeach()

foreach(path_variable IN ITEMS
        NATIVE_FACTORY_VARIANTS_MODULE
        SELECTION_INTENT
        NATIVE_BINARY
        OUTPUT_PATH)
    if(NOT IS_ABSOLUTE "${${path_variable}}")
        message(FATAL_ERROR "${path_variable} must be an absolute path")
    endif()
endforeach()

foreach(input_file IN ITEMS
        "${NATIVE_FACTORY_VARIANTS_MODULE}"
        "${SELECTION_INTENT}"
        "${NATIVE_BINARY}")
    if(NOT EXISTS "${input_file}" OR IS_DIRECTORY "${input_file}")
        message(FATAL_ERROR "Native factory evidence input is missing: ${input_file}")
    endif()
endforeach()

include("${NATIVE_FACTORY_VARIANTS_MODULE}")
file(READ "${SELECTION_INTENT}" intent_json)

function(require_json output json)
    string(JSON value ERROR_VARIABLE json_error GET "${json}" ${ARGN})
    if(NOT json_error STREQUAL "NOTFOUND")
        list(JOIN ARGN "." property)
        message(
            FATAL_ERROR
            "Native factory selection intent is missing or invalid: ${property}")
    endif()
    set(${output} "${value}" PARENT_SCOPE)
endfunction()

require_json(schema_version "${intent_json}" schemaVersion)
require_json(evidence_kind "${intent_json}" evidenceKind)
require_json(full_production "${intent_json}" fullProductionRequired)
if(NOT schema_version STREQUAL "1" OR
   NOT evidence_kind STREQUAL "native-factory-selection-intent")
    message(FATAL_ERROR "Unsupported native factory selection intent")
endif()

foreach(mapping IN ITEMS
        "MEDIA;media"
        "ENCODER_PROBE;encoderProbe"
        "SPOUT;spout"
        "STEAMVR;steamVr")
    list(GET mapping 0 family)
    list(GET mapping 1 property)
    require_json(variant "${intent_json}" "${property}" variant)
    require_json(source "${intent_json}" "${property}" source)
    vrrecorder_resolve_native_factory_source(
        expected_source "${family}" "${variant}")
    cmake_path(GET expected_source FILENAME expected_basename)
    if(NOT source STREQUAL expected_basename)
        message(
            FATAL_ERROR
            "Native factory selection source does not match ${family}/${variant}")
    endif()
    set("${family}_VARIANT" "${variant}")
    set("${family}_SOURCE" "${source}")
endforeach()

if(full_production)
    set(full_production_json true)
else()
    set(full_production_json false)
endif()

file(SHA256 "${NATIVE_BINARY}" native_binary_sha256)
file(SIZE "${NATIVE_BINARY}" native_binary_length)
cmake_path(GET NATIVE_BINARY FILENAME native_binary_name)
file(SHA256 "${SELECTION_INTENT}" selection_intent_sha256)
set(
    expected_binary_marker
    "VRRECORDER_FACTORY_SELECTION_V1:${selection_intent_sha256}")
file(
    STRINGS "${NATIVE_BINARY}" binary_markers
    REGEX "VRRECORDER_FACTORY_SELECTION_V1:[0-9a-f]+")
list(FILTER binary_markers INCLUDE REGEX "^${expected_binary_marker}$")
list(LENGTH binary_markers matching_marker_count)
if(NOT matching_marker_count EQUAL 1)
    message(
        FATAL_ERROR
        "The linked native binary does not contain the exact factory selection intent marker")
endif()

cmake_path(GET OUTPUT_PATH PARENT_PATH output_directory)
file(MAKE_DIRECTORY "${output_directory}")
set(temporary_path "${OUTPUT_PATH}.tmp")
file(
    WRITE "${temporary_path}"
    "{\n"
    "  \"schemaVersion\": 1,\n"
    "  \"evidenceKind\": \"linked-native-factory-selection\",\n"
    "  \"selectionIntentSha256\": \"${selection_intent_sha256}\",\n"
    "  \"fullProductionRequired\": ${full_production_json},\n"
    "  \"nativeBinary\": {\"file\": \"${native_binary_name}\", \"length\": ${native_binary_length}, \"sha256\": \"${native_binary_sha256}\"},\n"
    "  \"media\": {\"variant\": \"${MEDIA_VARIANT}\", \"source\": \"${MEDIA_SOURCE}\"},\n"
    "  \"encoderProbe\": {\"variant\": \"${ENCODER_PROBE_VARIANT}\", \"source\": \"${ENCODER_PROBE_SOURCE}\"},\n"
    "  \"spout\": {\"variant\": \"${SPOUT_VARIANT}\", \"source\": \"${SPOUT_SOURCE}\"},\n"
    "  \"steamVr\": {\"variant\": \"${STEAMVR_VARIANT}\", \"source\": \"${STEAMVR_SOURCE}\"}\n"
    "}\n")
file(RENAME "${temporary_path}" "${OUTPUT_PATH}")
