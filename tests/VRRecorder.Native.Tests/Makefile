CC := gcc
CXX := g++
NATIVE_ROOT := ../../src/VRRecorder.Native
BUILD_DIR := build
CPPFLAGS := -I$(NATIVE_ROOT)/include -I$(NATIVE_ROOT)/src -I.
CFLAGS := -std=c17 -Wall -Wextra -Wpedantic -Werror
CXXFLAGS := -std=c++20 -O0 -g --coverage -Wall -Wextra -Wpedantic -Werror
LDFLAGS := --coverage -pthread

.PHONY: all clean test export-check cmake-contract-sync managed-fixture

all: $(BUILD_DIR)/abi_contract_tests $(BUILD_DIR)/av_sync_monitor_tests $(BUILD_DIR)/av_sync_media_event_adapter_tests $(BUILD_DIR)/audio_encoder_config_tests $(BUILD_DIR)/audio_mixer_tests $(BUILD_DIR)/audio_timeline_buffer_tests $(BUILD_DIR)/audio_capture_timeline_tests $(BUILD_DIR)/audio_capture_pump_tests $(BUILD_DIR)/audio_capture_normalizer_tests $(BUILD_DIR)/audio_capture_source_factory_tests $(BUILD_DIR)/audio_capture_platform_support_tests $(BUILD_DIR)/audio_capture_input_worker_tests $(BUILD_DIR)/audio_capture_session_tests $(BUILD_DIR)/audio_mix_coordinator_tests $(BUILD_DIR)/audio_encoding_pump_tests $(BUILD_DIR)/audio_encoding_worker_tests $(BUILD_DIR)/audio_pipeline_session_tests $(BUILD_DIR)/audio_media_event_adapter_tests $(BUILD_DIR)/fragmented_mp4_mux_coordinator_tests $(BUILD_DIR)/media_mux_pipeline_tests $(BUILD_DIR)/shared_mux_finalization_session_tests $(BUILD_DIR)/muxing_audio_encoder_sink_tests $(BUILD_DIR)/muxing_video_encoder_sink_tests $(BUILD_DIR)/video_encoder_config_tests $(BUILD_DIR)/video_processing_plan_tests $(BUILD_DIR)/video_processing_encoder_sink_tests $(BUILD_DIR)/video_cfr_scheduler_tests $(BUILD_DIR)/video_encoding_pump_tests $(BUILD_DIR)/video_encoding_worker_tests $(BUILD_DIR)/spout_capture_pump_tests $(BUILD_DIR)/spout_capture_worker_tests $(BUILD_DIR)/video_pipeline_session_tests $(BUILD_DIR)/c_header_smoke.o

$(BUILD_DIR):
	mkdir -p $@

$(BUILD_DIR)/c_header_smoke.o: c_header_smoke.c | $(BUILD_DIR)
	$(CC) $(CPPFLAGS) $(CFLAGS) -c $< -o $@

$(BUILD_DIR)/abi_contract_tests: abi_contract_tests.cpp fake_media_backend.cpp $(NATIVE_ROOT)/src/abi.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/audio_mixer_tests: audio_mixer_tests.cpp $(NATIVE_ROOT)/src/audio_mixer.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/audio_encoder_config_tests: audio_encoder_config_tests.cpp $(NATIVE_ROOT)/src/audio_encoder_config.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/av_sync_monitor_tests: av_sync_monitor_tests.cpp $(NATIVE_ROOT)/src/av_sync_monitor.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/av_sync_media_event_adapter_tests: av_sync_media_event_adapter_tests.cpp $(NATIVE_ROOT)/src/av_sync_media_event_adapter.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/media_mux_pipeline_tests: media_mux_pipeline_tests.cpp $(NATIVE_ROOT)/src/media_mux_pipeline.cpp $(NATIVE_ROOT)/src/av_sync_monitor.cpp $(NATIVE_ROOT)/src/av_sync_media_event_adapter.cpp $(NATIVE_ROOT)/src/fragmented_mp4_mux_coordinator.cpp $(NATIVE_ROOT)/src/shared_mux_finalization_session.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/fragmented_mp4_mux_coordinator_tests: fragmented_mp4_mux_coordinator_tests.cpp $(NATIVE_ROOT)/src/fragmented_mp4_mux_coordinator.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/shared_mux_finalization_session_tests: shared_mux_finalization_session_tests.cpp $(NATIVE_ROOT)/src/shared_mux_finalization_session.cpp $(NATIVE_ROOT)/src/fragmented_mp4_mux_coordinator.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/muxing_video_encoder_sink_tests: muxing_video_encoder_sink_tests.cpp $(NATIVE_ROOT)/src/muxing_video_encoder_sink.cpp $(NATIVE_ROOT)/src/shared_mux_finalization_session.cpp $(NATIVE_ROOT)/src/fragmented_mp4_mux_coordinator.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/muxing_audio_encoder_sink_tests: muxing_audio_encoder_sink_tests.cpp $(NATIVE_ROOT)/src/muxing_audio_encoder_sink.cpp $(NATIVE_ROOT)/src/shared_mux_finalization_session.cpp $(NATIVE_ROOT)/src/fragmented_mp4_mux_coordinator.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/audio_timeline_buffer_tests: audio_timeline_buffer_tests.cpp $(NATIVE_ROOT)/src/audio_timeline_buffer.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/audio_capture_timeline_tests: audio_capture_timeline_tests.cpp $(NATIVE_ROOT)/src/audio_capture_timeline.cpp $(NATIVE_ROOT)/src/audio_timeline_buffer.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/audio_capture_pump_tests: audio_capture_pump_tests.cpp $(NATIVE_ROOT)/src/audio_capture_input_runner.cpp $(NATIVE_ROOT)/src/audio_capture_pump.cpp $(NATIVE_ROOT)/src/audio_capture_timeline.cpp $(NATIVE_ROOT)/src/audio_timeline_buffer.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/audio_capture_normalizer_tests: audio_capture_normalizer_tests.cpp $(NATIVE_ROOT)/src/audio_capture_normalizer.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/audio_capture_source_factory_tests: audio_capture_source_factory_tests.cpp $(NATIVE_ROOT)/src/unavailable_wasapi_audio_capture_source.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/audio_capture_platform_support_tests: audio_capture_platform_support_tests.cpp $(NATIVE_ROOT)/src/audio_capture_platform_support.cpp $(NATIVE_ROOT)/src/unavailable_wasapi_audio_capture_source.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/audio_capture_input_worker_tests: audio_capture_input_worker_tests.cpp $(NATIVE_ROOT)/src/audio_capture_input_worker.cpp $(NATIVE_ROOT)/src/audio_capture_input_runner.cpp $(NATIVE_ROOT)/src/audio_capture_pump.cpp $(NATIVE_ROOT)/src/audio_capture_timeline.cpp $(NATIVE_ROOT)/src/audio_timeline_buffer.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/audio_capture_session_tests: audio_capture_session_tests.cpp $(NATIVE_ROOT)/src/audio_capture_session.cpp $(NATIVE_ROOT)/src/audio_capture_input_worker.cpp $(NATIVE_ROOT)/src/audio_capture_input_runner.cpp $(NATIVE_ROOT)/src/audio_capture_pump.cpp $(NATIVE_ROOT)/src/audio_capture_timeline.cpp $(NATIVE_ROOT)/src/audio_mix_coordinator.cpp $(NATIVE_ROOT)/src/audio_mixer.cpp $(NATIVE_ROOT)/src/audio_timeline_buffer.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/audio_mix_coordinator_tests: audio_mix_coordinator_tests.cpp $(NATIVE_ROOT)/src/audio_mix_coordinator.cpp $(NATIVE_ROOT)/src/audio_mixer.cpp $(NATIVE_ROOT)/src/audio_capture_timeline.cpp $(NATIVE_ROOT)/src/audio_timeline_buffer.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/audio_encoding_pump_tests: audio_encoding_pump_tests.cpp $(NATIVE_ROOT)/src/audio_encoding_pump.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/audio_encoding_worker_tests: audio_encoding_worker_tests.cpp $(NATIVE_ROOT)/src/audio_encoding_worker.cpp $(NATIVE_ROOT)/src/audio_encoding_pump.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/audio_pipeline_session_tests: audio_pipeline_session_tests.cpp $(NATIVE_ROOT)/src/audio_pipeline_session.cpp $(NATIVE_ROOT)/src/audio_encoding_worker.cpp $(NATIVE_ROOT)/src/audio_encoding_pump.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/audio_media_event_adapter_tests: audio_media_event_adapter_tests.cpp $(NATIVE_ROOT)/src/audio_media_event_adapter.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/video_cfr_scheduler_tests: video_cfr_scheduler_tests.cpp $(NATIVE_ROOT)/src/video_cfr_scheduler.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/video_processing_plan_tests: video_processing_plan_tests.cpp $(NATIVE_ROOT)/src/video_processing_plan.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/video_encoder_config_tests: video_encoder_config_tests.cpp $(NATIVE_ROOT)/src/video_encoder_config.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/video_processing_encoder_sink_tests: video_processing_encoder_sink_tests.cpp $(NATIVE_ROOT)/src/video_processing_encoder_sink.cpp $(NATIVE_ROOT)/src/video_processing_plan.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/video_encoding_pump_tests: video_encoding_pump_tests.cpp $(NATIVE_ROOT)/src/video_encoding_pump.cpp $(NATIVE_ROOT)/src/video_cfr_scheduler.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/video_encoding_worker_tests: video_encoding_worker_tests.cpp $(NATIVE_ROOT)/src/video_encoding_worker.cpp $(NATIVE_ROOT)/src/video_encoding_pump.cpp $(NATIVE_ROOT)/src/video_cfr_scheduler.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/spout_capture_pump_tests: spout_capture_pump_tests.cpp $(NATIVE_ROOT)/src/spout_capture_pump.cpp $(NATIVE_ROOT)/src/video_cfr_scheduler.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/spout_capture_worker_tests: spout_capture_worker_tests.cpp $(NATIVE_ROOT)/src/spout_capture_worker.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/video_pipeline_session_tests: video_pipeline_session_tests.cpp $(NATIVE_ROOT)/src/video_pipeline_session.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

$(BUILD_DIR)/libvrrecorder_native_test.so: fake_media_backend.cpp fake_control_exports.cpp $(NATIVE_ROOT)/src/abi.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) -DVRRECORDER_NATIVE_EXPORTS -std=c++20 -O0 -g -fPIC -fvisibility=hidden -Wall -Wextra -Wpedantic -Werror $^ -shared -pthread -Wl,--no-undefined -o $@

managed-fixture: $(BUILD_DIR)/libvrrecorder_native_test.so

export-check:
	$(MAKE) -C $(NATIVE_ROOT) clean all
	nm -D --defined-only $(NATIVE_ROOT)/build/libvrrecorder_native.so | awk '{print $$3}' | sort > $(BUILD_DIR)/exports.actual
	diff -u expected_exports.txt $(BUILD_DIR)/exports.actual
	test -s $(NATIVE_ROOT)/build/vrrecorder_native.map

cmake-contract-sync:
	test "$$(wc -l < expected_exports.txt)" -eq 17
	grep -Fq "if(NOT export_count EQUAL 17)" cmake_build_contract.cmake
	grep -Fq "if(NOT export_count EQUAL 17)" verify_exports.cmake
	grep -Fq "Verified exactly 17 native exports" verify_exports.cmake

test: clean all export-check cmake-contract-sync
	$(BUILD_DIR)/abi_contract_tests
	$(BUILD_DIR)/av_sync_monitor_tests
	$(BUILD_DIR)/av_sync_media_event_adapter_tests
	$(BUILD_DIR)/audio_encoder_config_tests
	$(BUILD_DIR)/audio_mixer_tests
	$(BUILD_DIR)/audio_timeline_buffer_tests
	$(BUILD_DIR)/audio_capture_timeline_tests
	$(BUILD_DIR)/audio_capture_pump_tests
	$(BUILD_DIR)/audio_capture_normalizer_tests
	$(BUILD_DIR)/audio_capture_source_factory_tests
	$(BUILD_DIR)/audio_capture_platform_support_tests
	$(BUILD_DIR)/audio_capture_input_worker_tests
	$(BUILD_DIR)/audio_capture_session_tests
	$(BUILD_DIR)/audio_mix_coordinator_tests
	$(BUILD_DIR)/audio_encoding_pump_tests
	$(BUILD_DIR)/audio_encoding_worker_tests
	$(BUILD_DIR)/audio_pipeline_session_tests
	$(BUILD_DIR)/audio_media_event_adapter_tests
	$(BUILD_DIR)/fragmented_mp4_mux_coordinator_tests
	$(BUILD_DIR)/media_mux_pipeline_tests
	$(BUILD_DIR)/shared_mux_finalization_session_tests
	$(BUILD_DIR)/muxing_audio_encoder_sink_tests
	$(BUILD_DIR)/muxing_video_encoder_sink_tests
	$(BUILD_DIR)/video_encoder_config_tests
	$(BUILD_DIR)/video_processing_plan_tests
	$(BUILD_DIR)/video_processing_encoder_sink_tests
	$(BUILD_DIR)/video_cfr_scheduler_tests
	$(BUILD_DIR)/video_encoding_pump_tests
	$(BUILD_DIR)/video_encoding_worker_tests
	$(BUILD_DIR)/spout_capture_pump_tests
	$(BUILD_DIR)/spout_capture_worker_tests
	$(BUILD_DIR)/video_pipeline_session_tests

clean:
	rm -rf $(BUILD_DIR)
	if [ -d $(NATIVE_ROOT) ]; then $(MAKE) -C $(NATIVE_ROOT) clean; fi
