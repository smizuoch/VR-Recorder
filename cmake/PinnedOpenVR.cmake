include_guard(GLOBAL)

set(VRRECORDER_OPENVR_VERSION "2.15.6")
set(VRRECORDER_OPENVR_TAG "v2.15.6")
set(
    VRRECORDER_OPENVR_SOURCE_COMMIT
    "0924064316de3effbcd1acf1e309182a2deb1c05")
set(
    VRRECORDER_OPENVR_TAG_OBJECT
    "41bc3825fd35b04047610c86fee26fb33b017b29")
set(
    VRRECORDER_OPENVR_SOURCE_ARCHIVE_PATH
    "share/vrrecorder/sources/OpenVR-v2.15.6.tar.gz")
set(VRRECORDER_OPENVR_SOURCE_ARCHIVE_LENGTH "154998016")
set(
    VRRECORDER_OPENVR_SOURCE_ARCHIVE_SHA256
    "e184cb625010fab7043a9d5e1e000fdeb3067a152bb3169ef53f64dfac37164c")
set(
    VRRECORDER_OPENVR_LICENSE_PATH
    "share/vrrecorder/licenses/OpenVR-LICENSE.txt")
set(VRRECORDER_OPENVR_LICENSE_LENGTH "1488")
set(
    VRRECORDER_OPENVR_LICENSE_SHA256
    "f56ff606104d4ef18e617921a75c73ad73b5a1a1d70c69590c29de16919e04ad")
set(
    VRRECORDER_OPENVR_BUILD_RECIPE_PATH
    "share/vrrecorder/build-recipes/openvr-windows-x64-sdk.md")
set(VRRECORDER_OPENVR_BUILD_RECIPE_LENGTH "2249")
set(
    VRRECORDER_OPENVR_BUILD_RECIPE_SHA256
    "4f1fcbffe5f352d5f8c5252861dc2c9fca670f227d86d84a099fc22af6da61ca")
set(
    VRRECORDER_OPENVR_ARTIFACT_IDENTITIES
    "include/openvr.h|296217|1e6ed57199896cc1f7c5484e50fa18955e97be15be690beb28d998c877ead7fd"
    "lib/openvr_api.lib|5500|a0bf57c5920f569e8d21ab3e5bc95bac4b73e2016217f8b5b93495a2a7197bbb"
    "bin/openvr_api.dll|837272|bab8ac6ef64e68a9ca53315b0014d131088584b2efdfa6db511d67ec03cfcb4a"
    "bin/openvr_api.dll.sig|1450|6a47bb6e5e3d6850aef60abf4fb6b6f1799bee65f2af3bbdc89dac00b843bc5b")

function(_vrrecorder_openvr_require_json_value json property expected)
    string(JSON actual ERROR_VARIABLE json_error GET "${json}" "${property}")
    if(NOT json_error STREQUAL "NOTFOUND" OR NOT actual STREQUAL expected)
        message(
            FATAL_ERROR
            "Pinned OpenVR evidence property ${property} must be '${expected}'")
    endif()
endfunction()

function(_vrrecorder_openvr_require_exact_members json)
    set(expected ${ARGN})
    list(SORT expected)
    string(JSON count ERROR_VARIABLE json_error LENGTH "${json}")
    if(NOT json_error STREQUAL "NOTFOUND")
        message(FATAL_ERROR "Pinned OpenVR evidence object is invalid")
    endif()
    set(actual "")
    if(count GREATER 0)
        math(EXPR last "${count} - 1")
        foreach(index RANGE 0 ${last})
            string(JSON member ERROR_VARIABLE json_error MEMBER "${json}" ${index})
            if(NOT json_error STREQUAL "NOTFOUND")
                message(FATAL_ERROR "Pinned OpenVR evidence object is invalid")
            endif()
            list(APPEND actual "${member}")
        endforeach()
    endif()
    list(SORT actual)
    if(NOT "${actual}" STREQUAL "${expected}")
        message(FATAL_ERROR "Pinned OpenVR evidence object has unexpected fields")
    endif()
endfunction()

function(
        _vrrecorder_openvr_require_file
        root evidence path_property length_property sha_property
        expected_path expected_length expected_sha256 description)
    _vrrecorder_openvr_require_json_value(
        "${evidence}" "${path_property}" "${expected_path}")
    _vrrecorder_openvr_require_json_value(
        "${evidence}" "${length_property}" "${expected_length}")
    _vrrecorder_openvr_require_json_value(
        "${evidence}" "${sha_property}" "${expected_sha256}")
    set(path "${root}/${expected_path}")
    if(NOT EXISTS "${path}" OR IS_DIRECTORY "${path}" OR IS_SYMLINK "${path}")
        message(FATAL_ERROR "Pinned OpenVR ${description} is missing: ${path}")
    endif()
    file(SIZE "${path}" length)
    file(SHA256 "${path}" sha256)
    if(NOT length STREQUAL expected_length OR
       NOT sha256 STREQUAL expected_sha256)
        message(FATAL_ERROR "Pinned OpenVR ${description} identity does not match")
    endif()
endfunction()

function(vrrecorder_validate_pinned_openvr_sdk root)
    if(root STREQUAL "" OR NOT IS_ABSOLUTE "${root}")
        message(FATAL_ERROR "Pinned OpenVR SDK root must be absolute")
    endif()
    cmake_path(NORMAL_PATH root OUTPUT_VARIABLE normalized_root)
    if(NOT IS_DIRECTORY "${normalized_root}" OR IS_SYMLINK "${normalized_root}")
        message(FATAL_ERROR "Pinned OpenVR SDK root is missing or invalid")
    endif()

    set(evidence_path
        "${normalized_root}/share/vrrecorder/openvr-sdk-evidence.json")
    if(NOT EXISTS "${evidence_path}" OR
       IS_DIRECTORY "${evidence_path}" OR
       IS_SYMLINK "${evidence_path}")
        message(FATAL_ERROR "Pinned OpenVR SDK evidence is missing")
    endif()
    file(READ "${evidence_path}" evidence)
    _vrrecorder_openvr_require_exact_members(
        "${evidence}"
        architecture
        artifacts
        buildRecipeLength
        buildRecipePath
        buildRecipeSha256
        component
        deployment
        licenseLength
        licensePath
        licenseSha256
        schemaVersion
        sourceArchiveLength
        sourceArchivePath
        sourceArchiveSha256
        sourceCommit
        tag
        tagObject
        version)
    _vrrecorder_openvr_require_json_value("${evidence}" schemaVersion "1")
    _vrrecorder_openvr_require_json_value("${evidence}" component "openvr")
    _vrrecorder_openvr_require_json_value(
        "${evidence}" version "${VRRECORDER_OPENVR_VERSION}")
    _vrrecorder_openvr_require_json_value(
        "${evidence}" tag "${VRRECORDER_OPENVR_TAG}")
    _vrrecorder_openvr_require_json_value(
        "${evidence}" sourceCommit "${VRRECORDER_OPENVR_SOURCE_COMMIT}")
    _vrrecorder_openvr_require_json_value(
        "${evidence}" tagObject "${VRRECORDER_OPENVR_TAG_OBJECT}")
    _vrrecorder_openvr_require_json_value("${evidence}" architecture "x86_64")
    _vrrecorder_openvr_require_json_value("${evidence}" deployment "dynamic")

    _vrrecorder_openvr_require_file(
        "${normalized_root}" "${evidence}"
        sourceArchivePath sourceArchiveLength sourceArchiveSha256
        "${VRRECORDER_OPENVR_SOURCE_ARCHIVE_PATH}"
        "${VRRECORDER_OPENVR_SOURCE_ARCHIVE_LENGTH}"
        "${VRRECORDER_OPENVR_SOURCE_ARCHIVE_SHA256}"
        "source archive")
    _vrrecorder_openvr_require_file(
        "${normalized_root}" "${evidence}"
        licensePath licenseLength licenseSha256
        "${VRRECORDER_OPENVR_LICENSE_PATH}"
        "${VRRECORDER_OPENVR_LICENSE_LENGTH}"
        "${VRRECORDER_OPENVR_LICENSE_SHA256}"
        "license")
    _vrrecorder_openvr_require_file(
        "${normalized_root}" "${evidence}"
        buildRecipePath buildRecipeLength buildRecipeSha256
        "${VRRECORDER_OPENVR_BUILD_RECIPE_PATH}"
        "${VRRECORDER_OPENVR_BUILD_RECIPE_LENGTH}"
        "${VRRECORDER_OPENVR_BUILD_RECIPE_SHA256}"
        "build recipe")

    string(JSON artifact_count ERROR_VARIABLE json_error LENGTH "${evidence}" artifacts)
    list(LENGTH VRRECORDER_OPENVR_ARTIFACT_IDENTITIES expected_count)
    if(NOT json_error STREQUAL "NOTFOUND" OR
       NOT artifact_count EQUAL expected_count)
        message(FATAL_ERROR "Pinned OpenVR artifact count does not match")
    endif()
    set(expected_inventory
        "share/vrrecorder/openvr-sdk-evidence.json"
        "${VRRECORDER_OPENVR_SOURCE_ARCHIVE_PATH}"
        "${VRRECORDER_OPENVR_LICENSE_PATH}"
        "${VRRECORDER_OPENVR_BUILD_RECIPE_PATH}")
    if(artifact_count GREATER 0)
        math(EXPR last_artifact "${artifact_count} - 1")
        foreach(index RANGE 0 ${last_artifact})
            list(GET VRRECORDER_OPENVR_ARTIFACT_IDENTITIES ${index} identity)
            string(REPLACE "|" ";" fields "${identity}")
            list(GET fields 0 expected_path)
            list(GET fields 1 expected_length)
            list(GET fields 2 expected_sha256)
            string(JSON artifact ERROR_VARIABLE json_error GET "${evidence}" artifacts ${index})
            if(NOT json_error STREQUAL "NOTFOUND")
                message(FATAL_ERROR "Pinned OpenVR artifact evidence is invalid")
            endif()
            _vrrecorder_openvr_require_exact_members(
                "${artifact}" length path sha256)
            _vrrecorder_openvr_require_file(
                "${normalized_root}" "${artifact}"
                path length sha256
                "${expected_path}" "${expected_length}" "${expected_sha256}"
                "artifact ${expected_path}")
            list(APPEND expected_inventory "${expected_path}")
        endforeach()
    endif()

    file(
        GLOB_RECURSE actual_inventory
        LIST_DIRECTORIES false
        RELATIVE "${normalized_root}"
        "${normalized_root}/*")
    list(TRANSFORM actual_inventory REPLACE "\\\\" "/")
    list(SORT actual_inventory)
    list(SORT expected_inventory)
    if(NOT "${actual_inventory}" STREQUAL "${expected_inventory}")
        message(FATAL_ERROR "Pinned OpenVR SDK inventory is not exact")
    endif()
endfunction()

function(vrrecorder_import_pinned_openvr_sdk root)
    if(NOT WIN32 OR NOT MSVC)
        message(FATAL_ERROR "Pinned OpenVR SDK import requires Windows MSVC")
    endif()
    vrrecorder_validate_pinned_openvr_sdk("${root}")
    cmake_path(NORMAL_PATH root OUTPUT_VARIABLE normalized_root)
    if(TARGET OpenVR::openvr_api)
        message(FATAL_ERROR "Pinned OpenVR target already exists")
    endif()

    add_library(OpenVR::openvr_api SHARED IMPORTED GLOBAL)
    set_target_properties(
        OpenVR::openvr_api
        PROPERTIES
            IMPORTED_IMPLIB "${normalized_root}/lib/openvr_api.lib"
            IMPORTED_LOCATION "${normalized_root}/bin/openvr_api.dll"
            INTERFACE_INCLUDE_DIRECTORIES "${normalized_root}/include")
endfunction()
