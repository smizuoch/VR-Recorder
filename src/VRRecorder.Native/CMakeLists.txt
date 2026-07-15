vrrecorder_select_native_factory_sources(
    vrrecorder_native_factory_sources
    "${CMAKE_CURRENT_SOURCE_DIR}")
set(
    vrrecorder_native_factory_selection_intent
    "${CMAKE_CURRENT_BINARY_DIR}/native-factory-selection.intent.json")
vrrecorder_write_native_factory_selection_intent(
    "${vrrecorder_native_factory_selection_intent}")
file(
    SHA256
    "${vrrecorder_native_factory_selection_intent}"
    VRRECORDER_FACTORY_SELECTION_INTENT_SHA256)
set(
    vrrecorder_native_factory_selection_marker_source
    "${CMAKE_CURRENT_BINARY_DIR}/native_factory_selection_marker.cpp")
configure_file(
    "${CMAKE_CURRENT_SOURCE_DIR}/src/native_factory_selection_marker.cpp.in"
    "${vrrecorder_native_factory_selection_marker_source}"
    @ONLY)

add_library(vrrecorder_native SHARED
    src/abi.cpp
    src/av_sync_monitor.cpp
    src/av_sync_media_event_adapter.cpp
    src/media_mux_pipeline.cpp
    src/media_recording_session.cpp
    src/media_stream_pipeline_adapters.cpp
    src/media_recording_pipeline.cpp
    src/pipeline_media_backend.cpp
    src/pre_header_coordinator.cpp
    src/pre_header_media_mux_session.cpp
    src/production_media_configuration.cpp
    src/audio_encoder_config.cpp
    src/ffmpeg_encoder_state_machine.cpp
    src/ffmpeg_fragmented_mp4_muxer.cpp
    src/fragmented_mp4_mux_coordinator.cpp
    src/h264_bitstream_converter.cpp
    src/h264_avcc_to_annex_b.cpp
    src/annex_b_encoder_probe_decoder.cpp
    src/h264_packet_normalizer.cpp
    src/h264_sps_parser.cpp
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
    src/timestamping_audio_pipeline_session.cpp
    src/spout_capture_pump.cpp
    src/spout_capture_worker.cpp
    src/audio_timeline_buffer.cpp
    src/video_cfr_scheduler.cpp
    src/steady_video_cfr_clock.cpp
    "${vrrecorder_native_factory_selection_marker_source}"
    ${vrrecorder_native_factory_sources})

if(VRRECORDER_MEDIA_FACTORY_VARIANT STREQUAL "PRODUCTION")
    if(NOT WIN32 OR NOT MSVC OR
       NOT VRRECORDER_ENABLE_FFMPEG_ADAPTERS OR
       NOT VRRECORDER_SPOUT_FACTORY_VARIANT STREQUAL "PRODUCTION" OR
       NOT TARGET FFmpeg::avcodec OR
       NOT TARGET FFmpeg::avformat OR
       NOT TARGET FFmpeg::avutil OR
       NOT TARGET FFmpeg::swresample)
        message(
            FATAL_ERROR
            "The production media backend requires Windows MSVC, the pinned FFmpeg SDK, and the production Spout backend")
    endif()
    include("${PROJECT_SOURCE_DIR}/cmake/ProductionMediaAacAttachment.cmake")
    include("${PROJECT_SOURCE_DIR}/cmake/ProductionMediaH264Attachment.cmake")
    vrrecorder_resolve_production_media_aac_attachment(
        vrrecorder_production_media_aac_sources
        "${CMAKE_CURRENT_SOURCE_DIR}")
    vrrecorder_resolve_production_media_h264_attachment(
        vrrecorder_production_media_h264_sources
        "${CMAKE_CURRENT_SOURCE_DIR}")
    set(
        vrrecorder_production_media_sources
        ${vrrecorder_production_media_aac_sources}
        ${vrrecorder_production_media_h264_sources}
        src/ffmpeg_h264_software_codec_session.cpp
        src/ffmpeg_libavformat_fragmented_mp4_muxer_port.cpp)
    list(REMOVE_DUPLICATES vrrecorder_production_media_sources)
    target_sources(
        vrrecorder_native
        PRIVATE ${vrrecorder_production_media_sources})
    if(WIN32)
        target_link_libraries(
            vrrecorder_native
            PRIVATE
                FFmpeg::avcodec
                FFmpeg::avformat
                FFmpeg::avutil
                FFmpeg::swresample)
    endif()
endif()

target_sources(vrrecorder_native PRIVATE src/video_encoding_pump.cpp)
target_sources(vrrecorder_native PRIVATE src/video_encoding_worker.cpp)
target_sources(vrrecorder_native PRIVATE src/video_pipeline_session.cpp)
target_sources(vrrecorder_native PRIVATE src/video_processing_plan.cpp)
target_sources(vrrecorder_native PRIVATE src/video_processing_encoder_sink.cpp)
target_sources(vrrecorder_native PRIVATE src/video_processing_layout_controller.cpp)
target_sources(vrrecorder_native PRIVATE src/d3d11_video_frame_processor.cpp)
target_sources(vrrecorder_native PRIVATE src/d3d11_nv12_frame_mapper.cpp)
target_sources(vrrecorder_native PRIVATE src/keyed_mutex_video_surface.cpp)
target_sources(vrrecorder_native PRIVATE src/video_encoder_config.cpp)
target_sources(vrrecorder_native PRIVATE src/encoder_probe_evidence_builder.cpp)
target_sources(vrrecorder_native PRIVATE src/encoder_probe_pipeline.cpp)
target_sources(vrrecorder_native PRIVATE src/encoder_probe_synthetic_nv12.cpp)
target_sources(
    vrrecorder_native
    PRIVATE src/software_encoder_probe_platform_identity.cpp)

if(VRRECORDER_SPOUT_FACTORY_VARIANT STREQUAL "PRODUCTION")
    if(NOT WIN32 OR NOT MSVC OR NOT VRRECORDER_ENABLE_SPOUT2_ADAPTER OR
       NOT TARGET Spout2::SpoutDX_static)
        message(
            FATAL_ERROR
            "The production Spout2 backend requires the pinned Windows MSVC static SDK")
    endif()
    if(WIN32)
        target_sources(
            vrrecorder_native
            PRIVATE
                src/spout2_source_backend_core.cpp
                src/windows_d3d11_owned_video_surface.cpp
                src/windows_spout2_receiver_port.cpp)
        target_link_libraries(
            vrrecorder_native
            PRIVATE Spout2::SpoutDX_static)
    endif()
endif()

if(WIN32)
    target_sources(
        vrrecorder_native
        PRIVATE
            src/windows_d3d11_video_processor_port.cpp
            src/windows_d3d11_nv12_readback_port.cpp
            src/windows_d3d11_keyed_mutex_surface.cpp
            src/windows_software_encoder_probe_adapter_identity_lookup.cpp
            src/wasapi_audio_capture_source_core.cpp
            src/wasapi_audio_capture_source.cpp)
    target_compile_definitions(
        vrrecorder_native
        PRIVATE
            NOMINMAX
            WIN32_LEAN_AND_MEAN)
    if(MSVC)
        target_link_libraries(
            vrrecorder_native
            PRIVATE
                Ole32.lib
                Uuid.lib)
    else()
        target_link_libraries(
            vrrecorder_native
            PRIVATE
                ole32
                uuid)
    endif()
    target_link_libraries(
        vrrecorder_native
        PRIVATE
            d3d11
            dxgi
            ksuser)
else()
    target_sources(
        vrrecorder_native
        PRIVATE
            src/unavailable_wasapi_audio_capture_source.cpp)
endif()

add_library(VRRecorder::Native ALIAS vrrecorder_native)

target_compile_features(vrrecorder_native PRIVATE cxx_std_20)
target_compile_definitions(
    vrrecorder_native
    PRIVATE
        VRRECORDER_NATIVE_EXPORTS
        VRRECORDER_NATIVE_FACTORY_SELECTION_MARKER=1)
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

if(WIN32)
    set_target_properties(vrrecorder_native PROPERTIES PREFIX "")
endif()

vrrecorder_enable_strict_warnings(vrrecorder_native)

set(
    vrrecorder_native_factory_evidence
    "${CMAKE_BINARY_DIR}/native-factory-selection-$<CONFIG>.json")
add_custom_target(
    vrrecorder_native_factory_evidence ALL
    COMMAND
        "${CMAKE_COMMAND}"
        "-DNATIVE_FACTORY_VARIANTS_MODULE=${PROJECT_SOURCE_DIR}/cmake/NativeFactoryVariants.cmake"
        "-DSELECTION_INTENT=${vrrecorder_native_factory_selection_intent}"
        "-DNATIVE_BINARY=$<TARGET_FILE:vrrecorder_native>"
        "-DOUTPUT_PATH=${vrrecorder_native_factory_evidence}"
        -P
        "${PROJECT_SOURCE_DIR}/cmake/WriteNativeFactoryEvidence.cmake"
    BYPRODUCTS "${vrrecorder_native_factory_evidence}"
    DEPENDS
        vrrecorder_native
        "${vrrecorder_native_factory_selection_intent}"
        "${PROJECT_SOURCE_DIR}/cmake/NativeFactoryVariants.cmake"
        "${PROJECT_SOURCE_DIR}/cmake/WriteNativeFactoryEvidence.cmake"
    VERBATIM)

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
