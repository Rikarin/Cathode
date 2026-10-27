# cth_rtsan_check: cth_nam and cth_audio's real-time paths under RealtimeSanitizer (see cth_rtsan_check.cpp).
# Built only with -DCTH_RTSAN=ON, on Linux with Clang 20 or newer (RTSan's first release).
if(NOT CMAKE_SYSTEM_NAME STREQUAL "Linux" OR NOT CMAKE_CXX_COMPILER_ID MATCHES "Clang"
   OR CMAKE_CXX_COMPILER_VERSION VERSION_LESS 20)
  message(FATAL_ERROR "CTH_RTSAN needs Linux and Clang 20 or newer (RealtimeSanitizer); "
                      "configure with CC=clang-20 CXX=clang++-20")
endif()

add_executable(cth_rtsan_check cth_rtsan_check.cpp)
target_link_libraries(cth_rtsan_check PRIVATE cth_audio cth_nam)
# Calls from nonblocking functions into unmarked ones are the point here, so the compile-time effect warnings are off;
# RTSan checks those calls at run time instead.
target_compile_options(cth_rtsan_check PRIVATE -fsanitize=realtime -g -fno-omit-frame-pointer -Wall -Wextra
                                               -Wno-function-effects -Wno-perf-constraint-implies-noexcept)
# The runtime is linked into this executable; -rdynamic exports it so cth_audio's weak __rtsan_realtime_enter/exit
# hooks (src/cth_audio_linux.h) bind to it when the library loads.
target_link_options(cth_rtsan_check PRIVATE -fsanitize=realtime -rdynamic)
set_target_properties(cth_rtsan_check PROPERTIES RUNTIME_OUTPUT_DIRECTORY ${CTH_ARTIFACTS_DIR})
