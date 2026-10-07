# EasyRPG-Arabic Setup

This project now builds `dist/EasyRPG-Arabic-Setup.exe`, a self-contained
Windows x64 WinForms application. It detects vanilla Steam RPG Maker 2003,
shows the recommended components, and provides Browse and Advanced options.
The vanilla Steam folder is treated as a read-only source; installations are
staged in a separate managed folder.

The current GUI is the first modular-bootstrapper version. The dependency
planner, validation, transaction/state infrastructure, and Arabic Editor ZIP
module are implemented. Advanced options can inspect an extracted Maniacs
package using versioned layout, build, and Arabic Editor compatibility JSON
data. The current validated data covers the 2024-10-29 package. Unknown builds,
changed file fingerprints, and changed layouts are reported for validation.

Package inspection does not apply Maniacs. Its official utility and English
patch workflows remain gated until their safe invocation and verification are
validated. EasyRPG-Arabic Player installation is also not implemented, so the
default Install action reports blockers and makes no changes.

Build or republish the executable from a Windows machine with the .NET 8 SDK:

```powershell
.\packaging\publish.ps1
```

See [ARCHITECTURE.md](ARCHITECTURE.md) for module behavior and transaction
details. The existing Arabic Editor implementation remains in
`tools/maniacs-arabic-editor` and is embedded from its published ZIP without
changing that project.
