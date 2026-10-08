# EasyRPG-Arabic Setup

This project now builds `dist/EasyRPG-Arabic-Setup.exe`, a self-contained
Windows x64 WinForms application. It detects vanilla Steam RPG Maker 2003,
shows the recommended components, and provides Browse and Advanced options.
The vanilla Steam folder is treated as a read-only source; installations are
staged in a separate managed folder.

The modular installer can install the validated 2024-10-29 Maniacs package and
its English editor patch from a user-supplied extracted package, then install
the existing Arabic Editor ZIP and bundled EasyRPG-Arabic Player. It never
launches `utility.exe` or `patch.exe`: it applies the recipe's verified Common
payload and reads the English patch's WDiff delta directly. The managed root
keeps Maniacs `RPG_RT.exe` unchanged and installs EasyRPG-Arabic as `Player.exe`.
The editor hook copies `Player.exe` into projects created afterward and redirects
Play/Test Play to that project-local Player when present; projects without it
continue to use their original `RPG_RT.exe`. Existing projects are not changed.
Before staging, the installer shows the Maniacs package's `LICENSE.txt` and asks
the user to accept it. Unknown builds, changed fingerprints, and changed layouts
stop for validation.

Build the Release Player first, then publish the setup executable from a
Windows machine with the validated EasyRPG buildscripts toolchain and .NET 8
SDK. The setup embeds the Player binary and verifies it against
`data/easyrpg-arabic/player.json`.

```powershell
cmake --preset windows-x64-vs2022-release -DPLAYER_BUILD_LIBLCF=ON
cmake --build .\build\windows-x64-vs2022-release --config Release -j 8
.\packaging\publish.ps1
```

See [ARCHITECTURE.md](ARCHITECTURE.md) for module behavior and transaction
details. The existing Arabic Editor implementation remains in
`tools/maniacs-arabic-editor` and is embedded from its published ZIP without
changing that project.
