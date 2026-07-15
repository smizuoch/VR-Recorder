cmake_minimum_required(VERSION 3.24)

if(NOT DEFINED PINNED_SPOUT2_MODULE OR
   NOT IS_ABSOLUTE "${PINNED_SPOUT2_MODULE}")
    message(FATAL_ERROR "PINNED_SPOUT2_MODULE must be absolute")
endif()
file(TO_CMAKE_PATH "${PINNED_SPOUT2_MODULE}" PINNED_SPOUT2_MODULE)

set(work_root "${CMAKE_CURRENT_BINARY_DIR}/pinned-spout2-contract")
set(sdk_root "${work_root}/sdk")
set(runner_path "${work_root}/validate.cmake")
set(project_root "${work_root}/project")
set(project_build_root "${work_root}/project-build")
file(REMOVE_RECURSE "${work_root}")
file(MAKE_DIRECTORY
    "${sdk_root}/include/SpoutDX"
    "${sdk_root}/lib"
    "${sdk_root}/share/vrrecorder/sources"
    "${sdk_root}/share/vrrecorder/licenses"
    "${sdk_root}/share/vrrecorder/build-recipes"
    "${project_root}")

set(artifact_paths
    "include/SpoutDX/SpoutCommon.h"
    "include/SpoutDX/SpoutCopy.h"
    "include/SpoutDX/SpoutDirectX.h"
    "include/SpoutDX/SpoutDX.h"
    "include/SpoutDX/SpoutFrameCount.h"
    "include/SpoutDX/SpoutSenderNames.h"
    "include/SpoutDX/SpoutSharedMemory.h"
    "include/SpoutDX/SpoutUtils.h"
    "lib/SpoutDX_static.lib"
    "lib/Spout_static.lib")

foreach(path IN LISTS artifact_paths)
    get_filename_component(parent "${sdk_root}/${path}" DIRECTORY)
    file(MAKE_DIRECTORY "${parent}")
    file(WRITE "${sdk_root}/${path}" "exact fake Spout2 artifact: ${path}\n")
endforeach()

set(binary_archive_path
    "share/vrrecorder/sources/Spout-SDK-binaries_2-007-017_1.zip")
set(source_archive_path
    "share/vrrecorder/sources/Spout2-f49e2f469f8cb25f559a6eaa61a3f5b8173fc100.tar.gz")
set(license_path "share/vrrecorder/licenses/Spout2-LICENSE.txt")
set(build_recipe_path
    "share/vrrecorder/build-recipes/spout2-windows-x64-static.md")
file(WRITE "${sdk_root}/${binary_archive_path}" "fake binary archive\n")
file(WRITE "${sdk_root}/${source_archive_path}" "fake source archive\n")
file(WRITE "${sdk_root}/${license_path}" "fake BSD-2-Clause license\n")
file(WRITE "${sdk_root}/${build_recipe_path}" "fake build recipe\n")

function(file_identity path prefix)
    file(SIZE "${sdk_root}/${path}" length)
    file(SHA256 "${sdk_root}/${path}" sha256)
    set(${prefix}_length "${length}" PARENT_SCOPE)
    set(${prefix}_sha256 "${sha256}" PARENT_SCOPE)
endfunction()

file_identity("${binary_archive_path}" binary_archive)
file_identity("${source_archive_path}" source_archive)
file_identity("${license_path}" license)
file_identity("${build_recipe_path}" build_recipe)

set(artifact_identities "")
set(artifact_json "")
list(LENGTH artifact_paths artifact_count)
set(index 0)
foreach(path IN LISTS artifact_paths)
    file(SIZE "${sdk_root}/${path}" length)
    file(SHA256 "${sdk_root}/${path}" sha256)
    list(APPEND artifact_identities "${path}|${length}|${sha256}")
    math(EXPR index "${index} + 1")
    if(index LESS artifact_count)
        set(comma ",")
    else()
        set(comma "")
    endif()
    string(APPEND artifact_json
        "    {\"path\": \"${path}\", \"length\": ${length}, \"sha256\": \"${sha256}\"}${comma}\n")
endforeach()

set(evidence_path "${sdk_root}/share/vrrecorder/spout2-sdk-evidence.json")
file(WRITE "${evidence_path}"
    "{\n"
    "  \"schemaVersion\": 1,\n"
    "  \"component\": \"spout2\",\n"
    "  \"version\": \"2.007.017\",\n"
    "  \"tag\": \"2.007.017\",\n"
    "  \"sourceCommit\": \"f49e2f469f8cb25f559a6eaa61a3f5b8173fc100\",\n"
    "  \"architecture\": \"x86_64\",\n"
    "  \"runtimeLibrary\": \"MD\",\n"
    "  \"deployment\": \"static\",\n"
    "  \"binaryArchivePath\": \"${binary_archive_path}\",\n"
    "  \"binaryArchiveLength\": ${binary_archive_length},\n"
    "  \"binaryArchiveSha256\": \"${binary_archive_sha256}\",\n"
    "  \"sourceArchivePath\": \"${source_archive_path}\",\n"
    "  \"sourceArchiveLength\": ${source_archive_length},\n"
    "  \"sourceArchiveSha256\": \"${source_archive_sha256}\",\n"
    "  \"licensePath\": \"${license_path}\",\n"
    "  \"licenseLength\": ${license_length},\n"
    "  \"licenseSha256\": \"${license_sha256}\",\n"
    "  \"buildRecipePath\": \"${build_recipe_path}\",\n"
    "  \"buildRecipeLength\": ${build_recipe_length},\n"
    "  \"buildRecipeSha256\": \"${build_recipe_sha256}\",\n"
    "  \"artifacts\": [\n"
    "${artifact_json}"
    "  ]\n"
    "}\n")
file(READ "${evidence_path}" valid_evidence)

file(WRITE "${runner_path}"
    "cmake_minimum_required(VERSION 3.24)\n"
    "include(\"${PINNED_SPOUT2_MODULE}\")\n"
    "if(NOT VRRECORDER_SPOUT2_VERSION STREQUAL \"2.007.017\")\n"
    "  message(FATAL_ERROR \"Pinned Spout2 version drifted\")\n"
    "endif()\n"
    "if(NOT VRRECORDER_SPOUT2_SOURCE_COMMIT STREQUAL \"f49e2f469f8cb25f559a6eaa61a3f5b8173fc100\")\n"
    "  message(FATAL_ERROR \"Pinned Spout2 commit drifted\")\n"
    "endif()\n"
    "set(VRRECORDER_SPOUT2_BINARY_ARCHIVE_LENGTH \"${binary_archive_length}\")\n"
    "set(VRRECORDER_SPOUT2_BINARY_ARCHIVE_SHA256 \"${binary_archive_sha256}\")\n"
    "set(VRRECORDER_SPOUT2_SOURCE_ARCHIVE_LENGTH \"${source_archive_length}\")\n"
    "set(VRRECORDER_SPOUT2_SOURCE_ARCHIVE_SHA256 \"${source_archive_sha256}\")\n"
    "set(VRRECORDER_SPOUT2_LICENSE_LENGTH \"${license_length}\")\n"
    "set(VRRECORDER_SPOUT2_LICENSE_SHA256 \"${license_sha256}\")\n"
    "set(VRRECORDER_SPOUT2_BUILD_RECIPE_LENGTH \"${build_recipe_length}\")\n"
    "set(VRRECORDER_SPOUT2_BUILD_RECIPE_SHA256 \"${build_recipe_sha256}\")\n"
    "set(VRRECORDER_SPOUT2_ARTIFACT_IDENTITIES \"${artifact_identities}\")\n"
    "vrrecorder_validate_pinned_spout2_sdk(\"\${SDK_ROOT}\")\n")

function(run_validation expected_success label)
    execute_process(
        COMMAND "${CMAKE_COMMAND}" "-DSDK_ROOT=${sdk_root}" -P "${runner_path}"
        RESULT_VARIABLE result
        OUTPUT_VARIABLE output
        ERROR_VARIABLE error)
    if(expected_success AND NOT result EQUAL 0)
        message(FATAL_ERROR
            "${label} should pass, but failed (${result}):\n${output}\n${error}")
    endif()
    if(NOT expected_success AND result EQUAL 0)
        message(FATAL_ERROR "${label} should fail closed, but passed")
    endif()
endfunction()

run_validation(TRUE "exact pinned SDK")

string(REPLACE
    "\"runtimeLibrary\": \"MD\""
    "\"runtimeLibrary\": \"MT\""
    wrong_runtime_evidence "${valid_evidence}")
file(WRITE "${evidence_path}" "${wrong_runtime_evidence}")
run_validation(FALSE "wrong runtime library")
file(WRITE "${evidence_path}" "${valid_evidence}")

string(REPLACE
    "\"deployment\": \"static\""
    "\"deployment\": \"dynamic\""
    wrong_deployment_evidence "${valid_evidence}")
file(WRITE "${evidence_path}" "${wrong_deployment_evidence}")
run_validation(FALSE "dynamic deployment")
file(WRITE "${evidence_path}" "${valid_evidence}")

file(APPEND "${sdk_root}/lib/SpoutDX_static.lib" "tamper\n")
run_validation(FALSE "static library tamper")
file(WRITE "${sdk_root}/lib/SpoutDX_static.lib"
    "exact fake Spout2 artifact: lib/SpoutDX_static.lib\n")

file(REMOVE "${sdk_root}/${source_archive_path}")
run_validation(FALSE "missing source archive")
file(WRITE "${sdk_root}/${source_archive_path}" "fake source archive\n")

string(REPLACE
    "\"path\": \"include/SpoutDX/SpoutCommon.h\""
    "\"path\": \"include/SpoutDX/Unexpected.h\""
    wrong_artifact_evidence "${valid_evidence}")
file(WRITE "${evidence_path}" "${wrong_artifact_evidence}")
run_validation(FALSE "unexpected artifact path")
file(WRITE "${evidence_path}" "${valid_evidence}")

file(WRITE "${project_root}/CMakeLists.txt"
    "cmake_minimum_required(VERSION 3.24)\n"
    "project(PinnedSpout2Import LANGUAGES NONE)\n"
    "set(WIN32 TRUE)\n"
    "set(MSVC TRUE)\n"
    "include(\"${PINNED_SPOUT2_MODULE}\")\n"
    "set(VRRECORDER_SPOUT2_BINARY_ARCHIVE_LENGTH \"${binary_archive_length}\")\n"
    "set(VRRECORDER_SPOUT2_BINARY_ARCHIVE_SHA256 \"${binary_archive_sha256}\")\n"
    "set(VRRECORDER_SPOUT2_SOURCE_ARCHIVE_LENGTH \"${source_archive_length}\")\n"
    "set(VRRECORDER_SPOUT2_SOURCE_ARCHIVE_SHA256 \"${source_archive_sha256}\")\n"
    "set(VRRECORDER_SPOUT2_LICENSE_LENGTH \"${license_length}\")\n"
    "set(VRRECORDER_SPOUT2_LICENSE_SHA256 \"${license_sha256}\")\n"
    "set(VRRECORDER_SPOUT2_BUILD_RECIPE_LENGTH \"${build_recipe_length}\")\n"
    "set(VRRECORDER_SPOUT2_BUILD_RECIPE_SHA256 \"${build_recipe_sha256}\")\n"
    "set(VRRECORDER_SPOUT2_ARTIFACT_IDENTITIES \"${artifact_identities}\")\n"
    "vrrecorder_import_pinned_spout2_sdk(\"\${SDK_ROOT}\")\n"
    "foreach(target IN ITEMS Spout2::Spout_static Spout2::SpoutDX_static)\n"
    "  if(NOT TARGET \"\${target}\")\n"
    "    message(FATAL_ERROR \"Missing imported target: \${target}\")\n"
    "  endif()\n"
    "endforeach()\n"
    "get_target_property(dx_location Spout2::SpoutDX_static IMPORTED_LOCATION_RELEASE)\n"
    "if(NOT dx_location STREQUAL \"\${SDK_ROOT}/lib/SpoutDX_static.lib\")\n"
    "  message(FATAL_ERROR \"Unexpected SpoutDX location: \${dx_location}\")\n"
    "endif()\n"
    "get_target_property(dx_links Spout2::SpoutDX_static INTERFACE_LINK_LIBRARIES)\n"
    "if(NOT dx_links STREQUAL \"Spout2::Spout_static\")\n"
    "  message(FATAL_ERROR \"Unexpected SpoutDX links: \${dx_links}\")\n"
    "endif()\n")
execute_process(
    COMMAND "${CMAKE_COMMAND}"
        -S "${project_root}"
        -B "${project_build_root}"
        "-DSDK_ROOT=${sdk_root}"
    RESULT_VARIABLE configure_result
    OUTPUT_VARIABLE configure_output
    ERROR_VARIABLE configure_error)
if(NOT configure_result EQUAL 0)
    message(FATAL_ERROR
        "Pinned Spout2 import should configure:\n${configure_output}\n${configure_error}")
endif()
