# Neural Amp Modeler host (C++), wrapping NeuralAmpModelerCore behind a C ABI. See include/cth_nam.h.

include(FetchContent)

# Pinned: bump deliberately and regenerate the golden renders in engine/tests/assets/nam (see README there).
FetchContent_Declare(nam_core
  GIT_REPOSITORY https://github.com/sdatkinson/NeuralAmpModelerCore.git
  GIT_TAG 0b3d3c97b0859a3a8c92a8628c4dd89a25eb5842 # v0.6.0
  GIT_SUBMODULES "Dependencies/eigen"
  SOURCE_SUBDIR _cathode_does_not_use_upstream_cmake)
FetchContent_MakeAvailable(nam_core)

file(GLOB NAM_CORE_SOURCES CONFIGURE_DEPENDS ${nam_core_SOURCE_DIR}/NAM/*.cpp ${nam_core_SOURCE_DIR}/NAM/*/*.cpp)

# OBJECT, not STATIC: architectures self-register through static initialisers, which a static library
# would let the linker drop. Third-party code: built as-is, without our warning flags.
add_library(nam_core OBJECT ${NAM_CORE_SOURCES})
target_include_directories(nam_core SYSTEM PUBLIC
  ${nam_core_SOURCE_DIR}
  ${nam_core_SOURCE_DIR}/Dependencies/eigen
  ${nam_core_SOURCE_DIR}/Dependencies/nlohmann)
target_compile_definitions(nam_core PUBLIC NAM_ENABLE_A2_FAST $<$<PLATFORM_ID:Windows>:NOMINMAX WIN32_LEAN_AND_MEAN>)
if(NOT MSVC)
  target_compile_options(nam_core PRIVATE -w $<$<CONFIG:Release>:-O3>)
endif()

add_library(cth_nam SHARED src/cth_nam.cpp)
target_include_directories(cth_nam PUBLIC include)
target_link_libraries(cth_nam PRIVATE nam_core)
if(MSVC)
  target_compile_options(cth_nam PRIVATE /W4 /WX)
else()
  target_compile_options(cth_nam PRIVATE -Wall -Wextra -Werror $<$<CONFIG:Release>:-O3>)
endif()
cth_output_to_artifacts(cth_nam)

# Upstream example models, used by the tests and benchmarks (MIT, NeuralAmpModelerCore).
file(COPY ${nam_core_SOURCE_DIR}/example_models/ DESTINATION ${CTH_ARTIFACTS_DIR}/nam-models)
