# Audio I/O shim (C). See include/cth_audio.h.
if(APPLE)
  set(CTH_BACKEND src/cth_audio_coreaudio.c src/cth_midi_coremidi.c)
elseif(WIN32)
  # WASAPI (exclusive, else shared), ASIO for "asio:" uids, and WinMM MIDI. ASIO needs only the headers of Steinberg's
  # ASIO SDK, used under its GPLv3 option (THIRD-PARTY-NOTICES.md). Pinned by URL and hash: build.sh downloads it once
  # into engine/artifacts/deps and points FETCHCONTENT_SOURCE_DIR_ASIOSDK there.
  include(FetchContent)
  FetchContent_Declare(asiosdk
    URL https://download.steinberg.net/sdk_downloads/ASIO-SDK_2.3.4_2025-10-15.zip
    URL_HASH SHA256=d5ebf0c20dd2c5f43771fd0c1418f4b361bf52434ee670097cfa6b3a335e2eca
    DOWNLOAD_EXTRACT_TIMESTAMP TRUE)
  FetchContent_MakeAvailable(asiosdk)
  set(CTH_BACKEND src/cth_audio_wasapi.c src/cth_audio_asio.cpp src/cth_asio_util.c src/cth_midi_winmm.c)
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
  target_include_directories(cth_audio SYSTEM PRIVATE ${asiosdk_SOURCE_DIR}/common)
  target_link_libraries(cth_audio PRIVATE ole32 avrt winmm user32 advapi32)
elseif(CMAKE_SYSTEM_NAME STREQUAL "Linux")
  target_link_libraries(cth_audio PRIVATE ALSA::ALSA Threads::Threads ${CMAKE_DL_LIBS})
endif()

if(MSVC)
  target_compile_options(cth_audio PRIVATE /W4 /WX)
else()
  target_compile_options(cth_audio PRIVATE -Wall -Wextra -Werror -O2)
endif()

cth_output_to_artifacts(cth_audio)

# Unit tests of the parts that need no device (the ASIO backend's sample conversion, buffer sizing and driver-list
# parsing; plain C, so they run on every platform): ctest --test-dir artifacts/obj/native -C Release
add_executable(cth_audio_tests tests/cth_asio_util_tests.c src/cth_asio_util.c)
target_include_directories(cth_audio_tests PRIVATE src)
# Its own folder: MSBuild otherwise shares one output folder with cth_audio's import library.
set_target_properties(cth_audio_tests PROPERTIES RUNTIME_OUTPUT_DIRECTORY ${CMAKE_CURRENT_BINARY_DIR}/tests)
if(MSVC)
  target_compile_options(cth_audio_tests PRIVATE /W4 /WX)
else()
  target_compile_options(cth_audio_tests PRIVATE -Wall -Wextra -Werror)
  target_link_libraries(cth_audio_tests PRIVATE m)
endif()
add_test(NAME cth_audio_tests COMMAND cth_audio_tests)
