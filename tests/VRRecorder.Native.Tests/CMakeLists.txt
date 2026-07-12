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
            "${native_source_root}/src/wasapi_audio_capture_source.cpp")
    target_compile_definitions(
        vrrecorder_native_audio_capture_source_factory
        PRIVATE
            NOMINMAX
            WIN32_LEAN_AND_MEAN)
    target_link_libraries(
        vrrecorder_native_audio_capture_source_factory
        PRIVATE
            Ole32.lib
            Uuid.lib)
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
            "${native_source_root}/src/wasapi_audio_capture_source.cpp")
    target_compile_definitions(
        vrrecorder_native_audio_capture_platform_support
        PRIVATE
            NOMINMAX
            WIN32_LEAN_AND_MEAN)
    target_link_libraries(
        vrrecorder_native_audio_capture_platform_support
        PRIVATE
            Ole32.lib
            Uuid.lib)
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
    "${native_source_root}/src/video_pipeline_session.cpp")
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
add_test(NAME vrrecorder_native_c_header_smoke
    COMMAND vrrecorder_native_c_header_smoke)
add_test(NAME vrrecorder_native_audio_mixer
    COMMAND vrrecorder_native_audio_mixer)
add_test(NAME vrrecorder_native_audio_timeline_buffer
    COMMAND vrrecorder_native_audio_timeline_buffer)
add_test(NAME vrrecorder_native_audio_capture_timeline
    COMMAND vrrecorder_native_audio_capture_timeline)
add_test(NAME vrrecorder_native_audio_capture_pump
    COMMAND vrrecorder_native_audio_capture_pump)
add_test(NAME vrrecorder_native_audio_capture_normalizer
    COMMAND vrrecorder_native_audio_capture_normalizer)
add_test(NAME vrrecorder_native_audio_capture_source_factory
    COMMAND vrrecorder_native_audio_capture_source_factory)
add_test(NAME vrrecorder_native_audio_capture_platform_support
    COMMAND vrrecorder_native_audio_capture_platform_support)
add_test(NAME vrrecorder_native_audio_capture_input_worker
    COMMAND vrrecorder_native_audio_capture_input_worker)
add_test(NAME vrrecorder_native_audio_capture_session
    COMMAND vrrecorder_native_audio_capture_session)
add_test(NAME vrrecorder_native_audio_mix_coordinator
    COMMAND vrrecorder_native_audio_mix_coordinator)
add_test(NAME vrrecorder_native_audio_encoding_pump
    COMMAND vrrecorder_native_audio_encoding_pump)
add_test(NAME vrrecorder_native_audio_encoding_worker
    COMMAND vrrecorder_native_audio_encoding_worker)
add_test(NAME vrrecorder_native_audio_pipeline_session
    COMMAND vrrecorder_native_audio_pipeline_session)
add_test(NAME vrrecorder_native_audio_media_event_adapter
    COMMAND vrrecorder_native_audio_media_event_adapter)
add_test(NAME vrrecorder_native_video_cfr_scheduler
    COMMAND vrrecorder_native_video_cfr_scheduler)
add_test(NAME vrrecorder_native_video_encoding_pump
    COMMAND vrrecorder_native_video_encoding_pump)
add_test(NAME vrrecorder_native_video_encoding_worker
    COMMAND vrrecorder_native_video_encoding_worker)
add_test(NAME vrrecorder_native_spout_capture_pump
    COMMAND vrrecorder_native_spout_capture_pump)
add_test(NAME vrrecorder_native_spout_capture_worker
    COMMAND vrrecorder_native_spout_capture_worker)
add_test(NAME vrrecorder_native_video_pipeline_session
    COMMAND vrrecorder_native_video_pipeline_session)
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
