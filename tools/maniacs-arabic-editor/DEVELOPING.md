# Building and packaging

Requirements: Windows, Visual Studio 2022 with C++ development tools, and CMake.

From this directory, configure and build the x86 Release DLL:

```powershell
cmake -S . -B build -G "Visual Studio 17 2022" -A Win32
cmake --build build --config Release --target EasyCmdTos
```

The release build is `build\Release\EasyCmdTos.dll`. The packaging script also
uses the included `Plugin\EasyRpgCommands.dll` and `Plugin\0Harmony.dll` runtime
dependencies:

```powershell
.\scripts\package.ps1
```

The ZIP is written to `dist\Maniacs-Arabic-Editor-Windows-x86.zip`. The ZIP
contains the user installer and required runtime files only. The included
EasyRpgCommands loader is Ghabry's MIT-0-licensed component; it loads
`Plugin\EasyCmdTos.dll` for the Maniacs hook.

When rebuilding that loader from source, apply
`third_party/easyrpgcommands/patches/0001-call-hook-init-synchronously.patch`.
It calls the hook's initialization export immediately after loading the DLL.
