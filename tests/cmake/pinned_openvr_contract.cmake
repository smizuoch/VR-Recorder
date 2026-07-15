cmake_minimum_required(VERSION 3.24)

if(NOT DEFINED PINNED_OPENVR_MODULE OR
   NOT IS_ABSOLUTE "${PINNED_OPENVR_MODULE}")
    message(FATAL_ERROR "PINNED_OPENVR_MODULE must be absolute")
endif()

set(work_root "${CMAKE_CURRENT_BINARY_DIR}/pinned-openvr-contract")
set(sdk_root "${work_root}/sdk")
set(runner "${work_root}/validate.cmake")
set(project_root "${work_root}/project")
file(REMOVE_RECURSE "${work_root}")
file(MAKE_DIRECTORY
    "${sdk_root}/include"
    "${sdk_root}/lib"
    "${sdk_root}/bin"
    "${sdk_root}/share/vrrecorder/sources"
    "${sdk_root}/share/vrrecorder/licenses"
    "${sdk_root}/share/vrrecorder/build-recipes"
    "${project_root}")

set(artifact_paths
    include/openvr.h
    lib/openvr_api.lib
    bin/openvr_api.dll
    bin/openvr_api.dll.sig)
foreach(path IN LISTS artifact_paths)
    file(WRITE "${sdk_root}/${path}" "fake OpenVR artifact ${path}\n")
endforeach()
set(source_path "share/vrrecorder/sources/OpenVR-v2.15.6.tar.gz")
set(license_path "share/vrrecorder/licenses/OpenVR-LICENSE.txt")
set(recipe_path "share/vrrecorder/build-recipes/openvr-windows-x64-sdk.md")
file(WRITE "${sdk_root}/${source_path}" "fake source archive\n")
file(WRITE "${sdk_root}/${license_path}" "fake license\n")
file(WRITE "${sdk_root}/${recipe_path}" "fake recipe\n")

function(identity path prefix)
    file(SIZE "${sdk_root}/${path}" length)
    file(SHA256 "${sdk_root}/${path}" sha256)
    set(${prefix}_length "${length}" PARENT_SCOPE)
    set(${prefix}_sha256 "${sha256}" PARENT_SCOPE)
endfunction()
identity("${source_path}" source)
identity("${license_path}" license)
identity("${recipe_path}" recipe)

set(artifact_identities "")
set(artifact_json "")
list(LENGTH artifact_paths artifact_count)
set(index 0)
foreach(path IN LISTS artifact_paths)
    identity("${path}" artifact)
    list(APPEND artifact_identities
        "${path}|${artifact_length}|${artifact_sha256}")
    math(EXPR index "${index} + 1")
    set(comma ",")
    if(index EQUAL artifact_count)
        set(comma "")
    endif()
    string(APPEND artifact_json
        "    {\"path\": \"${path}\", \"length\": ${artifact_length}, \"sha256\": \"${artifact_sha256}\"}${comma}\n")
endforeach()

set(evidence_path "${sdk_root}/share/vrrecorder/openvr-sdk-evidence.json")
file(WRITE "${evidence_path}"
    "{\n"
    "  \"schemaVersion\": 1,\n"
    "  \"component\": \"openvr\",\n"
    "  \"version\": \"2.15.6\",\n"
    "  \"tag\": \"v2.15.6\",\n"
    "  \"sourceCommit\": \"0924064316de3effbcd1acf1e309182a2deb1c05\",\n"
    "  \"tagObject\": \"41bc3825fd35b04047610c86fee26fb33b017b29\",\n"
    "  \"architecture\": \"x86_64\",\n"
    "  \"deployment\": \"dynamic\",\n"
    "  \"sourceArchivePath\": \"${source_path}\",\n"
    "  \"sourceArchiveLength\": ${source_length},\n"
    "  \"sourceArchiveSha256\": \"${source_sha256}\",\n"
    "  \"licensePath\": \"${license_path}\",\n"
    "  \"licenseLength\": ${license_length},\n"
    "  \"licenseSha256\": \"${license_sha256}\",\n"
    "  \"buildRecipePath\": \"${recipe_path}\",\n"
    "  \"buildRecipeLength\": ${recipe_length},\n"
    "  \"buildRecipeSha256\": \"${recipe_sha256}\",\n"
    "  \"artifacts\": [\n${artifact_json}  ]\n}\n")
file(READ "${evidence_path}" valid_evidence)

file(WRITE "${runner}"
    "cmake_minimum_required(VERSION 3.24)\n"
    "include(\"${PINNED_OPENVR_MODULE}\")\n"
    "set(VRRECORDER_OPENVR_SOURCE_ARCHIVE_LENGTH \"${source_length}\")\n"
    "set(VRRECORDER_OPENVR_SOURCE_ARCHIVE_SHA256 \"${source_sha256}\")\n"
    "set(VRRECORDER_OPENVR_LICENSE_LENGTH \"${license_length}\")\n"
    "set(VRRECORDER_OPENVR_LICENSE_SHA256 \"${license_sha256}\")\n"
    "set(VRRECORDER_OPENVR_BUILD_RECIPE_LENGTH \"${recipe_length}\")\n"
    "set(VRRECORDER_OPENVR_BUILD_RECIPE_SHA256 \"${recipe_sha256}\")\n"
    "set(VRRECORDER_OPENVR_ARTIFACT_IDENTITIES \"${artifact_identities}\")\n"
    "vrrecorder_validate_pinned_openvr_sdk(\"\${SDK_ROOT}\")\n")

function(run expected label)
    execute_process(
        COMMAND "${CMAKE_COMMAND}" "-DSDK_ROOT=${sdk_root}" -P "${runner}"
        RESULT_VARIABLE result OUTPUT_VARIABLE output ERROR_VARIABLE error)
    if(expected AND NOT result EQUAL 0)
        message(FATAL_ERROR "${label} should pass:\n${output}\n${error}")
    endif()
    if(NOT expected AND result EQUAL 0)
        message(FATAL_ERROR "${label} should fail closed")
    endif()
endfunction()

run(TRUE "exact OpenVR SDK")
file(APPEND "${sdk_root}/bin/openvr_api.dll" "tamper\n")
run(FALSE "runtime tamper")
file(WRITE "${sdk_root}/bin/openvr_api.dll"
    "fake OpenVR artifact bin/openvr_api.dll\n")
file(WRITE "${sdk_root}/unexpected.txt" "unexpected\n")
run(FALSE "extra inventory")
file(REMOVE "${sdk_root}/unexpected.txt")
string(REPLACE
    "\"deployment\": \"dynamic\""
    "\"deployment\": \"static\""
    wrong_deployment "${valid_evidence}")
file(WRITE "${evidence_path}" "${wrong_deployment}")
run(FALSE "wrong deployment")
file(WRITE "${evidence_path}" "${valid_evidence}")

file(WRITE "${project_root}/CMakeLists.txt"
    "cmake_minimum_required(VERSION 3.24)\n"
    "project(PinnedOpenVRImport LANGUAGES NONE)\n"
    "set(WIN32 TRUE)\n"
    "set(MSVC TRUE)\n"
    "include(\"${PINNED_OPENVR_MODULE}\")\n"
    "set(VRRECORDER_OPENVR_SOURCE_ARCHIVE_LENGTH \"${source_length}\")\n"
    "set(VRRECORDER_OPENVR_SOURCE_ARCHIVE_SHA256 \"${source_sha256}\")\n"
    "set(VRRECORDER_OPENVR_LICENSE_LENGTH \"${license_length}\")\n"
    "set(VRRECORDER_OPENVR_LICENSE_SHA256 \"${license_sha256}\")\n"
    "set(VRRECORDER_OPENVR_BUILD_RECIPE_LENGTH \"${recipe_length}\")\n"
    "set(VRRECORDER_OPENVR_BUILD_RECIPE_SHA256 \"${recipe_sha256}\")\n"
    "set(VRRECORDER_OPENVR_ARTIFACT_IDENTITIES \"${artifact_identities}\")\n"
    "vrrecorder_import_pinned_openvr_sdk(\"\${SDK_ROOT}\")\n"
    "get_target_property(location OpenVR::openvr_api IMPORTED_LOCATION)\n"
    "get_target_property(implib OpenVR::openvr_api IMPORTED_IMPLIB)\n"
    "if(NOT location STREQUAL \"\${SDK_ROOT}/bin/openvr_api.dll\" OR\n"
    "   NOT implib STREQUAL \"\${SDK_ROOT}/lib/openvr_api.lib\")\n"
    "  message(FATAL_ERROR \"OpenVR imported locations drifted\")\n"
    "endif()\n")
execute_process(
    COMMAND "${CMAKE_COMMAND}"
        -S "${project_root}"
        -B "${work_root}/project-build"
        "-DSDK_ROOT=${sdk_root}"
    RESULT_VARIABLE configure_result
    OUTPUT_VARIABLE configure_output
    ERROR_VARIABLE configure_error)
if(NOT configure_result EQUAL 0)
    message(FATAL_ERROR
        "Pinned OpenVR import should configure:\n${configure_output}\n${configure_error}")
endif()
