include_guard(GLOBAL)

set(VRRECORDER_SPOUT2_VERSION "2.007.017")
set(VRRECORDER_SPOUT2_TAG "2.007.017")
set(
    VRRECORDER_SPOUT2_SOURCE_COMMIT
    "f49e2f469f8cb25f559a6eaa61a3f5b8173fc100")
set(
    VRRECORDER_SPOUT2_BINARY_ARCHIVE_PATH
    "share/vrrecorder/sources/Spout-SDK-binaries_2-007-017_1.zip")
set(VRRECORDER_SPOUT2_BINARY_ARCHIVE_LENGTH "3472666")
set(
    VRRECORDER_SPOUT2_BINARY_ARCHIVE_SHA256
    "695f20e3505fa0da51b2eb959af359f5d9e2c914bb9676e9118d19f6a5424bf4")
set(
    VRRECORDER_SPOUT2_SOURCE_ARCHIVE_PATH
    "share/vrrecorder/sources/Spout2-f49e2f469f8cb25f559a6eaa61a3f5b8173fc100.tar.gz")
set(VRRECORDER_SPOUT2_SOURCE_ARCHIVE_LENGTH "4920448")
set(
    VRRECORDER_SPOUT2_SOURCE_ARCHIVE_SHA256
    "9d93cadc7fea63d3e8b26384da8f8f23982a06a07adb0363d75630a99ab1f8f1")
set(
    VRRECORDER_SPOUT2_LICENSE_PATH
    "share/vrrecorder/licenses/Spout2-LICENSE.txt")
set(VRRECORDER_SPOUT2_LICENSE_LENGTH "1326")
set(
    VRRECORDER_SPOUT2_LICENSE_SHA256
    "7b602b5c652a76ced1c6ff5f3f4c15c37a733230eeb5b8d075f1282b446b10be")
set(
    VRRECORDER_SPOUT2_BUILD_RECIPE_PATH
    "share/vrrecorder/build-recipes/spout2-windows-x64-static.md")
set(VRRECORDER_SPOUT2_BUILD_RECIPE_LENGTH "2300")
set(
    VRRECORDER_SPOUT2_BUILD_RECIPE_SHA256
    "cc14b99a8797658139b04b215ff32d4f9a800ec8dcc8f769006fd07385772535")
set(
    VRRECORDER_SPOUT2_ARTIFACT_IDENTITIES
    "include/SpoutDX/SpoutCommon.h|3422|9dbe0846831f9b396578b1b3288b684f7653c7f35718f429499097a9bf9bf063"
    "include/SpoutDX/SpoutCopy.h|9704|0ca6e26cb5c3e280ca85854b36187dfc996437f11702410f4e028cbc7e4b6503"
    "include/SpoutDX/SpoutDirectX.h|7445|809f678d270b4a22ea1854a2a83daf4c534535f20a18b2eab8fa5038086bc397"
    "include/SpoutDX/SpoutDX.h|12426|b181be79f2cc3d1830a6c4cf38d8b63dda25fdc4f3558d606b2d1d23b3816446"
    "include/SpoutDX/SpoutFrameCount.h|5943|5e09d1aa49005f3cfb8dd1ed5328b78d0223d08630c06eee66213cbcdaf3b4f9"
    "include/SpoutDX/SpoutSenderNames.h|8545|d9ce7069c9403378eac4cb4d3082ce2657504269cd92bb4b8bb3461be2ec4e83"
    "include/SpoutDX/SpoutSharedMemory.h|2772|5567da567945d0feed074c73ffcf76c5917c10506f86d167fc765da5aa62271d"
    "include/SpoutDX/SpoutUtils.h|15025|22346227a4815855ada400b4980dd9a50a026dbf6d398fefbef4d34c0f4349eb"
    "lib/SpoutDX_static.lib|1081676|1e9aa2d17d05108af2f8eebb405a8d3b81355cef4633c110efab3886b7867afb"
    "lib/Spout_static.lib|1441554|ce3fdd36584d0e722f73f7eb26b66335c5948c25933304ba206af6ad32d7edbb")

set(
    _vrrecorder_spout2_system_libraries
    opengl32
    kernel32
    user32
    gdi32
    winspool
    comdlg32
    comctl32
    advapi32
    shell32
    ole32
    oleaut32
    uuid
    odbc32
    odbccp32
    d3d9
    d3d11
    dxgi
    version
    winmm)

function(_vrrecorder_spout2_require_json_value json property expected)
    string(JSON actual ERROR_VARIABLE json_error GET "${json}" "${property}")
    if(NOT json_error STREQUAL "NOTFOUND" OR NOT actual STREQUAL expected)
        message(
            FATAL_ERROR
            "Pinned Spout2 evidence property ${property} must be '${expected}'")
    endif()
endfunction()

function(_vrrecorder_spout2_require_exact_object_members json)
    set(expected ${ARGN})
    list(SORT expected)
    string(JSON member_count ERROR_VARIABLE json_error LENGTH "${json}")
    if(NOT json_error STREQUAL "NOTFOUND")
        message(FATAL_ERROR "Pinned Spout2 evidence object is invalid")
    endif()
    set(actual "")
    if(member_count GREATER 0)
        math(EXPR last_member "${member_count} - 1")
        foreach(index RANGE 0 ${last_member})
            string(
                JSON member
                ERROR_VARIABLE json_error
                MEMBER "${json}" ${index})
            if(NOT json_error STREQUAL "NOTFOUND")
                message(FATAL_ERROR "Pinned Spout2 evidence object is invalid")
            endif()
            list(APPEND actual "${member}")
        endforeach()
    endif()
    list(SORT actual)
    if(NOT "${actual}" STREQUAL "${expected}")
        message(FATAL_ERROR "Pinned Spout2 evidence object has unexpected fields")
    endif()
endfunction()

function(
        _vrrecorder_spout2_require_file_identity
        root evidence path_property length_property sha_property
        expected_path expected_length expected_sha256 description)
    _vrrecorder_spout2_require_json_value(
        "${evidence}" "${path_property}" "${expected_path}")
    _vrrecorder_spout2_require_json_value(
        "${evidence}" "${length_property}" "${expected_length}")
    _vrrecorder_spout2_require_json_value(
        "${evidence}" "${sha_property}" "${expected_sha256}")
    set(path "${root}/${expected_path}")
    if(NOT EXISTS "${path}" OR IS_DIRECTORY "${path}" OR IS_SYMLINK "${path}")
        message(FATAL_ERROR "Pinned Spout2 ${description} is missing: ${path}")
    endif()
    file(SIZE "${path}" actual_length)
    if(NOT actual_length STREQUAL expected_length)
        message(FATAL_ERROR "Pinned Spout2 ${description} length does not match")
    endif()
    file(SHA256 "${path}" actual_sha256)
    if(NOT actual_sha256 STREQUAL expected_sha256)
        message(FATAL_ERROR "Pinned Spout2 ${description} SHA-256 does not match")
    endif()
endfunction()

function(vrrecorder_validate_pinned_spout2_sdk root)
    if(root STREQUAL "" OR NOT IS_ABSOLUTE "${root}")
        message(FATAL_ERROR "Pinned Spout2 SDK root must be an absolute path")
    endif()
    cmake_path(NORMAL_PATH root OUTPUT_VARIABLE normalized_root)
    if(NOT EXISTS "${normalized_root}" OR
       NOT IS_DIRECTORY "${normalized_root}" OR
       IS_SYMLINK "${normalized_root}")
        message(FATAL_ERROR "Pinned Spout2 SDK root is missing or invalid")
    endif()

    set(evidence_path
        "${normalized_root}/share/vrrecorder/spout2-sdk-evidence.json")
    if(NOT EXISTS "${evidence_path}" OR
       IS_DIRECTORY "${evidence_path}" OR
       IS_SYMLINK "${evidence_path}")
        message(FATAL_ERROR "Pinned Spout2 SDK evidence is missing")
    endif()
    file(READ "${evidence_path}" evidence)
    _vrrecorder_spout2_require_exact_object_members(
        "${evidence}"
        architecture
        artifacts
        binaryArchiveLength
        binaryArchivePath
        binaryArchiveSha256
        buildRecipeLength
        buildRecipePath
        buildRecipeSha256
        component
        deployment
        licenseLength
        licensePath
        licenseSha256
        runtimeLibrary
        schemaVersion
        sourceArchiveLength
        sourceArchivePath
        sourceArchiveSha256
        sourceCommit
        tag
        version)
    _vrrecorder_spout2_require_json_value("${evidence}" schemaVersion "1")
    _vrrecorder_spout2_require_json_value("${evidence}" component "spout2")
    _vrrecorder_spout2_require_json_value(
        "${evidence}" version "${VRRECORDER_SPOUT2_VERSION}")
    _vrrecorder_spout2_require_json_value(
        "${evidence}" tag "${VRRECORDER_SPOUT2_TAG}")
    _vrrecorder_spout2_require_json_value(
        "${evidence}" sourceCommit "${VRRECORDER_SPOUT2_SOURCE_COMMIT}")
    _vrrecorder_spout2_require_json_value("${evidence}" architecture "x86_64")
    _vrrecorder_spout2_require_json_value("${evidence}" runtimeLibrary "MD")
    _vrrecorder_spout2_require_json_value("${evidence}" deployment "static")

    _vrrecorder_spout2_require_file_identity(
        "${normalized_root}" "${evidence}"
        binaryArchivePath binaryArchiveLength binaryArchiveSha256
        "${VRRECORDER_SPOUT2_BINARY_ARCHIVE_PATH}"
        "${VRRECORDER_SPOUT2_BINARY_ARCHIVE_LENGTH}"
        "${VRRECORDER_SPOUT2_BINARY_ARCHIVE_SHA256}"
        "binary archive")
    _vrrecorder_spout2_require_file_identity(
        "${normalized_root}" "${evidence}"
        sourceArchivePath sourceArchiveLength sourceArchiveSha256
        "${VRRECORDER_SPOUT2_SOURCE_ARCHIVE_PATH}"
        "${VRRECORDER_SPOUT2_SOURCE_ARCHIVE_LENGTH}"
        "${VRRECORDER_SPOUT2_SOURCE_ARCHIVE_SHA256}"
        "source archive")
    _vrrecorder_spout2_require_file_identity(
        "${normalized_root}" "${evidence}"
        licensePath licenseLength licenseSha256
        "${VRRECORDER_SPOUT2_LICENSE_PATH}"
        "${VRRECORDER_SPOUT2_LICENSE_LENGTH}"
        "${VRRECORDER_SPOUT2_LICENSE_SHA256}"
        "license")
    _vrrecorder_spout2_require_file_identity(
        "${normalized_root}" "${evidence}"
        buildRecipePath buildRecipeLength buildRecipeSha256
        "${VRRECORDER_SPOUT2_BUILD_RECIPE_PATH}"
        "${VRRECORDER_SPOUT2_BUILD_RECIPE_LENGTH}"
        "${VRRECORDER_SPOUT2_BUILD_RECIPE_SHA256}"
        "build recipe")

    string(
        JSON artifact_count
        ERROR_VARIABLE json_error
        LENGTH "${evidence}" artifacts)
    list(LENGTH VRRECORDER_SPOUT2_ARTIFACT_IDENTITIES expected_artifact_count)
    if(NOT json_error STREQUAL "NOTFOUND" OR
       NOT artifact_count EQUAL expected_artifact_count)
        message(FATAL_ERROR "Pinned Spout2 artifact evidence count does not match")
    endif()

    set(expected_inventory
        "share/vrrecorder/spout2-sdk-evidence.json"
        "${VRRECORDER_SPOUT2_BINARY_ARCHIVE_PATH}"
        "${VRRECORDER_SPOUT2_SOURCE_ARCHIVE_PATH}"
        "${VRRECORDER_SPOUT2_LICENSE_PATH}"
        "${VRRECORDER_SPOUT2_BUILD_RECIPE_PATH}")
    if(artifact_count GREATER 0)
        math(EXPR last_artifact "${artifact_count} - 1")
        foreach(index RANGE 0 ${last_artifact})
            list(GET VRRECORDER_SPOUT2_ARTIFACT_IDENTITIES ${index} identity)
            string(REPLACE "|" ";" identity_fields "${identity}")
            list(LENGTH identity_fields field_count)
            if(NOT field_count EQUAL 3)
                message(FATAL_ERROR "Pinned Spout2 artifact identity is invalid")
            endif()
            list(GET identity_fields 0 expected_path)
            list(GET identity_fields 1 expected_length)
            list(GET identity_fields 2 expected_sha256)
            string(
                JSON artifact
                ERROR_VARIABLE json_error
                GET "${evidence}" artifacts ${index})
            if(NOT json_error STREQUAL "NOTFOUND")
                message(FATAL_ERROR "Pinned Spout2 artifact evidence is invalid")
            endif()
            _vrrecorder_spout2_require_exact_object_members(
                "${artifact}" length path sha256)
            _vrrecorder_spout2_require_file_identity(
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
        message(FATAL_ERROR "Pinned Spout2 SDK inventory is not exact")
    endif()
endfunction()

function(vrrecorder_import_pinned_spout2_sdk root)
    if(NOT WIN32 OR NOT MSVC)
        message(FATAL_ERROR "Pinned Spout2 SDK import requires Windows MSVC")
    endif()
    vrrecorder_validate_pinned_spout2_sdk("${root}")
    cmake_path(NORMAL_PATH root OUTPUT_VARIABLE normalized_root)

    if(TARGET Spout2::Spout_static OR TARGET Spout2::SpoutDX_static)
        message(FATAL_ERROR "Pinned Spout2 targets already exist")
    endif()

    add_library(Spout2::Spout_static STATIC IMPORTED GLOBAL)
    set_target_properties(
        Spout2::Spout_static
        PROPERTIES
            IMPORTED_CONFIGURATIONS RELEASE
            IMPORTED_LOCATION "${normalized_root}/lib/Spout_static.lib"
            IMPORTED_LOCATION_RELEASE "${normalized_root}/lib/Spout_static.lib"
            MAP_IMPORTED_CONFIG_DEBUG Release
            MAP_IMPORTED_CONFIG_MINSIZEREL Release
            MAP_IMPORTED_CONFIG_RELWITHDEBINFO Release
            INTERFACE_COMPILE_DEFINITIONS SPOUT_BUILD_STATIC
            INTERFACE_INCLUDE_DIRECTORIES "${normalized_root}/include"
            INTERFACE_LINK_LIBRARIES "${_vrrecorder_spout2_system_libraries}")

    add_library(Spout2::SpoutDX_static STATIC IMPORTED GLOBAL)
    set_target_properties(
        Spout2::SpoutDX_static
        PROPERTIES
            IMPORTED_CONFIGURATIONS RELEASE
            IMPORTED_LOCATION "${normalized_root}/lib/SpoutDX_static.lib"
            IMPORTED_LOCATION_RELEASE "${normalized_root}/lib/SpoutDX_static.lib"
            MAP_IMPORTED_CONFIG_DEBUG Release
            MAP_IMPORTED_CONFIG_MINSIZEREL Release
            MAP_IMPORTED_CONFIG_RELWITHDEBINFO Release
            INTERFACE_COMPILE_DEFINITIONS SPOUT_BUILD_STATIC
            INTERFACE_INCLUDE_DIRECTORIES "${normalized_root}/include"
            INTERFACE_LINK_LIBRARIES "Spout2::Spout_static")
endfunction()
