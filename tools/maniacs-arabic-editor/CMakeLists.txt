cmake_minimum_required(VERSION 3.21)
cmake_policy(SET CMP0091 NEW)

project(ManiacsArabicEditorHook LANGUAGES C CXX)

if(NOT WIN32 OR NOT MSVC)
	message(FATAL_ERROR "The Maniacs Arabic editor hook requires MSVC on Windows.")
endif()

if(NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
	message(FATAL_ERROR "The Maniacs Arabic editor hook must be built for x86 (Win32).")
endif()

set(MINHOOK_DIR "${CMAKE_CURRENT_SOURCE_DIR}/third_party/minhook")

add_library(minhook STATIC
	"${MINHOOK_DIR}/src/buffer.c"
	"${MINHOOK_DIR}/src/hook.c"
	"${MINHOOK_DIR}/src/trampoline.c"
	"${MINHOOK_DIR}/src/hde/hde32.c"
)
target_include_directories(minhook
	PUBLIC "${MINHOOK_DIR}/include"
	PRIVATE "${MINHOOK_DIR}/src" "${MINHOOK_DIR}/src/hde"
)

add_library(EasyCmdTos SHARED src/hook.cpp src/project_runtime_support.cpp src/toveredit_support.cpp src/EasyCmdTos.def)
target_compile_features(EasyCmdTos PRIVATE cxx_std_17)
target_include_directories(EasyCmdTos PRIVATE "${MINHOOK_DIR}/include")
target_compile_definitions(EasyCmdTos PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX)
target_compile_options(EasyCmdTos PRIVATE /W4 /EHsc)
target_link_libraries(EasyCmdTos PRIVATE minhook user32 gdi32 comctl32)
set_target_properties(EasyCmdTos minhook PROPERTIES
	MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
)
set_target_properties(EasyCmdTos PROPERTIES PREFIX "" OUTPUT_NAME "EasyCmdTos")
