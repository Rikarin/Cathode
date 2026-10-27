# Cathode native libraries. Built by build.sh into engine/artifacts/native, where the .NET build picks them up
# (engine/Directory.Build.targets).
cmake_minimum_required(VERSION 3.24)
project(cathode_native C CXX)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_C_VISIBILITY_PRESET hidden)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_VISIBILITY_INLINES_HIDDEN ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

set(CTH_ARTIFACTS_DIR ${CMAKE_CURRENT_SOURCE_DIR}/../artifacts/native)

function(cth_output_to_artifacts target)
  foreach(kind LIBRARY RUNTIME)
    set_target_properties(${target} PROPERTIES ${kind}_OUTPUT_DIRECTORY ${CTH_ARTIFACTS_DIR})
    foreach(config Debug Release RelWithDebInfo MinSizeRel)
      string(TOUPPER ${config} upper)
      set_target_properties(${target} PROPERTIES ${kind}_OUTPUT_DIRECTORY_${upper} ${CTH_ARTIFACTS_DIR})
    endforeach()
  endforeach()
endfunction()

enable_testing()

add_subdirectory(cth_audio)
add_subdirectory(cth_nam)
