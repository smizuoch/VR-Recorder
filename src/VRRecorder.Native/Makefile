CXX := g++
BUILD_DIR := build
CPPFLAGS := -DVRRECORDER_NATIVE_EXPORTS -Iinclude -Isrc
CXXFLAGS := -std=c++20 -O2 -fPIC -fvisibility=hidden -Wall -Wextra -Wpedantic -Werror
LDFLAGS := -shared -pthread -Wl,--no-undefined -Wl,--version-script=exports.version -Wl,-Map,$(BUILD_DIR)/vrrecorder_native.map
SOURCES := src/abi.cpp src/audio_capture_input_runner.cpp src/audio_capture_normalizer.cpp src/audio_capture_platform_support.cpp src/audio_capture_pump.cpp src/audio_capture_timeline.cpp src/audio_mix_coordinator.cpp src/audio_mixer.cpp src/audio_timeline_buffer.cpp src/unavailable_encoder_probe_backend.cpp src/unavailable_media_backend.cpp src/unavailable_spout_source_backend.cpp src/unavailable_steamvr_input_backend.cpp src/unavailable_wasapi_audio_capture_source.cpp
OBJECTS := $(SOURCES:src/%.cpp=$(BUILD_DIR)/%.o)

.PHONY: all clean

all: $(BUILD_DIR)/libvrrecorder_native.so

$(BUILD_DIR):
	mkdir -p $@

$(BUILD_DIR)/%.o: src/%.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) -MMD -MP -c $< -o $@

$(BUILD_DIR)/libvrrecorder_native.so: $(OBJECTS)
	$(CXX) $^ $(LDFLAGS) -o $@

clean:
	rm -rf $(BUILD_DIR)

-include $(OBJECTS:.o=.d)
