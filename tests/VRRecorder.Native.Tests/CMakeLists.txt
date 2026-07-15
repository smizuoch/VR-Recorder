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
        VRRECORDER_NATIVE_EXPORTS
        VRRECORDER_NATIVE_ABI_CONTRACT_TEST_HOOKS)
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

add_executable(
    vrrecorder_native_audio_encoder_config
    audio_encoder_config_tests.cpp
    "${native_source_root}/src/audio_encoder_config.cpp")
target_compile_features(
    vrrecorder_native_audio_encoder_config PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_audio_encoder_config
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_audio_encoder_config
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_audio_encoder_config)

add_executable(
    vrrecorder_native_av_sync_monitor
    av_sync_monitor_tests.cpp
    "${native_source_root}/src/av_sync_monitor.cpp")
target_compile_features(
    vrrecorder_native_av_sync_monitor PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_av_sync_monitor
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_av_sync_monitor
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_av_sync_monitor)

add_executable(
    vrrecorder_native_av_sync_media_event_adapter
    av_sync_media_event_adapter_tests.cpp
    "${native_source_root}/src/av_sync_media_event_adapter.cpp")
target_compile_features(
    vrrecorder_native_av_sync_media_event_adapter PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_av_sync_media_event_adapter
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_av_sync_media_event_adapter
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_av_sync_media_event_adapter)

add_executable(
    vrrecorder_native_media_mux_pipeline
    media_mux_pipeline_tests.cpp
    "${native_source_root}/src/media_mux_pipeline.cpp"
    "${native_source_root}/src/av_sync_monitor.cpp"
    "${native_source_root}/src/av_sync_media_event_adapter.cpp"
    "${native_source_root}/src/fragmented_mp4_mux_coordinator.cpp"
    "${native_source_root}/src/muxing_audio_encoder_sink.cpp"
    "${native_source_root}/src/muxing_video_encoder_sink.cpp"
    "${native_source_root}/src/shared_mux_finalization_session.cpp")
target_compile_features(
    vrrecorder_native_media_mux_pipeline PRIVATE cxx_std_20)
target_compile_definitions(
    vrrecorder_native_media_mux_pipeline
    PRIVATE VRRECORDER_NATIVE_TESTING=1)
target_include_directories(
    vrrecorder_native_media_mux_pipeline
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_media_mux_pipeline
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_media_mux_pipeline)

add_executable(
    vrrecorder_native_media_recording_session
    media_recording_session_tests.cpp
    "${native_source_root}/src/media_recording_session.cpp")
target_compile_features(
    vrrecorder_native_media_recording_session PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_media_recording_session
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_media_recording_session
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_media_recording_session)

add_executable(
    vrrecorder_native_media_stream_pipeline_adapters
    media_stream_pipeline_adapter_tests.cpp
    "${native_source_root}/src/media_stream_pipeline_adapters.cpp")
target_compile_features(
    vrrecorder_native_media_stream_pipeline_adapters PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_media_stream_pipeline_adapters
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_media_stream_pipeline_adapters
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_media_stream_pipeline_adapters)

add_executable(
    vrrecorder_native_media_recording_pipeline
    media_recording_pipeline_tests.cpp
    "${native_source_root}/src/media_recording_pipeline.cpp"
    "${native_source_root}/src/media_stream_pipeline_adapters.cpp"
    "${native_source_root}/src/media_recording_session.cpp")
target_compile_features(
    vrrecorder_native_media_recording_pipeline PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_media_recording_pipeline
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_media_recording_pipeline
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_media_recording_pipeline)

add_executable(
    vrrecorder_native_pipeline_media_backend
    pipeline_media_backend_tests.cpp
    "${native_source_root}/src/pipeline_media_backend.cpp")
target_compile_features(
    vrrecorder_native_pipeline_media_backend PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_pipeline_media_backend
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_pipeline_media_backend
    PROPERTIES CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_pipeline_media_backend)

add_executable(
    vrrecorder_native_production_media_configuration
    production_media_configuration_tests.cpp
    "${native_source_root}/src/production_media_configuration.cpp")
target_compile_features(
    vrrecorder_native_production_media_configuration PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_production_media_configuration
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_production_media_configuration
    PROPERTIES CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_production_media_configuration)

add_executable(
    vrrecorder_native_pre_header_coordinator
    pre_header_coordinator_tests.cpp
    "${native_source_root}/src/pre_header_coordinator.cpp")
target_compile_features(
    vrrecorder_native_pre_header_coordinator PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_pre_header_coordinator
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
target_compile_definitions(
    vrrecorder_native_pre_header_coordinator
    PRIVATE VRRECORDER_NATIVE_TESTING=1)
set_target_properties(
    vrrecorder_native_pre_header_coordinator
    PROPERTIES CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_pre_header_coordinator)

add_executable(
    vrrecorder_native_pre_header_muxing_composition
    pre_header_muxing_composition_tests.cpp
    "${native_source_root}/src/pre_header_coordinator.cpp"
    "${native_source_root}/src/muxing_video_encoder_sink.cpp"
    "${native_source_root}/src/muxing_audio_encoder_sink.cpp")
target_compile_features(
    vrrecorder_native_pre_header_muxing_composition PRIVATE cxx_std_20)
target_compile_definitions(
    vrrecorder_native_pre_header_muxing_composition
    PRIVATE VRRECORDER_NATIVE_TESTING=1)
target_include_directories(
    vrrecorder_native_pre_header_muxing_composition
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_pre_header_muxing_composition
    PROPERTIES CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_pre_header_muxing_composition)

add_executable(
    vrrecorder_native_pre_header_media_mux_session
    pre_header_media_mux_session_tests.cpp
    "${native_source_root}/src/pre_header_media_mux_session.cpp"
    "${native_source_root}/src/pre_header_coordinator.cpp")
target_compile_features(
    vrrecorder_native_pre_header_media_mux_session PRIVATE cxx_std_20)
target_compile_definitions(
    vrrecorder_native_pre_header_media_mux_session
    PRIVATE VRRECORDER_NATIVE_TESTING=1)
target_include_directories(
    vrrecorder_native_pre_header_media_mux_session
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_pre_header_media_mux_session
    PROPERTIES CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_pre_header_media_mux_session)

add_executable(
    vrrecorder_native_full_media_graph_abort
    full_media_graph_abort_tests.cpp
    "${native_source_root}/src/pipeline_media_backend.cpp"
    "${native_source_root}/src/media_recording_pipeline.cpp"
    "${native_source_root}/src/media_stream_pipeline_adapters.cpp"
    "${native_source_root}/src/media_recording_session.cpp"
    "${native_source_root}/src/media_mux_pipeline.cpp"
    "${native_source_root}/src/av_sync_monitor.cpp"
    "${native_source_root}/src/av_sync_media_event_adapter.cpp"
    "${native_source_root}/src/fragmented_mp4_mux_coordinator.cpp"
    "${native_source_root}/src/shared_mux_finalization_session.cpp"
    "${native_source_root}/src/muxing_video_encoder_sink.cpp"
    "${native_source_root}/src/muxing_audio_encoder_sink.cpp"
    "${native_source_root}/src/video_cfr_scheduler.cpp"
    "${native_source_root}/src/video_encoding_pump.cpp"
    "${native_source_root}/src/video_encoding_worker.cpp"
    "${native_source_root}/src/video_pipeline_session.cpp"
    "${native_source_root}/src/audio_encoding_pump.cpp"
    "${native_source_root}/src/audio_encoding_worker.cpp"
    "${native_source_root}/src/audio_pipeline_session.cpp")
target_compile_features(
    vrrecorder_native_full_media_graph_abort PRIVATE cxx_std_20)
target_compile_definitions(
    vrrecorder_native_full_media_graph_abort
    PRIVATE VRRECORDER_NATIVE_TESTING=1)
target_include_directories(
    vrrecorder_native_full_media_graph_abort
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src"
        "${CMAKE_CURRENT_SOURCE_DIR}")
target_link_libraries(
    vrrecorder_native_full_media_graph_abort
    PRIVATE Threads::Threads)
set_target_properties(
    vrrecorder_native_full_media_graph_abort
    PROPERTIES CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_full_media_graph_abort)

add_executable(
    vrrecorder_native_audio_timeline_buffer
    audio_timeline_buffer_tests.cpp
    "${native_source_root}/src/audio_timeline_buffer.cpp")
target_compile_features(
    vrrecorder_native_audio_timeline_buffer PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_audio_timeline_buffer
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_audio_timeline_buffer
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(vrrecorder_native_audio_timeline_buffer)

add_executable(
    vrrecorder_native_audio_capture_timeline
    audio_capture_timeline_tests.cpp
    "${native_source_root}/src/audio_capture_timeline.cpp"
    "${native_source_root}/src/audio_timeline_buffer.cpp")
target_compile_features(
    vrrecorder_native_audio_capture_timeline PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_audio_capture_timeline
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_audio_capture_timeline
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(vrrecorder_native_audio_capture_timeline)

add_executable(
    vrrecorder_native_audio_capture_pump
    audio_capture_pump_tests.cpp
    "${native_source_root}/src/audio_capture_input_runner.cpp"
    "${native_source_root}/src/audio_capture_pump.cpp"
    "${native_source_root}/src/audio_capture_timeline.cpp"
    "${native_source_root}/src/audio_timeline_buffer.cpp")
target_compile_features(
    vrrecorder_native_audio_capture_pump PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_audio_capture_pump
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_audio_capture_pump
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(vrrecorder_native_audio_capture_pump)

add_executable(
    vrrecorder_native_audio_capture_normalizer
    audio_capture_normalizer_tests.cpp
    "${native_source_root}/src/audio_capture_normalizer.cpp")
target_compile_features(
    vrrecorder_native_audio_capture_normalizer PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_audio_capture_normalizer
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_audio_capture_normalizer
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(vrrecorder_native_audio_capture_normalizer)

add_executable(
    vrrecorder_native_wasapi_audio_capture_source_core
    wasapi_audio_capture_source_core_tests.cpp
    "${native_source_root}/src/wasapi_audio_capture_source_core.cpp"
    "${native_source_root}/src/audio_capture_normalizer.cpp")
target_compile_features(
    vrrecorder_native_wasapi_audio_capture_source_core PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_wasapi_audio_capture_source_core
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_wasapi_audio_capture_source_core
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_wasapi_audio_capture_source_core)

add_executable(
    vrrecorder_native_audio_capture_source_factory
    audio_capture_source_factory_tests.cpp)
target_compile_features(
    vrrecorder_native_audio_capture_source_factory PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_audio_capture_source_factory
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
if(WIN32)
    target_sources(
        vrrecorder_native_audio_capture_source_factory
        PRIVATE
            "${native_source_root}/src/audio_capture_normalizer.cpp"
            "${native_source_root}/src/wasapi_audio_capture_source_core.cpp"
            "${native_source_root}/src/wasapi_audio_capture_source.cpp")
    target_compile_definitions(
        vrrecorder_native_audio_capture_source_factory
        PRIVATE
            NOMINMAX
            WIN32_LEAN_AND_MEAN)
    if(MSVC)
        target_link_libraries(
            vrrecorder_native_audio_capture_source_factory
            PRIVATE
                Ole32.lib
                Uuid.lib)
    else()
        target_link_libraries(
            vrrecorder_native_audio_capture_source_factory
            PRIVATE
                ole32
                uuid)
    endif()
    target_link_libraries(
        vrrecorder_native_audio_capture_source_factory
        PRIVATE
            ksuser)
else()
    target_sources(
        vrrecorder_native_audio_capture_source_factory
        PRIVATE
            "${native_source_root}/src/unavailable_wasapi_audio_capture_source.cpp")
endif()
set_target_properties(
    vrrecorder_native_audio_capture_source_factory
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_audio_capture_source_factory)

add_executable(
    vrrecorder_native_audio_capture_platform_support
    audio_capture_platform_support_tests.cpp
    "${native_source_root}/src/audio_capture_platform_support.cpp")
target_compile_features(
    vrrecorder_native_audio_capture_platform_support PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_audio_capture_platform_support
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
if(WIN32)
    target_sources(
        vrrecorder_native_audio_capture_platform_support
        PRIVATE
            "${native_source_root}/src/audio_capture_normalizer.cpp"
            "${native_source_root}/src/wasapi_audio_capture_source_core.cpp"
            "${native_source_root}/src/wasapi_audio_capture_source.cpp")
    target_compile_definitions(
        vrrecorder_native_audio_capture_platform_support
        PRIVATE
            NOMINMAX
            WIN32_LEAN_AND_MEAN)
    if(MSVC)
        target_link_libraries(
            vrrecorder_native_audio_capture_platform_support
            PRIVATE
                Ole32.lib
                Uuid.lib)
    else()
        target_link_libraries(
            vrrecorder_native_audio_capture_platform_support
            PRIVATE
                ole32
                uuid)
    endif()
    target_link_libraries(
        vrrecorder_native_audio_capture_platform_support
        PRIVATE
            ksuser)
else()
    target_sources(
        vrrecorder_native_audio_capture_platform_support
        PRIVATE
            "${native_source_root}/src/unavailable_wasapi_audio_capture_source.cpp")
endif()
set_target_properties(
    vrrecorder_native_audio_capture_platform_support
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_audio_capture_platform_support)

add_executable(
    vrrecorder_native_audio_capture_input_worker
    audio_capture_input_worker_tests.cpp
    "${native_source_root}/src/audio_capture_input_worker.cpp"
    "${native_source_root}/src/audio_capture_input_runner.cpp"
    "${native_source_root}/src/audio_capture_pump.cpp"
    "${native_source_root}/src/audio_capture_timeline.cpp"
    "${native_source_root}/src/audio_timeline_buffer.cpp")
target_compile_features(
    vrrecorder_native_audio_capture_input_worker PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_audio_capture_input_worker
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_audio_capture_input_worker
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_audio_capture_input_worker)

add_executable(
    vrrecorder_native_audio_capture_session
    audio_capture_session_tests.cpp
    "${native_source_root}/src/audio_capture_session.cpp"
    "${native_source_root}/src/audio_capture_input_worker.cpp"
    "${native_source_root}/src/audio_capture_input_runner.cpp"
    "${native_source_root}/src/audio_capture_pump.cpp"
    "${native_source_root}/src/audio_capture_timeline.cpp"
    "${native_source_root}/src/audio_mix_coordinator.cpp"
    "${native_source_root}/src/audio_mixer.cpp"
    "${native_source_root}/src/audio_timeline_buffer.cpp")
target_compile_features(
    vrrecorder_native_audio_capture_session PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_audio_capture_session
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_audio_capture_session
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_audio_capture_session)

add_executable(
    vrrecorder_native_audio_mix_coordinator
    audio_mix_coordinator_tests.cpp
    "${native_source_root}/src/audio_mix_coordinator.cpp"
    "${native_source_root}/src/audio_mixer.cpp"
    "${native_source_root}/src/audio_capture_timeline.cpp"
    "${native_source_root}/src/audio_timeline_buffer.cpp")
target_compile_features(
    vrrecorder_native_audio_mix_coordinator PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_audio_mix_coordinator
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_audio_mix_coordinator
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_audio_mix_coordinator)

add_executable(
    vrrecorder_native_audio_encoding_pump
    audio_encoding_pump_tests.cpp
    "${native_source_root}/src/audio_encoding_pump.cpp")
target_compile_features(
    vrrecorder_native_audio_encoding_pump PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_audio_encoding_pump
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_audio_encoding_pump
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_audio_encoding_pump)

add_executable(
    vrrecorder_native_audio_encoding_worker
    audio_encoding_worker_tests.cpp
    "${native_source_root}/src/audio_encoding_worker.cpp"
    "${native_source_root}/src/audio_encoding_pump.cpp")
target_compile_features(
    vrrecorder_native_audio_encoding_worker PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_audio_encoding_worker
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_audio_encoding_worker
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_audio_encoding_worker)

add_executable(
    vrrecorder_native_audio_pipeline_session
    audio_pipeline_session_tests.cpp
    "${native_source_root}/src/audio_pipeline_session.cpp"
    "${native_source_root}/src/audio_encoding_worker.cpp"
    "${native_source_root}/src/audio_encoding_pump.cpp")
target_compile_features(
    vrrecorder_native_audio_pipeline_session PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_audio_pipeline_session
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_audio_pipeline_session
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_audio_pipeline_session)

add_executable(
    vrrecorder_native_timestamping_audio_pipeline_session
    timestamping_audio_pipeline_session_tests.cpp
    "${native_source_root}/src/timestamping_audio_pipeline_session.cpp")
target_compile_features(
    vrrecorder_native_timestamping_audio_pipeline_session PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_timestamping_audio_pipeline_session
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_timestamping_audio_pipeline_session
    PROPERTIES CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_timestamping_audio_pipeline_session)

add_executable(
    vrrecorder_native_audio_media_event_adapter
    audio_media_event_adapter_tests.cpp
    "${native_source_root}/src/audio_media_event_adapter.cpp")
target_compile_features(
    vrrecorder_native_audio_media_event_adapter PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_audio_media_event_adapter
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_audio_media_event_adapter
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_audio_media_event_adapter)

add_executable(
    vrrecorder_native_video_cfr_scheduler
    video_cfr_scheduler_tests.cpp
    "${native_source_root}/src/video_cfr_scheduler.cpp")
target_compile_features(
    vrrecorder_native_video_cfr_scheduler PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_video_cfr_scheduler
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_video_cfr_scheduler
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_video_cfr_scheduler)

add_executable(
    vrrecorder_native_steady_video_cfr_clock
    steady_video_cfr_clock_tests.cpp
    "${native_source_root}/src/steady_video_cfr_clock.cpp")
target_compile_features(
    vrrecorder_native_steady_video_cfr_clock PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_steady_video_cfr_clock
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_steady_video_cfr_clock
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_steady_video_cfr_clock)

add_executable(
    vrrecorder_native_video_processing_plan
    video_processing_plan_tests.cpp
    "${native_source_root}/src/video_processing_plan.cpp")
target_compile_features(
    vrrecorder_native_video_processing_plan PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_video_processing_plan
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_video_processing_plan
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_video_processing_plan)

add_executable(
    vrrecorder_native_h264_bitstream_converter
    h264_bitstream_converter_tests.cpp
    "${native_source_root}/src/h264_bitstream_converter.cpp"
    "${native_source_root}/src/h264_sps_parser.cpp")
target_compile_features(
    vrrecorder_native_h264_bitstream_converter PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_h264_bitstream_converter
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_h264_bitstream_converter
    PROPERTIES CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_h264_bitstream_converter)

add_executable(
    vrrecorder_native_h264_avcc_to_annex_b
    h264_avcc_to_annex_b_tests.cpp
    "${native_source_root}/src/h264_avcc_to_annex_b.cpp")
target_compile_features(
    vrrecorder_native_h264_avcc_to_annex_b PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_h264_avcc_to_annex_b
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_h264_avcc_to_annex_b
    PROPERTIES CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_h264_avcc_to_annex_b)

add_executable(
    vrrecorder_native_h264_sps_parser
    h264_sps_parser_tests.cpp
    "${native_source_root}/src/h264_sps_parser.cpp")
target_compile_features(
    vrrecorder_native_h264_sps_parser PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_h264_sps_parser
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_h264_sps_parser
    PROPERTIES CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_h264_sps_parser)

add_executable(
    vrrecorder_native_h264_packet_normalizer
    h264_packet_normalizer_tests.cpp
    "${native_source_root}/src/h264_packet_normalizer.cpp"
    "${native_source_root}/src/h264_bitstream_converter.cpp"
    "${native_source_root}/src/h264_sps_parser.cpp"
    "${native_source_root}/src/video_encoder_config.cpp")
target_compile_features(
    vrrecorder_native_h264_packet_normalizer PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_h264_packet_normalizer
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src"
        "${CMAKE_CURRENT_SOURCE_DIR}")
set_target_properties(
    vrrecorder_native_h264_packet_normalizer
    PROPERTIES CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_h264_packet_normalizer)

add_executable(
    vrrecorder_native_encoder_probe_evidence_builder
    encoder_probe_evidence_builder_tests.cpp
    "${native_source_root}/src/encoder_probe_evidence_builder.cpp"
    "${native_source_root}/src/h264_packet_normalizer.cpp"
    "${native_source_root}/src/h264_bitstream_converter.cpp"
    "${native_source_root}/src/h264_sps_parser.cpp"
    "${native_source_root}/src/video_encoder_config.cpp")
target_compile_features(
    vrrecorder_native_encoder_probe_evidence_builder PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_encoder_probe_evidence_builder
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src"
        "${CMAKE_CURRENT_SOURCE_DIR}")
set_target_properties(
    vrrecorder_native_encoder_probe_evidence_builder
    PROPERTIES CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_encoder_probe_evidence_builder)

add_executable(
    vrrecorder_native_annex_b_encoder_probe_decoder
    annex_b_encoder_probe_decoder_tests.cpp
    "${native_source_root}/src/annex_b_encoder_probe_decoder.cpp"
    "${native_source_root}/src/h264_avcc_to_annex_b.cpp")
target_compile_features(
    vrrecorder_native_annex_b_encoder_probe_decoder PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_annex_b_encoder_probe_decoder
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_annex_b_encoder_probe_decoder
    PROPERTIES CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_annex_b_encoder_probe_decoder)

add_executable(
    vrrecorder_native_software_encoder_probe_platform_identity
    software_encoder_probe_platform_identity_tests.cpp
    "${native_source_root}/src/software_encoder_probe_platform_identity.cpp")
target_compile_features(
    vrrecorder_native_software_encoder_probe_platform_identity
    PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_software_encoder_probe_platform_identity
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_software_encoder_probe_platform_identity
    PROPERTIES CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_software_encoder_probe_platform_identity)

add_executable(
    vrrecorder_native_encoder_probe_pipeline
    encoder_probe_pipeline_tests.cpp
    "${native_source_root}/src/encoder_probe_pipeline.cpp"
    "${native_source_root}/src/encoder_probe_evidence_builder.cpp"
    "${native_source_root}/src/h264_packet_normalizer.cpp"
    "${native_source_root}/src/h264_bitstream_converter.cpp"
    "${native_source_root}/src/h264_sps_parser.cpp"
    "${native_source_root}/src/video_encoder_config.cpp")
target_compile_features(
    vrrecorder_native_encoder_probe_pipeline PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_encoder_probe_pipeline
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src"
        "${CMAKE_CURRENT_SOURCE_DIR}")
set_target_properties(
    vrrecorder_native_encoder_probe_pipeline
    PROPERTIES CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_encoder_probe_pipeline)

add_executable(
    vrrecorder_native_encoder_probe_synthetic_nv12
    encoder_probe_synthetic_nv12_tests.cpp
    "${native_source_root}/src/encoder_probe_synthetic_nv12.cpp")
target_compile_features(
    vrrecorder_native_encoder_probe_synthetic_nv12 PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_encoder_probe_synthetic_nv12
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_encoder_probe_synthetic_nv12
    PROPERTIES CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_encoder_probe_synthetic_nv12)

add_executable(
    vrrecorder_native_video_encoder_config
    video_encoder_config_tests.cpp
    "${native_source_root}/src/video_encoder_config.cpp")
target_compile_features(
    vrrecorder_native_video_encoder_config PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_video_encoder_config
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_video_encoder_config
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_video_encoder_config)

add_executable(
    vrrecorder_native_fragmented_mp4_mux_coordinator
    fragmented_mp4_mux_coordinator_tests.cpp
    "${native_source_root}/src/fragmented_mp4_mux_coordinator.cpp")
target_compile_features(
    vrrecorder_native_fragmented_mp4_mux_coordinator PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_fragmented_mp4_mux_coordinator
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_fragmented_mp4_mux_coordinator
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_fragmented_mp4_mux_coordinator)

add_executable(
    vrrecorder_native_ffmpeg_encoder_state_machine
    ffmpeg_encoder_state_machine_tests.cpp
    "${native_source_root}/src/ffmpeg_encoder_state_machine.cpp")
target_compile_features(
    vrrecorder_native_ffmpeg_encoder_state_machine PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_ffmpeg_encoder_state_machine
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
target_link_libraries(
    vrrecorder_native_ffmpeg_encoder_state_machine
    PRIVATE
        Threads::Threads)
set_target_properties(
    vrrecorder_native_ffmpeg_encoder_state_machine
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_ffmpeg_encoder_state_machine)

set(ffmpeg_libavcodec_test_dependency "")
if(TARGET FFmpeg::avcodec)
    set(ffmpeg_libavcodec_test_dependency FFmpeg::avcodec)
elseif(TARGET FFmpegContractTest::avcodec)
    set(ffmpeg_libavcodec_test_dependency FFmpegContractTest::avcodec)
endif()

set(ffmpeg_swresample_test_dependency "")
if(TARGET FFmpeg::swresample)
    set(ffmpeg_swresample_test_dependency FFmpeg::swresample)
elseif(TARGET FFmpegContractTest::swresample)
    set(ffmpeg_swresample_test_dependency FFmpegContractTest::swresample)
endif()

if(NOT ffmpeg_libavcodec_test_dependency STREQUAL "")
    add_executable(
        vrrecorder_native_ffmpeg_h264_packet_encoder
        ffmpeg_h264_packet_encoder_tests.cpp
        "${native_source_root}/src/ffmpeg_h264_packet_encoder.cpp"
        "${native_source_root}/src/ffmpeg_h264_software_codec_session.cpp"
        "${native_source_root}/src/ffmpeg_h264_media_foundation_configuration.cpp"
        "${native_source_root}/src/ffmpeg_h264_nv12_frame.cpp"
        "${native_source_root}/src/ffmpeg_libavcodec_encoder_port.cpp"
        "${native_source_root}/src/ffmpeg_encoder_state_machine.cpp"
        "${native_source_root}/src/h264_packet_normalizer.cpp"
        "${native_source_root}/src/h264_bitstream_converter.cpp"
        "${native_source_root}/src/h264_sps_parser.cpp"
        "${native_source_root}/src/video_encoder_config.cpp")
    target_compile_features(
        vrrecorder_native_ffmpeg_h264_packet_encoder PRIVATE cxx_std_20)
    target_compile_definitions(
        vrrecorder_native_ffmpeg_h264_packet_encoder
        PRIVATE VRRECORDER_NATIVE_TESTING=1)
    target_include_directories(
        vrrecorder_native_ffmpeg_h264_packet_encoder
        PRIVATE
            "${native_source_root}/include"
            "${native_source_root}/src"
            "${CMAKE_CURRENT_SOURCE_DIR}")
    target_link_libraries(
        vrrecorder_native_ffmpeg_h264_packet_encoder
        PRIVATE ${ffmpeg_libavcodec_test_dependency} Threads::Threads)
    set_target_properties(
        vrrecorder_native_ffmpeg_h264_packet_encoder
        PROPERTIES CXX_EXTENSIONS OFF)
    vrrecorder_enable_strict_warnings(
        vrrecorder_native_ffmpeg_h264_packet_encoder)

    add_executable(
        vrrecorder_native_ffmpeg_system_memory_encoder_probe_session
        ffmpeg_system_memory_encoder_probe_session_tests.cpp
        "${native_source_root}/src/ffmpeg_system_memory_encoder_probe_session.cpp"
        "${native_source_root}/src/encoder_probe_synthetic_nv12.cpp"
        "${native_source_root}/src/encoder_probe_pipeline.cpp"
        "${native_source_root}/src/encoder_probe_evidence_builder.cpp"
        "${native_source_root}/src/ffmpeg_h264_nv12_frame.cpp"
        "${native_source_root}/src/h264_packet_normalizer.cpp"
        "${native_source_root}/src/h264_bitstream_converter.cpp"
        "${native_source_root}/src/h264_sps_parser.cpp"
        "${native_source_root}/src/video_encoder_config.cpp")
    target_compile_features(
        vrrecorder_native_ffmpeg_system_memory_encoder_probe_session
        PRIVATE cxx_std_20)
    target_include_directories(
        vrrecorder_native_ffmpeg_system_memory_encoder_probe_session
        PRIVATE
            "${native_source_root}/include"
            "${native_source_root}/src"
            "${CMAKE_CURRENT_SOURCE_DIR}")
    target_link_libraries(
        vrrecorder_native_ffmpeg_system_memory_encoder_probe_session
        PRIVATE ${ffmpeg_libavcodec_test_dependency} Threads::Threads)
    set_target_properties(
        vrrecorder_native_ffmpeg_system_memory_encoder_probe_session
        PROPERTIES CXX_EXTENSIONS OFF)
    vrrecorder_enable_strict_warnings(
        vrrecorder_native_ffmpeg_system_memory_encoder_probe_session)

    add_executable(
        vrrecorder_native_software_encoder_probe_session_factory
        software_encoder_probe_session_factory_tests.cpp
        "${native_source_root}/src/software_encoder_probe_session_factory.cpp"
        "${native_source_root}/src/ffmpeg_system_memory_encoder_probe_session.cpp"
        "${native_source_root}/src/encoder_probe_synthetic_nv12.cpp"
        "${native_source_root}/src/ffmpeg_h264_nv12_frame.cpp"
        "${native_source_root}/src/video_encoder_config.cpp")
    target_compile_features(
        vrrecorder_native_software_encoder_probe_session_factory
        PRIVATE cxx_std_20)
    target_include_directories(
        vrrecorder_native_software_encoder_probe_session_factory
        PRIVATE
            "${native_source_root}/include"
            "${native_source_root}/src")
    target_link_libraries(
        vrrecorder_native_software_encoder_probe_session_factory
        PRIVATE ${ffmpeg_libavcodec_test_dependency} Threads::Threads)
    set_target_properties(
        vrrecorder_native_software_encoder_probe_session_factory
        PROPERTIES CXX_EXTENSIONS OFF)
    vrrecorder_enable_strict_warnings(
        vrrecorder_native_software_encoder_probe_session_factory)

    add_executable(
        vrrecorder_native_ffmpeg_h264_nv12_frame
        ffmpeg_h264_nv12_frame_tests.cpp
        "${native_source_root}/src/ffmpeg_h264_nv12_frame.cpp")
    target_compile_features(
        vrrecorder_native_ffmpeg_h264_nv12_frame PRIVATE cxx_std_20)
    target_include_directories(
        vrrecorder_native_ffmpeg_h264_nv12_frame
        PRIVATE
            "${native_source_root}/include"
            "${native_source_root}/src")
    target_link_libraries(
        vrrecorder_native_ffmpeg_h264_nv12_frame
        PRIVATE ${ffmpeg_libavcodec_test_dependency})
    set_target_properties(
        vrrecorder_native_ffmpeg_h264_nv12_frame
        PROPERTIES CXX_EXTENSIONS OFF)
    vrrecorder_enable_strict_warnings(
        vrrecorder_native_ffmpeg_h264_nv12_frame)

    add_executable(
        vrrecorder_native_ffmpeg_h264_media_foundation_configuration
        ffmpeg_h264_media_foundation_configuration_tests.cpp
        "${native_source_root}/src/video_encoder_config.cpp"
        "${native_source_root}/src/ffmpeg_h264_media_foundation_configuration.cpp")
    target_compile_features(
        vrrecorder_native_ffmpeg_h264_media_foundation_configuration
        PRIVATE cxx_std_20)
    target_include_directories(
        vrrecorder_native_ffmpeg_h264_media_foundation_configuration
        PRIVATE
            "${native_source_root}/include"
            "${native_source_root}/src")
    target_link_libraries(
        vrrecorder_native_ffmpeg_h264_media_foundation_configuration
        PRIVATE ${ffmpeg_libavcodec_test_dependency})
    set_target_properties(
        vrrecorder_native_ffmpeg_h264_media_foundation_configuration
        PROPERTIES CXX_EXTENSIONS OFF)
    vrrecorder_enable_strict_warnings(
        vrrecorder_native_ffmpeg_h264_media_foundation_configuration)

    add_executable(
        vrrecorder_native_ffmpeg_libavcodec_encoder_port
        ffmpeg_libavcodec_encoder_port_tests.cpp
        "${native_source_root}/src/ffmpeg_libavcodec_encoder_port.cpp"
        "${native_source_root}/src/ffmpeg_encoder_state_machine.cpp")
    target_compile_features(
        vrrecorder_native_ffmpeg_libavcodec_encoder_port PRIVATE cxx_std_20)
    target_compile_definitions(
        vrrecorder_native_ffmpeg_libavcodec_encoder_port
        PRIVATE VRRECORDER_NATIVE_TESTING=1)
    target_include_directories(
        vrrecorder_native_ffmpeg_libavcodec_encoder_port
        PRIVATE
            "${native_source_root}/include"
            "${native_source_root}/src")
    target_link_libraries(
        vrrecorder_native_ffmpeg_libavcodec_encoder_port
        PRIVATE
            ${ffmpeg_libavcodec_test_dependency}
            Threads::Threads)
    set_target_properties(
        vrrecorder_native_ffmpeg_libavcodec_encoder_port
        PROPERTIES
            CXX_EXTENSIONS OFF)
    vrrecorder_enable_strict_warnings(
        vrrecorder_native_ffmpeg_libavcodec_encoder_port)
    if(WIN32)
        add_custom_command(
            TARGET vrrecorder_native_ffmpeg_libavcodec_encoder_port
            POST_BUILD
            COMMAND
                "${CMAKE_COMMAND}" -E copy_if_different
                "$<TARGET_FILE:FFmpeg::avcodec>"
                "$<TARGET_FILE:FFmpeg::avutil>"
                "$<TARGET_FILE_DIR:vrrecorder_native_ffmpeg_libavcodec_encoder_port>"
            VERBATIM)
    endif()

    if(NOT ffmpeg_swresample_test_dependency STREQUAL "")
        add_executable(
            vrrecorder_native_ffmpeg_aac_packet_encoder
            ffmpeg_aac_packet_encoder_tests.cpp
            "${native_source_root}/src/audio_encoder_config.cpp"
            "${native_source_root}/src/ffmpeg_aac_packet_encoder.cpp"
            "${native_source_root}/src/ffmpeg_libavcodec_encoder_port.cpp"
            "${native_source_root}/src/ffmpeg_encoder_state_machine.cpp")
        target_compile_features(
            vrrecorder_native_ffmpeg_aac_packet_encoder PRIVATE cxx_std_20)
        target_compile_definitions(
            vrrecorder_native_ffmpeg_aac_packet_encoder
            PRIVATE VRRECORDER_NATIVE_TESTING=1)
        target_include_directories(
            vrrecorder_native_ffmpeg_aac_packet_encoder
            PRIVATE
                "${native_source_root}/include"
                "${native_source_root}/src")
        target_link_libraries(
            vrrecorder_native_ffmpeg_aac_packet_encoder
            PRIVATE
                ${ffmpeg_libavcodec_test_dependency}
                ${ffmpeg_swresample_test_dependency}
                Threads::Threads)
        set_target_properties(
            vrrecorder_native_ffmpeg_aac_packet_encoder
            PROPERTIES CXX_EXTENSIONS OFF)
        vrrecorder_enable_strict_warnings(
            vrrecorder_native_ffmpeg_aac_packet_encoder)
        if(WIN32)
            add_custom_command(
                TARGET vrrecorder_native_ffmpeg_aac_packet_encoder
                POST_BUILD
                COMMAND
                    "${CMAKE_COMMAND}" -E copy_if_different
                    "$<TARGET_FILE:FFmpeg::avcodec>"
                    "$<TARGET_FILE:FFmpeg::avutil>"
                    "$<TARGET_FILE:FFmpeg::swresample>"
                    "$<TARGET_FILE_DIR:vrrecorder_native_ffmpeg_aac_packet_encoder>"
                VERBATIM)
        endif()

        include(
            "${PROJECT_SOURCE_DIR}/cmake/ProductionMediaAacAttachment.cmake")
        vrrecorder_resolve_production_media_aac_sources(
            production_media_aac_sources
            "${native_source_root}")
        add_executable(
            vrrecorder_native_ffmpeg_aac_audio_pipeline
            ffmpeg_aac_audio_pipeline_tests.cpp
            ${production_media_aac_sources}
            "${native_source_root}/src/audio_encoder_config.cpp"
            "${native_source_root}/src/ffmpeg_encoder_state_machine.cpp"
            "${native_source_root}/src/muxing_audio_encoder_sink.cpp"
            "${native_source_root}/src/audio_pipeline_session.cpp"
            "${native_source_root}/src/audio_encoding_worker.cpp"
            "${native_source_root}/src/audio_encoding_pump.cpp")
        target_compile_features(
            vrrecorder_native_ffmpeg_aac_audio_pipeline PRIVATE cxx_std_20)
        target_compile_definitions(
            vrrecorder_native_ffmpeg_aac_audio_pipeline
            PRIVATE VRRECORDER_NATIVE_TESTING=1)
        target_include_directories(
            vrrecorder_native_ffmpeg_aac_audio_pipeline
            PRIVATE
                "${native_source_root}/include"
                "${native_source_root}/src")
        target_link_libraries(
            vrrecorder_native_ffmpeg_aac_audio_pipeline
            PRIVATE
                ${ffmpeg_libavcodec_test_dependency}
                ${ffmpeg_swresample_test_dependency}
                Threads::Threads)
        set_target_properties(
            vrrecorder_native_ffmpeg_aac_audio_pipeline
            PROPERTIES CXX_EXTENSIONS OFF)
        vrrecorder_enable_strict_warnings(
            vrrecorder_native_ffmpeg_aac_audio_pipeline)
        if(WIN32)
            add_custom_command(
                TARGET vrrecorder_native_ffmpeg_aac_audio_pipeline
                POST_BUILD
                COMMAND
                    "${CMAKE_COMMAND}" -E copy_if_different
                    "$<TARGET_FILE:FFmpeg::avcodec>"
                    "$<TARGET_FILE:FFmpeg::avutil>"
                    "$<TARGET_FILE:FFmpeg::swresample>"
                    "$<TARGET_FILE_DIR:vrrecorder_native_ffmpeg_aac_audio_pipeline>"
                VERBATIM)
        endif()
    endif()
endif()

set(ffmpeg_libavformat_test_dependency "")
if(TARGET FFmpeg::avformat)
    set(ffmpeg_libavformat_test_dependency FFmpeg::avformat)
elseif(TARGET FFmpegContractTest::avformat)
    set(ffmpeg_libavformat_test_dependency FFmpegContractTest::avformat)
endif()

if(NOT ffmpeg_libavformat_test_dependency STREQUAL "")
    add_executable(
        vrrecorder_native_ffmpeg_libavformat_fragmented_mp4_muxer_port
        ffmpeg_libavformat_fragmented_mp4_muxer_port_tests.cpp
        "${native_source_root}/src/ffmpeg_libavformat_fragmented_mp4_muxer_port.cpp"
        "${native_source_root}/src/ffmpeg_fragmented_mp4_muxer.cpp")
    target_compile_features(
        vrrecorder_native_ffmpeg_libavformat_fragmented_mp4_muxer_port
        PRIVATE cxx_std_20)
    target_compile_definitions(
        vrrecorder_native_ffmpeg_libavformat_fragmented_mp4_muxer_port
        PRIVATE VRRECORDER_NATIVE_TESTING=1)
    target_include_directories(
        vrrecorder_native_ffmpeg_libavformat_fragmented_mp4_muxer_port
        PRIVATE
            "${native_source_root}/include"
            "${native_source_root}/src")
    target_link_libraries(
        vrrecorder_native_ffmpeg_libavformat_fragmented_mp4_muxer_port
        PRIVATE ${ffmpeg_libavformat_test_dependency})
    set_target_properties(
        vrrecorder_native_ffmpeg_libavformat_fragmented_mp4_muxer_port
        PROPERTIES CXX_EXTENSIONS OFF)
    vrrecorder_enable_strict_warnings(
        vrrecorder_native_ffmpeg_libavformat_fragmented_mp4_muxer_port)
    if(WIN32)
        add_custom_command(
            TARGET
                vrrecorder_native_ffmpeg_libavformat_fragmented_mp4_muxer_port
            POST_BUILD
            COMMAND
                "${CMAKE_COMMAND}" -E copy_if_different
                "$<TARGET_FILE:FFmpeg::avformat>"
                "$<TARGET_FILE:FFmpeg::avcodec>"
                "$<TARGET_FILE:FFmpeg::avutil>"
                "$<TARGET_FILE_DIR:vrrecorder_native_ffmpeg_libavformat_fragmented_mp4_muxer_port>"
            VERBATIM)
    endif()
endif()


if(TARGET FFmpegContractOracle::avformat AND
        TARGET FFmpegContractOracle::avcodec AND
        TARGET FFmpegContractOracle::avutil)
    add_executable(
        vrrecorder_native_ffmpeg_aac_decode_oracle
        ffmpeg_aac_decode_oracle.cpp)
    target_compile_features(
        vrrecorder_native_ffmpeg_aac_decode_oracle PRIVATE cxx_std_20)
    target_link_libraries(
        vrrecorder_native_ffmpeg_aac_decode_oracle
        PRIVATE FFmpegContractOracle::avformat)
    set_target_properties(
        vrrecorder_native_ffmpeg_aac_decode_oracle
        PROPERTIES CXX_EXTENSIONS OFF)
    vrrecorder_enable_strict_warnings(
        vrrecorder_native_ffmpeg_aac_decode_oracle)
    if(WIN32)
        set(
            vrrecorder_native_ffmpeg_aac_decode_oracle_runtime
            "${CMAKE_CURRENT_BINARY_DIR}/ffmpeg-aac-decode-oracle")
        set_target_properties(
            vrrecorder_native_ffmpeg_aac_decode_oracle
            PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${vrrecorder_native_ffmpeg_aac_decode_oracle_runtime}")
        add_custom_command(
            TARGET vrrecorder_native_ffmpeg_aac_decode_oracle
            POST_BUILD
            COMMAND
                "${CMAKE_COMMAND}" -E copy_if_different
                "$<TARGET_FILE:FFmpegContractOracle::avformat>"
                "$<TARGET_FILE:FFmpegContractOracle::avcodec>"
                "$<TARGET_FILE:FFmpegContractOracle::avutil>"
                "$<TARGET_FILE_DIR:vrrecorder_native_ffmpeg_aac_decode_oracle>"
            VERBATIM)
    endif()
endif()

if(NOT ffmpeg_libavcodec_test_dependency STREQUAL "" AND
        NOT ffmpeg_swresample_test_dependency STREQUAL "" AND
        NOT ffmpeg_libavformat_test_dependency STREQUAL "")
    if(NOT TARGET vrrecorder_native_ffmpeg_aac_decode_oracle)
        message(
            FATAL_ERROR
            "vrrecorder_native_ffmpeg_aac_to_fragmented_mp4_integration requires VRRECORDER_FFMPEG_CONTRACT_ORACLE_ROOT")
    endif()
    add_executable(
        vrrecorder_native_ffmpeg_aac_to_fragmented_mp4_integration
        ffmpeg_aac_to_fragmented_mp4_integration_tests.cpp
        ${production_media_aac_sources}
        "${native_source_root}/src/audio_encoder_config.cpp"
        "${native_source_root}/src/ffmpeg_encoder_state_machine.cpp"
        "${native_source_root}/src/ffmpeg_libavformat_fragmented_mp4_muxer_port.cpp"
        "${native_source_root}/src/ffmpeg_fragmented_mp4_muxer.cpp"
        "${native_source_root}/src/media_mux_pipeline.cpp"
        "${native_source_root}/src/av_sync_monitor.cpp"
        "${native_source_root}/src/av_sync_media_event_adapter.cpp"
        "${native_source_root}/src/fragmented_mp4_mux_coordinator.cpp"
        "${native_source_root}/src/shared_mux_finalization_session.cpp"
        "${native_source_root}/src/muxing_audio_encoder_sink.cpp"
        "${native_source_root}/src/audio_pipeline_session.cpp"
        "${native_source_root}/src/audio_encoding_worker.cpp"
        "${native_source_root}/src/audio_encoding_pump.cpp")
    if(WIN32)
        target_sources(
            vrrecorder_native_ffmpeg_aac_to_fragmented_mp4_integration
            PRIVATE
                "${native_source_root}/src/ffmpeg_h264_packet_encoder.cpp"
                "${native_source_root}/src/ffmpeg_h264_software_codec_session.cpp"
                "${native_source_root}/src/ffmpeg_h264_media_foundation_configuration.cpp"
                "${native_source_root}/src/ffmpeg_h264_nv12_frame.cpp"
                "${native_source_root}/src/h264_packet_normalizer.cpp"
                "${native_source_root}/src/h264_bitstream_converter.cpp"
                "${native_source_root}/src/h264_sps_parser.cpp"
                "${native_source_root}/src/video_encoder_config.cpp")
    endif()
    target_compile_features(
        vrrecorder_native_ffmpeg_aac_to_fragmented_mp4_integration
        PRIVATE cxx_std_20)
    target_compile_definitions(
        vrrecorder_native_ffmpeg_aac_to_fragmented_mp4_integration
        PRIVATE VRRECORDER_NATIVE_TESTING=1)
    target_compile_definitions(
        vrrecorder_native_ffmpeg_aac_to_fragmented_mp4_integration
        PRIVATE
            VRRECORDER_AAC_DECODE_ORACLE_EXECUTABLE="$<TARGET_FILE:vrrecorder_native_ffmpeg_aac_decode_oracle>")
    add_dependencies(
        vrrecorder_native_ffmpeg_aac_to_fragmented_mp4_integration
        vrrecorder_native_ffmpeg_aac_decode_oracle)
    target_include_directories(
        vrrecorder_native_ffmpeg_aac_to_fragmented_mp4_integration
        PRIVATE
            "${native_source_root}/include"
            "${native_source_root}/src")
    target_link_libraries(
        vrrecorder_native_ffmpeg_aac_to_fragmented_mp4_integration
        PRIVATE
            ${ffmpeg_libavcodec_test_dependency}
            ${ffmpeg_swresample_test_dependency}
            ${ffmpeg_libavformat_test_dependency}
            Threads::Threads)
    set_target_properties(
        vrrecorder_native_ffmpeg_aac_to_fragmented_mp4_integration
        PROPERTIES CXX_EXTENSIONS OFF)
    vrrecorder_enable_strict_warnings(
        vrrecorder_native_ffmpeg_aac_to_fragmented_mp4_integration)
    if(WIN32)
        add_custom_command(
            TARGET
                vrrecorder_native_ffmpeg_aac_to_fragmented_mp4_integration
            POST_BUILD
            COMMAND
                "${CMAKE_COMMAND}" -E copy_if_different
                "$<TARGET_FILE:FFmpeg::avformat>"
                "$<TARGET_FILE:FFmpeg::avcodec>"
                "$<TARGET_FILE:FFmpeg::avutil>"
                "$<TARGET_FILE:FFmpeg::swresample>"
                "$<TARGET_FILE_DIR:vrrecorder_native_ffmpeg_aac_to_fragmented_mp4_integration>"
            VERBATIM)
    endif()
endif()

add_executable(
    vrrecorder_native_ffmpeg_fragmented_mp4_muxer
    ffmpeg_fragmented_mp4_muxer_tests.cpp
    "${native_source_root}/src/ffmpeg_fragmented_mp4_muxer.cpp")
target_compile_features(
    vrrecorder_native_ffmpeg_fragmented_mp4_muxer PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_ffmpeg_fragmented_mp4_muxer
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src"
        "${CMAKE_CURRENT_SOURCE_DIR}")
set_target_properties(
    vrrecorder_native_ffmpeg_fragmented_mp4_muxer
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_ffmpeg_fragmented_mp4_muxer)

add_executable(
    vrrecorder_native_shared_mux_finalization_session
    shared_mux_finalization_session_tests.cpp
    "${native_source_root}/src/shared_mux_finalization_session.cpp"
    "${native_source_root}/src/fragmented_mp4_mux_coordinator.cpp")
target_compile_features(
    vrrecorder_native_shared_mux_finalization_session PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_shared_mux_finalization_session
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_shared_mux_finalization_session
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_shared_mux_finalization_session)

add_executable(
    vrrecorder_native_muxing_video_encoder_sink
    muxing_video_encoder_sink_tests.cpp
    "${native_source_root}/src/muxing_video_encoder_sink.cpp")
target_compile_features(
    vrrecorder_native_muxing_video_encoder_sink PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_muxing_video_encoder_sink
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_muxing_video_encoder_sink
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_muxing_video_encoder_sink)

add_executable(
    vrrecorder_native_muxing_audio_encoder_sink
    muxing_audio_encoder_sink_tests.cpp
    "${native_source_root}/src/muxing_audio_encoder_sink.cpp")
target_compile_features(
    vrrecorder_native_muxing_audio_encoder_sink PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_muxing_audio_encoder_sink
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_muxing_audio_encoder_sink
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_muxing_audio_encoder_sink)

add_executable(
    vrrecorder_native_video_processing_encoder_sink
    video_processing_encoder_sink_tests.cpp
    "${native_source_root}/src/video_processing_encoder_sink.cpp"
    "${native_source_root}/src/video_processing_layout_controller.cpp"
    "${native_source_root}/src/video_processing_plan.cpp")
target_compile_features(
    vrrecorder_native_video_processing_encoder_sink PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_video_processing_encoder_sink
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_video_processing_encoder_sink
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_video_processing_encoder_sink)

add_executable(
    vrrecorder_native_d3d11_video_frame_processor
    d3d11_video_frame_processor_tests.cpp
    "${native_source_root}/src/d3d11_video_frame_processor.cpp"
    "${native_source_root}/src/video_processing_plan.cpp")
target_compile_features(
    vrrecorder_native_d3d11_video_frame_processor PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_d3d11_video_frame_processor
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_d3d11_video_frame_processor
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_d3d11_video_frame_processor)

add_executable(
    vrrecorder_native_d3d11_nv12_frame_mapper
    d3d11_nv12_frame_mapper_tests.cpp
    "${native_source_root}/src/d3d11_nv12_frame_mapper.cpp")
target_compile_features(
    vrrecorder_native_d3d11_nv12_frame_mapper PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_d3d11_nv12_frame_mapper
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_d3d11_nv12_frame_mapper
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_d3d11_nv12_frame_mapper)

add_executable(
    vrrecorder_native_keyed_mutex_video_surface
    keyed_mutex_video_surface_tests.cpp
    "${native_source_root}/src/keyed_mutex_video_surface.cpp")
target_compile_features(
    vrrecorder_native_keyed_mutex_video_surface PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_keyed_mutex_video_surface
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_keyed_mutex_video_surface
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_keyed_mutex_video_surface)

if(WIN32)
    add_executable(
        vrrecorder_native_windows_d3d11_video_processor_integration
        windows_d3d11_video_processor_integration_tests.cpp
        "${native_source_root}/src/d3d11_video_frame_processor.cpp"
        "${native_source_root}/src/d3d11_nv12_frame_mapper.cpp"
        "${native_source_root}/src/keyed_mutex_video_surface.cpp"
        "${native_source_root}/src/windows_d3d11_video_processor_port.cpp"
        "${native_source_root}/src/windows_d3d11_nv12_readback_port.cpp"
        "${native_source_root}/src/windows_d3d11_keyed_mutex_surface.cpp"
        "${native_source_root}/src/video_processing_plan.cpp")
    target_compile_features(
        vrrecorder_native_windows_d3d11_video_processor_integration
        PRIVATE cxx_std_20)
    target_compile_definitions(
        vrrecorder_native_windows_d3d11_video_processor_integration
        PRIVATE
            NOMINMAX
            WIN32_LEAN_AND_MEAN)
    target_include_directories(
        vrrecorder_native_windows_d3d11_video_processor_integration
        PRIVATE
            "${native_source_root}/include"
            "${native_source_root}/src")
    target_link_libraries(
        vrrecorder_native_windows_d3d11_video_processor_integration
        PRIVATE
            d3d11
            dxgi)
    set_target_properties(
        vrrecorder_native_windows_d3d11_video_processor_integration
        PROPERTIES
            CXX_EXTENSIONS OFF)
    vrrecorder_enable_strict_warnings(
        vrrecorder_native_windows_d3d11_video_processor_integration)

endif()

add_executable(
    vrrecorder_native_video_encoding_pump
    video_encoding_pump_tests.cpp
    "${native_source_root}/src/video_encoding_pump.cpp"
    "${native_source_root}/src/video_cfr_scheduler.cpp")
target_compile_features(
    vrrecorder_native_video_encoding_pump PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_video_encoding_pump
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_video_encoding_pump
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_video_encoding_pump)

add_executable(
    vrrecorder_native_video_encoding_worker
    video_encoding_worker_tests.cpp
    "${native_source_root}/src/video_encoding_worker.cpp"
    "${native_source_root}/src/video_encoding_pump.cpp"
    "${native_source_root}/src/video_cfr_scheduler.cpp")
target_compile_features(
    vrrecorder_native_video_encoding_worker PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_video_encoding_worker
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_video_encoding_worker
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_video_encoding_worker)

add_executable(
    vrrecorder_native_spout_capture_pump
    spout_capture_pump_tests.cpp
    "${native_source_root}/src/spout_capture_pump.cpp"
    "${native_source_root}/src/video_cfr_scheduler.cpp")
target_compile_features(
    vrrecorder_native_spout_capture_pump PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_spout_capture_pump
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_spout_capture_pump
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_spout_capture_pump)

add_executable(
    vrrecorder_native_spout2_source_backend
    spout2_source_backend_tests.cpp
    "${native_source_root}/src/spout2_source_backend_core.cpp")
target_compile_features(
    vrrecorder_native_spout2_source_backend PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_spout2_source_backend
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
target_link_libraries(
    vrrecorder_native_spout2_source_backend PRIVATE Threads::Threads)
set_target_properties(
    vrrecorder_native_spout2_source_backend
    PROPERTIES CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_spout2_source_backend)

add_executable(
    vrrecorder_native_openvr_steamvr_input_backend_core
    openvr_steamvr_input_backend_core_tests.cpp
    "${native_source_root}/src/openvr_steamvr_input_backend_core.cpp")
target_compile_features(
    vrrecorder_native_openvr_steamvr_input_backend_core PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_openvr_steamvr_input_backend_core
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_openvr_steamvr_input_backend_core
    PROPERTIES CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_openvr_steamvr_input_backend_core)

add_executable(
    vrrecorder_native_spout_capture_worker
    spout_capture_worker_tests.cpp
    "${native_source_root}/src/spout_capture_worker.cpp")
target_compile_features(
    vrrecorder_native_spout_capture_worker PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_spout_capture_worker
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_spout_capture_worker
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_spout_capture_worker)

add_executable(
    vrrecorder_native_video_pipeline_session
    video_pipeline_session_tests.cpp
    "${native_source_root}/src/video_pipeline_session.cpp"
    "${native_source_root}/src/video_encoding_worker.cpp"
    "${native_source_root}/src/video_encoding_pump.cpp"
    "${native_source_root}/src/video_cfr_scheduler.cpp")
target_compile_features(
    vrrecorder_native_video_pipeline_session PRIVATE cxx_std_20)
target_include_directories(
    vrrecorder_native_video_pipeline_session
    PRIVATE
        "${native_source_root}/include"
        "${native_source_root}/src")
set_target_properties(
    vrrecorder_native_video_pipeline_session
    PROPERTIES
        CXX_EXTENSIONS OFF)
vrrecorder_enable_strict_warnings(
    vrrecorder_native_video_pipeline_session)

add_test(NAME vrrecorder_native_abi_contract
    COMMAND vrrecorder_native_abi_contract)
set_tests_properties(
    vrrecorder_native_abi_contract
    PROPERTIES TIMEOUT 5)
add_test(NAME vrrecorder_native_c_header_smoke
    COMMAND vrrecorder_native_c_header_smoke)
add_test(NAME vrrecorder_native_audio_mixer
    COMMAND vrrecorder_native_audio_mixer)
add_test(NAME vrrecorder_native_audio_encoder_config
    COMMAND vrrecorder_native_audio_encoder_config)
add_test(NAME vrrecorder_native_av_sync_monitor
    COMMAND vrrecorder_native_av_sync_monitor)
add_test(NAME vrrecorder_native_av_sync_media_event_adapter
    COMMAND vrrecorder_native_av_sync_media_event_adapter)
add_test(NAME vrrecorder_native_media_mux_pipeline
    COMMAND vrrecorder_native_media_mux_pipeline)
add_test(NAME vrrecorder_native_media_recording_session
    COMMAND vrrecorder_native_media_recording_session)
add_test(NAME vrrecorder_native_media_stream_pipeline_adapters
    COMMAND vrrecorder_native_media_stream_pipeline_adapters)
add_test(NAME vrrecorder_native_media_recording_pipeline
    COMMAND vrrecorder_native_media_recording_pipeline)
add_test(NAME vrrecorder_native_pipeline_media_backend
    COMMAND vrrecorder_native_pipeline_media_backend)
add_test(NAME vrrecorder_native_production_media_configuration
    COMMAND vrrecorder_native_production_media_configuration)
add_test(NAME vrrecorder_native_pre_header_coordinator
    COMMAND vrrecorder_native_pre_header_coordinator)
add_test(NAME vrrecorder_native_pre_header_muxing_composition
    COMMAND vrrecorder_native_pre_header_muxing_composition)
add_test(NAME vrrecorder_native_pre_header_media_mux_session
    COMMAND vrrecorder_native_pre_header_media_mux_session)
set_tests_properties(
    vrrecorder_native_pipeline_media_backend
    PROPERTIES TIMEOUT 5)
add_test(NAME vrrecorder_native_full_media_graph_abort
    COMMAND vrrecorder_native_full_media_graph_abort)
set_tests_properties(
    vrrecorder_native_full_media_graph_abort
    PROPERTIES TIMEOUT 5)
add_test(NAME vrrecorder_native_audio_timeline_buffer
    COMMAND vrrecorder_native_audio_timeline_buffer)
add_test(NAME vrrecorder_native_audio_capture_timeline
    COMMAND vrrecorder_native_audio_capture_timeline)
add_test(NAME vrrecorder_native_audio_capture_pump
    COMMAND vrrecorder_native_audio_capture_pump)
add_test(NAME vrrecorder_native_audio_capture_normalizer
    COMMAND vrrecorder_native_audio_capture_normalizer)
add_test(NAME vrrecorder_native_wasapi_audio_capture_source_core
    COMMAND vrrecorder_native_wasapi_audio_capture_source_core)
add_test(NAME vrrecorder_native_audio_capture_source_factory
    COMMAND vrrecorder_native_audio_capture_source_factory)
add_test(NAME vrrecorder_native_audio_capture_platform_support
    COMMAND vrrecorder_native_audio_capture_platform_support)
add_test(NAME vrrecorder_native_audio_capture_input_worker
    COMMAND vrrecorder_native_audio_capture_input_worker)
add_test(NAME vrrecorder_native_audio_capture_session
    COMMAND vrrecorder_native_audio_capture_session)
set_tests_properties(
    vrrecorder_native_audio_capture_input_worker
    vrrecorder_native_audio_capture_session
    PROPERTIES TIMEOUT 5)
add_test(NAME vrrecorder_native_audio_mix_coordinator
    COMMAND vrrecorder_native_audio_mix_coordinator)
add_test(NAME vrrecorder_native_audio_encoding_pump
    COMMAND vrrecorder_native_audio_encoding_pump)
add_test(NAME vrrecorder_native_audio_encoding_worker
    COMMAND vrrecorder_native_audio_encoding_worker)
add_test(NAME vrrecorder_native_audio_pipeline_session
    COMMAND vrrecorder_native_audio_pipeline_session)
add_test(NAME vrrecorder_native_timestamping_audio_pipeline_session
    COMMAND vrrecorder_native_timestamping_audio_pipeline_session)
set_tests_properties(
    vrrecorder_native_audio_pipeline_session
    PROPERTIES TIMEOUT 5)
add_test(NAME vrrecorder_native_audio_media_event_adapter
    COMMAND vrrecorder_native_audio_media_event_adapter)
add_test(NAME vrrecorder_native_video_cfr_scheduler
    COMMAND vrrecorder_native_video_cfr_scheduler)
add_test(NAME vrrecorder_native_steady_video_cfr_clock
    COMMAND vrrecorder_native_steady_video_cfr_clock)
add_test(NAME vrrecorder_native_video_processing_plan
    COMMAND vrrecorder_native_video_processing_plan)
add_test(NAME vrrecorder_native_h264_bitstream_converter
    COMMAND vrrecorder_native_h264_bitstream_converter)
add_test(NAME vrrecorder_native_h264_avcc_to_annex_b
    COMMAND vrrecorder_native_h264_avcc_to_annex_b)
add_test(NAME vrrecorder_native_h264_sps_parser
    COMMAND vrrecorder_native_h264_sps_parser)
add_test(NAME vrrecorder_native_h264_packet_normalizer
    COMMAND vrrecorder_native_h264_packet_normalizer)
add_test(NAME vrrecorder_native_encoder_probe_evidence_builder
    COMMAND vrrecorder_native_encoder_probe_evidence_builder)
add_test(NAME vrrecorder_native_annex_b_encoder_probe_decoder
    COMMAND vrrecorder_native_annex_b_encoder_probe_decoder)
add_test(NAME vrrecorder_native_software_encoder_probe_platform_identity
    COMMAND vrrecorder_native_software_encoder_probe_platform_identity)
add_test(NAME vrrecorder_native_encoder_probe_pipeline
    COMMAND vrrecorder_native_encoder_probe_pipeline)
add_test(NAME vrrecorder_native_encoder_probe_synthetic_nv12
    COMMAND vrrecorder_native_encoder_probe_synthetic_nv12)
add_test(NAME vrrecorder_native_video_encoder_config
    COMMAND vrrecorder_native_video_encoder_config)
add_test(NAME vrrecorder_native_fragmented_mp4_mux_coordinator
    COMMAND vrrecorder_native_fragmented_mp4_mux_coordinator)
add_test(NAME vrrecorder_native_ffmpeg_encoder_state_machine
    COMMAND vrrecorder_native_ffmpeg_encoder_state_machine)
if(TARGET vrrecorder_native_ffmpeg_libavcodec_encoder_port)
    add_test(NAME vrrecorder_native_ffmpeg_libavcodec_encoder_port
        COMMAND vrrecorder_native_ffmpeg_libavcodec_encoder_port)
endif()
if(TARGET vrrecorder_native_ffmpeg_h264_media_foundation_configuration)
    add_test(
        NAME vrrecorder_native_ffmpeg_h264_media_foundation_configuration
        COMMAND
            vrrecorder_native_ffmpeg_h264_media_foundation_configuration)
endif()
if(TARGET vrrecorder_native_ffmpeg_h264_nv12_frame)
    add_test(
        NAME vrrecorder_native_ffmpeg_h264_nv12_frame
        COMMAND vrrecorder_native_ffmpeg_h264_nv12_frame)
endif()
if(TARGET vrrecorder_native_ffmpeg_h264_packet_encoder)
    add_test(
        NAME vrrecorder_native_ffmpeg_h264_packet_encoder
        COMMAND vrrecorder_native_ffmpeg_h264_packet_encoder)
endif()
if(TARGET vrrecorder_native_ffmpeg_system_memory_encoder_probe_session)
    add_test(
        NAME vrrecorder_native_ffmpeg_system_memory_encoder_probe_session
        COMMAND vrrecorder_native_ffmpeg_system_memory_encoder_probe_session)
endif()
if(TARGET vrrecorder_native_software_encoder_probe_session_factory)
    add_test(
        NAME vrrecorder_native_software_encoder_probe_session_factory
        COMMAND vrrecorder_native_software_encoder_probe_session_factory)
endif()
if(TARGET vrrecorder_native_ffmpeg_aac_packet_encoder)
    add_test(NAME vrrecorder_native_ffmpeg_aac_packet_encoder
        COMMAND vrrecorder_native_ffmpeg_aac_packet_encoder)
endif()
if(TARGET vrrecorder_native_ffmpeg_aac_audio_pipeline)
    add_test(NAME vrrecorder_native_ffmpeg_aac_audio_pipeline
        COMMAND vrrecorder_native_ffmpeg_aac_audio_pipeline)
    set_tests_properties(
        vrrecorder_native_ffmpeg_aac_audio_pipeline
        PROPERTIES TIMEOUT 5)
endif()
if(TARGET vrrecorder_native_ffmpeg_libavformat_fragmented_mp4_muxer_port)
    add_test(
        NAME vrrecorder_native_ffmpeg_libavformat_fragmented_mp4_muxer_port
        COMMAND
            vrrecorder_native_ffmpeg_libavformat_fragmented_mp4_muxer_port)
endif()
if(TARGET vrrecorder_native_ffmpeg_aac_to_fragmented_mp4_integration)
    add_test(
        NAME vrrecorder_native_ffmpeg_aac_to_fragmented_mp4_integration
        COMMAND
            vrrecorder_native_ffmpeg_aac_to_fragmented_mp4_integration)
    set_tests_properties(
        vrrecorder_native_ffmpeg_aac_to_fragmented_mp4_integration
        PROPERTIES TIMEOUT 5)
endif()
add_test(NAME vrrecorder_native_ffmpeg_fragmented_mp4_muxer
    COMMAND vrrecorder_native_ffmpeg_fragmented_mp4_muxer)
add_test(NAME vrrecorder_native_shared_mux_finalization_session
    COMMAND vrrecorder_native_shared_mux_finalization_session)
add_test(NAME vrrecorder_native_muxing_video_encoder_sink
    COMMAND vrrecorder_native_muxing_video_encoder_sink)
add_test(NAME vrrecorder_native_muxing_audio_encoder_sink
    COMMAND vrrecorder_native_muxing_audio_encoder_sink)
add_test(NAME vrrecorder_native_video_processing_encoder_sink
    COMMAND vrrecorder_native_video_processing_encoder_sink)
add_test(NAME vrrecorder_native_d3d11_video_frame_processor
    COMMAND vrrecorder_native_d3d11_video_frame_processor)
add_test(NAME vrrecorder_native_d3d11_nv12_frame_mapper
    COMMAND vrrecorder_native_d3d11_nv12_frame_mapper)
add_test(NAME vrrecorder_native_keyed_mutex_video_surface
    COMMAND vrrecorder_native_keyed_mutex_video_surface)
if(TARGET vrrecorder_native_windows_d3d11_video_processor_integration)
    add_test(
        NAME vrrecorder_native_windows_d3d11_video_processor_integration
        COMMAND vrrecorder_native_windows_d3d11_video_processor_integration)
endif()
add_test(NAME vrrecorder_native_video_encoding_pump
    COMMAND vrrecorder_native_video_encoding_pump)
add_test(NAME vrrecorder_native_video_encoding_worker
    COMMAND vrrecorder_native_video_encoding_worker)
add_test(NAME vrrecorder_native_spout_capture_pump
    COMMAND vrrecorder_native_spout_capture_pump)
add_test(NAME vrrecorder_native_spout2_source_backend
    COMMAND vrrecorder_native_spout2_source_backend)
set_tests_properties(
    vrrecorder_native_spout2_source_backend
    PROPERTIES TIMEOUT 5)
add_test(NAME vrrecorder_native_openvr_steamvr_input_backend_core
    COMMAND vrrecorder_native_openvr_steamvr_input_backend_core)
add_test(NAME vrrecorder_native_spout_capture_worker
    COMMAND vrrecorder_native_spout_capture_worker)
add_test(NAME vrrecorder_native_video_pipeline_session
    COMMAND vrrecorder_native_video_pipeline_session)
set_tests_properties(
    vrrecorder_native_audio_encoding_worker
    vrrecorder_native_video_encoding_worker
    PROPERTIES TIMEOUT 5)
set_tests_properties(
    vrrecorder_native_spout_capture_worker
    vrrecorder_native_video_pipeline_session
    PROPERTIES TIMEOUT 5)
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
