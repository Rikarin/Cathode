# Audio I/O shim (C). See include/cth_audio.h.
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

cth_output_to_artifacts(cth_audio)
