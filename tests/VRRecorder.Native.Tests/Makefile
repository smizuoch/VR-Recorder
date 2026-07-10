CC := gcc
CXX := g++
NATIVE_ROOT := ../../src/VRRecorder.Native
BUILD_DIR := build
CPPFLAGS := -I$(NATIVE_ROOT)/include -I$(NATIVE_ROOT)/src -I.
CFLAGS := -std=c17 -Wall -Wextra -Wpedantic -Werror
CXXFLAGS := -std=c++20 -O0 -g --coverage -Wall -Wextra -Wpedantic -Werror
LDFLAGS := --coverage -pthread

.PHONY: all clean test export-check

all: $(BUILD_DIR)/abi_contract_tests $(BUILD_DIR)/c_header_smoke.o

$(BUILD_DIR):
	mkdir -p $@

$(BUILD_DIR)/c_header_smoke.o: c_header_smoke.c | $(BUILD_DIR)
	$(CC) $(CPPFLAGS) $(CFLAGS) -c $< -o $@

$(BUILD_DIR)/abi_contract_tests: abi_contract_tests.cpp fake_media_backend.cpp $(NATIVE_ROOT)/src/abi.cpp | $(BUILD_DIR)
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) $^ $(LDFLAGS) -o $@

export-check:
	$(MAKE) -C $(NATIVE_ROOT) clean all
	nm -D --defined-only $(NATIVE_ROOT)/build/libvrrecorder_native.so | awk '{print $$3}' | sort > $(BUILD_DIR)/exports.actual
	diff -u expected_exports.txt $(BUILD_DIR)/exports.actual
	test -s $(NATIVE_ROOT)/build/vrrecorder_native.map

test: clean all export-check
	$(BUILD_DIR)/abi_contract_tests

clean:
	rm -rf $(BUILD_DIR)
	if [ -d $(NATIVE_ROOT) ]; then $(MAKE) -C $(NATIVE_ROOT) clean; fi
