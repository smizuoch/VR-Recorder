add_library(vrrecorder_native SHARED
    src/abi.cpp
    src/audio_mixer.cpp
    src/audio_timeline_buffer.cpp
    src/unavailable_encoder_probe_backend.cpp
    src/unavailable_media_backend.cpp
    src/unavailable_spout_source_backend.cpp
    src/unavailable_steamvr_input_backend.cpp)

add_library(VRRecorder::Native ALIAS vrrecorder_native)

target_compile_features(vrrecorder_native PRIVATE cxx_std_20)
target_compile_definitions(vrrecorder_native PRIVATE VRRECORDER_NATIVE_EXPORTS)
target_include_directories(
    vrrecorder_native
    PUBLIC
        "$<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>"
    PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}/src")
target_link_libraries(vrrecorder_native PRIVATE Threads::Threads)

set_target_properties(
    vrrecorder_native
    PROPERTIES
        CXX_EXTENSIONS OFF
        CXX_VISIBILITY_PRESET hidden
        OUTPUT_NAME vrrecorder_native
        VISIBILITY_INLINES_HIDDEN YES)

vrrecorder_enable_strict_warnings(vrrecorder_native)

if(WIN32)
    target_sources(
        vrrecorder_native
        PRIVATE
            "${CMAKE_CURRENT_SOURCE_DIR}/vrrecorder_native.def")
elseif(UNIX AND NOT APPLE)
    target_link_options(
        vrrecorder_native
        PRIVATE
            "LINKER:--no-undefined"
            "LINKER:--version-script=${CMAKE_CURRENT_SOURCE_DIR}/exports.version"
            "LINKER:-Map,${CMAKE_CURRENT_BINARY_DIR}/vrrecorder_native.map")
    set_property(
        TARGET vrrecorder_native
        APPEND
        PROPERTY LINK_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/exports.version")
endif()
