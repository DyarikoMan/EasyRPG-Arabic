# Building

## Dependencies

If your operating system has a package manager, we recommend installing the
dependencies with it.

In case you want to compile the dependencies yourself, you can find them,
except for [liblcf], in our [buildscripts] repository.


## Standard build with CMake

Building requirements:

- pkg-config (only on Linux)
- CMake 3.18 or newer

Step-by-step instructions:

    tar xf easyrpg-player-0.8.1.tar.xz    # unpack the tarball
    cd easyrpg-player-0.8.1               # enter in the package directory
    cmake . -DCMAKE_BUILD_TYPE=Release    # configure project
    cmake --build .                       # compile the executable
    sudo cmake --build . --target install # install system-wide

CMake is the only supported way to build Player. On Windows all dependencies
must be installed with [vcpkg].


## Windows build (EasyRPG-Arabic)

Requirements: Visual Studio 2022 with C++ development tools, CMake, Git, and the
EasyRPG [buildscripts] prebuilt Windows dependencies.

From PowerShell:

```powershell
cd C:\Dev
git clone https://github.com/EasyRPG/buildscripts.git

cd C:\Dev\buildscripts\windows
.\download_prebuilt.cmd

$env:EASYRPG_BUILDSCRIPTS = "C:\Dev\buildscripts"
cd C:\Dev\EasyRPG-Arabic

cmake --preset windows-x64-vs2022-release `
  -DPLAYER_BUILD_LIBLCF=ON

cmake --build .\build\windows-x64-vs2022-release `
  --config Release `
  -j 8
```

Alternatively, run `setup_env.cmd` in `C:\Dev\buildscripts\windows` to set
`EASYRPG_BUILDSCRIPTS` persistently. The executable is
`build\windows-x64-vs2022-release\Release\Player.exe`.

## libretro core

Additional commands required before building:

    git submodule init   # Init submodules
    git submodule update # Clone libretro-common submodule

Invoke CMake with these additional parameters:

    -DPLAYER_TARGET_PLATFORM=libretro -DBUILD_SHARED_LIBS=ON|OFF

Set shared libs to ON or OFF depending on which type of libraries RetroArch
uses on the platform you are targeting.


## Android APK

Building requirements:

- Android SDK with NDK r21

Step-by-step instructions:

    tar xf easyrpg-player-0.8.1.tar.xz     # unpack the tarball
    cd easyrpg-player-0.8.1/builds/android # enter in the android directory
    ./gradlew -PtoolchainDirs="DIR1;DIR2" assembleRelease # create the APK

Replace ``DIR1`` etc. with the path to the player dependencies. You can use
the scripts in the ``android`` folder of our [buildscripts] to compile them.

To pass additional CMake arguments use ``-PcmakeOptions``:

    -PcmakeOptions="-DSOME_OPTION1=ON -DSOME_OPTION2=OFF"

The unsigned APK is stored in:

    app/build/outputs/apk/release/app-release-unsigned.apk


## Nintendo and Sony Homebrew ports (Wii (U), 3DS, Switch, PSVita/PSTV)

Building requirements:

- devkitPPC for Wii and Wii U
- devkitARM for 3DS
- devkitA64 for Switch
- vitasdk for PSVita/PSTV

You can get them at [devkitpro.org] and [vitasdk.org].

Invoke CMake with these additional parameters:

    -DCMAKE_TOOLCHAIN_FILE=<DEVKITPRO>/cmake/3DS|Switch|Wii|WiiU.cmake
                         (or <VITASDK>/share/vita.toolchain.cmake)

Switch, Wii U and 3DS support shipping games, use these parameters:

    -DPLAYER_BUNDLE=ON -DPLAYER_BUNDLE_PATH=path/to/myGame


[buildscripts]: https://github.com/EasyRPG/buildscripts
[liblcf]: https://github.com/EasyRPG/liblcf
[vcpkg]: https://github.com/Microsoft/vcpkg
[devkitpro.org]: https://devkitpro.org/wiki/Getting_Started
[vitasdk.org]: https://vitasdk.org
