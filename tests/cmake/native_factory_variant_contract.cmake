cmake_minimum_required(VERSION 3.24)

if(NOT DEFINED NATIVE_FACTORY_VARIANTS_MODULE)
    message(FATAL_ERROR "NATIVE_FACTORY_VARIANTS_MODULE is required")
endif()
if(NOT DEFINED NATIVE_FACTORY_EVIDENCE_WRITER OR
   NOT IS_ABSOLUTE "${NATIVE_FACTORY_EVIDENCE_WRITER}")
    message(FATAL_ERROR "NATIVE_FACTORY_EVIDENCE_WRITER must be absolute")
endif()
if(NOT DEFINED NATIVE_SOURCE_ROOT)
    message(FATAL_ERROR "NATIVE_SOURCE_ROOT is required")
endif()
if(NOT DEFINED NATIVE_FACTORY_CONTRACT_WORK_ROOT OR
   NOT IS_ABSOLUTE "${NATIVE_FACTORY_CONTRACT_WORK_ROOT}")
    message(
        FATAL_ERROR
        "NATIVE_FACTORY_CONTRACT_WORK_ROOT must be an absolute path")
endif()
foreach(required IN ITEMS
        NATIVE_BINARY
        NATIVE_FACTORY_SELECTION_INTENT
        NATIVE_FACTORY_BINARY_EVIDENCE)
    if(NOT DEFINED ${required} OR
       NOT IS_ABSOLUTE "${${required}}")
        message(FATAL_ERROR "${required} must be an absolute path")
    endif()
endforeach()

include("${NATIVE_FACTORY_VARIANTS_MODULE}")

function(assert_source family variant expected)
    vrrecorder_resolve_native_factory_source(
        actual "${family}" "${variant}")
    if(NOT actual STREQUAL expected)
        message(
            FATAL_ERROR
            "${family}/${variant} selected '${actual}', expected '${expected}'")
    endif()
endfunction()

assert_source(
    MEDIA UNAVAILABLE src/unavailable_media_backend.cpp)
assert_source(
    MEDIA PRODUCTION src/production_media_backend.cpp)
assert_source(
    ENCODER_PROBE UNAVAILABLE src/unavailable_encoder_probe_backend.cpp)
assert_source(
    ENCODER_PROBE PRODUCTION src/production_encoder_probe_backend.cpp)
assert_source(
    SPOUT UNAVAILABLE src/unavailable_spout_source_backend.cpp)
assert_source(
    SPOUT PRODUCTION src/spout2_source_backend.cpp)
assert_source(
    STEAMVR UNAVAILABLE src/unavailable_steamvr_input_backend.cpp)
assert_source(
    STEAMVR PRODUCTION src/openvr_steamvr_input_backend.cpp)

set(work_root "${NATIVE_FACTORY_CONTRACT_WORK_ROOT}")
set(fake_source_root "${work_root}/source")
set(runner "${work_root}/runner.cmake")
file(REMOVE_RECURSE "${work_root}")
file(MAKE_DIRECTORY "${fake_source_root}/src")

foreach(source IN ITEMS
        unavailable_media_backend.cpp
        production_media_backend.cpp
        unavailable_encoder_probe_backend.cpp
        production_encoder_probe_backend.cpp
        unavailable_spout_source_backend.cpp
        spout2_source_backend.cpp
        unavailable_steamvr_input_backend.cpp
        openvr_steamvr_input_backend.cpp)
    file(WRITE "${fake_source_root}/src/${source}" "// contract fixture\n")
endforeach()

function(run_selection expected_success label)
    execute_process(
        COMMAND
            "${CMAKE_COMMAND}"
            "-DMODULE=${NATIVE_FACTORY_VARIANTS_MODULE}"
            "-DSOURCE_ROOT=${fake_source_root}"
            "-DMEDIA=${ARGN}"
            -P "${runner}"
        RESULT_VARIABLE result
        OUTPUT_VARIABLE output
        ERROR_VARIABLE error)
    if(expected_success AND NOT result EQUAL 0)
        message(
            FATAL_ERROR
            "${label} should pass (${result}):\n${output}\n${error}")
    endif()
    if(NOT expected_success AND result EQUAL 0)
        message(FATAL_ERROR "${label} should fail closed, but passed")
    endif()
endfunction()

file(
    WRITE "${runner}"
    "cmake_minimum_required(VERSION 3.24)\n"
    "include(\"\${MODULE}\")\n"
    "set(VRRECORDER_MEDIA_FACTORY_VARIANT \"\${MEDIA}\")\n"
    "set(VRRECORDER_ENCODER_PROBE_FACTORY_VARIANT \"UNAVAILABLE\")\n"
    "set(VRRECORDER_SPOUT_FACTORY_VARIANT \"UNAVAILABLE\")\n"
    "set(VRRECORDER_STEAMVR_FACTORY_VARIANT \"UNAVAILABLE\")\n"
    "set(VRRECORDER_REQUIRE_FULL_PRODUCTION_FACTORIES OFF)\n"
    "set(VRRECORDER_ENABLE_FFMPEG_ADAPTERS OFF)\n"
    "vrrecorder_select_native_factory_sources(sources \"\${SOURCE_ROOT}\")\n")

run_selection(TRUE "portable selection" UNAVAILABLE)
run_selection(FALSE "empty variant" "")
run_selection(FALSE "combined variant" BOTH)
run_selection(FALSE "unknown variant" HARDWARE)

file(
    WRITE "${runner}"
    "cmake_minimum_required(VERSION 3.24)\n"
    "include(\"\${MODULE}\")\n"
    "set(VRRECORDER_MEDIA_FACTORY_VARIANT \"PRODUCTION\")\n"
    "set(VRRECORDER_ENCODER_PROBE_FACTORY_VARIANT \"PRODUCTION\")\n"
    "set(VRRECORDER_SPOUT_FACTORY_VARIANT \"PRODUCTION\")\n"
    "set(VRRECORDER_STEAMVR_FACTORY_VARIANT \"PRODUCTION\")\n"
    "set(VRRECORDER_REQUIRE_FULL_PRODUCTION_FACTORIES ON)\n"
    "set(VRRECORDER_ENABLE_FFMPEG_ADAPTERS ON)\n"
    "vrrecorder_select_native_factory_sources(sources \"\${SOURCE_ROOT}\")\n"
    "list(LENGTH sources source_count)\n"
    "if(NOT source_count EQUAL 4)\n"
    "  message(FATAL_ERROR \"exactly four factory sources are required\")\n"
    "endif()\n"
    "foreach(source IN LISTS sources)\n"
    "  if(source MATCHES \"unavailable_\")\n"
    "    message(FATAL_ERROR \"full production selected a placeholder\")\n"
    "  endif()\n"
    "endforeach()\n")
run_selection(TRUE "full production selection" PRODUCTION)

file(REMOVE "${fake_source_root}/src/production_media_backend.cpp")
run_selection(FALSE "missing selected production source" PRODUCTION)
file(
    WRITE
    "${fake_source_root}/src/production_media_backend.cpp"
    "// contract fixture\n")

file(
    WRITE "${runner}"
    "cmake_minimum_required(VERSION 3.24)\n"
    "include(\"\${MODULE}\")\n"
    "set(VRRECORDER_MEDIA_FACTORY_VARIANT \"PRODUCTION\")\n"
    "set(VRRECORDER_ENCODER_PROBE_FACTORY_VARIANT \"PRODUCTION\")\n"
    "set(VRRECORDER_SPOUT_FACTORY_VARIANT \"PRODUCTION\")\n"
    "set(VRRECORDER_STEAMVR_FACTORY_VARIANT \"UNAVAILABLE\")\n"
    "set(VRRECORDER_REQUIRE_FULL_PRODUCTION_FACTORIES ON)\n"
    "set(VRRECORDER_ENABLE_FFMPEG_ADAPTERS ON)\n"
    "vrrecorder_select_native_factory_sources(sources \"\${SOURCE_ROOT}\")\n")
run_selection(FALSE "incomplete full production selection" PRODUCTION)

file(
    WRITE "${runner}"
    "cmake_minimum_required(VERSION 3.24)\n"
    "include(\"\${MODULE}\")\n"
    "set(VRRECORDER_MEDIA_FACTORY_VARIANT \"PRODUCTION\")\n"
    "set(VRRECORDER_ENCODER_PROBE_FACTORY_VARIANT \"UNAVAILABLE\")\n"
    "set(VRRECORDER_SPOUT_FACTORY_VARIANT \"UNAVAILABLE\")\n"
    "set(VRRECORDER_STEAMVR_FACTORY_VARIANT \"UNAVAILABLE\")\n"
    "set(VRRECORDER_REQUIRE_FULL_PRODUCTION_FACTORIES OFF)\n"
    "set(VRRECORDER_ENABLE_FFMPEG_ADAPTERS OFF)\n"
    "vrrecorder_select_native_factory_sources(sources \"\${SOURCE_ROOT}\")\n")
run_selection(FALSE "production media without pinned FFmpeg" PRODUCTION)

set(VRRECORDER_MEDIA_FACTORY_VARIANT UNAVAILABLE)
set(VRRECORDER_ENCODER_PROBE_FACTORY_VARIANT UNAVAILABLE)
set(VRRECORDER_SPOUT_FACTORY_VARIANT UNAVAILABLE)
set(VRRECORDER_STEAMVR_FACTORY_VARIANT UNAVAILABLE)
set(VRRECORDER_REQUIRE_FULL_PRODUCTION_FACTORIES OFF)
set(VRRECORDER_ENABLE_FFMPEG_ADAPTERS OFF)
vrrecorder_select_native_factory_sources(
    selected_sources "${NATIVE_SOURCE_ROOT}")
set(expected_sources
    "${NATIVE_SOURCE_ROOT}/src/unavailable_media_backend.cpp"
    "${NATIVE_SOURCE_ROOT}/src/unavailable_encoder_probe_backend.cpp"
    "${NATIVE_SOURCE_ROOT}/src/unavailable_spout_source_backend.cpp"
    "${NATIVE_SOURCE_ROOT}/src/unavailable_steamvr_input_backend.cpp")
if(NOT "${selected_sources}" STREQUAL "${expected_sources}")
    message(FATAL_ERROR "portable target source selection is not exact")
endif()

set(intent "${work_root}/native-factory-selection.intent.json")
vrrecorder_write_native_factory_selection_intent("${intent}")
file(READ "${intent}" intent_json)
foreach(required IN ITEMS
        [["schemaVersion": 1]]
        [["evidenceKind": "native-factory-selection-intent"]]
        [["media": {"variant": "UNAVAILABLE", "source": "unavailable_media_backend.cpp"}]]
        [["encoderProbe": {"variant": "UNAVAILABLE", "source": "unavailable_encoder_probe_backend.cpp"}]]
        [["spout": {"variant": "UNAVAILABLE", "source": "unavailable_spout_source_backend.cpp"}]]
        [["steamVr": {"variant": "UNAVAILABLE", "source": "unavailable_steamvr_input_backend.cpp"}]])
    string(FIND "${intent_json}" "${required}" required_offset)
    if(required_offset EQUAL -1)
        message(FATAL_ERROR "factory intent is missing: ${required}")
    endif()
endforeach()

foreach(required_file IN ITEMS
        "${NATIVE_BINARY}"
        "${NATIVE_FACTORY_SELECTION_INTENT}"
        "${NATIVE_FACTORY_BINARY_EVIDENCE}")
    if(NOT EXISTS "${required_file}" OR IS_DIRECTORY "${required_file}")
        message(
            FATAL_ERROR
            "linked native factory evidence input is missing: ${required_file}")
    endif()
endforeach()
file(SHA256 "${NATIVE_BINARY}" expected_binary_sha256)
file(SIZE "${NATIVE_BINARY}" expected_binary_length)
cmake_path(GET NATIVE_BINARY FILENAME expected_binary_name)
file(READ "${NATIVE_FACTORY_SELECTION_INTENT}" linked_intent_json)
string(SHA256 expected_intent_sha256 "${linked_intent_json}")
file(READ "${NATIVE_FACTORY_BINARY_EVIDENCE}" binary_evidence_json)
foreach(property IN ITEMS schemaVersion evidenceKind)
    string(
        JSON value
        ERROR_VARIABLE json_error
        GET "${binary_evidence_json}" "${property}")
    if(NOT json_error STREQUAL "NOTFOUND")
        message(FATAL_ERROR "linked factory evidence is missing ${property}")
    endif()
    set("evidence_${property}" "${value}")
endforeach()
if(NOT evidence_schemaVersion STREQUAL "1" OR
   NOT evidence_evidenceKind STREQUAL "linked-native-factory-selection")
    message(FATAL_ERROR "linked factory evidence identity is invalid")
endif()
string(
    JSON observed_intent_sha256
    ERROR_VARIABLE intent_json_error
    GET "${binary_evidence_json}" selectionIntentSha256)
if(NOT intent_json_error STREQUAL "NOTFOUND" OR
   NOT observed_intent_sha256 STREQUAL expected_intent_sha256)
    message(FATAL_ERROR "linked factory evidence has a stale selection intent")
endif()
foreach(mapping IN ITEMS
        "file;${expected_binary_name}"
        "length;${expected_binary_length}"
        "sha256;${expected_binary_sha256}")
    list(GET mapping 0 property)
    list(GET mapping 1 expected)
    string(
        JSON actual
        ERROR_VARIABLE json_error
        GET "${binary_evidence_json}" nativeBinary "${property}")
    if(NOT json_error STREQUAL "NOTFOUND" OR NOT actual STREQUAL expected)
        message(
            FATAL_ERROR
            "linked factory evidence nativeBinary.${property} is invalid")
    endif()
endforeach()

set(writer_binary "${work_root}/writer-native.dll")
set(writer_evidence "${work_root}/writer-evidence.json")
string(SHA256 writer_intent_sha256 "${intent_json}")
file(
    WRITE "${writer_binary}"
    "native factory evidence fixture\n"
    "VRRECORDER_FACTORY_SELECTION_V1:${writer_intent_sha256}\n")

function(run_evidence_writer expected_success label writer_intent writer_native)
    execute_process(
        COMMAND
            "${CMAKE_COMMAND}"
            "-DNATIVE_FACTORY_VARIANTS_MODULE=${NATIVE_FACTORY_VARIANTS_MODULE}"
            "-DSELECTION_INTENT=${writer_intent}"
            "-DNATIVE_BINARY=${writer_native}"
            "-DOUTPUT_PATH=${writer_evidence}"
            -P "${NATIVE_FACTORY_EVIDENCE_WRITER}"
        RESULT_VARIABLE result
        OUTPUT_VARIABLE output
        ERROR_VARIABLE error)
    if(expected_success AND NOT result EQUAL 0)
        message(
            FATAL_ERROR
            "${label} should pass (${result}):\n${output}\n${error}")
    endif()
    if(NOT expected_success AND result EQUAL 0)
        message(FATAL_ERROR "${label} should fail closed, but passed")
    endif()
endfunction()

run_evidence_writer(
    TRUE "valid linked binary evidence" "${intent}" "${writer_binary}")
file(SHA256 "${writer_binary}" writer_binary_sha256)
file(READ "${writer_evidence}" writer_evidence_json)
string(
    JSON writer_observed_sha256
    ERROR_VARIABLE writer_json_error
    GET "${writer_evidence_json}" nativeBinary sha256)
if(NOT writer_json_error STREQUAL "NOTFOUND" OR
   NOT writer_observed_sha256 STREQUAL writer_binary_sha256)
    message(FATAL_ERROR "writer evidence is not bound to the input binary")
endif()

string(
    REPLACE
        "unavailable_media_backend.cpp"
        "production_media_backend.cpp"
        tampered_intent
        "${intent_json}")
set(tampered_intent_path "${work_root}/tampered-selection.intent.json")
file(WRITE "${tampered_intent_path}" "${tampered_intent}")
run_evidence_writer(
    FALSE
    "source and variant mismatch"
    "${tampered_intent_path}"
    "${writer_binary}")

set(VRRECORDER_MEDIA_FACTORY_VARIANT PRODUCTION)
set(VRRECORDER_ENCODER_PROBE_FACTORY_VARIANT PRODUCTION)
set(VRRECORDER_SPOUT_FACTORY_VARIANT PRODUCTION)
set(VRRECORDER_STEAMVR_FACTORY_VARIANT PRODUCTION)
set(VRRECORDER_REQUIRE_FULL_PRODUCTION_FACTORIES ON)
set(swapped_intent_path "${work_root}/swapped-selection.intent.json")
vrrecorder_write_native_factory_selection_intent("${swapped_intent_path}")
run_evidence_writer(
    FALSE
    "valid but unlinked production intent"
    "${swapped_intent_path}"
    "${writer_binary}")
set(VRRECORDER_MEDIA_FACTORY_VARIANT UNAVAILABLE)
set(VRRECORDER_ENCODER_PROBE_FACTORY_VARIANT UNAVAILABLE)
set(VRRECORDER_SPOUT_FACTORY_VARIANT UNAVAILABLE)
set(VRRECORDER_STEAMVR_FACTORY_VARIANT UNAVAILABLE)
set(VRRECORDER_REQUIRE_FULL_PRODUCTION_FACTORIES OFF)

run_evidence_writer(
    FALSE
    "missing linked native binary"
    "${intent}"
    "${work_root}/missing-native.dll")

file(READ "${NATIVE_SOURCE_ROOT}/CMakeLists.txt" native_cmake)
if(NOT native_cmake MATCHES "vrrecorder_select_native_factory_sources")
    message(FATAL_ERROR "the native target must use the exactly-one selector")
endif()
foreach(forbidden IN ITEMS
        src/unavailable_media_backend.cpp
        src/unavailable_encoder_probe_backend.cpp
        src/unavailable_spout_source_backend.cpp
        src/unavailable_steamvr_input_backend.cpp)
    string(FIND "${native_cmake}" "${forbidden}" forbidden_offset)
    if(NOT forbidden_offset EQUAL -1)
        message(
            FATAL_ERROR
            "the native target hard-codes a placeholder outside the selector: ${forbidden}")
    endif()
endforeach()

message(STATUS "Native factory variant contract passed")
