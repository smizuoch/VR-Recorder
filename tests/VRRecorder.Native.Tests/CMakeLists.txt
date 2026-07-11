set(native_source_root "${PROJECT_SOURCE_DIR}/src/VRRecorder.Native")

add_executable(
    vrrecorder_native_abi_contract
    abi_contract_tests.cpp
    fake_media_backend.cpp
    "${native_source_root}/src/abi.cpp")
target_compile_features(vrrecorder_native_abi_contract PRIVATE cxx_std_20)
target_compile_definitions(
    vrrecorder_native_abi_contract
    PRIVATE
        VRRECORDER_NATIVE_EXPORTS)
target_include_directories(
    vrrecorder_native_abi_contract
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src"
        "${CMAKE_CURRENT_SOURCE_DIR}")
target_link_libraries(vrrecorder_native_abi_contract PRIVATE Threads::Threads)
set_target_properties(
    vrrecorder_native_abi_contract
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(vrrecorder_native_abi_contract)

add_executable(
    vrrecorder_native_c_header_smoke
    c_header_smoke.c
    c_header_runtime_smoke.c)
target_compile_features(vrrecorder_native_c_header_smoke PRIVATE c_std_17)
target_include_directories(
    vrrecorder_native_c_header_smoke
    PRIVATE
        "${native_source_root}/include")
target_link_libraries(
    vrrecorder_native_c_header_smoke
    PRIVATE
        VRRecorder::Native)
set_target_properties(
    vrrecorder_native_c_header_smoke
    PROPERTIES
        C_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(vrrecorder_native_c_header_smoke)

add_executable(
    vrrecorder_native_audio_mixer
    audio_mixer_tests.cpp
    "${native_source_root}/src/audio_mixer.cpp")
target_compile_features(vrrecorder_native_audio_mixer PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_audio_mixer
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_audio_mixer
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(vrrecorder_native_audio_mixer)

add_test(NAME vrrecorder_native_abi_contract
    COMMAND vrrecorder_native_abi_contract)
add_test(NAME vrrecorder_native_c_header_smoke
    COMMAND vrrecorder_native_c_header_smoke)
add_test(NAME vrrecorder_native_audio_mixer
    COMMAND vrrecorder_native_audio_mixer)
add_test(NAME vrrecorder_native_exports
    COMMAND
        "${CMAKE_COMMAND}"
        "-DLIBRARY_FILE=$<TARGET_FILE:vrrecorder_native>"
        "-DEXPECTED_EXPORTS_FILE=${CMAKE_CURRENT_SOURCE_DIR}/expected_exports.txt"
        "-DPLATFORM_NAME=${CMAKE_SYSTEM_NAME}"
        "-DNM_TOOL=${CMAKE_NM}"
        "-DPE_EXPORT_TOOL=${CMAKE_LINKER}"
        -P
        "${CMAKE_CURRENT_SOURCE_DIR}/verify_exports.cmake")
add_test(NAME vrrecorder_native_cmake_build_contract
    COMMAND
        "${CMAKE_COMMAND}"
        -P
        "${CMAKE_CURRENT_SOURCE_DIR}/cmake_build_contract.cmake")
