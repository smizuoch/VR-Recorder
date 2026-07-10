cmake_minimum_required(VERSION 3.24)

project(VRRecorderNative VERSION 0.1.0 LANGUAGES C CXX)

if(NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "VR Recorder native ABI supports x64 builds only")
endif()

function(vrrecorder_enable_strict_warnings target)
    if(MSVC)
        target_compile_options(
            ${target}
            PRIVATE
                "$<$<COMPILE_LANGUAGE:C>:/W4;/WX>"
                "$<$<COMPILE_LANGUAGE:CXX>:/W4;/WX;/permissive-;/EHsc>")
    else()
        target_compile_options(
            ${target}
            PRIVATE
                -Wall
                -Wextra
                -Wpedantic
                -Werror)
    endif()
endfunction()

include(CTest)
find_package(Threads REQUIRED)

add_subdirectory(src/VRRecorder.Native)

if(BUILD_TESTING)
    add_subdirectory(tests/VRRecorder.Native.Tests)
endif()
