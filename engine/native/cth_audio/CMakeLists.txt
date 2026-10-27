# Audio I/O shim (C). See include/cth_audio.h.
if(APPLE)
  set(CTH_BACKEND src/cth_audio_coreaudio.c src/cth_midi_coremidi.c)
elseif(WIN32)
  # WASAPI (exclusive, else shared) and WinMM MIDI. ASIO is still to come.
  set(CTH_BACKEND src/cth_audio_wasapi.c src/cth_midi_winmm.c)
elseif(CMAKE_SYSTEM_NAME STREQUAL "Linux")
  # ALSA (hw: PCMs, sequencer MIDI) is required; JACK is loaded at run time with dlopen, so it needs nothing here.
  find_package(ALSA)
  if(NOT ALSA_FOUND)
    message(FATAL_ERROR "cth_audio needs the ALSA headers: install libasound2-dev (Debian, Ubuntu, Raspberry Pi OS) "
                        "or alsa-lib-devel (Fedora).")
  endif()
  find_package(Threads REQUIRED)
  set(CTH_BACKEND src/cth_audio_alsa.c src/cth_audio_jack.c src/cth_midi_alsa.c)
else()
  # Any other platform: no backend yet.
  set(CTH_BACKEND src/cth_audio_stub.c src/cth_midi_stub.c)
endif()

add_library(cth_audio SHARED src/cth_fpu.c ${CTH_BACKEND})
target_include_directories(cth_audio PUBLIC include)

if(APPLE)
  target_link_libraries(cth_audio PRIVATE "-framework CoreAudio" "-framework CoreFoundation" "-framework CoreMIDI")
elseif(WIN32)
  target_link_libraries(cth_audio PRIVATE ole32 avrt winmm)
elseif(CMAKE_SYSTEM_NAME STREQUAL "Linux")
  target_link_libraries(cth_audio PRIVATE ALSA::ALSA Threads::Threads ${CMAKE_DL_LIBS})
endif()

if(MSVC)
  target_compile_options(cth_audio PRIVATE /W4 /WX)
else()
  target_compile_options(cth_audio PRIVATE -Wall -Wextra -Werror -O2)
endif()

if(CTH_RTSAN)
  # The real-time part of each cycle enters RealtimeSanitizer's real-time context (src/cth_audio_linux.h).
  target_compile_definitions(cth_audio PRIVATE CTH_RTSAN)
endif()

cth_output_to_artifacts(cth_audio)
