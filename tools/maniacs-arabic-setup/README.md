# EasyRPG-Arabic Setup

This project now builds `dist/EasyRPG-Arabic-Setup.exe`, a self-contained
Windows x64 WinForms application. It detects vanilla Steam RPG Maker 2003,
shows the recommended components, and provides Browse and Advanced options.
The vanilla Steam folder is treated as a read-only source; installations are
staged in a separate managed folder.

The modular installer can install the validated 2024-10-29 Maniacs package and
its English editor patch from a user-supplied extracted package, then install
the existing Arabic Editor ZIP. It never launches `utility.exe` or
`patch.exe`: it applies the recipe's verified Common payload and reads the
English patch's WDiff delta directly. Before staging, the installer shows the
package's `LICENSE.txt` and asks the user to accept it. Unknown builds, changed
fingerprints, and changed layouts stop for validation. EasyRPG-Arabic Player
installation is still not implemented and remains visibly unavailable.

Build or republish the executable from a Windows machine with the .NET 8 SDK:

```powershell
.\packaging\publish.ps1
```

See [ARCHITECTURE.md](ARCHITECTURE.md) for module behavior and transaction
details. The existing Arabic Editor implementation remains in
`tools/maniacs-arabic-editor` and is embedded from its published ZIP without
changing that project.
