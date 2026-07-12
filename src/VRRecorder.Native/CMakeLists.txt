add_library(vrrecorder_native SHARED
    src/abi.cpp
    src/audio_encoder_config.cpp
    src/fragmented_mp4_mux_coordinator.cpp
    src/muxing_video_encoder_sink.cpp
    src/muxing_audio_encoder_sink.cpp
    src/shared_mux_finalization_session.cpp
    src/audio_capture_input_worker.cpp
    src/audio_capture_input_runner.cpp
    src/audio_capture_session.cpp
    src/audio_capture_normalizer.cpp
    src/audio_capture_platform_support.cpp
    src/audio_capture_pump.cpp
    src/audio_capture_timeline.cpp
    src/audio_encoding_pump.cpp
    src/audio_encoding_worker.cpp
    src/audio_media_event_adapter.cpp
    src/audio_mix_coordinator.cpp
    src/audio_mixer.cpp
    src/audio_pipeline_session.cpp
    src/spout_capture_pump.cpp
    src/spout_capture_worker.cpp
    src/audio_timeline_buffer.cpp
    src/unavailable_encoder_probe_backend.cpp
    src/unavailable_media_backend.cpp
    src/unavailable_spout_source_backend.cpp
    src/unavailable_steamvr_input_backend.cpp
    src/video_cfr_scheduler.cpp)

target_sources(vrrecorder_native PRIVATE src/video_encoding_pump.cpp)
target_sources(vrrecorder_native PRIVATE src/video_encoding_worker.cpp)
target_sources(vrrecorder_native PRIVATE src/video_pipeline_session.cpp)
target_sources(vrrecorder_native PRIVATE src/video_processing_plan.cpp)
target_sources(vrrecorder_native PRIVATE src/video_processing_encoder_sink.cpp)
target_sources(vrrecorder_native PRIVATE src/video_encoder_config.cpp)

if(WIN32)
    target_sources(
        vrrecorder_native
        PRIVATE
            src/wasapi_audio_capture_source.cpp)
    target_compile_definitions(
        vrrecorder_native
        PRIVATE
            NOMINMAX
            WIN32_LEAN_AND_MEAN)
    target_link_libraries(
        vrrecorder_native
        PRIVATE
            Ole32.lib
            Uuid.lib)
else()
    target_sources(
        vrrecorder_native
        PRIVATE
            src/unavailable_wasapi_audio_capture_source.cpp)
endif()

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
