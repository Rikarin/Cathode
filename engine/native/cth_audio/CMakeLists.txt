cmake_minimum_required(VERSION 3.20)
project(cth_audio C)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_VISIBILITY_PRESET hidden)

if(APPLE)
  set(CTH_BACKEND src/cth_audio_coreaudio.c)
else()
  # ASIO/WASAPI (Windows) and JACK (Linux) backends are still to come.
  set(CTH_BACKEND src/cth_audio_stub.c)
endif()

add_library(cth_audio SHARED src/cth_fpu.c ${CTH_BACKEND})
target_include_directories(cth_audio PUBLIC include)

if(APPLE)
  target_link_libraries(cth_audio PRIVATE "-framework CoreAudio" "-framework CoreFoundation")
endif()

if(MSVC)
  target_compile_options(cth_audio PRIVATE /W4 /WX)
else()
  target_compile_options(cth_audio PRIVATE -Wall -Wextra -Werror -O2)
endif()

# Single output folder that the .NET build picks the library up from (see engine/Directory.Build.targets).
set_target_properties(cth_audio PROPERTIES
  LIBRARY_OUTPUT_DIRECTORY ${CMAKE_SOURCE_DIR}/../../artifacts/native
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_SOURCE_DIR}/../../artifacts/native
  LIBRARY_OUTPUT_DIRECTORY_RELEASE ${CMAKE_SOURCE_DIR}/../../artifacts/native
  RUNTIME_OUTPUT_DIRECTORY_RELEASE ${CMAKE_SOURCE_DIR}/../../artifacts/native)
