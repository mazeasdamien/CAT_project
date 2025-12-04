cmake_minimum_required(VERSION 3.19.2)
project(FanucDDS_DAMIEN_MAZEASWrapper CXX)

find_package(OpenDDS REQUIRED)

if(APPLE)
elseif(UNIX)
    SET(CMAKE_SKIP_BUILD_RPATH FALSE)
    SET(CMAKE_BUILD_WITH_INSTALL_RPATH TRUE)
    set(CMAKE_INSTALL_RPATH $ORIGIN)
endif()

add_library(FanucDDS_DAMIEN_MAZEASWrapper SHARED RobotStateTypeSupport.h RobotStateTypeSupport.cpp TeleopDataTypeSupport.h TeleopDataTypeSupport.cpp)

if(MSVC)
   add_compile_definitions(_CRT_SECURE_NO_WARNINGS)
   add_compile_definitions(_WINSOCK_DEPRECATED_NO_WARNINGS)
   set(CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS} /bigobj /wd4190 /std:c++17 /MD")
   set(CMAKE_CXX_FLAGS_RELEASE "${CMAKE_CXX_FLAGS_RELEASE} /O2 /D NDEBUG /D NOMINMAX")
elseif(APPLE)
    set_target_properties(FanucDDS_DAMIEN_MAZEASWrapper PROPERTIES LINK_FLAGS "-Wl,-rpath,@executable_path -Wl,-rpath,@loader_path")
    set(CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS} -Wpointer-arith -Wno-switch -Wno-return-type-c-linkage -Wno-tautological-pointer-compare -pthread -fno-inline -std=c++17")
    set(CMAKE_CXX_FLAGS_RELEASE "${CMAKE_CXX_FLAGS_RELEASE} -O3 -DNDEBUG")
elseif(UNIX)
    set_target_properties(FanucDDS_DAMIEN_MAZEASWrapper PROPERTIES LINK_FLAGS "-Wl,-rpath,$ORIGIN")
    set(CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS} -Wpointer-arith -Wno-switch -Wno-return-type-c-linkage -Wno-tautological-pointer-compare -pthread -fno-inline -std=c++17")
    set(CMAKE_CXX_FLAGS_RELEASE "${CMAKE_CXX_FLAGS_RELEASE} -O3 -DNDEBUG")
endif()

OPENDDS_TARGET_SOURCES(FanucDDS_DAMIEN_MAZEASWrapper
 PUBLIC
  RobotState.idl TeleopData.idl
  OPENDDS_IDL_OPTIONS -Sa --no-dcps-data-type-warnings)
target_link_libraries(FanucDDS_DAMIEN_MAZEASWrapper OpenDDS::OpenDDS ACE::ACE TAO::TAO)
