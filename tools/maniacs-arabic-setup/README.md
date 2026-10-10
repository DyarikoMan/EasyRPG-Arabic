# EasyRPG-Arabic Setup

This project builds a self-contained Windows x64 WinForms installer. Its normal
screen detects vanilla Steam RPG Maker 2003 and offers one Install action; the
recommended modules and technical controls are in collapsed Advanced options.
The default destination is `<vanilla RPG Maker 2003>\Arabic`. Setup stages and
manages files only inside that folder; the parent installation remains a
read-only source. Source inputs are explicit recipe paths, so the `Arabic`
destination is never copied back into itself.

The public setup build uses the validated 2024-10-29 Maniacs package and its
English editor patch from a user-supplied extracted package. A separate
private/internal build can embed the exact fingerprint-validated 241029
payload. The installer then installs
the existing Arabic Editor ZIP and bundled EasyRPG-Arabic Player. It never
launches `utility.exe` or `patch.exe`: it applies the recipe's verified Common
payload and reads the English patch's WDiff delta directly. The managed root
keeps Maniacs `RPG_RT.exe` unchanged and installs EasyRPG-Arabic as `Player.exe`.
The editor hook copies `Player.exe` into projects created afterward and redirects
Play/Test Play to that project-local Player when present; projects without it
continue to use their original `RPG_RT.exe`. On a newly created project, the
hook also runs the bundled CP1256 PO updater after the project files are ready,
creating `Language\ar` from that project's database and filling canonical
system terms. It copies the historical Arabic PixelSmall font only when the
project has no `Language\ar\Font\Font.ttf`, and initializes only new projects
to 426x240 while preserving other `RPG_RT.ini` values. Existing projects are
not changed.
Before staging, the installer shows the Maniacs package's `LICENSE.txt` and asks
the user to accept it. Unknown builds, changed fingerprints, and changed layouts
stop for validation.

Build the Release Player first, then publish the setup executable from a
Windows machine with the validated EasyRPG buildscripts toolchain and .NET 8
SDK. The setup embeds the Player binary and verifies it against
`data/easyrpg-arabic/player.json`. After success it opens the managed folder in
Windows Explorer and closes Setup shortly afterward; it does not launch the
editor.

```powershell
cmake --preset windows-x64-vs2022-release -DPLAYER_BUILD_LIBLCF=ON
cmake --build .\build\windows-x64-vs2022-release --config Release -j 8
.\packaging\publish.ps1
```

`publish.ps1` defaults to a public build with
`UserSuppliedValidatedPackage`; it does not embed Maniacs. A private/internal
build can embed a local extracted package only after the same 241029 fingerprints,
layout, architectures, and Arabic Editor compatibility are validated:

```powershell
.\packaging\publish.ps1 -Mode PrivateInternal `
  -ValidatedManiacsPackageDirectory 'C:\path\to\Official-Package'
```

That build writes to `builds\private-internal`, separately from the public
`dist` executable. A public embedded build is blocked unless redistribution
permission is explicitly confirmed.

See [ARCHITECTURE.md](ARCHITECTURE.md) for module behavior and transaction
details. The existing Arabic Editor implementation remains in
`tools/maniacs-arabic-editor` and is embedded from its published ZIP without
changing that project.
