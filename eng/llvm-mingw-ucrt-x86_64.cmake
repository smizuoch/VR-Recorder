if(NOT DEFINED ENV{LLVM_MINGW_ROOT} OR "$ENV{LLVM_MINGW_ROOT}" STREQUAL "")
    message(FATAL_ERROR "LLVM_MINGW_ROOT must name an extracted llvm-mingw toolchain")
endif()

file(TO_CMAKE_PATH "$ENV{LLVM_MINGW_ROOT}" llvm_mingw_root)
set(llvm_mingw_target "x86_64-w64-mingw32")

set(CMAKE_SYSTEM_NAME Windows)
set(CMAKE_SYSTEM_PROCESSOR AMD64)
set(CMAKE_C_COMPILER
    "${llvm_mingw_root}/bin/${llvm_mingw_target}-clang")
set(CMAKE_CXX_COMPILER
    "${llvm_mingw_root}/bin/${llvm_mingw_target}-clang++")
set(CMAKE_RC_COMPILER
    "${llvm_mingw_root}/bin/${llvm_mingw_target}-windres")
set(CMAKE_EXE_LINKER_FLAGS_INIT "-static")
set(CMAKE_SHARED_LINKER_FLAGS_INIT "-static")

set(CMAKE_FIND_ROOT_PATH
    "${llvm_mingw_root}/${llvm_mingw_target}")
set(CMAKE_FIND_ROOT_PATH_MODE_PROGRAM NEVER)
set(CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_INCLUDE ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_PACKAGE ONLY)
