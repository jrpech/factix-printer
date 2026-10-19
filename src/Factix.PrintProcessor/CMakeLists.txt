cmake_minimum_required(VERSION 3.15)
project(factixpp LANGUAGES CXX)

add_library(factixpp SHARED factixpp.cpp factixpp.def)
target_compile_definitions(factixpp PRIVATE UNICODE _UNICODE WINVER=0x0601 _WIN32_WINNT=0x0601)
target_link_libraries(factixpp PRIVATE gdi32 winspool advapi32 shell32 ole32)

if(MSVC)
  set_property(TARGET factixpp PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
  target_compile_options(factixpp PRIVATE /W3)
endif()
